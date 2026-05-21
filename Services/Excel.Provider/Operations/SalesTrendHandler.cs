using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportingPlatform.ExcelProvider.Excel;
using ReportingPlatform.Provider.V1;

namespace ReportingPlatform.ExcelProvider.Operations;

/// <summary>
/// Handler for <c>report.sales.trend</c>.
/// Aggregates daily sales rows by day / week / month and returns time-series data.
/// </summary>
public sealed class SalesTrendHandler : IOperationHandler
{
    private readonly ExcelDataLoader _loader;
    private readonly ILogger<SalesTrendHandler> _logger;

    public string OperationPattern => "report.sales.trend";

    public SalesTrendHandler(ExcelDataLoader loader, ILogger<SalesTrendHandler> logger)
    {
        _loader = loader;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(
        OperationRequest request,
        Func<int, string, Task> reportProgress,
        CancellationToken ct)
    {
        await reportProgress(10, "Loading Excel data…");
        var data = await _loader.GetDataAsync(ct);

        await reportProgress(30, "Parsing parameters…");

        using var doc      = JsonDocument.Parse(request.ParamsJson ?? "{}");
        var root           = doc.RootElement;
        var fromDate       = DateOnly.Parse(root.GetProperty("fromDate").GetString()!);
        var toDate         = DateOnly.Parse(root.GetProperty("toDate").GetString()!);
        var groupBy        = root.GetProperty("groupBy").GetString() ?? "day";

        _logger.LogInformation("SalesTrend from={From} to={To} groupBy={Group}", fromDate, toDate, groupBy);

        await reportProgress(50, $"Filtering rows from {fromDate} to {toDate}…");

        var filtered = data.Sales
            .Where(r => r.Date >= fromDate && r.Date <= toDate)
            .ToList();

        await reportProgress(70, $"Grouping by {groupBy}…");

        var grouped = groupBy switch
        {
            "week"  => GroupByWeek(filtered),
            "month" => GroupByMonth(filtered),
            _       => GroupByDay(filtered, fromDate, toDate),
        };

        await reportProgress(90, "Building response…");

        var result = new
        {
            labels = grouped.Keys.ToList(),
            series = new[]
            {
                new { name = "Revenue", data = grouped.Values.Select(v => Math.Round(v.Revenue, 2)).ToList() },
                new { name = "Units",   data = grouped.Values.Select(v => (decimal)v.Units).ToList() },
            }
        };

        return JsonSerializer.Serialize(result);
    }

    // ─── Grouping helpers ─────────────────────────────────────────────────────

    private static SortedDictionary<string, (decimal Revenue, int Units)> GroupByDay(
        List<SalesRow> rows, DateOnly from, DateOnly to)
    {
        var dict = new SortedDictionary<string, (decimal, int)>();

        // Ensure every day in range is present (zero fill)
        for (var d = from; d <= to; d = d.AddDays(1))
            dict[d.ToString("yyyy-MM-dd")] = (0m, 0);

        foreach (var r in rows)
        {
            var key = r.Date.ToString("yyyy-MM-dd");
            if (dict.TryGetValue(key, out var existing))
                dict[key] = (existing.Item1 + r.Revenue, existing.Item2 + r.Units);
        }

        return dict;
    }

    private static SortedDictionary<string, (decimal Revenue, int Units)> GroupByWeek(
        List<SalesRow> rows)
    {
        var dict = new SortedDictionary<string, (decimal, int)>();

        foreach (var r in rows)
        {
            int weekNumber = ISOWeek.GetWeekOfYear(r.Date.ToDateTime(TimeOnly.MinValue));
            var key = $"{r.Date.Year}-W{weekNumber:D2}";
            dict.TryGetValue(key, out var existing);
            dict[key] = (existing.Item1 + r.Revenue, existing.Item2 + r.Units);
        }

        return dict;
    }

    private static SortedDictionary<string, (decimal Revenue, int Units)> GroupByMonth(
        List<SalesRow> rows)
    {
        var dict = new SortedDictionary<string, (decimal, int)>();

        foreach (var r in rows)
        {
            var key = $"{r.Date.Year}-{r.Date.Month:D2}";
            dict.TryGetValue(key, out var existing);
            dict[key] = (existing.Item1 + r.Revenue, existing.Item2 + r.Units);
        }

        return dict;
    }
}
