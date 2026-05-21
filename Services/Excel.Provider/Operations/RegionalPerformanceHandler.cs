using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportingPlatform.ExcelProvider.Excel;
using ReportingPlatform.Provider.V1;

namespace ReportingPlatform.ExcelProvider.Operations;

/// <summary>
/// Handler for <c>report.regional.performance</c>.
/// Aggregates sales by region for a given period (today / week / month) and compares against targets.
/// </summary>
public sealed class RegionalPerformanceHandler : IOperationHandler
{
    private readonly ExcelDataLoader _loader;
    private readonly ILogger<RegionalPerformanceHandler> _logger;

    public string OperationPattern => "report.regional.performance";

    public RegionalPerformanceHandler(ExcelDataLoader loader, ILogger<RegionalPerformanceHandler> logger)
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

        using var doc = JsonDocument.Parse(request.ParamsJson ?? """{"period":"today"}""");
        var period    = doc.RootElement.GetProperty("period").GetString() ?? "today";

        _logger.LogInformation("RegionalPerformance period={Period}", period);

        var today = DateOnly.FromDateTime(DateTime.Today);
        (DateOnly From, DateOnly To) range = period switch
        {
            "week"  => (today.AddDays(-(int)today.DayOfWeek + 1), today),   // Mon–today (ISO)
            "month" => (new DateOnly(today.Year, today.Month, 1), today),
            _       => (today, today), // "today"
        };

        await reportProgress(50, $"Filtering rows for period {range.From} – {range.To}…");

        var filteredRows = data.Sales
            .Where(r => r.Date >= range.From && r.Date <= range.To)
            .ToList();

        await reportProgress(70, "Aggregating by region…");

        // Calculate target for the period (scale MonthlyTarget)
        int periodDays = (range.To.ToDateTime(TimeOnly.MinValue) - range.From.ToDateTime(TimeOnly.MinValue)).Days + 1;
        double monthFraction = periodDays / 30.0;

        var regionMap = data.Regions.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        var regionGroups = filteredRows
            .GroupBy(r => r.Region)
            .Select(g =>
            {
                decimal revenue = g.Sum(r => r.Revenue);
                int     units   = g.Sum(r => r.Units);

                decimal target = regionMap.TryGetValue(g.Key, out var regionInfo)
                    ? Math.Round(regionInfo.MonthlyTarget * (decimal)monthFraction, 2)
                    : 50_000m;

                double achievementPct = target > 0
                    ? Math.Round((double)revenue / (double)target * 100, 1)
                    : 0;

                return new
                {
                    name           = g.Key,
                    revenue        = Math.Round(revenue, 2),
                    units,
                    target,
                    achievementPct,
                };
            })
            .OrderByDescending(r => r.revenue)
            .ToList();

        // Ensure all regions appear even if they have no sales in the period
        var presentRegions = regionGroups.Select(r => r.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var zeroRegions = data.Regions
            .Where(r => !presentRegions.Contains(r.Name))
            .Select(r =>
            {
                decimal target = Math.Round(r.MonthlyTarget * (decimal)monthFraction, 2);
                return new
                {
                    name           = r.Name,
                    revenue        = 0m,
                    units          = 0,
                    target,
                    achievementPct = 0.0,
                };
            })
            .ToList();

        await reportProgress(90, "Building response…");

        var result = new
        {
            regions = regionGroups
                .Cast<object>()
                .Concat(zeroRegions)
                .ToList()
        };

        return JsonSerializer.Serialize(result);
    }
}
