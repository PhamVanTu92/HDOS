using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OfficeOpenXml;
using ReportingPlatform.ExcelProvider.Config;
using ReportingPlatform.ExcelProvider.Excel;

namespace ReportingPlatform.ExcelProvider.Management;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// A sales row with a 1-based RowIndex that identifies its physical Excel row (row 2 = RowIndex 1).
/// RowIndex is stable as long as no deletions happen above it.
/// </summary>
public sealed record SaleRecord(
    int     RowIndex,
    string  Date,
    string  Region,
    string  Product,
    string  Category,
    decimal Revenue,
    int     Units,
    string  Channel);

public sealed record ProductRecord(
    string  ProductId,
    string  Name,
    string  Category,
    decimal Price,
    int     CurrentStock,
    int     MinStock,
    string  Status);

public sealed record CreateSaleRequest(
    string  Date,
    string  Region,
    string  Product,
    string  Category,
    decimal Revenue,
    int     Units,
    string  Channel);

public sealed record UpdateSaleRequest(
    string?  Region,
    string?  Product,
    string?  Category,
    decimal? Revenue,
    int?     Units,
    string?  Channel);

// ─── Service ──────────────────────────────────────────────────────────────────

/// <summary>
/// Reads and writes the Excel file directly via EPPlus, then invalidates the
/// <see cref="ExcelDataLoader"/> cache so the next gRPC query sees fresh data.
/// All methods are thread-safe via a shared <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class DataManagementService
{
    // One writer at a time; reads under the same lock for consistency.
    private readonly SemaphoreSlim        _lock = new(1, 1);
    private readonly ExcelDataLoader      _loader;
    private readonly ExcelOptions         _excelOpts;
    private readonly ILogger<DataManagementService> _logger;

    // Operations that should trigger WidgetStale after a data change.
    public static readonly string[] SalesOperations =
    [
        "report.dashboard.summary",
        "report.sales.trend",
        "report.regional.performance",
    ];

    public static readonly string[] InventoryOperations =
    [
        "report.dashboard.summary",
        "report.inventory.status",
    ];

    public DataManagementService(
        ExcelDataLoader                  loader,
        IOptions<ExcelOptions>           excelOpts,
        ILogger<DataManagementService>   logger)
    {
        _loader    = loader;
        _excelOpts = excelOpts.Value;
        _logger    = logger;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private string FilePath => Path.Combine(_excelOpts.DataPath, "SalesData.xlsx");

    private static string StockStatus(int current, int min) =>
        current == 0 ? "Out of Stock"
        : current < min ? "Low Stock"
        : "In Stock";

    // ─── Sales: GET ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all sales rows, optionally filtered by date (yyyy-MM-dd) and/or region.
    /// </summary>
    public async Task<List<SaleRecord>> GetSalesAsync(
        string?           date,
        string?           region,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var pkg = new ExcelPackage(FilePath);
            var ws  = GetSheet(pkg, "DailySales");
            int rows = ws.Dimension?.Rows ?? 1;

            var result = new List<SaleRecord>(rows);
            for (int r = 2; r <= rows; r++)
            {
                var dateStr = ParseDateStr(ws.Cells[r, 1].Value);
                if (dateStr is null) continue;

                if (date   is not null && !dateStr.StartsWith(date, StringComparison.OrdinalIgnoreCase)) continue;
                if (region is not null
                    && !string.Equals(ws.Cells[r, 2].GetValue<string>(), region, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(RowToSaleRecord(ws, r));
            }

            _logger.LogInformation(
                "GetSales — date={Date}, region={Region}, returned {Count} rows",
                date, region, result.Count);
            return result;
        }
        finally { _lock.Release(); }
    }

    // ─── Sales: ADD ───────────────────────────────────────────────────────────

    public async Task<SaleRecord> AddSaleAsync(
        CreateSaleRequest req,
        CancellationToken ct = default)
    {
        ValidateSaleRequest(req.Revenue, req.Units);

        await _lock.WaitAsync(ct);
        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var pkg = new ExcelPackage(FilePath);
            var ws  = GetSheet(pkg, "DailySales");
            int newRow = (ws.Dimension?.Rows ?? 1) + 1;

            WriteSaleRow(ws, newRow, req.Date, req.Region, req.Product,
                         req.Category, req.Revenue, req.Units, req.Channel);

            pkg.Save();
            _loader.InvalidateCache();

            var record = RowToSaleRecord(ws, newRow);
            _logger.LogInformation("Sale added at row {Row}: {Record}", newRow, record);
            return record;
        }
        finally { _lock.Release(); }
    }

    // ─── Sales: UPDATE ────────────────────────────────────────────────────────

    /// <param name="rowIndex">1-based logical row index (RowIndex in <see cref="SaleRecord"/>).</param>
    public async Task<SaleRecord> UpdateSaleAsync(
        int               rowIndex,
        UpdateSaleRequest req,
        CancellationToken ct = default)
    {
        if (req.Revenue.HasValue) ValidateSaleRequest(req.Revenue.Value, req.Units ?? 0);

        await _lock.WaitAsync(ct);
        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var pkg = new ExcelPackage(FilePath);
            var ws      = GetSheet(pkg, "DailySales");
            int physRow = rowIndex + 1;   // row 2 = rowIndex 1
            ValidateRowExists(ws, physRow);

            if (req.Region   is not null) ws.Cells[physRow, 2].Value = req.Region;
            if (req.Product  is not null) ws.Cells[physRow, 3].Value = req.Product;
            if (req.Category is not null) ws.Cells[physRow, 4].Value = req.Category;
            if (req.Revenue.HasValue)
            {
                ws.Cells[physRow, 5].Value = (double)req.Revenue.Value;
                ws.Cells[physRow, 5].Style.Numberformat.Format = "#,##0.00";
            }
            if (req.Units.HasValue) ws.Cells[physRow, 6].Value = req.Units.Value;
            if (req.Channel  is not null) ws.Cells[physRow, 7].Value = req.Channel;

            pkg.Save();
            _loader.InvalidateCache();

            var record = RowToSaleRecord(ws, physRow);
            _logger.LogInformation("Sale updated at row {Row}: {Record}", physRow, record);
            return record;
        }
        finally { _lock.Release(); }
    }

    // ─── Sales: DELETE ────────────────────────────────────────────────────────

    public async Task DeleteSaleAsync(int rowIndex, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var pkg = new ExcelPackage(FilePath);
            var ws      = GetSheet(pkg, "DailySales");
            int physRow = rowIndex + 1;
            ValidateRowExists(ws, physRow);

            ws.DeleteRow(physRow);
            pkg.Save();
            _loader.InvalidateCache();

            _logger.LogInformation("Sale deleted — rowIndex={RowIndex} (physRow={PhysRow})",
                rowIndex, physRow);
        }
        finally { _lock.Release(); }
    }

    // ─── Products: GET ────────────────────────────────────────────────────────

    public async Task<List<ProductRecord>> GetProductsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var pkg = new ExcelPackage(FilePath);
            var ws   = GetSheet(pkg, "Products");
            int rows = ws.Dimension?.Rows ?? 1;

            var result = new List<ProductRecord>(rows);
            for (int r = 2; r <= rows; r++)
            {
                var name = ws.Cells[r, 2].GetValue<string>();
                if (string.IsNullOrEmpty(name)) continue;
                result.Add(RowToProductRecord(ws, r));
            }

            _logger.LogInformation("GetProducts — returned {Count} rows", result.Count);
            return result;
        }
        finally { _lock.Release(); }
    }

    // ─── Products: UPDATE STOCK ───────────────────────────────────────────────

    public async Task<ProductRecord> UpdateProductStockAsync(
        string            productId,
        int               newStock,
        CancellationToken ct = default)
    {
        if (newStock < 0)
            throw new ArgumentOutOfRangeException(nameof(newStock), "Stock cannot be negative.");

        await _lock.WaitAsync(ct);
        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var pkg = new ExcelPackage(FilePath);
            var ws   = GetSheet(pkg, "Products");
            int rows = ws.Dimension?.Rows ?? 1;

            for (int r = 2; r <= rows; r++)
            {
                var id = ws.Cells[r, 1].GetValue<string>();
                if (!string.Equals(id, productId, StringComparison.OrdinalIgnoreCase)) continue;

                ws.Cells[r, 5].Value = newStock;
                pkg.Save();
                _loader.InvalidateCache();

                var record = RowToProductRecord(ws, r);
                _logger.LogInformation(
                    "Product stock updated — productId={ProductId}, newStock={Stock}",
                    productId, newStock);
                return record;
            }

            throw new KeyNotFoundException($"Product '{productId}' not found.");
        }
        finally { _lock.Release(); }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static ExcelWorksheet GetSheet(ExcelPackage pkg, string name) =>
        pkg.Workbook.Worksheets[name]
        ?? throw new InvalidOperationException($"Sheet '{name}' not found in SalesData.xlsx");

    private static void ValidateRowExists(ExcelWorksheet ws, int physRow)
    {
        int totalRows = ws.Dimension?.Rows ?? 1;
        if (physRow < 2 || physRow > totalRows)
            throw new ArgumentOutOfRangeException(
                nameof(physRow), $"Row {physRow - 1} does not exist (total data rows: {totalRows - 1}).");
    }

    private static void ValidateSaleRequest(decimal revenue, int units)
    {
        if (revenue <= 0)
            throw new ArgumentOutOfRangeException(nameof(revenue), "Revenue must be greater than 0.");
        if (units < 0)
            throw new ArgumentOutOfRangeException(nameof(units), "Units cannot be negative.");
    }

    private static string? ParseDateStr(object? cellValue) =>
        cellValue switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd"),
            double d    => DateTime.FromOADate(d).ToString("yyyy-MM-dd"),
            string s    => s,
            _           => null
        };

    private static SaleRecord RowToSaleRecord(ExcelWorksheet ws, int r) =>
        new(
            RowIndex: r - 1,
            Date:     ParseDateStr(ws.Cells[r, 1].Value) ?? string.Empty,
            Region:   ws.Cells[r, 2].GetValue<string>() ?? string.Empty,
            Product:  ws.Cells[r, 3].GetValue<string>() ?? string.Empty,
            Category: ws.Cells[r, 4].GetValue<string>() ?? string.Empty,
            Revenue:  (decimal)(ws.Cells[r, 5].GetValue<double>()),
            Units:    ws.Cells[r, 6].GetValue<int>(),
            Channel:  ws.Cells[r, 7].GetValue<string>() ?? string.Empty);

    private static ProductRecord RowToProductRecord(ExcelWorksheet ws, int r)
    {
        int current = ws.Cells[r, 5].GetValue<int>();
        int min     = ws.Cells[r, 6].GetValue<int>();
        return new(
            ProductId:    ws.Cells[r, 1].GetValue<string>() ?? string.Empty,
            Name:         ws.Cells[r, 2].GetValue<string>() ?? string.Empty,
            Category:     ws.Cells[r, 3].GetValue<string>() ?? string.Empty,
            Price:        (decimal)(ws.Cells[r, 4].GetValue<double>()),
            CurrentStock: current,
            MinStock:     min,
            Status:       StockStatus(current, min));
    }

    private static void WriteSaleRow(
        ExcelWorksheet ws,
        int row,
        string date, string region, string product, string category,
        decimal revenue, int units, string channel)
    {
        // Try to parse date; store as DateTime for Excel OA date compatibility
        if (DateTime.TryParse(date, out var dt))
        {
            ws.Cells[row, 1].Value = dt;
            ws.Cells[row, 1].Style.Numberformat.Format = "yyyy-mm-dd";
        }
        else
        {
            ws.Cells[row, 1].Value = date;
        }

        ws.Cells[row, 2].Value = region;
        ws.Cells[row, 3].Value = product;
        ws.Cells[row, 4].Value = category;
        ws.Cells[row, 5].Value = (double)revenue;
        ws.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
        ws.Cells[row, 6].Value = units;
        ws.Cells[row, 7].Value = channel;
    }
}
