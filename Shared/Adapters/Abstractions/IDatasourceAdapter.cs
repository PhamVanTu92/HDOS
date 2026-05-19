namespace ReportingPlatform.Adapters.Abstractions;

public interface IDatasourceAdapter
{
    Task<AdapterResult> FetchAsync(AdapterRequest request, CancellationToken ct = default);
}
