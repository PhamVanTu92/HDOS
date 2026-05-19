# PHASE_3_PLAN.md — Operation Registry & Provider Registry
> Status: APPROVED | Author: Claude Sonnet 4.6 | Date: 2026-05-18

Phase 3 builds the runtime registry that maps operation patterns to providers and validates request parameters against JSON Schemas. It is the gatekeeper between `RequestSubmissionService` and the RabbitMQ queues.

---

## 0. CVE scan (pre-phase gate)

```
dotnet list package --vulnerable --include-transitive
```
Result at plan time: **0 vulnerable packages** across all 4 Shared projects. ✓

---

## 1. Build order and rationale

```
Shared/Contracts        (Phase 2 — done)
       ↓
Shared/Providers        (Phase 3 — this phase)
       ↓
Services/Gateway        (Phase 5 — uses IOperationRegistry for routing)
Services/Resolver       (Phase 6 — uses IOperationRegistry for dashboard fan-out)
```

`Shared/Providers` depends only on `Shared/Contracts` (for `IParamsValidator`, `ValidationResult`, `ValidationError`) and on PostgreSQL + Redis clients. No service projects depend on it yet.

---

## 2. Project structure — `Shared/Providers/`

```
Shared/Providers/
│
├── Providers.csproj
├── GlobalUsings.cs
│
├── Abstractions/
│   ├── IOperationRegistry.cs
│   ├── IProviderRegistry.cs
│   └── IParamsValidator.cs           ← implementation of Contracts interface
│
├── Models/
│   ├── OperationRegistration.cs      ← in-memory model (hydrated from DB row)
│   ├── ProviderRegistration.cs       ← in-memory model (hydrated from DB row)
│   └── CircuitBreakerConfig.cs       ← sub-model for ProviderRegistration
│
├── Registry/
│   ├── PostgresOperationRegistry.cs
│   ├── PostgresProviderRegistry.cs
│   └── OperationRegistryRefreshService.cs   ← IHostedService, Redis pub/sub
│
├── Matching/
│   └── WildcardMatcher.cs            ← "most specific wins" pattern matching
│
├── Validation/
│   └── JsonSchemaParamsValidator.cs  ← JsonSchema.Net, compile-at-load
│
└── Extensions/
    └── ProvidersExtensions.cs        ← AddPlatformProviders() DI registration
```

### 2.2 Configuration

`Shared/Providers` reads two configuration keys injected via `IConfiguration`:

| Key | Type | Usage |
|---|---|---|
| `Database:Registry` | `string` (connection string) | `NpgsqlDataSource` singleton for registry queries |
| `Redis:Configuration` | `string` | Reuse the `IConnectionMultiplexer` already registered by `Shared/Caching` |

`NpgsqlDataSource` is registered as a singleton in `ProvidersExtensions.AddPlatformProviders()` using `NpgsqlDataSourceBuilder`. Services that need PostgreSQL inject `NpgsqlDataSource` directly — no `IDbConnectionFactory` wrapper.

`IConnectionMultiplexer` is NOT re-registered by `Shared/Providers`; it relies on `Shared/Caching`'s `AddPlatformCaching()` having been called first (enforced by ordering in each service's `Program.cs`).

---

### 2.1 Project file (`Providers.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RootNamespace>ReportingPlatform.Providers</RootNamespace>
    <AssemblyName>ReportingPlatform.Providers</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Contracts\Contracts.csproj" />
    <PackageReference Include="Npgsql" Version="9.0.x" />
    <PackageReference Include="JsonSchema.Net" Version="7.x.x" />
    <PackageReference Include="StackExchange.Redis" Version="2.7.33" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="9.0.x" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.x" />
  </ItemGroup>
</Project>
```

**No EF Core** — direct Npgsql with `NpgsqlDataSource`. Rationale: the registry is a thin read-heavy service; EF Core's overhead and migration tooling adds complexity that DbUp (chosen below) handles better.

**No MassTransit** — reload signalling uses Redis pub/sub directly (already in `Shared/Caching`).

---

## 3. PostgreSQL schema — migrations

### 3.1 Migration tool: DbUp

**Decision**: DbUp over Flyway.
- DbUp is a .NET library — no JVM runtime, no shell scripts, no Docker image required
- Migrations run as part of the application startup (`DeployChanges.To.PostgresqlDatabase(...)`)
- SQL scripts are embedded resources in a dedicated `db/Migrations/` folder
- Flyway would require a separate process or container; DbUp integrates cleanly into `dotnet run` dev loop

Add to `DECISIONS.md` under Phase 3: "Migration tooling: DbUp (NuGet `dbup-postgresql`). Scripts in `db/Migrations/`, named `V{NNN}__{description}.sql` (double underscore). Applied at app startup by `MigrationRunner` called from `Program.cs` before `app.Run()`."

### 3.2 `db/Migrations/` naming convention

```
db/Migrations/
├── V001__create_operation_registry.sql
├── V002__create_provider_registry.sql
└── V003__create_provider_credentials_audit.sql
```

### 3.3 CREATE TABLE statements

#### `operation_registry`

```sql
CREATE TABLE IF NOT EXISTS operation_registry (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    operation_pattern   TEXT        NOT NULL,   -- e.g. "ml.fraud.*", "dashboard.render"
    handler_type        TEXT        NOT NULL,   -- "internal" | "external"
    provider_id         TEXT,                   -- NULL for internal handlers
    schema_version      TEXT        NOT NULL DEFAULT '1.0',
    params_schema       JSONB,                  -- JSON Schema (Draft 2020-12) per PROVIDER_ONBOARDING.md
    payload_schema      JSONB,                  -- JSON Schema for response payload (optional, for docs)
    timeout_ms          INT         NOT NULL DEFAULT 30000,
    cacheable           BOOLEAN     NOT NULL DEFAULT FALSE,
    cache_ttl_seconds   INT,                    -- NULL = use system default when cacheable=true
    idempotent          BOOLEAN     NOT NULL DEFAULT TRUE,
    required_role       TEXT,                   -- NULL = any authenticated user
    status              TEXT        NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active', 'deprecated', 'disabled')),
    deprecation_message TEXT,                   -- non-null when status='deprecated'
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_operation_pattern UNIQUE (operation_pattern)
);

CREATE INDEX idx_op_registry_status ON operation_registry (status);
CREATE INDEX idx_op_registry_provider ON operation_registry (provider_id) WHERE provider_id IS NOT NULL;

-- Auto-bump updated_at on every UPDATE
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN NEW.updated_at = NOW(); RETURN NEW; END;
$$;

CREATE TRIGGER trg_op_registry_updated_at
    BEFORE UPDATE ON operation_registry
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
```

#### `provider_registry`

```sql
CREATE TABLE IF NOT EXISTS provider_registry (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider_id         TEXT        NOT NULL,   -- stable slug, e.g. "ml-service"
    display_name        TEXT        NOT NULL,
    description         TEXT,
    client_id           TEXT        NOT NULL,   -- OAuth2 client_id for JWT validation
    client_secret_hash  TEXT        NOT NULL,   -- bcrypt hash; never stored in plain
    operations          TEXT[]      NOT NULL DEFAULT '{}',  -- operation patterns this provider handles
    chart_types         TEXT[]      NOT NULL DEFAULT '{}',  -- chartType strings it can produce
    transformers        TEXT[]      NOT NULL DEFAULT '{}',  -- ComputedTransform keys it supports
    timeout_ms          INT         NOT NULL DEFAULT 30000,
    circuit_breaker     JSONB       NOT NULL DEFAULT '{"failureThreshold":5,"windowSeconds":60,"cooldownSeconds":30}',
    priority            SMALLINT    NOT NULL DEFAULT 5      -- 1 (lowest) – 10 (highest)
                            CHECK (priority BETWEEN 1 AND 10),
    status              TEXT        NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active', 'suspended', 'maintenance')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_provider_id UNIQUE (provider_id),
    CONSTRAINT uq_client_id   UNIQUE (client_id)
);

CREATE INDEX idx_provider_status ON provider_registry (status);

CREATE TRIGGER trg_provider_updated_at
    BEFORE UPDATE ON provider_registry
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
```

#### `provider_credentials_audit`

```sql
CREATE TABLE IF NOT EXISTS provider_credentials_audit (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider_id  TEXT        NOT NULL,
    action       TEXT        NOT NULL    -- "rotate", "revoke", "issue"
                     CHECK (action IN ('rotate', 'revoke', 'issue')),
    jti          TEXT,                  -- JWT ID of the affected credential (if applicable)
    performed_by TEXT        NOT NULL,  -- userId or "system"
    at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT fk_audit_provider
        FOREIGN KEY (provider_id) REFERENCES provider_registry (provider_id)
        ON DELETE CASCADE
);

CREATE INDEX idx_cred_audit_provider ON provider_credentials_audit (provider_id, at DESC);
```

---

## 4. Interface signatures

### 4.1 `IOperationRegistry`

```csharp
namespace ReportingPlatform.Providers.Abstractions;

public interface IOperationRegistry
{
    // Returns the best-matching registration for the given operation string,
    // or null if no active pattern matches. "Most specific wins" — see §6.
    Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default);

    // Returns all active registrations (used by hot-reload diff and admin endpoints).
    Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default);

    // Triggers an immediate in-process reload from PostgreSQL.
    // Called by OperationRegistryRefreshService on Redis pub/sub notification.
    Task ReloadAsync(CancellationToken ct = default);
}
```

### 4.2 `IProviderRegistry`

```csharp
namespace ReportingPlatform.Providers.Abstractions;

public interface IProviderRegistry
{
    // Returns the provider for a given providerId, or null if not found / suspended.
    Task<ProviderRegistration?> GetAsync(string providerId, CancellationToken ct = default);

    // Returns all active providers (used by admin health checks).
    Task<IReadOnlyList<ProviderRegistration>> GetAllActiveAsync(CancellationToken ct = default);

    // Validates the client_id / client_secret pair (called during provider JWT issuance).
    Task<bool> ValidateCredentialsAsync(string clientId, string clientSecret, CancellationToken ct = default);

    Task ReloadAsync(CancellationToken ct = default);
}
```

**`ValidateCredentialsAsync` implementation**: `BCrypt.Verify(clientSecret, registration.ClientSecretHash)`. Hash generation at registration time uses `BCrypt.HashPassword(secret, workFactor: 12)` (~250ms verify cost — deliberately slow to resist brute force). Package: `BCrypt.Net-Next`.

### 4.3 `IParamsValidator` (implementation contract — interface defined in Contracts)

```csharp
// Interface defined in Shared/Contracts/Validation/IParamsValidator.cs:
// Task<ValidationResult> ValidateAsync(string operation, JsonElement @params, CancellationToken ct)

// Implementation in Shared/Providers/Validation/JsonSchemaParamsValidator.cs:
// - Looks up compiled JsonSchema from IOperationRegistry
// - Evaluates params against schema
// - Maps JsonSchema.Net EvaluationResults → ValidationError[]
// - PARAMS_TOO_LARGE guard: params.GetRawText().Length > 65_536 → immediate reject
```

---

## 5. In-memory models

```csharp
public sealed record OperationRegistration
{
    public required string OperationPattern { get; init; }
    public required string HandlerType { get; init; }       // "internal" | "external"
    public string? ProviderId { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public JsonElement? ParamsSchema { get; init; }
    public int TimeoutMs { get; init; } = 30_000;
    public bool Cacheable { get; init; }
    public int? CacheTtlSeconds { get; init; }
    public bool Idempotent { get; init; } = true;
    public string? RequiredRole { get; init; }
    public string Status { get; init; } = "active";
    public string? DeprecationMessage { get; init; }

    // Compiled at load time by PostgresOperationRegistry; not from DB.
    // Null when ParamsSchema is null (no validation required).
    public JsonSchema? CompiledSchema { get; init; }
}

public sealed record ProviderRegistration
{
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecretHash { get; init; }
    public required IReadOnlyList<string> Operations { get; init; }
    public required IReadOnlyList<string> ChartTypes { get; init; }
    public required IReadOnlyList<string> Transformers { get; init; }
    public int TimeoutMs { get; init; } = 30_000;
    public required CircuitBreakerConfig CircuitBreaker { get; init; }
    public int Priority { get; init; } = 5;
    public string Status { get; init; } = "active";
}

public sealed record CircuitBreakerConfig
{
    public int FailureThreshold { get; init; } = 5;
    public int WindowSeconds { get; init; } = 60;
    public int CooldownSeconds { get; init; } = 30;
}
```

---

## 6. Wildcard matching algorithm — "most specific wins"

### 6.1 Pattern syntax

- Literal: `dashboard.render` — matches exactly
- Single-segment wildcard: `ml.fraud.*` — matches `ml.fraud.score`, `ml.fraud.explain`, but NOT `ml.fraud.score.v2`
- Multi-segment wildcard: `ml.**` — matches any suffix including nested dots

Patterns are dot-separated. `*` matches one segment. `**` matches one or more segments (greedy).

Trailing `.*` matches exactly one segment; trailing `.**` matches one or more segments (greedy). These are distinct: `ml.*` does NOT match `ml.fraud.score` (two segments after `ml`), but `ml.**` does.

### 6.2 Specificity scoring (pseudocode)

```
function specificity(pattern: string) -> int:
    segments = pattern.split(".")
    score = 0
    for segment in segments:
        if segment == "**":
            score += 1       // lowest weight — greedy wildcard
        elif segment == "*":
            score += 10      // medium weight — single wildcard
        else:
            score += 100     // highest weight — literal match
    return score

function resolve(operation: string, registrations: List<OperationRegistration>)
        -> OperationRegistration | null:

    candidates = []
    for reg in registrations where reg.status == "active":
        if matches(operation, reg.operationPattern):
            candidates.append((reg, specificity(reg.operationPattern)))

    if candidates.empty:
        return null

    // Sort descending by score; ties broken by pattern length (longer = more specific)
    candidates.sort_by(score DESC, len(pattern) DESC)
    return candidates[0].reg

function matches(operation: string, pattern: string) -> bool:
    opParts  = operation.split(".")
    patParts = pattern.split(".")
    return matchSegments(opParts, 0, patParts, 0)

function matchSegments(op, oi, pat, pi) -> bool:
    if pi == len(pat) and oi == len(op): return true
    if pi == len(pat): return false
    if pat[pi] == "**":
        // ** can consume 1 or more remaining segments
        for consumed in 1..(len(op) - oi + 1):
            if matchSegments(op, oi + consumed, pat, pi + 1):
                return true
        return false
    if oi == len(op): return false
    if pat[pi] == "*" or pat[pi] == op[oi]:
        return matchSegments(op, oi + 1, pat, pi + 1)
    return false
```

### 6.3 Examples

| Operation | Pattern | Score | Matches? |
|---|---|---|---|
| `ml.fraud.score` | `ml.fraud.score` | 300 | ✓ |
| `ml.fraud.score` | `ml.fraud.*` | 210 | ✓ |
| `ml.fraud.score` | `ml.**` | 111 | ✓ |
| `ml.fraud.score` | `dashboard.render` | — | ✗ |
| `ml.fraud.score.v2` | `ml.fraud.*` | — | ✗ (single `*` won't cross dot) |
| `ml.fraud.score.v2` | `ml.**` | 111 | ✓ |

Winner for `ml.fraud.score` with all three patterns registered: `ml.fraud.score` (score 300).

### 6.4 Implementation note

The matching runs against an in-memory snapshot (see §7). No I/O per request. For a registry of < 10,000 patterns, linear scan is ≤ 1ms. If the registry grows beyond ~50,000 patterns, introduce a trie index — but this is not expected in Phase 3.

**Validation result caching (Q2 answer — deferred)**: Do NOT cache `(operationPattern, paramsHash) → ValidationResult` in Phase 3. Defer until Phase 12 (NBomber load test). If Phase 12 reveals p95 validation latency > 10ms, add a bounded `ConcurrentDictionary<(string, string), ValidationResult>` cache in `Shared/Caching/ValidationCache.cs`. JsonSchema.Net evaluation is expected to be < 1ms for typical schemas; caching before measurement is premature.

---

## 7. Hot-reload concurrency strategy

### 7.1 Chosen approach: immutable snapshot + atomic reference swap

```csharp
// Inside PostgresOperationRegistry:
private volatile RegistrySnapshot _snapshot = RegistrySnapshot.Empty;

sealed record RegistrySnapshot(
    IReadOnlyDictionary<string, OperationRegistration> ByPattern,
    IReadOnlyList<OperationRegistration> All
)
{
    public static readonly RegistrySnapshot Empty =
        new(new Dictionary<string, OperationRegistration>(), []);
}
```

**Read path** (per-request, hot):
```csharp
var snap = _snapshot;          // single volatile read — no lock
var match = Resolve(operation, snap.All);
```

**Write path** (on reload, cold):
```csharp
async Task ReloadAsync(CancellationToken ct)
{
    var rows   = await LoadFromPostgresAsync(ct);
    var newSnap = BuildSnapshot(rows);   // compiles schemas, builds dict
    Volatile.Write(ref _snapshot, newSnap);  // atomic reference swap
}
```

### 7.2 Why immutable snapshot, not `ConcurrentDictionary` swap or `ReaderWriterLockSlim`

| Strategy | Torn-state risk | Lock contention | Schema compile during lock |
|---|---|---|---|
| `ConcurrentDictionary` swap per key | **Yes** — partial update visible | None | N/A |
| `ReaderWriterLockSlim` | No | Write starves readers briefly | Must hold write lock during compile |
| **Immutable snapshot + volatile write** | **No** — readers see old or new, never partial | **None** | Compile happens before swap |

The snapshot strategy gives readers a consistent, fully-compiled view at all times. The reload builds a complete new snapshot off-thread and does a single pointer swap. Any request that started with the old snapshot completes against it safely; new requests get the new snapshot.

### 7.3 Redis pub/sub reload trigger

```
Channel: "operation-registry:updated"
Message: "all" | "{operationPattern}"   (for future selective reload; Phase 3 always does full reload)
```

`OperationRegistryRefreshService` (IHostedService):
1. On start: subscribe to `operation-registry:updated` via `ISubscriber`
2. On message: call `IOperationRegistry.ReloadAsync(ct)`
3. On startup: call `ReloadAsync` once to pre-warm the snapshot before accepting traffic
4. On stop: unsubscribe cleanly

**Reload latency budget (Q1 answer)**: p99 reload time MUST be < 2 seconds for a registry of ≤ 1,000 operations (full Postgres SELECT + JSON schema compile + snapshot swap). Exceeding this budget is a Phase 3 blocker. Measure via Histogram metric: `operation_registry_reload_duration_seconds` (labels: `status = "success" | "error"`). Instrument in `ReloadAsync` using a `System.Diagnostics.Metrics.Meter` registered in `ProvidersExtensions`.

### 7.4 Schema compilation during reload

```csharp
JsonSchema? compiled = null;
if (row.ParamsSchema is not null)
{
    var schemaJson = row.ParamsSchema.GetRawText();
    compiled = JsonSchema.FromText(schemaJson);  // compiles; throws on invalid schema
}
```

If a row's `params_schema` is invalid JSON Schema, the reload logs a warning and skips that registration (does not crash the service). The old snapshot remains active for that pattern.

Additionally, if `$schema` is present in the parsed JSON but is not `https://json-schema.org/draft/2020-12/schema`, log a warning: `"Operation '{pattern}' params_schema declares $schema '{declared}' which is not Draft 2020-12; validation will still run but behaviour may differ."` This is a warning, not an error — reload continues normally.

---

## 8. Test plan

### Test project: `tests/Providers.Tests/`

```
tests/Providers.Tests/
├── Providers.Tests.csproj           (xUnit, Testcontainers.PostgreSQL, Testcontainers.Redis)
├── Matching/
│   └── WildcardMatcherTests.cs
├── Validation/
│   └── JsonSchemaValidatorTests.cs
└── Registry/
    ├── OperationRegistryReloadTests.cs
    └── ProviderRegistryTests.cs
```

### 8.1 Critical test scenarios

| # | Name | What it verifies |
|---|---|---|
| T1 | **Wildcard specificity — literal beats wildcard** | `ml.fraud.score` registered with patterns `ml.fraud.score` (score 300) and `ml.fraud.*` (score 210) and `ml.**` (score 111). Resolve `ml.fraud.score` → returns literal registration. |
| T2 | **Wildcard fallback chain** | Only `ml.**` registered. Resolve `ml.fraud.score` → returns `ml.**` registration. Resolve `dashboard.render` → returns null. |
| T3 | **Schema validation — valid params** | Schema requires `{ "type": "object", "required": ["startDate"] }`. Params `{"startDate":"2026-01-01"}` → `ValidationResult.IsValid = true`, `Errors = []`. |
| T4 | **Schema validation — invalid params** | Same schema. Params `{}` (missing required field) → `IsValid = false`, `Errors[0].Field = "startDate"`, `Code = "VALIDATION_ERROR"`. |
| T5 | **PARAMS_TOO_LARGE guard** | Params string of 65,537 bytes → `IsValid = false` before schema lookup, `Code = "PARAMS_TOO_LARGE"`. Verifies schema is NOT evaluated (no schema hit logged). |
| T6 | **Hot reload — no torn state under concurrent reads** | Start 50 reader threads calling `ResolveAsync` in a tight loop. Trigger `ReloadAsync` 10 times concurrently. Assert: no `NullReferenceException`, no partial/empty result returned, all resolved registrations are fully populated (non-null `CompiledSchema` where schema exists). |
| T7 | **Redis pub/sub reload trigger (integration)** | Use Testcontainers Redis. Publish `operation-registry:updated` to Redis. Assert `ReloadAsync` was called within 500ms (mock the DB layer, spy on reload count). |
| T8 | **Invalid schema in DB — graceful skip** | One DB row has `params_schema = '{"invalid":"json schema"'` (malformed). `ReloadAsync` completes without throwing. Valid registrations are still reachable. Warns in log. |

---

## 9. Packages summary

| Package | Version | Rationale |
|---|---|---|
| `Npgsql` | 9.0.x | Direct PostgreSQL driver; no EF Core needed for read-heavy registry |
| `JsonSchema.Net` | 7.x.x | .NET-native JSON Schema Draft 2020-12 implementation; active maintenance; no reflection serialization |
| `BCrypt.Net-Next` | latest stable | BCrypt work factor 12 for provider clientSecret hashing and verification |
| `StackExchange.Redis` | 2.7.33 | Already in Caching; same version for pub/sub client |
| `Microsoft.Extensions.Hosting.Abstractions` | 9.0.x | `IHostedService` for refresh background service |
| `dbup-postgresql` | latest stable | SQL migration runner (in a separate `db/` project or startup host) |

**Test-only**:
| Package | Rationale |
|---|---|
| `xunit` | Standard .NET test framework |
| `Testcontainers.PostgreSql` | Spin up real Postgres for integration tests |
| `Testcontainers.Redis` | Spin up real Redis for pub/sub integration test (T7) |
| `Microsoft.NET.Test.Sdk` | Test runner |
| `coverlet.collector` | Code coverage |

---

## 10. What this plan does NOT cover

- gRPC provider connection pool (Phase 8 — Provider Bridge)
- RBAC enforcement beyond `required_role` lookup (Phase 5 — Gateway)
- Circuit breaker state machine (Phase 8 — uses `CircuitBreakerConfig` from this phase)
- Admin CRUD endpoints for `operation_registry` / `provider_registry` (Phase 8 — Admin API)
- Credential rotation flow (Phase 8 — uses `provider_credentials_audit` table from this phase)
- Resolver cache lookup using `Cacheable` + `CacheTtlSeconds` from `OperationRegistration` — Phase 6
