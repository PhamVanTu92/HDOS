using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Npgsql;
using StackExchange.Redis;
using Microsoft.Extensions.Hosting;

namespace Providers.Tests.Registry;

// Integration tests — require Docker (PostgreSQL + Redis via Testcontainers).
// Execution deferred to Phase 12 per DECISIONS.md §"Phase 3 integration tests".
public sealed class ProviderRegistryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().Build();
    private readonly RedisContainer _redis = new RedisBuilder().Build();
    private NpgsqlDataSource _db = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_pg.StartAsync(), _redis.StartAsync());
        _db = NpgsqlDataSource.Create(_pg.GetConnectionString());
        await CreateSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await Task.WhenAll(_pg.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    // T7: Integration test — requires Docker (Redis via Testcontainers).
    // Execution deferred to Phase 12 per DECISIONS.md §"Phase 3 integration tests".
    [Fact(Skip = "Requires Docker (PostgreSQL + Redis via Testcontainers). Run manually when Docker is available.")]
    [Trait("Category", "Integration")]
    [Trait("RequiresDocker", "true")]
    public async Task T7_RedisPubSubTriggersReload()
    {
        var spy = new ReloadCounterRegistry();
        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());

        var svc = new OperationRegistryRefreshService(
            spy,
            new NoOpProviderRegistry(),
            mux,
            NullLogger<OperationRegistryRefreshService>.Instance);

        var startCts = new CancellationTokenSource();
        await svc.StartAsync(startCts.Token);

        var reloadsBefore = spy.ReloadCount;
        await mux.GetSubscriber().PublishAsync(
            RedisChannel.Literal("operation-registry:updated"),
            "all");

        // Wait up to 500ms for the reload
        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (spy.ReloadCount == reloadsBefore && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.True(spy.ReloadCount > reloadsBefore,
            "ReloadAsync should have been called within 500ms of pub/sub message");

        await svc.StopAsync(CancellationToken.None);
        mux.Dispose();
    }

    private async Task CreateSchemaAsync()
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS operation_registry (
                id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                operation_pattern   TEXT        NOT NULL,
                handler_type        TEXT        NOT NULL,
                provider_id         TEXT,
                schema_version      TEXT        NOT NULL DEFAULT '1.0',
                params_schema       JSONB,
                payload_schema      JSONB,
                timeout_ms          INT         NOT NULL DEFAULT 30000,
                cacheable           BOOLEAN     NOT NULL DEFAULT FALSE,
                cache_ttl_seconds   INT,
                idempotent          BOOLEAN     NOT NULL DEFAULT TRUE,
                required_role       TEXT,
                status              TEXT        NOT NULL DEFAULT 'active'
                                        CHECK (status IN ('active', 'deprecated', 'disabled')),
                deprecation_message TEXT,
                created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_operation_pattern UNIQUE (operation_pattern)
            );
            CREATE TABLE IF NOT EXISTS provider_registry (
                id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                provider_id         TEXT        NOT NULL,
                display_name        TEXT        NOT NULL,
                description         TEXT,
                client_id           TEXT        NOT NULL,
                client_secret_hash  TEXT        NOT NULL,
                operations          TEXT[]      NOT NULL DEFAULT '{}',
                chart_types         TEXT[]      NOT NULL DEFAULT '{}',
                transformers        TEXT[]      NOT NULL DEFAULT '{}',
                timeout_ms          INT         NOT NULL DEFAULT 30000,
                circuit_breaker     JSONB       NOT NULL DEFAULT '{"failureThreshold":5,"windowSeconds":60,"cooldownSeconds":30}',
                priority            SMALLINT    NOT NULL DEFAULT 5
                                        CHECK (priority BETWEEN 1 AND 10),
                status              TEXT        NOT NULL DEFAULT 'active'
                                        CHECK (status IN ('active', 'suspended', 'maintenance')),
                created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_provider_id UNIQUE (provider_id),
                CONSTRAINT uq_client_id   UNIQUE (client_id)
            )
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class ReloadCounterRegistry : IOperationRegistry
    {
        private int _reloadCount;
        public int ReloadCount => Volatile.Read(ref _reloadCount);

        public Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default) =>
            Task.FromResult<OperationRegistration?>(null);

        public Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OperationRegistration>>([]);

        public Task ReloadAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reloadCount);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpProviderRegistry : IProviderRegistry
    {
        public Task<ProviderRegistration?> GetAsync(string providerId, CancellationToken ct = default) =>
            Task.FromResult<ProviderRegistration?>(null);

        public Task<IReadOnlyList<ProviderRegistration>> GetAllActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProviderRegistration>>([]);

        public Task<bool> ValidateCredentialsAsync(string clientId, string clientSecret, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
