using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportingPlatform.ExcelProvider.Excel;
using ReportingPlatform.Provider.V1;

namespace ReportingPlatform.ExcelProvider.Operations;

/// <summary>
/// Handler for <c>report.dashboard.summary</c>.
/// Reads daily sales data for a given date (default: today) and computes KPIs.
/// </summary>
public sealed class DashboardSummaryHandler : IOperationHandler
{
    private readonly ExcelDataLoader _loader;
    private readonly ILogger<DashboardSummaryHandler> _logger;

    public string OperationPattern => "report.dashboard.summary";

    public DashboardSummaryHandler(ExcelDataLoader loader, ILogger<DashboardSummaryHandler> logger)
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

        await reportProgress(40, "Parsing parameters…");

        // Parse date parameter (default: today)
        DateOnly date;
        if (!string.IsNullOrWhiteSpace(request.ParamsJson))
        {
            using var doc = JsonDocument.Parse(request.ParamsJson);
            if (doc.RootElement.TryGetProperty("date", out var dateProp)
                && !string.IsNullOrWhiteSpace(dateProp.GetString()))
            {
                date = DateOnly.Parse(dateProp.GetString()!);
            }
            else
            {
                date = DateOnly.FromDateTime(DateTime.Today);
            }
        }
        else
        {
            date = DateOnly.FromDateTime(DateTime.Today);
        }

        _logger.LogInformation("DashboardSummary for date={Date}", date);

        await reportProgress(60, $"Aggregating data for {date:yyyy-MM-dd}…");

        var dayRows = data.Sales.Where(r => r.Date == date).ToList();

        decimal totalRevenue = dayRows.Sum(r => r.Revenue);
        int     totalUnits   = dayRows.Sum(r => r.Units);

        // Top region by revenue
        string topRegion = dayRows
            .GroupBy(r => r.Region)
            .OrderByDescending(g => g.Sum(r => r.Revenue))
            .Select(g => g.Key)
            .FirstOrDefault() ?? "N/A";

        // Top product by revenue
        string topProduct = dayRows
            .GroupBy(r => r.Product)
            .OrderByDescending(g => g.Sum(r => r.Revenue))
            .Select(g => g.Key)
            .FirstOrDefault() ?? "N/A";

        // Revenue by channel
        decimal onlineRevenue = dayRows.Where(r => r.Channel == "Online").Sum(r => r.Revenue);
        decimal storeRevenue  = dayRows.Where(r => r.Channel == "Store").Sum(r => r.Revenue);

        // Alerts: products with low/out stock
        var alerts = data.Products
            .Where(p => p.CurrentStock == 0)
            .Select(p => $"STOCK_OUT: {p.Name} has zero stock")
            .Concat(data.Products
                .Where(p => p.CurrentStock > 0 && p.CurrentStock < p.MinStock)
                .Select(p => $"LOW_STOCK: {p.Name} has only {p.CurrentStock} units (min: {p.MinStock})"))
            .ToList();

        if (dayRows.Count == 0)
            alerts.Insert(0, $"NO_DATA: No sales records found for {date:yyyy-MM-dd}");

        await reportProgress(90, "Building response…");

        var result = new
        {
            totalRevenue    = Math.Round(totalRevenue, 2),
            totalUnits,
            topRegion,
            topProduct,
            revenueByChannel = new
            {
                online = Math.Round(onlineRevenue, 2),
                store  = Math.Round(storeRevenue, 2),
            },
            alerts,
        };

        return JsonSerializer.Serialize(result);
    }
}
