using Microsoft.Extensions.Configuration;
using OpenTelemetry.Metrics;

namespace ReportingPlatform.Telemetry;

public static class TelemetryExtensions
{
    // Call from each service's Program.cs: builder.Services.AddPlatformTelemetry(builder.Configuration, "Gateway")
    public static IServiceCollection AddPlatformTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var otlpEndpoint = configuration["Telemetry:OtlpEndpoint"] ?? "http://localhost:4317";
        var serviceVersion = configuration["Telemetry:ServiceVersion"] ?? "0.0.0";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes([
                    new("deployment.environment", configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production"),
                ]))
            .WithTracing(tracing => tracing
                .AddSource(ActivitySources.All)
                .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

        return services;
    }

    // Call as: Log.Logger = new LoggerConfiguration().EnrichWithPlatformContext().WriteTo...
    public static LoggerConfiguration EnrichWithPlatformContext(this LoggerConfiguration config) =>
        config
            .Enrich.WithThreadId()
            .Enrich.With<RequestContextEnricher>();
}
