using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace ReportingPlatform.ExcelProvider.Excel;

/// <summary>
/// Generates SalesData.xlsx with realistic sample data if the file does not already exist.
/// Sheets:  DailySales  |  Products  |  Regions
/// </summary>
public sealed class SampleDataGenerator
{
    private readonly ILogger<SampleDataGenerator> _logger;

    private static readonly string[] Regions    = ["North", "South", "East", "West", "Central"];
    private static readonly string[] Products   = ["Laptop Pro", "Wireless Mouse", "USB Hub", "Monitor 24\"", "Keyboard MX", "Webcam HD", "SSD 1TB", "RAM 16GB", "Headset Pro", "Desk Lamp"];
    private static readonly string[] Categories = ["Electronics", "Peripherals", "Storage"];
    private static readonly string[] Channels   = ["Online", "Store"];

    // Product → category mapping (index-aligned with Products array)
    private static readonly string[] ProductCategories = ["Electronics", "Peripherals", "Peripherals", "Electronics", "Peripherals", "Peripherals", "Storage", "Storage", "Peripherals", "Electronics"];
    private static readonly decimal[] BaseRevenue       = [1200m, 45m, 35m, 350m, 120m, 80m, 95m, 75m, 150m, 25m];

    public SampleDataGenerator(ILogger<SampleDataGenerator> logger)
    {
        _logger = logger;
    }

    public void GenerateIfMissing(string filePath)
    {
        if (File.Exists(filePath))
        {
            _logger.LogDebug("SalesData.xlsx already exists at {Path} — skipping generation", filePath);
            return;
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _logger.LogInformation("Generating sample SalesData.xlsx at {Path}", filePath);

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using var package = new ExcelPackage();

        WriteDailySalesSheet(package);
        WriteProductsSheet(package);
        WriteRegionsSheet(package);

        package.SaveAs(filePath);

        _logger.LogInformation("Sample data generated successfully");
    }

    // ─── Sheet 1: DailySales ─────────────────────────────────────────────────

    private static void WriteDailySalesSheet(ExcelPackage package)
    {
        var ws = package.Workbook.Worksheets.Add("DailySales");

        // Headers
        ws.Cells[1, 1].Value = "Date";
        ws.Cells[1, 2].Value = "Region";
        ws.Cells[1, 3].Value = "Product";
        ws.Cells[1, 4].Value = "Category";
        ws.Cells[1, 5].Value = "Revenue";
        ws.Cells[1, 6].Value = "Units";
        ws.Cells[1, 7].Value = "Channel";

        StyleHeader(ws, 1, 7);

        var rng   = new Random(42); // fixed seed for reproducibility
        int row   = 2;
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (int dayOffset = 179; dayOffset >= 0; dayOffset--)
        {
            var date = today.AddDays(-dayOffset);

            // ~5-8 rows per day
            int rowsToday = rng.Next(5, 9);
            for (int r = 0; r < rowsToday; r++)
            {
                int  prodIdx  = rng.Next(Products.Length);
                int  region   = rng.Next(Regions.Length);
                int  channel  = rng.Next(Channels.Length);
                int  units    = rng.Next(1, 20);
                // Add upward trend over time: newer dates sell more
                double trendMult = 0.7 + 0.3 * (1.0 - dayOffset / 180.0);
                decimal revenue  = Math.Round(BaseRevenue[prodIdx] * units * (decimal)(trendMult + rng.NextDouble() * 0.4 - 0.2), 2);
                if (revenue < 0) revenue = BaseRevenue[prodIdx];

                ws.Cells[row, 1].Value = date.ToDateTime(TimeOnly.MinValue); // store as DateTime for Excel
                ws.Cells[row, 1].Style.Numberformat.Format = "yyyy-mm-dd";
                ws.Cells[row, 2].Value = Regions[region];
                ws.Cells[row, 3].Value = Products[prodIdx];
                ws.Cells[row, 4].Value = ProductCategories[prodIdx];
                ws.Cells[row, 5].Value = (double)revenue;
                ws.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[row, 6].Value = units;
                ws.Cells[row, 7].Value = Channels[channel];
                row++;
            }
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    // ─── Sheet 2: Products ───────────────────────────────────────────────────

    private static void WriteProductsSheet(ExcelPackage package)
    {
        var ws = package.Workbook.Worksheets.Add("Products");

        ws.Cells[1, 1].Value = "ProductId";
        ws.Cells[1, 2].Value = "Name";
        ws.Cells[1, 3].Value = "Category";
        ws.Cells[1, 4].Value = "Price";
        ws.Cells[1, 5].Value = "CurrentStock";
        ws.Cells[1, 6].Value = "MinStock";

        StyleHeader(ws, 1, 6);

        // Stock levels: some products intentionally low/out
        int[] currentStock = [45, 3, 0, 22, 8, 0, 60, 12, 2, 100];
        int[] minStock      = [20, 10, 5, 10, 10, 5, 30, 20, 10, 50];

        for (int i = 0; i < Products.Length; i++)
        {
            int row = i + 2;
            ws.Cells[row, 1].Value = $"P{i + 1:D3}";
            ws.Cells[row, 2].Value = Products[i];
            ws.Cells[row, 3].Value = ProductCategories[i];
            ws.Cells[row, 4].Value = (double)BaseRevenue[i];
            ws.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
            ws.Cells[row, 5].Value = currentStock[i];
            ws.Cells[row, 6].Value = minStock[i];
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    // ─── Sheet 3: Regions ────────────────────────────────────────────────────

    private static void WriteRegionsSheet(ExcelPackage package)
    {
        var ws = package.Workbook.Worksheets.Add("Regions");

        ws.Cells[1, 1].Value = "RegionId";
        ws.Cells[1, 2].Value = "Name";
        ws.Cells[1, 3].Value = "Manager";
        ws.Cells[1, 4].Value = "MonthlyTarget";
        ws.Cells[1, 5].Value = "YearlyTarget";

        StyleHeader(ws, 1, 5);

        string[] managers       = ["Alice Nguyen", "Bob Tran", "Carol Le", "David Pham", "Eve Vo"];
        decimal[] monthlyTarget = [80_000m, 60_000m, 70_000m, 55_000m, 65_000m];

        for (int i = 0; i < Regions.Length; i++)
        {
            int row = i + 2;
            ws.Cells[row, 1].Value = $"R{i + 1:D2}";
            ws.Cells[row, 2].Value = Regions[i];
            ws.Cells[row, 3].Value = managers[i];
            ws.Cells[row, 4].Value = (double)monthlyTarget[i];
            ws.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
            ws.Cells[row, 5].Value = (double)(monthlyTarget[i] * 12);
            ws.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static void StyleHeader(ExcelWorksheet ws, int headerRow, int lastCol)
    {
        using var range = ws.Cells[headerRow, 1, headerRow, lastCol];
        range.Style.Font.Bold = true;
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.SteelBlue);
        range.Style.Font.Color.SetColor(System.Drawing.Color.White);
    }
}
