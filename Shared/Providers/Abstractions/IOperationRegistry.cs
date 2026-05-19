namespace ReportingPlatform.Providers.Abstractions;

public interface IOperationRegistry
{
    Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default);
    Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default);
    Task ReloadAsync(CancellationToken ct = default);
}
