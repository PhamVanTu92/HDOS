using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportingPlatform.ExcelProvider.Excel;
using ReportingPlatform.Provider.V1;

namespace ReportingPlatform.ExcelProvider.Operations;

/// <summary>
/// Handler for <c>report.inventory.status</c>.
/// Reads the Products sheet and classifies each product as ok / low / out.
/// </summary>
public sealed class InventoryStatusHandler : IOperationHandler
{
    private readonly ExcelDataLoader _loader;
    private readonly ILogger<InventoryStatusHandler> _logger;

    public string OperationPattern => "report.inventory.status";

    public InventoryStatusHandler(ExcelDataLoader loader, ILogger<InventoryStatusHandler> logger)
    {
        _loader = loader;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(
        OperationRequest request,
        Func<int, string, Task> reportProgress,
        CancellationToken ct)
    {
        await reportProgress(20, "Loading product data…");
        var data = await _loader.GetDataAsync(ct);

        _logger.LogInformation("InventoryStatus for {Count} products", data.Products.Count);

        await reportProgress(70, "Classifying stock levels…");

        var products = data.Products.Select(p =>
        {
            string status = p.CurrentStock == 0    ? "out"
                          : p.CurrentStock < p.MinStock ? "low"
                          :                              "ok";
            return new
            {
                name     = p.Name,
                category = p.Category,
                stock    = p.CurrentStock,
                status,
            };
        }).ToList();

        int okCount  = products.Count(p => p.status == "ok");
        int lowCount = products.Count(p => p.status == "low");
        int outCount = products.Count(p => p.status == "out");

        await reportProgress(90, "Building response…");

        var result = new
        {
            products,
            summary = new { ok = okCount, low = lowCount, @out = outCount },
        };

        return JsonSerializer.Serialize(result);
    }
}
