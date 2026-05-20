# Gateway

Public ingress for the Realtime Reporting Platform. Built on [YARP](https://microsoft.github.io/reverse-proxy/) (Yet Another Reverse Proxy) with JWT authentication, CORS, and per-IP rate limiting.

**Port:** 5500 (host) → 5500 (container)

---

## Responsibilities

- **Authentication gate** — all `/api/*` and `/hubs/*` routes require a valid JWT. Unauthenticated requests are rejected with 401 before reaching any backend service.
- **Claim forwarding** — extracts `tenant_id`, `scope`, and `sub` from the validated JWT and forwards them as `X-Tenant-Id`, `X-Token-Scope`, and `X-User-Id` headers to backend services.
- **Reverse proxy** — routes requests to the correct downstream cluster via YARP.
- **CORS** — applies per-origin allow-list from configuration.
- **Rate limiting** — global per-IP fixed-window limit protects all downstream services.
- **SSE buffering** — adds `X-Accel-Buffering: no` on `/sse/*` routes to prevent nginx/proxy buffering of event streams.

## Route table

| Path pattern | Backend cluster | Auth |
|---|---|---|
| `/api/v1/requests/**` | `request-api` | Required |
| `/api/v1/events/**` | `ingestion-api` | Required |
| `/api/v1/admin/**` | `request-api` | Required |
| `/.well-known/jwks.json` | `request-api` | Anonymous |
| `/sse/requests/**` | `request-api` | Required |
| `/hubs/main` | `realtime-hub` | Required |
| `/health` | _(local endpoint)_ | Anonymous |

## Configuration

| Key | Default | Description |
|---|---|---|
| `Auth:Authority` | _(required)_ | OIDC authority URL. Empty = JWT validation disabled (dev only). |
| `Auth:Audience` | `reporting-platform` | Expected `aud` claim in JWT. |
| `Cors:AllowedOrigins` | `["http://localhost:3000", "http://localhost:5173"]` | Origins allowed for CORS preflight. |
| `ReverseProxy:Clusters:*:Destinations:primary:Address` | _(docker internal)_ | Override for testing; set per-cluster in `appsettings.json`. |

## Health check

`GET /health` → 200. No authentication required. Used by Docker Compose dependency checks.
