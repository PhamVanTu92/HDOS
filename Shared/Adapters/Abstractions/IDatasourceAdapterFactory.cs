namespace ReportingPlatform.Adapters.Abstractions;

/// <summary>
/// Resolves the appropriate <see cref="IDatasourceAdapter"/> for a given
/// <see cref="DatasourceDefinition"/>.
/// </summary>
public interface IDatasourceAdapterFactory
{
    IDatasourceAdapter Resolve(DatasourceDefinition definition);
}
