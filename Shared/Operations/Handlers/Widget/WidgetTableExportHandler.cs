using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using CsvHelper;
using ReportingPlatform.Adapters.Abstractions;
using ReportingPlatform.Adapters.Models;
using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;
using ReportingPlatform.Resolver.Abstractions;

namespace ReportingPlatform.Operations.Handlers.Widget;

internal sealed class WidgetTableExportHandler : IOperationHandler
{
    private const int InlineRowLimit = 5_000;

    public string OperationName => "widget.tableExport";

    private readonly IDashboardDefinitionRepository _definitions;
    private readonly IDatasourceAdapterFactory _adapters;

    public WidgetTableExportHandler(
        IDashboardDefinitionRepository definitions,
        IDatasourceAdapterFactory adapters)
    {
        _definitions = definitions;
        _adapters    = adapters;
    }

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<WidgetTableExportParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "dashboardCode, widgetId, and format are required.");

        var format = p.Format.ToLowerInvariant();
        if (format is not ("csv" or "xlsx"))
            throw new OperationException("INVALID_PARAMS",
                "Unsupported format. Allowed: csv, xlsx");

        var defResult = await _definitions.GetAsync(context.TenantId, p.DashboardCode, ct)
            ?? throw new OperationException("DASHBOARD_NOT_FOUND",
                $"Dashboard '{p.DashboardCode}' not found.");

        var (dashboard, _) = defResult;
        var widget = (dashboard.Widgets ?? []).FirstOrDefault(w => w.WidgetId == p.WidgetId)
            ?? throw new OperationException("WIDGET_NOT_FOUND",
                $"Widget '{p.WidgetId}' not found in dashboard '{p.DashboardCode}'.");

        var filters = p.Filters ?? new Dictionary<string, JsonElement>();

        // Fetch datasource for this widget
        var dsIds = new[] { widget.DatasourceId };
        var datasources = await _definitions.GetDatasourcesAsync(context.TenantId, dsIds, ct);
        if (!datasources.TryGetValue(widget.DatasourceId, out var datasource))
            throw new OperationException("DATASOURCE_NOT_FOUND",
                $"Datasource '{widget.DatasourceId}' not found.");

        var adapter = _adapters.Resolve(datasource);
        var request = new AdapterRequest
        {
            TenantId   = context.TenantId,
            Datasource = datasource,
            Filters    = filters,
        };

        context.Progress?.Report(new ProgressUpdate(20, "Fetching data..."));
        var result = await adapter.FetchAsync(request, ct);

        var rows = result.Rows;
        if (rows.Count > InlineRowLimit)
            throw new OperationException("LARGE_EXPORT_NOT_SUPPORTED",
                $"Export of {rows.Count} rows exceeds the inline limit of {InlineRowLimit}. Large export is not supported in this phase.");

        context.Progress?.Report(new ProgressUpdate(70, "Serializing..."));

        var fileName = $"{p.WidgetId}_{DateTime.UtcNow:yyyyMMdd}.{format}";
        using var ms = new MemoryStream();

        if (format == "csv")
            WriteCsv(ms, rows);
        else
            WriteXlsx(ms, rows, p.WidgetId);

        var bytes  = ms.ToArray();
        var base64 = Convert.ToBase64String(bytes);

        var exportResult = new TableExportResult
        {
            Format        = format,
            ContentBase64 = base64,
            FileName      = fileName,
            SizeBytes     = bytes.Length,
        };

        context.Progress?.Report(new ProgressUpdate(100, "Export complete."));

        return JsonSerializer.SerializeToElement(exportResult,
            OperationsJsonContext.Default.TableExportResult);
    }

    private static void WriteCsv(MemoryStream ms, IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows)
    {
        if (rows.Count == 0) return;

        using var writer = new StreamWriter(ms, leaveOpen: true);
        using var csv    = new CsvWriter(writer, CultureInfo.InvariantCulture);

        // Write header
        var columns = rows[0].Keys.ToList();
        foreach (var col in columns)
        {
            csv.WriteField(col);
        }
        csv.NextRecord();

        // Write rows
        foreach (var row in rows)
        {
            foreach (var col in columns)
            {
                var val = row.TryGetValue(col, out var el)
                    ? (el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText())
                    : string.Empty;
                csv.WriteField(val);
            }
            csv.NextRecord();
        }
    }

    private static void WriteXlsx(MemoryStream ms, IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows, string sheetName)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);

        if (rows.Count == 0)
        {
            wb.SaveAs(ms);
            return;
        }

        var columns = rows[0].Keys.ToList();

        // Header row
        for (var col = 0; col < columns.Count; col++)
            ws.Cell(1, col + 1).Value = columns[col];

        // Data rows
        for (var row = 0; row < rows.Count; row++)
        {
            for (var col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cell(row + 2, col + 1);
                if (rows[row].TryGetValue(columns[col], out var el))
                {
                    cell.Value = el.ValueKind switch
                    {
                        JsonValueKind.Number when el.TryGetDouble(out var d) => d,
                        JsonValueKind.True    => "true",
                        JsonValueKind.False   => "false",
                        JsonValueKind.Null    => string.Empty,
                        _                     => el.ValueKind == JsonValueKind.String
                                                 ? el.GetString() ?? string.Empty
                                                 : el.GetRawText(),
                    };
                }
            }
        }

        wb.SaveAs(ms);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
