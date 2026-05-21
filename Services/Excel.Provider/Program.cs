using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReportingPlatform.ExcelProvider.Config;
using ReportingPlatform.ExcelProvider.Database;
using ReportingPlatform.ExcelProvider.Excel;
using ReportingPlatform.ExcelProvider.Grpc;
using ReportingPlatform.ExcelProvider.Management;
using ReportingPlatform.ExcelProvider.Operations;
using ReportingPlatform.ExcelProvider.Services;

// ── EPPlus non-commercial licence must be set before any EPPlus call ──────────
OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts => opts.FormatterName = "simple");
builder.Logging.SetMinimumLevel(LogLevel.Information);

var config = builder.Configuration;

// ── Options ───────────────────────────────────────────────────────────────────
builder.Services.Configure<ProviderOptions>(config.GetSection(ProviderOptions.SectionName));
builder.Services.Configure<ExcelOptions>(config.GetSection(ExcelOptions.SectionName));
builder.Services.Configure<IngestionOptions>(config.GetSection(IngestionOptions.Section));

// ── HttpClients ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<TokenService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Named client used by NotificationService to post events to Ingestion API
builder.Services.AddHttpClient("ingestion", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ── Excel ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<SampleDataGenerator>();
builder.Services.AddSingleton<ExcelDataLoader>();

// ── Operation handlers ────────────────────────────────────────────────────────
builder.Services.AddSingleton<IOperationHandler, DashboardSummaryHandler>();
builder.Services.AddSingleton<IOperationHandler, SalesTrendHandler>();
builder.Services.AddSingleton<IOperationHandler, InventoryStatusHandler>();
builder.Services.AddSingleton<IOperationHandler, RegionalPerformanceHandler>();
builder.Services.AddSingleton<IOperationHandler, ChannelComparisonHandler>();
builder.Services.AddSingleton<IOperationHandler, ProductDetailHandler>();
builder.Services.AddSingleton<IOperationHandler, TopPerformersHandler>();

// ── Dispatcher ────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<OperationDispatcher>();

// ── gRPC bridge client (BackgroundService) ────────────────────────────────────
builder.Services.AddSingleton<ProviderBridgeClient>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProviderBridgeClient>());

// ── HTTP Management API ───────────────────────────────────────────────────────
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<DataManagementService>();
builder.Services.AddControllers();

// ── Build app ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Startup tasks ─────────────────────────────────────────────────────────────

var logger       = app.Services.GetRequiredService<ILogger<Program>>();
var excelOpts    = app.Services.GetRequiredService<IOptions<ExcelOptions>>().Value;
var providerOpts = app.Services.GetRequiredService<IOptions<ProviderOptions>>().Value;

// 1. Generate sample Excel data if file does not exist
var generator     = app.Services.GetRequiredService<SampleDataGenerator>();
var excelFilePath = Path.Combine(excelOpts.DataPath, "SalesData.xlsx");
try
{
    generator.GenerateIfMissing(excelFilePath);
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Failed to generate sample Excel data — continuing anyway");
}

// 2. Auto-seed provider and operations in platform DB
var pgConnStr = config.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=hdos;Username=hdos;Password=hdos";

var seeder = new ProviderSeeder(
    pgConnStr,
    app.Services.GetRequiredService<IOptions<ProviderOptions>>(),
    app.Services.GetRequiredService<ILogger<ProviderSeeder>>());

try
{
    await seeder.SeedAsync();
}
catch (Exception ex)
{
    // Non-fatal — provider may already be registered, or DB may be unavailable in dev
    logger.LogWarning(ex, "DB seed failed — continuing without seeding. Provider must be registered manually.");
}

logger.LogInformation(
    "Excel Provider starting — providerId={ProviderId}, bridge={Bridge}, httpPort=5600",
    providerOpts.ProviderId, providerOpts.BridgeGrpcUrl);

// ── HTTP middleware ────────────────────────────────────────────────────────────
app.MapControllers();

// Simple health-check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "excel-provider" }));

await app.RunAsync();
