namespace ReportingPlatform.Contracts.Definitions;

public sealed record SchemaDefinition
{
    // "params" | "payload" | "render"
    public required string SchemaType { get; init; }

    public required string SchemaId { get; init; }
    public required string Version { get; init; }
    public required JsonElement Schema { get; init; }
}
