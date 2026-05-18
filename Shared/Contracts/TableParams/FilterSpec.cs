namespace ReportingPlatform.Contracts.TableParams;

public sealed record FilterSpec
{
    public required string Key { get; init; }

    // "=" | "!=" | ">" | ">=" | "<" | "<=" | "in" | "contains"
    public required string Op { get; init; }

    // Scalar or array — JsonElement handles both cases transparently.
    public required JsonElement Value { get; init; }
}
