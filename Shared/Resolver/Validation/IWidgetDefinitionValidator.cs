using ReportingPlatform.Contracts.Validation;

namespace ReportingPlatform.Resolver.Validation;

public interface IWidgetDefinitionValidator
{
    /// <summary>
    /// Validates all widgets in <paramref name="dashboard"/> against rules R1–R9.
    /// Returns aggregate <see cref="ValidationResult"/> (all errors, not fail-fast).
    /// </summary>
    Task<ValidationResult> ValidateAsync(
        string tenantId,
        DashboardDefinition dashboard,
        IReadOnlyDictionary<string, DatasourceDefinition> datasources,
        CancellationToken ct = default);
}
