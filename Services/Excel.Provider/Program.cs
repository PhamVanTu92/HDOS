using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReportingPlatform.ExcelProvider.Config;
using ReportingPlatform.ExcelProvider.Database;
using ReportingPlatform.ExcelProvider.Excel;
using ReportingPlatform.ExcelProvider.Grpc;
using ReportingPlatform.ExcelProvider.Operations;

// ── EPPlus non-commercial licence must be set before any EPPlus call ──────────
OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole(opts =>
        {
            opts.FormatterName = "simple";
        });
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // ── Options ────────────────────────────────────────────────────────────
        services.Configure<ProviderOptions>(config.GetSection(ProviderOptions.SectionName));
        services.Configure<ExcelOptions>(config.GetSection(ExcelOptions.SectionName));

        // ── HttpClient for token endpoint ──────────────────────────────────────
        services.AddHttpClient<TokenService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // ── Excel ──────────────────────────────────────────────────────────────
        services.AddSingleton<SampleDataGenerator>();
        services.AddSingleton<ExcelDataLoader>();

        // ── Operation handlers ─────────────────────────────────────────────────
        services.AddSingleton<IOperationHandler, DashboardSummaryHandler>();
        services.AddSingleton<IOperationHandler, SalesTrendHandler>();
        services.AddSingleton<IOperationHandler, InventoryStatusHandler>();
        services.AddSingleton<IOperationHandler, RegionalPerformanceHandler>();

        // ── Dispatcher ─────────────────────────────────────────────────────────
        services.AddSingleton<OperationDispatcher>();

        // ── gRPC client (BackgroundService) ────────────────────────────────────
        services.AddSingleton<ProviderBridgeClient>();
        services.AddHostedService(sp => sp.GetRequiredService<ProviderBridgeClient>());
    })
    .Build();

// ── Startup tasks ─────────────────────────────────────────────────────────────

var logger  = host.Services.GetRequiredService<ILogger<Program>>();
var excelOpts  = host.Services.GetRequiredService<IOptions<ExcelOptions>>().Value;
var providerOpts = host.Services.GetRequiredService<IOptions<ProviderOptions>>().Value;

// 1. Generate sample Excel data if file does not exist
var generator = host.Services.GetRequiredService<SampleDataGenerator>();
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
var pgConnStr = host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
    .GetConnectionString("Postgres")
    ?? "Host=localhost;Database=hdos;Username=hdos;Password=hdos";

var seeder = new ProviderSeeder(
    pgConnStr,
    host.Services.GetRequiredService<IOptions<ProviderOptions>>(),
    host.Services.GetRequiredService<ILogger<ProviderSeeder>>());

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
    "Excel Provider starting up — providerId={ProviderId}, bridge={Bridge}",
    providerOpts.ProviderId, providerOpts.BridgeGrpcUrl);

await host.RunAsync();
