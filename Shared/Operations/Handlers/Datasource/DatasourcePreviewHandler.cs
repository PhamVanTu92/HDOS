using ReportingPlatform.Adapters.Abstractions;
using ReportingPlatform.Adapters.Models;
using ReportingPlatform.Contracts.RenderPayloads.Shared;
using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Datasource;

internal sealed class DatasourcePreviewHandler : IOperationHandler
{
    private const int DefaultPreviewLimit = 50;

    public string OperationName => "datasource.preview";

    private readonly IDatasourceMetadataRepository _repo;
    private readonly IDatasourceAdapterFactory _adapters;

    public DatasourcePreviewHandler(
        IDatasourceMetadataRepository repo,
        IDatasourceAdapterFactory adapters)
    {
        _repo     = repo;
        _adapters = adapters;
    }

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<DatasourcePreviewParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "datasourceId is required.");

        var datasource = await _repo.GetAsync(context.TenantId, p.DatasourceId, ct)
            ?? throw new OperationException("DATASOURCE_NOT_FOUND",
                $"Datasource '{p.DatasourceId}' not found.");

        var limit = p.Limit ?? DefaultPreviewLimit;
        var adapter = _adapters.Resolve(datasource);
        var request = new AdapterRequest
        {
            TenantId   = context.TenantId,
            Datasource = datasource,
            Filters    = new Dictionary<string, JsonElement>(),
            Pagination = new Contracts.TableParams.TablePaginationParams
            {
                Page     = 1,
                PageSize = limit,
            },
        };

        var result = await adapter.FetchAsync(request, ct);
        var truncated = result.TotalRows.HasValue && result.TotalRows.Value > limit;

        // Build column descriptors from first row keys
        var columns = result.Rows.Count > 0
            ? result.Rows[0].Keys.Select(k => new TableColumn { Key = k, Label = k, Type = "string" }).ToList()
            : new List<TableColumn>();

        var payload = new DatasourcePreviewPayload
        {
            Columns   = columns,
            Rows      = result.Rows,
            TotalRows = result.TotalRows ?? result.Rows.Count,
            Truncated = truncated,
        };

        return JsonSerializer.SerializeToElement(payload,
            OperationsJsonContext.Default.DatasourcePreviewPayload);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
