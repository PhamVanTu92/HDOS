using ReportingPlatform.Adapters.Models;
using ReportingPlatform.Contracts.TableParams;

namespace ReportingPlatform.Resolver.Tests.Helpers;

// ---------------------------------------------------------------------------
// Fake IDashboardDefinitionRepository
// ---------------------------------------------------------------------------

internal sealed class FakeDashboardRepo : IDashboardDefinitionRepository
{
    private readonly DashboardDefinition _dashboard;
    private readonly string _version;
    private readonly Dictionary<string, DatasourceDefinition> _datasources;

    public FakeDashboardRepo(
        DashboardDefinition dashboard,
        string version = "1",
        Dictionary<string, DatasourceDefinition>? datasources = null)
    {
        _dashboard   = dashboard;
        _version     = version;
        _datasources = datasources ?? [];
    }

    public Task<(DashboardDefinition Definition, string Version)?> GetAsync(
        string tenantId, string dashboardCode, CancellationToken ct = default) =>
        Task.FromResult<(DashboardDefinition, string)?>( (_dashboard, _version) );

    public Task<IReadOnlyDictionary<string, DatasourceDefinition>> GetDatasourcesAsync(
        string tenantId, IReadOnlyCollection<string> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, DatasourceDefinition>>(_datasources);
}

// ---------------------------------------------------------------------------
// Fake IDatasourceAdapterFactory
// ---------------------------------------------------------------------------

internal sealed class FakeAdapterFactory : IDatasourceAdapterFactory
{
    private readonly IDatasourceAdapter _adapter;

    public FakeAdapterFactory(IDatasourceAdapter adapter) => _adapter = adapter;

    public IDatasourceAdapter Resolve(DatasourceDefinition definition) => _adapter;
}

// ---------------------------------------------------------------------------
// Configurable fake adapter: returns fixed rows or throws
// ---------------------------------------------------------------------------

internal sealed class FakeAdapter : IDatasourceAdapter
{
    private readonly Func<AdapterRequest, Task<AdapterResult>> _handler;

    public FakeAdapter(Func<AdapterRequest, Task<AdapterResult>>? handler = null)
    {
        _handler = handler ?? (_ => Task.FromResult(new AdapterResult
        {
            Rows      = [],
            TotalRows = null,
        }));
    }

    public static FakeAdapter Empty() => new();

    public static FakeAdapter Throwing(string code, string msg) =>
        new(_ => throw new AdapterException(code, msg));

    public static FakeAdapter WithRows(params IReadOnlyDictionary<string, JsonElement>[] rows) =>
        new(_ => Task.FromResult(new AdapterResult
        {
            Rows      = rows,
            TotalRows = rows.Length,
        }));

    public Task<AdapterResult> FetchAsync(AdapterRequest request, CancellationToken ct = default) =>
        _handler(request);
}
