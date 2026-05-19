using DotnetProviderSample.Handlers;
using DotnetProviderSample.SelfRegistration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ReportingPlatform.ProviderSdk;

var builder = WebApplication.CreateBuilder(args);

// ----- Provider SDK -----
// Capture a logger factory reference after the app is built (set below).
ILoggerFactory? loggerFactory = null;

builder.Services
    .AddProviderSdk(opts =>
    {
        var config = builder.Configuration;
        opts.ProviderId     = config["Provider:ProviderId"]!;
        opts.ClientId       = config["Provider:ClientId"]!;
        opts.ClientSecret   = config["Provider:ClientSecret"]!;
        opts.TokenEndpoint  = new Uri(config["Provider:TokenEndpoint"]!);
        opts.BridgeEndpoint = new Uri(config["Provider:BridgeEndpoint"]!);
        opts.Version        = config["Provider:Version"] ?? "1.0.0";
    })
    .Handle<FraudScoreParams, FraudScoreResult>("ml.fraud.score")
    .Handle<BatchScoreParams, BatchScoreResult>("ml.fraud.batchScore")
    .OnCredentialsRevoked(() =>
    {
        var log = loggerFactory?.CreateLogger<Program>();
        log?.LogCritical("Credentials revoked — stopping host");
    });

// ----- Handlers (registered as interface so SDK DI resolution works) -----
builder.Services.AddScoped<IOperationHandler<FraudScoreParams, FraudScoreResult>, FraudScoreHandler>();
builder.Services.AddScoped<IOperationHandler<BatchScoreParams, BatchScoreResult>, BatchScoreHandler>();

// ----- Self-registrar -----
builder.Services.AddHostedService<ProviderSelfRegistrar>();

// ----- Health + HTTP client -----
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();

// Configure Kestrel to listen on port 8080 (consistent with healthcheck)
builder.WebHost.UseUrls("http://+:8080");

var app = builder.Build();

// Wire up the logger factory captured by the OnCredentialsRevoked callback.
loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

app.MapHealthChecks("/health");

app.Run();
