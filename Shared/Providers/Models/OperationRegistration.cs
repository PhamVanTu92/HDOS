namespace ReportingPlatform.Providers.Models;

public sealed record OperationRegistration
{
    public required string OperationPattern { get; init; }
    public required string HandlerType { get; init; }
    public string? ProviderId { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public JsonElement? ParamsSchema { get; init; }
    public int TimeoutMs { get; init; } = 30_000;
    public bool Cacheable { get; init; }
    public int? CacheTtlSeconds { get; init; }
    public bool Idempotent { get; init; } = true;
    public string? RequiredRole { get; init; }
    public string Status { get; init; } = "active";
    public string? DeprecationMessage { get; init; }

    // Compiled at load time during snapshot build; null when ParamsSchema is null.
    public JsonSchema? CompiledSchema { get; init; }
}
