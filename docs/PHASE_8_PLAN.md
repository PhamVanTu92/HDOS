# PHASE_8_PLAN.md — Provider Bridge + JWT Authentication
> Status: **APPROVED** | Author: Engineering | Date: 2026-05-19 | OQ-C resolved 2026-05-19

This document is the complete design for Phase 8. Approved 2026-05-19. All security-critical choices are explicitly justified.

## Approved patches (applied 2026-05-19)

**Patch 1 — OQ-C resolved**: External operation routing uses Option C — `RequestSubmissionService` pre-routes at submission time. See `docs/DECISIONS.md §Phase 8 — OQ-C`. `RequestSubmissionService.cs` Step 8 patched. No changes to `Operation.Router.Worker`.

**Patch 2 — Probe JWT differentiation (§12.1)**: Probe JWTs carry `purpose: "probe"` claim with max 60s TTL. `JwtValidationInterceptor` cross-checks purpose vs `Hello.supportedOperations`. Probe JTI audit-logged with `action = 'probe'`. Migration V007 expands audit action constraint to include `'probe'`.

**Patch 3 — Pending hash cleanup (§10.3)**: Short-circuit `pending_secret_expires_at > UtcNow` evaluated BEFORE `BCrypt.Verify` on pending hash. `PendingHashCleanupService` (`IHostedService`) runs every 5 minutes, clears rows where `pending_secret_expires_at < NOW() - INTERVAL '5 minutes'`.

**Patch 4 — TB4 lockout assertion clarity**: TB4 verifies that `FakeBcryptVerifier.CallCount` is NOT incremented on attempt 6 (lockout short-circuits before BCrypt). Lockout key exists with TTL > 0. Failure counter = 5 in Redis.

---

## Table of Contents

1. [Scope summary](#1-scope-summary)
2. [Project structure](#2-project-structure)
3. [Migration V007](#3-migration-v007)
4. [JWT signing key lifecycle](#4-jwt-signing-key-lifecycle)
5. [Token endpoint design](#5-token-endpoint-design)
6. [gRPC server config](#6-grpc-server-config)
7. [Hello/Welcome handshake state machine](#7-hellowelcome-handshake-state-machine)
8. [Per-provider RabbitMQ consumer architecture](#8-per-provider-rabbitmq-consumer-architecture)
9. [Per-provider Polly resilience](#9-per-provider-polly-resilience)
10. [Credential rotation flow (planned, 60s grace)](#10-credential-rotation-flow)
11. [Credential revocation via Redis pub/sub](#11-credential-revocation-via-redis-pubsub)
12. [Admin probe endpoint](#12-admin-probe-endpoint)
13. [Test scenarios (ProviderBridge.Tests)](#13-test-scenarios)
14. [Security checklist verification](#14-security-checklist-verification)
15. [Open questions](#15-open-questions)
16. [Package decisions](#16-package-decisions)

---

## 1. Scope Summary

Phase 8 ships four independent sub-scopes:

| Sub-scope | Primary surface | Status |
|---|---|---|
| JWT signing key infrastructure | `db`, `Request.Api`, `Shared/Auth` | New |
| Provider token endpoint | `Request.Api` | New controller |
| `Services/Provider.Bridge/` | New gRPC service | New project |
| Credential rotation + revocation | `Request.Api` admin endpoints + Redis | New |

**What does NOT change in Phase 8**:
- `Operation.Router.Worker` (Phase 6) — no changes
- RabbitMQ topology — queues already declared in Phase 6 topology; Phase 8 only adds consumers
- `Shared/Providers` — `IProviderRegistry` + `PostgresProviderRegistry` already exist; minor additions only
- Frontend-visible API (`PROTOCOL.md`) — no changes; provider authentication is server-side only

---

## 2. Project Structure

### 2.1 New project: `Services/Provider.Bridge/`

```
Services/Provider.Bridge/
├── Provider.Bridge.csproj
├── Program.cs
├── GlobalUsings.cs
├── appsettings.json
├── appsettings.Development.json
├── Bridge/
│   ├── ProviderBridgeService.cs          # IOperationProvider.Connect implementation
│   ├── ProviderSession.cs                # per-stream state machine + concurrency
│   └── HeartbeatMonitor.cs              # 30s idle watchdog per session
├── Interceptors/
│   └── JwtValidationInterceptor.cs      # gRPC server interceptor — validates JWT before any RPC
├── Services/
│   ├── ProviderSessionManager.cs        # ConcurrentDictionary<sessionId, ProviderSession>
│   ├── JwksCache.cs                     # in-memory cache of public keys, refreshed from JWKS URL
│   └── RevocationSubscriber.cs          # Redis pub/sub listener for provider:revoked events
├── Resilience/
│   └── ProviderResiliencePipeline.cs    # Polly pipeline factory, one pipeline per providerId
└── Consumers/
    └── ProviderRequestConsumer.cs       # per-session RabbitMQ consumer; prefetch = maxConcurrent
```

**csproj package references** (names only — exact versions set at implementation time):
- `Grpc.AspNetCore` — ASP.NET Core gRPC server hosting
- `Google.Protobuf` — protobuf runtime
- `Grpc.Tools` (PrivateAssets=All) — proto codegen at build time
- `Polly` — resilience pipelines
- `Microsoft.IdentityModel.Tokens` + `System.IdentityModel.Tokens.Jwt` — JWT validation
- `RabbitMQ.Client` — direct AMQP consumer (not MassTransit; see §8 rationale)
- `AspNetCore.HealthChecks.Redis` + `AspNetCore.HealthChecks.RabbitMQ`

**Project references**:
- `Shared/Caching` — `RedisKeys`, `AddPlatformCaching`
- `Shared/Telemetry` — `AddPlatformTelemetry`
- `Shared/Contracts` — `OperationResponseMessage`, store records
- `Shared/Providers` — `IProviderRegistry`, `ProviderRegistration`
- `Shared/Messaging` — `IOperationBus` (to publish terminal results to response exchange)

Port: **5400**

### 2.2 New shared project: `Shared/Auth/`

```
Shared/Auth/
├── Auth.csproj
├── JwtSigningKey.cs       # immutable record: kid, algorithm, privateKeyBytes (encrypted), publicKeyDerBytes
├── JwksDocument.cs        # source-gen JSON shape: { keys: [...] }
├── JwkEntry.cs            # per-key: kty, use, alg, kid, n, e
└── ISigningKeyService.cs  # interface for loading/rotating keys
```

`Request.Api` references `Shared/Auth` for JWT issuance.
`Provider.Bridge` does NOT reference `Shared/Auth` — it fetches public keys via JWKS HTTP endpoint.

> **Security invariant**: private key bytes NEVER appear in `Provider.Bridge`. Bridge only holds public keys from the JWKS cache.

### 2.3 Additions to `Request.Api`

New files:
```
Services/Request.Api/
├── Controllers/
│   ├── ProviderTokenController.cs        # POST /api/v1/providers/token
│   ├── JwksController.cs                 # GET /.well-known/jwks.json
│   └── AdminProvidersController.cs       # rotate, revoke, probe
├── Services/
│   ├── SigningKeyService.cs              # loads active RSA key from Postgres, caches
│   ├── JwtIssuerService.cs              # creates provider JWTs using active key
│   └── ProviderLockoutService.cs        # Redis-backed failure counter + lockout state
```

New project references:
- `Shared/Auth` — signing key types + `ISigningKeyService`
- `Shared/Providers` — `IProviderRegistry` (for credential validation)
- Npgsql data source (already wired via `AddPlatformCaching`? No — need to add explicit Postgres for `Request.Api`)

> **OQ-A** (open question): Does adding Postgres to `Request.Api` violate the "stateless API gateway" architecture intention? See §15 for resolution options.

---

## 3. Migration V007

File: `db/Migrations/V007__signing_keys_and_provider_status.sql`

### 3.1 New table: `signing_keys`

```sql
CREATE TABLE IF NOT EXISTS signing_keys (
    id                    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- kid header value in issued JWTs. UUID v7 string for chronological ordering.
    key_id                TEXT        NOT NULL,

    algorithm             TEXT        NOT NULL DEFAULT 'RS256',

    -- RSA private key in PKCS#8 DER format, encrypted with ASP.NET Data Protection.
    -- NEVER returned by any API endpoint. Only read by the process that issues tokens.
    private_key_encrypted BYTEA       NOT NULL,

    -- SubjectPublicKeyInfo DER bytes. Exported to JWKS endpoint as n/e components.
    -- Safe to cache in-memory; safe to serve publicly.
    public_key_spki       BYTEA       NOT NULL,

    status                TEXT        NOT NULL DEFAULT 'active'
                              CHECK (status IN ('active', 'retired', 'revoked')),

    created_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- NULL = key lives until manually retired.
    -- Set on rotation: old key retires_at = NOW() + '60 minutes'.
    retires_at            TIMESTAMPTZ,

    retired_at            TIMESTAMPTZ,

    CONSTRAINT uq_signing_key_id UNIQUE (key_id)
);

CREATE INDEX idx_signing_keys_status ON signing_keys (status, created_at DESC);
```

**Why encrypted storage for private key**:
- If `signing_keys` table is exfiltrated (e.g., SQL injection with read access), the private keys are still ciphertext
- ASP.NET Data Protection uses AES-256-CBC with a key ring stored separately (local FS in dev, Redis/S3 key ring in production)
- The `DataProtectionProvider` key ring can be rotated independently of the signing keys
- This is the same pattern ASP.NET Core uses for anti-forgery tokens, auth cookies, etc.

**Why RSA-2048 (not ECDSA P-256)**:
- Maximum compatibility with provider SDK implementations across languages
- `System.Security.Cryptography.RSA.Create(2048)` is straightforward in .NET
- P-256 would be preferred for performance in Phase 11 if key rotation is automated

### 3.2 Alter `provider_registry` for credential rotation grace period

```sql
-- Expand status check to include credentials_revoked state.
-- The auto-generated constraint name in PostgreSQL for an unnamed CHECK on column status
-- in table provider_registry is: provider_registry_status_check
ALTER TABLE provider_registry
    DROP CONSTRAINT provider_registry_status_check;

ALTER TABLE provider_registry
    ADD CONSTRAINT provider_registry_status_check
    CHECK (status IN ('active', 'suspended', 'maintenance', 'credentials_revoked'));

-- Pending client secret columns for 60-second rotation grace period.
-- During grace period: both old hash (pending_client_secret_hash) and
-- new hash (client_secret_hash) are accepted.
ALTER TABLE provider_registry
    ADD COLUMN IF NOT EXISTS pending_client_secret_hash TEXT,
    ADD COLUMN IF NOT EXISTS pending_secret_expires_at  TIMESTAMPTZ;
```

> **Schema invariant**: `pending_client_secret_hash` is always NULL when no rotation is in progress. It is set to the OLD hash during rotation and cleared by scheduled job or next snapshot reload after `pending_secret_expires_at` has passed.

### 3.3 Expand `provider_credentials_audit` for token issuance events

The existing table has `action IN ('rotate', 'revoke', 'issue')`. `'issue'` is used for token minting events. The table already has a `jti` column for JWT ID logging.

No schema change needed — `'issue'` action already covers `token_minted` events. Each `POST /providers/token` call writes one audit row with `action = 'issue'` and `jti = jwt.jti`.

---

## 4. JWT Signing Key Lifecycle

### 4.1 Key generation on startup

On `Request.Api` startup, `SigningKeyService`:
1. Query `signing_keys WHERE status = 'active' ORDER BY created_at DESC LIMIT 1`
2. If no active key found:
   - Generate RSA-2048 keypair: `RSA.Create(2048)`
   - Export private key: `rsa.ExportPkcs8PrivateKey()` → PKCS#8 DER bytes
   - Export public key: `rsa.ExportSubjectPublicKeyInfo()` → SPKI DER bytes
   - Encrypt private key with `IDataProtector.Protect(pkcs8Bytes)` → `private_key_encrypted`
   - `kid` = `Guid.CreateVersion7().ToString()`
   - Insert into `signing_keys`
3. Load active key into memory (`RsaSecurityKey` from decrypted private key bytes)
4. Reload every 5 minutes (background `IHostedService`) to pick up rotated keys

> **Security invariant**: `private_key_encrypted` bytes are NEVER logged, returned in HTTP responses, or serialized outside of the Postgres column. The `IDataProtector` is scoped to the `Provider.Auth` purpose string.

### 4.2 JWKS endpoint (`GET /.well-known/jwks.json`)

Served by `Request.Api`. No authentication required.

Returns all non-revoked keys: `status IN ('active', 'retired')` AND (`retires_at IS NULL` OR `retires_at > NOW() - INTERVAL '5 minutes'`).

The 5-minute buffer after `retires_at` accounts for clock skew between DB write and JWKS consumer cache expiry.

Response shape:
```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "alg": "RS256",
      "kid": "01jtxxxxxxxxxxxxxxxxxx",
      "n": "<base64url-encoded RSA modulus>",
      "e": "AQAB"
    }
  ]
}
```

`n` and `e` are derived from `public_key_spki` (SPKI DER bytes → `RSA.ImportSubjectPublicKeyInfo()` → `ExportParameters()` → `Modulus`/`Exponent` base64url-encoded).

Cache-Control: `public, max-age=300` (5-minute browser/CDN cache). Provider SDK refreshes every 5 minutes regardless.

### 4.3 Key rotation (admin-initiated)

`POST /api/v1/admin/signing-keys/rotate` (requires `role: admin`):

1. Generate new RSA-2048 keypair (same as startup procedure)
2. INSERT new key with `status = 'active'`
3. UPDATE old active key: `status = 'retired'`, `retires_at = NOW() + '60 minutes'`
4. Reload in-memory active key in `SigningKeyService`
5. Return `{ "kid": "new-kid", "rotatedAt": "ISO-8601" }` (no private key exposed)

**Why 60-minute grace for retired key in JWKS**:
- Provider tokens expire in 15 minutes
- Worst case: a token was issued 1 second before rotation, expires 15 minutes later
- JWKS serves the retired key for 60 minutes — well beyond the 15-minute token lifetime
- After 60 minutes: all tokens signed with the old key have expired; retired key can be dropped from JWKS safely

**NEVER expose private keys via API** — rotation returns only the `kid` of the new key.

---

## 5. Token Endpoint Design

### 5.1 Location

`POST /api/v1/providers/token` — in `Request.Api`.

**Rationale for Request.Api (not a dedicated Auth service)**:
- Only one new endpoint + two admin endpoints; insufficient justification for a new service boundary
- `Request.Api` already has Redis (for rate limiting counters), JWT middleware, and ASP.NET Core rate limiting infrastructure
- The only new dependency is Postgres (for `IProviderRegistry` + `SigningKeyService`)
- If the token endpoint ever needs independent scaling (unlikely — 10 req/min per provider), extract to `Services/Provider.Auth/` in Phase 11

**Postgres access in Request.Api**: Request.Api gets `NpgsqlDataSource` via `AddPlatformCaching` extension — wait, that's wrong. `AddPlatformCaching` is Redis only. We add a second DI registration for Postgres (`AddNpgsqlDataSource`) in `Request.Api/Program.cs`. The connection string comes from `appsettings.json ConnectionStrings:Postgres`.

### 5.2 Request/response shape

Request body:
```json
{
  "clientId":     "ml-team-fraud-c83hf",
  "clientSecret": "...",
  "grantType":    "client_credentials"
}
```

Success (`200 OK`):
```json
{
  "accessToken": "<jwt>",
  "expiresIn":   900,
  "tokenType":   "Bearer"
}
```

Error (`401`):
```json
{ "error": "invalid_client" }
```

Rate limit (`429`):
```json
{ "error": "rate_limited", "retryAfterSeconds": 60 }
```

> **Security invariant**: `401` body is **identical** for unknown `clientId` AND wrong `clientSecret`. No oracle attack possible. This is explicitly specified in `PROVIDER_PROTOCOL.md §3.2`.

### 5.3 Endpoint execution flow

```
POST /api/v1/providers/token
```

1. **Parse body** — deserialize; validate `grantType == "client_credentials"`. If not: `400 Bad Request`.

2. **IP rate limit** — check Redis `INCR rp:auth:rate:ip:{clientIp}` with 60s TTL:
   - Counter > 100 → `429 rate_limited`
   - (Implemented as ASP.NET Core fixed-window policy `ProviderTokenByIp`, 100/min)

3. **clientId rate limit** — check Redis `INCR rp:auth:rate:cid:{clientId}` with 60s TTL:
   - Counter > 10 → `429 rate_limited`
   - (Manual Redis check in handler; ASP.NET Core rate limiting cannot key on request body)

4. **Lockout check** — `EXISTS rp:auth:locked:{clientId}`:
   - Key exists → `401 invalid_client` (same body as wrong password — no oracle)

5. **Registry lookup** — `IProviderRegistry.GetAsync(providerId)` where `providerId` is looked up by `clientId`. (The registry exposes `ValidateCredentialsAsync(clientId, secret)` which handles the lookup internally.)
   - Not found: increment failure counter → check lockout → `401 invalid_client`

6. **Status check** — if `status != 'active'`:
   - `suspended` / `maintenance` / `credentials_revoked` → `401 invalid_client`
   - This prevents suspended providers from obtaining new tokens

7. **BCrypt verification** — `BCrypt.Verify(clientSecret, storedHash)` (~250ms):
   - Also check `pending_client_secret_hash` if within grace period (see §10)
   - Invalid: `INCR rp:auth:failures:{clientId}` (60s TTL); if counter ≥ 5: `SET rp:auth:locked:{clientId} 1 EX 300`; return `401 invalid_client`
   - Valid: `DEL rp:auth:failures:{clientId}`; `DEL rp:auth:rate:cid:{clientId}`

8. **JWT issuance** — `JwtIssuerService.IssueAsync(providerId)`:
   - Load active signing key (`RsaSecurityKey` from `SigningKeyService`)
   - `jti = Guid.CreateVersion7().ToString()`
   - Claims: `iss`, `sub = providerId`, `aud = "provider-bridge"`, `scope = "provider"`, `iat`, `exp = iat + 900`, `jti`
   - `kid` in JWT header = active key's `kid`
   - Sign with RS256 using RSA private key
   - Return JWT string

9. **Audit log** — append to `provider_credentials_audit`:
   ```
   (provider_id, action='issue', jti=<jti>, performed_by='system:{clientId}', at=NOW())
   ```
   Fire-and-forget async (do not delay the response; log failure separately).

10. **Response** — `200 OK` with `{ accessToken, expiresIn: 900, tokenType: "Bearer" }`.

### 5.4 Rate limiting specifics

| Dimension | Limit | Mechanism | Storage |
|---|---|---|---|
| Per source IP | 100/min | ASP.NET Core fixed-window `ProviderTokenByIp` | In-memory (per node) |
| Per `clientId` | 10/min | Manual `INCR` in handler | Redis `rp:auth:rate:cid:{clientId}` |
| Lockout after failures | 5 fails → locked 5 min | Manual Redis key | Redis `rp:auth:locked:{clientId}` |

> **Why manual Redis for clientId rate limit**: ASP.NET Core rate limiting middleware runs before request body is parsed (it can only key on claims or headers, not body fields). The `clientId` is in the request body (OAuth2 client_credentials). Manual Redis check inside the handler is the correct approach. It adds one Redis round-trip before the expensive BCrypt call, which is acceptable.

> **Why per-node (in-memory) for IP rate limit**: Source IP rate limiting is a DoS mitigation. Per-node enforcement is sufficient for this; a coordinated attack from a single IP would still be throttled at each node. Redis-backed cross-node IP rate limiting is deferred to Phase 11.

---

## 6. gRPC Server Config

### 6.1 Project and endpoint

`Provider.Bridge` is a gRPC server exposed at `grpcs://provider-bridge.platform:443` (production) and `grpc://localhost:5400` (development, TLS terminated by dev proxy or disabled in dev mode).

```csharp
// Program.cs
builder.Services.AddGrpc(opts =>
{
    opts.Interceptors.Add<JwtValidationInterceptor>();
    opts.MaxReceiveMessageSize = 4 * 1024 * 1024;  // 4 MB
    opts.MaxSendMessageSize    = 4 * 1024 * 1024;  // 4 MB
});
builder.WebHost.UseKestrel(opts =>
{
    opts.ListenAnyIP(5400, listenOpts =>
    {
        if (builder.Environment.IsProduction())
            listenOpts.UseHttps(serverCertPath, serverCertPassword);
        // else: plain HTTP/2 for local dev (no client cert required)
    });
});
app.MapGrpcService<ProviderBridgeService>();
```

### 6.2 TLS configuration

- **Server-only TLS** (no mTLS) — standard TLS, providers validate platform CA chain, no client certificate required. This matches `PROVIDER_PROTOCOL.md §4.1`.
- Server certificate path configured via `appsettings.json` (`Bridge:TlsCertPath`, `Bridge:TlsCertPassword`)
- Development: plain HTTP/2 or self-signed cert (configured in `appsettings.Development.json`)
- Production: cert from Let's Encrypt or enterprise PKI

> **Security invariant**: TLS is required in production. Plain text is NEVER an option. The Kestrel config checks `IsProduction()` before deciding whether to use HTTPS. A misconfigured production deploy (missing cert) fails fast at startup.

### 6.3 `JwtValidationInterceptor`

Runs on every RPC call (only `Connect` exists, but interceptor is future-proof).

```
Interceptor execution:
  1. Extract "authorization" metadata → parse "Bearer <token>"
  2. Get kid from JWT header (without signature verification — safe, kid is informational)
  3. Fetch public key from JwksCache by kid
  4. Verify RSA-256 signature → fail: UNAUTHENTICATED
  5. Validate exp (allow max 30s clock skew) → fail: UNAUTHENTICATED
  6. Validate iss == configured issuer → fail: UNAUTHENTICATED
  7. Validate aud == "provider-bridge" → fail: UNAUTHENTICATED
  8. Validate scope contains "provider" → fail: UNAUTHENTICATED
  9. Store validated claims in gRPC ServerCallContext.UserState for use in ProviderBridgeService
  10. Call next interceptor / handler
```

If ANY check fails: return `Status.Unauthenticated` with detail message. Do NOT distinguish between "no JWT", "expired JWT", "wrong aud", etc. — they all return the same `UNAUTHENTICATED` status (no oracle attack).

> **Clock skew**: 30 seconds maximum tolerance. JWT `exp` is validated as `exp + 30s > now`. This is stricter than the 5-minute skew allowed by some libraries (e.g., `TokenValidationParameters.ClockSkew`). Configure explicitly: do not rely on defaults.

### 6.4 `JwksCache`

In-memory cache of public keys. Provider.Bridge does NOT talk to Postgres.

```
On startup: GET /.well-known/jwks.json from Request.Api (configurable URL)
Refresh: every 5 minutes via IHostedService background timer
On cache miss (unknown kid): immediate refresh (once per 30 seconds max to prevent DoS)
```

Implementation: `Dictionary<string, RsaSecurityKey>` keyed by `kid`, protected by `ReaderWriterLockSlim`.

> **Why HTTP JWKS fetch, not Postgres**: Provider.Bridge should not need Postgres access. The public key is NOT secret (it is publicly served). HTTP fetch of JWKS is the standard OIDC/OAuth2 pattern. It decouples Bridge from DB schema and allows independent scaling.

---

## 7. Hello/Welcome Handshake State Machine

### 7.1 States

```
             ┌──────────────────────────────────────────────────────┐
             │                  Connect() called                     │
             └──────────────────────────┬───────────────────────────┘
                                        ▼
                            ┌───────────────────────┐
                            │    WaitingForHello     │ ← 5-second deadline
                            └───────────┬───────────┘
                                        │
                  ┌─────────────────────┼─────────────────────┐
                  │ Hello received      │ 5s timeout           │ Stream closed
                  ▼                     ▼                       ▼
        ┌──────────────────┐   DEADLINE_EXCEEDED          (cleanup)
        │  ValidatingHello │
        └────────┬─────────┘
                 │
    ┌────────────┼────────────┐
    │ All valid  │ JWT fail   │ Hello fail
    ▼            ▼            ▼
┌──────────┐ UNAUTHENTICATED  INVALID_ARGUMENT
│  Active  │ / PERMISSION_DENIED
└────┬─────┘
     │
  ┌──┴──────────────────────┐
  │ Normal operation         │
  │ • Queue consumer running │
  │ • Heartbeat monitor active│
  │ • RefreshAuth timer active│
  └──┬──────────────────────┘
     │
  ┌──┴────────────────────────────────────────────────────┐
  │ Closing (any exit path)                                │
  │ • Send Disconnect with reason string                   │
  │ • Stop queue consumer (NACK unprocessed messages)      │
  │ • Cancel all in-flight request tasks                   │
  │ • Remove from ProviderSessionManager                   │
  └───────────────────────────────────────────────────────┘
```

### 7.2 ValidatingHello checks (in order)

All validation happens synchronously (async registry lookup):

1. **`Hello.providerId == jwt.sub`** — if mismatch: close with `UNAUTHENTICATED`
   ("stolen token from another provider" attack prevention)

2. **Provider status == 'active'** — `IProviderRegistry.GetAsync(Hello.providerId)`:
   - `null` (unknown provider) → `UNAUTHENTICATED`
   - `status == 'credentials_revoked'` → `UNAUTHENTICATED`
   - `status IN ('suspended', 'maintenance', 'disabled')` → `PERMISSION_DENIED`
   - `status == 'active'` → continue

3. **`Hello.supportedOperations` is a subset of `ProviderRegistration.Operations`**:
   - Any operation not in the registered list → `INVALID_ARGUMENT` with detail listing the offending operations

4. **`Hello.version` is valid semver** — informational only; malformed = log warning, do not reject

### 7.3 Welcome message construction

On successful validation:

```csharp
var sessionId = Guid.CreateVersion7().ToString();
var welcome   = new Welcome
{
    SessionId                = sessionId,
    MaxConcurrentRequests    = registration.CircuitBreaker is { } cb ? /* configured */ 8 : 8,
    HeartbeatIntervalSeconds = 30,
};
```

`MaxConcurrentRequests` is a per-provider configured value (stored in `provider_registry`? — see OQ-B in §15). Default: 8.

After sending Welcome: start heartbeat monitor, start queue consumer, register session.

### 7.4 RefreshAuthRequired timing

```csharp
// After Welcome is sent:
var jwtExp = validatedClaims.ValidTo;  // from JWT token
var refreshAt = jwtExp - TimeSpan.FromSeconds(60);
var delay = refreshAt - DateTime.UtcNow;
if (delay > TimeSpan.Zero)
{
    _ = Task.Delay(delay, sessionCt).ContinueWith(_ =>
        SendRefreshAuthRequiredAsync(jwtExp), sessionCt);
}
```

If `delay <= 0` (token already within 60s of expiry when Hello arrives): send `RefreshAuthRequired` immediately after Welcome. The provider is expected to reconnect within 60s or the stream is forcibly closed at `jwtExp`.

Stream is forcibly closed at `jwtExp` via a second timer:
```csharp
var forceCloseDelay = jwtExp - DateTime.UtcNow;
_ = Task.Delay(forceCloseDelay, sessionCt).ContinueWith(_ =>
    CloseSessionAsync("token_expired"), sessionCt);
```

### 7.5 Heartbeat monitor

`HeartbeatMonitor` tracks `lastHeartbeatAt` per session. Background timer fires every 10s, checks: if `now - lastHeartbeatAt > 30s`, close session with `Disconnect { reason = "idle_timeout" }`.

On receiving `FromProvider.Heartbeat`: update `lastHeartbeatAt = DateTime.UtcNow`.

---

## 8. Per-Provider RabbitMQ Consumer Architecture

### 8.1 Design decision: raw RabbitMQ consumer (not MassTransit)

**Why not MassTransit**:
MassTransit consumer lifecycle is tied to host startup/shutdown. We need to start/stop consumers dynamically as providers connect/disconnect. MassTransit does not support runtime consumer registration after host start.

**Decision**: Use `RabbitMQ.Client` directly for per-session consumers. This is the same library MassTransit wraps internally.

### 8.2 Queue naming convention

Phase 6 (`Operation.Router.Worker`) already declares queues. For external providers:
- Queue name: `q.provider.{providerId}` (e.g., `q.provider.ml-team-fraud`)
- Exchange: `operation.request` (existing direct exchange from Phase 6)
- Routing key: `provider.{providerId}`

> **OQ-C** (open question): Phase 6 implementation must verify that it routes external operations to `q.provider.{providerId}` queues. If routing is by operation pattern, multiple providers for different operations might need separate queues. See §15.

### 8.3 Per-session consumer lifecycle

```
Provider connects → Hello validated → Welcome sent →
  Start ProviderRequestConsumer:
    - Channel.BasicQos(prefetchCount = maxConcurrentRequests)
    - Channel.BasicConsume("q.provider.{providerId}", autoAck=false, consumer)
    
  For each delivery:
    - Deserialize OperationRequestMessage
    - Wrap in Task → Run through Polly pipeline → send OperationRequest on gRPC stream
    - Await OperationResponseChunk (terminal) from provider
    - Publish OperationResponseMessage to response exchange (for Response.Dispatcher.Worker)
    - Channel.BasicAck(deliveryTag)
    
  On provider disconnect:
    - Channel.BasicNack(deliveryTag, multiple=false, requeue=true) for ALL unACKed messages
    - Channel.BasicCancel(consumerTag)
    - Channel.Close()
```

**Concurrency**: up to `maxConcurrentRequests` messages processed concurrently per session. RabbitMQ prefetch count enforces the hard limit (no additional semaphore needed — broker handles backpressure).

**Multiple provider instances**: if two instances of `ml-team-fraud` connect simultaneously (to the same or different Bridge nodes), they both start consuming from `q.provider.ml-team-fraud` as competing consumers. RabbitMQ delivers each message to exactly one consumer. This is correct per §4.8 of `PROVIDER_PROTOCOL.md`.

### 8.4 Progress chunk routing

When provider sends `OperationResponseChunk { progress }`:
- Bridge publishes to `rp:sse-notify:{requestId}` (Redis pub/sub) so `Request.Api`'s `ProgressPubSubSubscriber` picks it up and fans out to SSE clients
- This is the same path used by `Progress.Dispatcher.Worker` for internal operations

When provider sends `OperationResponseChunk { terminal }`:
- Bridge publishes `OperationResponseMessage` to MassTransit response exchange → `Response.Dispatcher.Worker` handles routing to SignalR client

---

## 9. Per-Provider Polly Resilience

### 9.1 Pipeline composition (per `providerId`)

`ProviderResiliencePipeline` builds one `ResiliencePipeline` per `providerId` on first use, cached in `ConcurrentDictionary<string, ResiliencePipeline>`.

```csharp
var pipeline = new ResiliencePipelineBuilder()
    // Layer 1: timeout — hard deadline from operation registry
    .AddTimeout(TimeSpan.FromMilliseconds(registration.TimeoutMs))
    
    // Layer 2: circuit breaker — per-provider config from provider_registry
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio        = registration.CircuitBreaker.FailureThreshold / 100.0,
        SamplingDuration    = TimeSpan.FromSeconds(registration.CircuitBreaker.WindowSeconds),
        BreakDuration       = TimeSpan.FromSeconds(registration.CircuitBreaker.CooldownSeconds),
        MinimumThroughput   = 3,   // don't trip on first isolated failure
        ShouldHandle        = args => ValueTask.FromResult(
            args.Outcome.Exception is not null),  // terminal results are successes
    })
    .Build();
```

No retry layer: `PROVIDER_PROTOCOL.md §12.2` states Bridge retries only if `idempotent: true` in the operation registration. Non-idempotent operations are NOT retried. Retry policy is per-operation, not per-provider. Phase 8 ships with no automatic retry; operation-level retry configuration is Phase 11.

**Bulkhead**: enforced via RabbitMQ prefetch count (§8.3). No explicit Polly bulkhead needed.

### 9.2 Circuit breaker open → PROVIDER_UNAVAILABLE

When the circuit is open, `BrokenCircuitException` is thrown before the request reaches the gRPC stream. Bridge catches this and publishes a `OperationResponseMessage` with `Status = Cancelled` and `ErrorCode = PROVIDER_UNAVAILABLE` directly (without involving the provider). The provider never sees the request — it stays in RabbitMQ (requeued if prefetch allows) or is NACKed.

> **Wait** — if the circuit is open, should the message be NACKed (requeued) or ACKed (discarded)? 
> **Decision**: NACK with requeue=true when circuit is open. The message stays in the queue until the circuit half-opens. This prevents message loss while the provider recovers. The client receives `PROVIDER_UNAVAILABLE` immediately (via the terminal publish). When the provider reconnects and circuit closes, the requeued message will be processed.
> **Risk**: if the provider never recovers, messages accumulate. Mitigated by `MessageTtlMs` on the queue (messages expire naturally).

---

## 10. Credential Rotation Flow

### 10.1 Sequence

`POST /api/v1/admin/providers/{id}/credentials/rotate` (requires `role: admin`):

1. Load provider by `{id}` from Postgres
2. Generate new `clientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))`
   (32 bytes = 256 bits from `System.Security.Cryptography.RandomNumberGenerator`)
3. Hash: `newHash = BCrypt.HashPassword(newSecret, workFactor: 12)`
4. In a single Postgres transaction:
   ```sql
   UPDATE provider_registry SET
     pending_client_secret_hash = client_secret_hash,  -- save old hash
     pending_secret_expires_at  = NOW() + INTERVAL '60 seconds',
     client_secret_hash         = :newHash,
     updated_at                 = NOW()
   WHERE provider_id = :providerId;
   
   INSERT INTO provider_credentials_audit 
     (provider_id, action, performed_by)
   VALUES (:providerId, 'rotate', :adminUserId);
   ```
5. Trigger provider registry reload (Redis pub/sub `rp:registry:reload`)
6. Return new `clientSecret` to admin response body — **exactly once**

> **Security invariant**: `clientSecret` is returned to the admin caller ONCE, in the HTTP response body of the rotate call. It is never stored. The admin must securely deliver it to the provider team (via Vault, sealed secret, etc.). If the admin loses it, the only recourse is another rotation.

### 10.2 BCrypt verify with grace period

In `PostgresProviderRegistry.ValidateCredentialsAsync`:

```csharp
// Primary check — new hash
if (BCrypt.Net.BCrypt.Verify(clientSecret, reg.ClientSecretHash))
    return true;

// Grace period check — short-circuit BEFORE BCrypt call (DoS mitigation invariant).
// pending_secret_expires_at > UtcNow is evaluated first; only if within grace window
// do we pay the second ~250ms BCrypt.Verify cost.
if (reg.PendingClientSecretHash is not null
    && reg.PendingSecretExpiresAt > DateTimeOffset.UtcNow)   // ← timestamp check FIRST
{
    if (BCrypt.Net.BCrypt.Verify(clientSecret, reg.PendingClientSecretHash))
        return true;
}

return false;
```

> **Security invariant**: `pending_secret_expires_at > UtcNow` is evaluated BEFORE `BCrypt.Verify`. This prevents an unauthenticated caller who knows rotation is in progress from burning ~250ms of BCrypt time per attempt when the grace window has already closed. The snapshot pattern means `reg` is read from an in-memory snapshot — the timestamp check is O(1).

> **Security note**: Two BCrypt.Verify calls in the grace period path. Each costs ~250ms. Total: ~500ms for a provider using the old secret during rotation. This is acceptable — rotation is a rare admin operation. After the grace period expires (60s), only the new hash is checked (single BCrypt call).

### 10.3 Pending hash cleanup (IHostedService — Patch 3)

`PendingHashCleanupService` runs as a background `IHostedService` in `Request.Api`. Schedule: every 5 minutes.

```sql
UPDATE provider_registry
SET pending_client_secret_hash = NULL,
    pending_secret_expires_at  = NULL
WHERE pending_secret_expires_at IS NOT NULL
  AND pending_secret_expires_at < NOW() - INTERVAL '5 minutes';
```

The extra 5-minute buffer beyond `pending_secret_expires_at` ensures no race with an active verification that loaded the provider snapshot just before the cleanup ran. Defense-in-depth: `ValidateCredentialsAsync` already handles expired grace via the `> UtcNow` check; this cleanup ensures the DB row is eventually consistent and the pending columns don't accumulate indefinitely.

---

## 11. Credential Revocation via Redis pub/sub

### 11.1 Sequence

`POST /api/v1/admin/providers/{id}/credentials/revoke` (requires `role: admin`):

1. Update provider status in Postgres: `status = 'credentials_revoked'`
2. Append audit log row: `action = 'revoke'`
3. Publish to Redis: `PUBLISH rp:provider:revoked:{providerId} "revoked"`
4. Trigger provider registry reload
5. Return `202 Accepted`

### 11.2 Bridge subscription

`RevocationSubscriber` (in `Provider.Bridge`) subscribes on startup:
```csharp
subscriber.Subscribe(
    RedisChannel.Pattern("rp:provider:revoked:*"),
    (channel, _) =>
    {
        var providerId = channel.ToString()
            .Replace("rp:provider:revoked:", "");
        _sessionManager.CloseAllSessions(providerId, "credentials_revoked");
    });
```

`ProviderSessionManager.CloseAllSessions(providerId, reason)`:
- Find all `ProviderSession`s where `session.ProviderId == providerId`
- For each: send `Disconnect { reason = "credentials_revoked" }` on the gRPC stream
- Set gRPC stream status to `CANCELLED` with trailer `x-platform-disconnect-reason: credentials_revoked`
- Stop the session's RabbitMQ consumer (NACK all unACKed messages with requeue=true)

Target: all active sessions closed within 5 seconds of the revoke call. In practice: Redis pub/sub delivery is sub-millisecond; stream close is one gRPC write.

---

## 12. Admin Probe Endpoint

### 12.1 Probe JWT differentiation (Patch 2)

Probe JWTs are differentiated from real provider JWTs by an additional claim:
- `purpose: "probe"` — present on probe JWTs only
- `exp = iat + 60` — maximum 60-second TTL (hard-coded in `JwtIssuerService.IssueProbeAsync`)
- `sub = providerId` — same as real JWT (Bridge validates provider exists)

`JwtValidationInterceptor` enforces mutual exclusion:
```
purpose == "probe" AND Hello.supportedOperations.Length > 0
    → INVALID_ARGUMENT ("probe sessions cannot declare operations")

purpose absent or != "probe" AND Hello.supportedOperations.Length == 0
    → INVALID_ARGUMENT ("provider must declare at least one supported operation")
```

Audit: each probe JTI is logged with `action = 'probe'` in `provider_credentials_audit`. This makes probe activity visible in the audit trail separately from real token issuances.

`POST /api/v1/admin/providers/{id}/probe` (requires `role: admin`) — per PROTOCOL.md §8.7.

This endpoint verifies end-to-end connectivity from `Request.Api` → `Provider.Bridge` → provider process.

Execution:
1. Load provider registration from registry
2. Issue a synthetic JWT (`JwtIssuerService.IssueAsync(providerId)`) with same claims as a real token
3. Open a gRPC channel to `Provider.Bridge` using the synthetic JWT
4. Send `Hello` with `supportedOperations = []` (probe-only, no real ops)
5. Await `Welcome` (5-second timeout)
6. Send `Disconnect { reason = "probe_complete" }` and close channel
7. Return:
```json
{
  "tlsHandshake":    true,
  "jwtAccepted":     true,
  "welcomeReceived": true,
  "latencyMs":       42,
  "sessionId":       "01JT...",
  "errorDetail":     null
}
```

On any step failure: fill `errorDetail` with the step name and error, set corresponding boolean to `false`.

This endpoint is used by:
- Monitoring scripts to verify provider is online
- Admin UI during provider onboarding (see `PROVIDER_ONBOARDING.md §5.2`)
- CI/CD post-deploy health checks

---

## 13. Test Scenarios

### 13.1 Project: `tests/ProviderBridge.Tests/`

All tests use fakes (no Docker, no actual gRPC channel, no real Redis). Following the Gateway.Tests pattern: fake infrastructure implementations.

**Fakes needed**:
- `FakeProviderRegistry` — in-memory provider registrations
- `FakeSigningKeyService` — returns a fixed in-memory RSA key (no Postgres)
- `FakeJwksCache` — pre-loaded with test public key
- `FakeRedis` — extends existing `FakeDatabase` pattern for `IDatabase`; adds `ISubscriber` fake
- `FakeBcryptVerifier` — injectable delegate to control BCrypt result without 250ms wait
- `FakeGrpcStream` — `IServerStreamWriter<ToProvider>` + `IAsyncStreamReader<FromProvider>` backed by channels

### 13.2 Token endpoint tests (`TokenEndpointTests.cs`)

| ID | Scenario | Expected |
|---|---|---|
| TB1 | Valid `clientId` + `clientSecret` + `grantType=client_credentials` | 200, JWT with correct claims (`sub`, `aud`, `scope`, `jti`), audit row written |
| TB2 | Unknown `clientId` | 401 `invalid_client`, same body as TB3 (no oracle) |
| TB3 | Known `clientId`, wrong `clientSecret` | 401 `invalid_client` |
| TB4 | After 5 consecutive failures, attempt 6 with CORRECT secret returns 401 (Patch 4): failure counter = 5 in Redis; lockout key exists with TTL > 0; `FakeBcryptVerifier.CallCount` NOT incremented on attempt 6 (lockout short-circuits before BCrypt) | 401 `invalid_client` |
| TB5 | Lock state TTL expires → correct secret | 200 (lock expired) |
| TB6 | 11th request within 1 minute for same `clientId` | 429 `rate_limited` |
| TB7 | Provider with `status = 'suspended'` | 401 `invalid_client` |
| TB8 | Provider with `status = 'credentials_revoked'` | 401 `invalid_client` |
| TB9 | `grantType != 'client_credentials'` | 400 |
| TB10 | Valid rotation: old secret accepted during grace period | 200 (pending hash check) |
| TB11 | Valid rotation: old secret rejected after grace period expires | 401 |

### 13.3 JWT validation tests (`JwtValidationTests.cs`)

Interceptor-level tests using `FakeGrpcStream` and `FakeJwksCache`.

| ID | Scenario | Expected gRPC status |
|---|---|---|
| JV1 | Valid JWT, correct claims, active provider | `OK` (stream proceeds to Hello) |
| JV2 | JWT `exp` in the past (> 30s ago) | `UNAUTHENTICATED` |
| JV3 | JWT `exp` 29s in the past (within 30s clock skew) | `OK` (accepted within skew) |
| JV4 | Wrong `aud` (`"user-api"` instead of `"provider-bridge"`) | `UNAUTHENTICATED` |
| JV5 | Missing `scope` claim | `UNAUTHENTICATED` |
| JV6 | Unknown `kid` (not in JwksCache) | `UNAUTHENTICATED` |
| JV7 | Valid JWT structure but tampered signature (last byte flipped) | `UNAUTHENTICATED` |
| JV8 | No `authorization` metadata key | `UNAUTHENTICATED` |
| JV9 | `authorization` present but empty string | `UNAUTHENTICATED` |

### 13.4 Hello/Welcome handshake tests (`HandshakeTests.cs`)

| ID | Scenario | Expected |
|---|---|---|
| HW1 | Valid JWT + valid Hello (providerId matches jwt.sub, operations subset of registered) | Welcome sent with correct sessionId, maxConcurrentRequests, heartbeatIntervalSeconds |
| HW2 | Hello not sent within 5 seconds | Stream closed with `DEADLINE_EXCEEDED` |
| HW3 | `Hello.providerId != jwt.sub` | Stream closed with `UNAUTHENTICATED` |
| HW4 | `Hello.supportedOperations` contains unregistered operation | Stream closed with `INVALID_ARGUMENT` |
| HW5 | Provider `status = 'suspended'` (JWT valid, Hello rejected by registry check) | `PERMISSION_DENIED` |

### 13.5 RefreshAuthRequired + revocation tests (`LifecycleTests.cs`)

| ID | Scenario | Expected |
|---|---|---|
| RA1 | JWT with 61s remaining at connection time | `RefreshAuthRequired` sent after ~1s; `currentTokenExpiresAtUnixMs` correct |
| RA2 | JWT with 59s remaining at connection time | `RefreshAuthRequired` sent immediately after Welcome |
| SR1 | Redis `rp:provider:revoked:{providerId}` published | Active session receives `Disconnect { reason = "credentials_revoked" }` within 1s |
| HB1 | No `Heartbeat` message for 31 seconds | Stream closed with `Disconnect { reason = "idle_timeout" }` |

### 13.6 Resilience tests (`ResilienceTests.cs`)

| ID | Scenario | Expected |
|---|---|---|
| CB1 | 5 consecutive terminal FAILED responses exceed `failureThreshold` | Circuit opens; next request returns `PROVIDER_UNAVAILABLE` without sending to provider |
| CB2 | Circuit open → wait `cooldownSeconds` → next request passes through | Provider receives request (circuit half-open → closed) |
| PC1 | Provider disconnects gRPC stream while request is in flight | `PROVIDER_DISCONNECTED` terminal published for the in-flight request |
| PC2 | Provider disconnects → unACKed RabbitMQ messages requeued (not lost) | Messages visible in queue for next connection |

### 13.7 Probe endpoint tests (`ProbeEndpointTests.cs`)

| ID | Scenario | Expected |
|---|---|---|
| PE1 | Provider connected and responding to Hello/Welcome | `{ tlsHandshake: true, jwtAccepted: true, welcomeReceived: true, latencyMs: <N> }` |
| PE2 | Provider not connected (no session for providerId) | `{ welcomeReceived: false, errorDetail: "timeout" }` |

**Total**: 11 (TB) + 9 (JV) + 5 (HW) + 4 (RA/SR/HB) + 4 (CB/PC) + 2 (PE) = **35 tests minimum**

---

## 14. Security Checklist Verification

Each item from the Phase 8 security checklist, with the specific design decision that satisfies it:

| Requirement | Implementation | Location |
|---|---|---|
| JWT signing keys NEVER logged or exposed | Private key bytes stored only in Postgres (encrypted); never returned by any API; never in logs; `[LogMasked]` on any type holding key material | `SigningKeyService`, `JwtIssuerService` |
| `clientSecret` hashed with BCrypt cost factor 12 | `BCrypt.HashPassword(secret, workFactor: 12)` at registration (Phase 3); Phase 8 only verifies — `BCrypt.Verify(secret, hash)` | `PostgresProviderRegistry.ValidateCredentialsAsync` |
| Plain `clientSecret` returned ONCE at registration | Not changed in Phase 8 (registration is Phase 3). Rotation: `clientSecret` returned once in rotate response, never stored. | `AdminProvidersController.Rotate` |
| Failed token attempts rate-limited | Redis failure counter; lockout at 5 failures within 60s; 5-minute lockout TTL; per-clientId 10/min rate limit | `ProviderLockoutService`, `ProviderTokenController` |
| Audit log immutable (append-only) | Postgres `provider_credentials_audit` has no UPDATE/DELETE grants; only INSERT allowed from application role; BIGINT IDENTITY prevents row ID reuse | `provider_credentials_audit` table, DB role grants |
| gRPC TLS enforced — no plain text option | Kestrel config: `UseHttps()` in production; build fails with warning if production cert path not configured | `Provider.Bridge/Program.cs` |
| JWT `exp` validation strict (≤ 30s clock skew) | `TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30)` — explicit override of library default (which is 5 minutes) | `JwtValidationInterceptor` |
| `jwt.sub` MUST match `Hello.providerId` | Check in `ProviderBridgeService` after JWT validation; `HW3` test covers this | `ProviderBridgeService.HandleHelloAsync` |
| BCrypt work factor 12 verified at build time | `const int WorkFactor = 12` in `ProviderRegistrationService`; this constant is checked in TB1 test assertion via round-trip verification | `ProviderRegistrationService` |
| Token endpoint oracle attack prevention | `401 invalid_client` for BOTH unknown `clientId` AND wrong `clientSecret`; identical response body; TB2 and TB3 test that body is identical | `ProviderTokenController` |
| No private key in Provider.Bridge | Bridge only fetches JWKS (public keys) from HTTP endpoint; `Shared/Auth` project NOT referenced by `Provider.Bridge.csproj` | `Provider.Bridge.csproj` references |
| `credentials_revoked` immediately closes streams | Redis pub/sub → `RevocationSubscriber` → `CloseAllSessions` within 1 second of revoke call | `RevocationSubscriber`, SR1 test |

---

## 15. Open Questions

| # | Question | Options | Recommendation |
|---|---|---|---|
| OQ-A | Does adding Postgres to `Request.Api` violate "stateless gateway" intent? | (A) Add Postgres to Request.Api (simplest) | (B) Create `Services/Provider.Auth/` (cleaner, more ops surface) | **A** — Phase 8 scope doesn't justify a new service. `Request.Api` already does auth for user JWTs. Postgres is a well-understood dependency. Extract in Phase 11 if needed. |
| OQ-B | Where does `maxConcurrentRequests` come from? `provider_registry` has no such column. | (A) Add column `max_concurrent_requests INT NOT NULL DEFAULT 8` to `provider_registry` in V007 | (B) Derive from `circuit_breaker.failureThreshold` (wrong semantics) | (C) Hard-code to 8 for Phase 8, make configurable in Phase 11 | **A** — add `max_concurrent_requests INT NOT NULL DEFAULT 8` to V007 migration. Simple column, correct semantics. |
| OQ-C | How does Phase 6 route external operations to `q.provider.{providerId}` queues? | Must verify Phase 6 `Operation.Router.Worker` produces to `q.provider.{providerId}`. If it doesn't, add routing in Phase 8 or backport to Phase 6. | **Verify before writing Phase 8 code.** Read `Services/Operation.Router.Worker/` to confirm queue topology. |
| OQ-D | Is `FakeBcryptVerifier` sufficient for tests, or do we need real BCrypt (slow)? | (A) Inject `Func<string, string, bool>` as verifier delegate — fake in tests, real BCrypt in production | (B) Use real BCrypt (250ms × N tests = slow CI) | **A** — BCrypt is an implementation detail. The interface is `ValidateCredentialsAsync`. Inject the verifier for testability. TB1 should also have one integration-level test with real BCrypt to verify the hash algorithm is not bypassed. |
| OQ-E | Should `POST /providers/token` be under `/api/v1/` or at the root level? | OAuth2 convention: token endpoints often at `/oauth/token` or `/token`. Our current path: `/api/v1/providers/token` (per PROVIDER_PROTOCOL.md). | **Keep `/api/v1/providers/token`** as documented. Changing it now would require updating `PROVIDER_PROTOCOL.md` and all SDK samples. |
| OQ-F | Data Protection key ring in production: local FS or Redis? | (A) Local FS (developer machines only) | (B) Redis key ring (multi-node safe) | **B for production, A for dev.** Configure via `ASPNETCORE_ENVIRONMENT`. `PersistKeysToStackExchangeRedis` in `Program.cs` for production. |

---

## 16. Package Decisions

Packages to be added at implementation time (confirm latest stable non-RC versions):

| Package | Used by | Purpose |
|---|---|---|
| `Grpc.AspNetCore` | `Provider.Bridge` | gRPC server hosting |
| `Google.Protobuf` | `Provider.Bridge` | Proto runtime |
| `Grpc.Tools` | `Provider.Bridge` | Proto codegen (PrivateAssets=All) |
| `Polly` | `Provider.Bridge` | Resilience pipelines |
| `Microsoft.IdentityModel.Tokens` | `Provider.Bridge`, `Request.Api` | JWT validation primitives, RSA key types |
| `System.IdentityModel.Tokens.Jwt` | `Provider.Bridge`, `Request.Api` | JWT validation/creation |
| `RabbitMQ.Client` | `Provider.Bridge` | Direct AMQP consumer |
| `BCrypt.Net-Next` | `Request.Api` (already in `Shared/Providers`) | No new reference needed — verify it's transitively available |
| `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` | `Request.Api` | Production key ring in Redis |

**Version discipline**: run `dotnet list package --vulnerable --include-transitive` at Phase 8 start. Any NU1902 warning is a hard block.

---

*End of PHASE_8_PLAN.md — awaiting approval before any `.cs` files are created.*
