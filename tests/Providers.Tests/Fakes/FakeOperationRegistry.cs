using ReportingPlatform.Providers.Matching;
using ReportingPlatform.Providers.Registry;

namespace Providers.Tests.Fakes;

// Mirrors the exact snapshot + volatile pattern used by PostgresOperationRegistry.
// The field is NOT marked volatile; Volatile.Read/Write are used exclusively (avoids CS0420).
internal sealed class FakeOperationRegistry : IOperationRegistry
{
    private RegistrySnapshot _snapshot = RegistrySnapshot.Empty;

    public Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default)
    {
        var snap = Volatile.Read(ref _snapshot);
        return Task.FromResult(WildcardMatcher.Resolve(operation, snap.All));
    }

    public Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default)
    {
        var snap = Volatile.Read(ref _snapshot);
        return Task.FromResult(snap.All);
    }

    public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;

    // Atomically replaces the in-memory snapshot — same pattern as PostgresOperationRegistry.ReloadAsync.
    public void SeedSnapshot(IReadOnlyList<OperationRegistration> registrations)
    {
        var dict = registrations.ToDictionary(r => r.OperationPattern, StringComparer.Ordinal);
        Volatile.Write(ref _snapshot, new RegistrySnapshot(dict, registrations));
    }
}
