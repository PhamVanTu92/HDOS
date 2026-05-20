# Provider Bridge

gRPC server that acts as the secure boundary between the platform and external data providers. Authenticates provider connections using JWT, validates that the provider is registered, and streams request execution and progress events.

**Port:** 5400 (gRPC — providers connect here directly, not via Gateway)

---

## Responsibilities

- Expose the `ProviderService` gRPC interface (defined in `proto/provider.proto`).
- Authenticate each provider connection using a JWT signed with the provider's registered keypair.
- Validate that `jwt.sub` matches the provider's registered `providerId` (prevents stolen-token replay).
- Forward `ExecuteRequest` calls from Operation Router to the correct connected provider.
- Stream `ProgressEvent` messages from providers back to Operation Router (via RabbitMQ).
- Cache provider public JWKS from Request API (`/.well-known/jwks.json`) with a short TTL.

## gRPC interface

See [`proto/provider.proto`](../../proto/provider.proto) for the full service definition.

Key RPCs:

| RPC | Direction | Description |
|---|---|---|
| `Connect` | Provider → Bridge (streaming) | Provider opens a long-lived stream; bridge sends requests down it |
| `SendProgress` | Provider → Bridge (unary) | Provider sends a progress update for an in-flight request |
| `SendResult` | Provider → Bridge (unary) | Provider sends the final result |

## Key dependencies

| Dependency | Purpose |
|---|---|
| Request API | Fetch JWKS for provider JWT validation |
| Redis | Provider connection registry (which bridge instance holds which provider) |
| RabbitMQ | Consume `OperationRequested`, publish `OperationCompleted` + `ProgressPublished` |

## Configuration

| Key | Description |
|---|---|
| `Bridge:JwksUrl` | URL to fetch platform JWKS from (e.g., `http://request-api:5000/.well-known/jwks.json`) |
| `Bridge:GrpcUrl` | This bridge's own gRPC URL (for self-registration) |

## Health check

`GET /healthz` → 200.

## Security notes

- Provider Bridge does NOT reference `Shared/Auth` — it only holds provider public keys obtained from the JWKS cache.
- JWT `sub` MUST match the connecting provider's registered `providerId`. Mismatches are rejected with `Unauthenticated`.
- No private keys are stored in Provider Bridge. Private key material never appears in Bridge logs or configuration.
- gRPC channel is plain HTTP inside the Docker network. In production, enable TLS at the load balancer or configure Kestrel TLS certificates.

## Provider onboarding

See [docs/PROVIDER_ONBOARDING.md](../../docs/PROVIDER_ONBOARDING.md) and [docs/PROVIDER_PROTOCOL.md](../../docs/PROVIDER_PROTOCOL.md).
