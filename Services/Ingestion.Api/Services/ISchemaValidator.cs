namespace ReportingPlatform.IngestionApi.Services;

// Public so MVC can resolve it into a public controller constructor (CS0051).
// InternalsVisibleTo allows NSubstitute to substitute it in Ingestion.Tests.
public interface ISchemaValidator
{
    /// <summary>
    /// Returns null if validation passes (or no schema is registered).
    /// Returns a validation error message if the payload fails schema validation.
    /// </summary>
    Task<string?> ValidateAsync(
        string tenantId, string eventType, JsonElement payload, CancellationToken ct = default);
}
