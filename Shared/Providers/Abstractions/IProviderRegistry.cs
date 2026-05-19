namespace ReportingPlatform.Providers.Abstractions;

public interface IProviderRegistry
{
    Task<ProviderRegistration?> GetAsync(string providerId, CancellationToken ct = default);
    Task<IReadOnlyList<ProviderRegistration>> GetAllActiveAsync(CancellationToken ct = default);
    Task<bool> ValidateCredentialsAsync(string clientId, string clientSecret, CancellationToken ct = default);
    Task ReloadAsync(CancellationToken ct = default);
}
