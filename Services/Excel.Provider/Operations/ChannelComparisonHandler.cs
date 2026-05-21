using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportingPlatform.ExcelProvider.Excel;
using ReportingPlatform.Provider.V1;

namespace ReportingPlatform.ExcelProvider.Operations;

/// <summary>
/// Handler for <c>report.channel.comparison</c>.
/// Compares Online vs Store channel performance for a given date range.
/// </summary>
public sealed class ChannelComparisonHandler : IOperationHandler
{
    private readonly ExcelDataLoader _loader;
    private readonly ILogger<ChannelComparisonHandler> _logger;

    public string OperationPattern => "report.channel.comparison";

    public ChannelComparisonHandler(ExcelDataLoader loader, ILogger<ChannelComparisonHandler> logger)
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
        _logger.LogInformation("ChannelComparison starting — requestId={RequestId}", request.RequestId);

        await reportProgress(10, "Loading Excel data…");
        var data = await _loader.GetDataAsync(ct);

        await reportProgress(25, "Parsing parameters…");

        using var doc  = JsonDocument.Parse(request.ParamsJson ?? "{}");
        var root       = doc.RootElement;
        var fromDate   = DateOnly.Parse(root.GetProperty("fromDate").GetString()!);
        var toDate     = DateOnly.Parse(root.GetProperty("toDate").GetString()!);

        _logger.LogInformation(
            "ChannelComparison from={From} to={To}", fromDate, toDate);

        await reportProgress(40, $"Filtering rows from {fromDate} to {toDate}…");

        var filtered = data.Sales
            .Where(r => r.Date >= fromDate && r.Date <= toDate)
            .ToList();

        // ── Aggregation ────────────────────────────────────────────────────────

        decimal onlineRevenue = filtered.Where(r => r.Channel == "Online").Sum(r => r.Revenue);
        int     onlineUnits   = filtered.Where(r => r.Channel == "Online").Sum(r => r.Units);
        decimal storeRevenue  = filtered.Where(r => r.Channel == "Store").Sum(r => r.Revenue);
        int     storeUnits    = filtered.Where(r => r.Channel == "Store").Sum(r => r.Units);

        decimal totalRevenue = onlineRevenue + storeRevenue;
        double  onlinePct    = totalRevenue == 0 ? 0 : Math.Round((double)(onlineRevenue / totalRevenue * 100), 1);
        double  storePct     = totalRevenue == 0 ? 0 : Math.Round((double)(storeRevenue  / totalRevenue * 100), 1);

        await reportProgress(50, "Aggregation complete, building trend series…");

        // ── Daily trend ────────────────────────────────────────────────────────

        // Build a sorted list of every date in the range
        var labels       = new List<string>();
        var onlineSeries = new List<decimal>();
        var storeSeries  = new List<decimal>();

        // Pre-index revenue by (date, channel) for O(n) lookup
        var byDateChannel = filtered
            .GroupBy(r => (r.Date, r.Channel))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Revenue));

        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var label = d.ToString("yyyy-MM-dd");
            labels.Add(label);

            byDateChannel.TryGetValue((d, "Online"), out var oRev);
            byDateChannel.TryGetValue((d, "Store"),  out var sRev);
            onlineSeries.Add(Math.Round(oRev, 2));
            storeSeries.Add(Math.Round(sRev, 2));
        }

        await reportProgress(80, "Building response…");

        var result = new
        {
            online = new
            {
                revenue    = Math.Round(onlineRevenue, 2),
                units      = onlineUnits,
                percentage = onlinePct,
            },
            store = new
            {
                revenue    = Math.Round(storeRevenue, 2),
                units      = storeUnits,
                percentage = storePct,
            },
            trend = new
            {
                labels,
                online = onlineSeries,
                store  = storeSeries,
            },
        };

        sw.Stop();
        _logger.LogInformation(
            "ChannelComparison complete — elapsed={Elapsed}ms, rows={Rows}",
            sw.ElapsedMilliseconds, filtered.Count);

        return JsonSerializer.Serialize(result);
    }
}
