using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OfficeOpenXml;
using ReportingPlatform.ExcelProvider.Config;

namespace ReportingPlatform.ExcelProvider.Excel;

// ─── Domain models ────────────────────────────────────────────────────────────

public sealed record SalesRow(
    DateOnly   Date,
    string     Region,
    string     Product,
    string     Category,
    decimal    Revenue,
    int        Units,
    string     Channel);

public sealed record ProductRow(
    string  ProductId,
    string  Name,
    string  Category,
    decimal Price,
    int     CurrentStock,
    int     MinStock);

public sealed record RegionRow(
    string  RegionId,
    string  Name,
    string  Manager,
    decimal MonthlyTarget,
    decimal YearlyTarget);

public sealed record ExcelDataSet(
    IReadOnlyList<SalesRow>   Sales,
    IReadOnlyList<ProductRow> Products,
    IReadOnlyList<RegionRow>  Regions,
    DateTime                  LoadedAt);

// ─── Loader / cache ───────────────────────────────────────────────────────────

/// <summary>
/// Reads SalesData.xlsx and caches the result in memory.
/// Cache is invalidated after <see cref="ExcelOptions.CacheMinutes"/> minutes.
/// Thread-safe via <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class ExcelDataLoader
{
    private readonly ExcelOptions _opts;
    private readonly ILogger<ExcelDataLoader> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private ExcelDataSet? _cache;

    public ExcelDataLoader(IOptions<ExcelOptions> opts, ILogger<ExcelDataLoader> logger)
    {
        _opts   = opts.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the cached dataset, reloading from disk if the cache has expired.
    /// </summary>
    public async Task<ExcelDataSet> GetDataAsync(CancellationToken ct = default)
    {
        // Fast path — read without lock if cache is still valid
        var cached = _cache;
        if (cached is not null && (DateTime.UtcNow - cached.LoadedAt).TotalMinutes < _opts.CacheMinutes)
            return cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            cached = _cache;
            if (cached is not null && (DateTime.UtcNow - cached.LoadedAt).TotalMinutes < _opts.CacheMinutes)
                return cached;

            _cache = await LoadFromDiskAsync(ct);
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Forces a reload on the next call to <see cref="GetDataAsync"/>.</summary>
    public void Invalidate() => _cache = null;

    // ─── Disk I/O ─────────────────────────────────────────────────────────────

    private Task<ExcelDataSet> LoadFromDiskAsync(CancellationToken ct)
    {
        // EPPlus is synchronous; run on thread pool to avoid blocking the calling async context
        return Task.Run(() =>
        {
            var filePath = Path.Combine(_opts.DataPath, "SalesData.xlsx");
            _logger.LogInformation("Loading Excel data from {Path}", filePath);

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage(filePath);

            var sales    = ReadSalesSheet(package);
            var products = ReadProductsSheet(package);
            var regions  = ReadRegionsSheet(package);

            _logger.LogInformation(
                "Excel load complete — {SalesCount} sales rows, {ProdCount} products, {RegCount} regions",
                sales.Count, products.Count, regions.Count);

            return new ExcelDataSet(sales, products, regions, DateTime.UtcNow);
        }, ct);
    }

    // ─── Sheet readers ────────────────────────────────────────────────────────

    private static List<SalesRow> ReadSalesSheet(ExcelPackage package)
    {
        var ws   = package.Workbook.Worksheets["DailySales"]
                   ?? throw new InvalidOperationException("Sheet 'DailySales' not found in SalesData.xlsx");
        int rows = ws.Dimension?.Rows ?? 1;
        var list = new List<SalesRow>(rows);

        for (int r = 2; r <= rows; r++)
        {
            var dateVal = ws.Cells[r, 1].Value;
            if (dateVal is null) continue;

            DateOnly date;
            if (dateVal is DateTime dt)
                date = DateOnly.FromDateTime(dt);
            else if (dateVal is double d)
                date = DateOnly.FromDateTime(DateTime.FromOADate(d));
            else if (DateOnly.TryParse(dateVal.ToString(), out var parsed))
                date = parsed;
            else
                continue;

            list.Add(new SalesRow(
                Date:     date,
                Region:   ws.Cells[r, 2].GetValue<string>() ?? string.Empty,
                Product:  ws.Cells[r, 3].GetValue<string>() ?? string.Empty,
                Category: ws.Cells[r, 4].GetValue<string>() ?? string.Empty,
                Revenue:  (decimal)(ws.Cells[r, 5].GetValue<double>()),
                Units:    ws.Cells[r, 6].GetValue<int>(),
                Channel:  ws.Cells[r, 7].GetValue<string>() ?? string.Empty));
        }

        return list;
    }

    private static List<ProductRow> ReadProductsSheet(ExcelPackage package)
    {
        var ws   = package.Workbook.Worksheets["Products"]
                   ?? throw new InvalidOperationException("Sheet 'Products' not found in SalesData.xlsx");
        int rows = ws.Dimension?.Rows ?? 1;
        var list = new List<ProductRow>(rows);

        for (int r = 2; r <= rows; r++)
        {
            var name = ws.Cells[r, 2].GetValue<string>();
            if (string.IsNullOrEmpty(name)) continue;

            list.Add(new ProductRow(
                ProductId:    ws.Cells[r, 1].GetValue<string>() ?? string.Empty,
                Name:         name,
                Category:     ws.Cells[r, 3].GetValue<string>() ?? string.Empty,
                Price:        (decimal)(ws.Cells[r, 4].GetValue<double>()),
                CurrentStock: ws.Cells[r, 5].GetValue<int>(),
                MinStock:     ws.Cells[r, 6].GetValue<int>()));
        }

        return list;
    }

    private static List<RegionRow> ReadRegionsSheet(ExcelPackage package)
    {
        var ws   = package.Workbook.Worksheets["Regions"]
                   ?? throw new InvalidOperationException("Sheet 'Regions' not found in SalesData.xlsx");
        int rows = ws.Dimension?.Rows ?? 1;
        var list = new List<RegionRow>(rows);

        for (int r = 2; r <= rows; r++)
        {
            var name = ws.Cells[r, 2].GetValue<string>();
            if (string.IsNullOrEmpty(name)) continue;

            list.Add(new RegionRow(
                RegionId:      ws.Cells[r, 1].GetValue<string>() ?? string.Empty,
                Name:          name,
                Manager:       ws.Cells[r, 3].GetValue<string>() ?? string.Empty,
                MonthlyTarget: (decimal)(ws.Cells[r, 4].GetValue<double>()),
                YearlyTarget:  (decimal)(ws.Cells[r, 5].GetValue<double>())));
        }

        return list;
    }
}
