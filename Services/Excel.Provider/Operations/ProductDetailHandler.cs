using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportingPlatform.ExcelProvider.Excel;
using ReportingPlatform.Provider.V1;

namespace ReportingPlatform.ExcelProvider.Operations;

/// <summary>
/// Handler for <c>report.product.detail</c>.
/// Returns detailed performance metrics for a single product over a date range.
/// </summary>
public sealed class ProductDetailHandler : IOperationHandler
{
    private readonly ExcelDataLoader _loader;
    private readonly ILogger<ProductDetailHandler> _logger;

    public string OperationPattern => "report.product.detail";

    public ProductDetailHandler(ExcelDataLoader loader, ILogger<ProductDetailHandler> logger)
    {
        _loader = loader;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(
        OperationRequest request,
        Func<int, string, Task> reportProgress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("ProductDetail starting — requestId={RequestId}", request.RequestId);

        await reportProgress(10, "Loading Excel data…");
        var data = await _loader.GetDataAsync(ct);

        await reportProgress(30, "Parsing parameters…");

        using var doc  = JsonDocument.Parse(request.ParamsJson ?? "{}");
        var root       = doc.RootElement;
        var productName = root.GetProperty("productName").GetString()!;
        var fromDate    = DateOnly.Parse(root.GetProperty("fromDate").GetString()!);
        var toDate      = DateOnly.Parse(root.GetProperty("toDate").GetString()!);

        _logger.LogInformation(
            "ProductDetail product={Product} from={From} to={To}", productName, fromDate, toDate);

        await reportProgress(50, $"Filtering rows for '{productName}'…");

        // Case-insensitive product match + date range filter
        var filtered = data.Sales
            .Where(r => r.Date >= fromDate
                     && r.Date <= toDate
                     && string.Equals(r.Product, productName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await reportProgress(70, "Aggregating metrics…");

        // ── Totals ─────────────────────────────────────────────────────────────

        decimal totalRevenue     = filtered.Sum(r => r.Revenue);
        int     totalUnits       = filtered.Sum(r => r.Units);
        int     dayCount         = (int)(toDate.DayNumber - fromDate.DayNumber) + 1;
        decimal avgDailyRevenue  = dayCount > 0 ? Math.Round(totalRevenue / dayCount, 2) : 0m;

        // ── By region ─────────────────────────────────────────────────────────

        var byRegion = filtered
            .GroupBy(r => r.Region)
            .Select(g => new
            {
                name    = g.Key,
                revenue = Math.Round(g.Sum(r => r.Revenue), 2),
                units   = g.Sum(r => r.Units),
            })
            .OrderByDescending(x => x.revenue)
            .ToList();

        // ── Daily trend ────────────────────────────────────────────────────────

        // Pre-index by date
        var byDate = filtered
            .GroupBy(r => r.Date)
            .ToDictionary(g => g.Key, g => (Revenue: g.Sum(r => r.Revenue), Units: g.Sum(r => r.Units)));

        var labels        = new List<string>();
        var revenueSeries = new List<decimal>();
        var unitsSeries   = new List<int>();

        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            labels.Add(d.ToString("yyyy-MM-dd"));

            if (byDate.TryGetValue(d, out var agg))
            {
                revenueSeries.Add(Math.Round(agg.Revenue, 2));
                unitsSeries.Add(agg.Units);
            }
            else
            {
                revenueSeries.Add(0m);
                unitsSeries.Add(0);
            }
        }

        var result = new
        {
            productName     = productName,
            totalRevenue    = Math.Round(totalRevenue, 2),
            totalUnits,
            avgDailyRevenue,
            byRegion,
            trend = new
            {
                labels,
                revenue = revenueSeries,
                units   = unitsSeries,
            },
        };

        sw.Stop();
        _logger.LogInformation(
            "ProductDetail complete — elapsed={Elapsed}ms, rows={Rows}",
            sw.ElapsedMilliseconds, filtered.Count);

        return JsonSerializer.Serialize(result);
    }
}
