using Testcontainers.PostgreSql;
using Npgsql;

namespace Providers.Tests.Registry;

// T8: Integration test — requires Docker (PostgreSQL via Testcontainers).
// Execution deferred to Phase 12 per DECISIONS.md §"Phase 3 integration tests".
public sealed class OperationRegistryReloadTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().Build();
    private NpgsqlDataSource _db = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _db = NpgsqlDataSource.Create(_pg.GetConnectionString());
        await CreateSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    // T8: Invalid schema in DB — graceful skip, valid registrations still reachable
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("RequiresDocker", "true")]
    public async Task T8_InvalidSchemaInDb_GracefulSkip_ValidRegistrationsReachable()
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO operation_registry (operation_pattern, handler_type, params_schema)
            VALUES
              ('op.valid',   'internal', '{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object"}'),
              ('op.invalid', 'internal', 'not valid json schema {{{{')
            """;
        await cmd.ExecuteNonQueryAsync();

        var registry = new PostgresOperationRegistry(_db, NullLogger<PostgresOperationRegistry>.Instance);

        var ex = await Record.ExceptionAsync(() => registry.ReloadAsync());
        Assert.Null(ex);

        var valid = await registry.ResolveAsync("op.valid");
        Assert.NotNull(valid);
        Assert.Equal("op.valid", valid!.OperationPattern);

        // The invalid-schema registration loads as an active registration but with CompiledSchema = null
        // (schema compilation was skipped, not the whole row).
        var invalid = await registry.ResolveAsync("op.invalid");
        Assert.NotNull(invalid);
        Assert.Null(invalid!.CompiledSchema);
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
            )
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
