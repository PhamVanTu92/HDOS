# PROVIDER_ONBOARDING.md — Operator's Manual
> Version: 7.0 | Audience: Platform administrators | Last updated: 2026-05-27

This document covers the full administrative lifecycle of an external provider: from first registration through monitoring, credential management, and decommission. An ops engineer with admin API access should be able to complete any procedure here without asking the platform team.

**Related documents:**
- `docs/PROVIDER_PROTOCOL.md` — what the provider team needs to read
- `api/bruno-collection/` — ready-to-run API requests for every step below

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
   - 1.1 Required access: admin JWT with role `platform-admin`
   - 1.2 Tools: Bruno (API client), `grpcurl` (gRPC testing), `jq` (JSON parsing)
   - 1.3 Information to collect from provider team before starting
     - Provider name and `providerId` (kebab-case, globally unique)
     - List of operations they will serve (exact patterns)
     - Per-operation: `paramsSchema`, `payloadSchema`, `timeoutMs`
     - Their expected `maxConcurrentRequests` and circuit breaker preferences

2. [Step 1 — Register the Provider](#2-step-1--register-the-provider)
   - 2.1 **Via Admin UI** (recommended):
     - Go to **Admin UI → Admin page → Providers tab → "Đăng ký Provider"**
     - Fill in: `providerId` (kebab-case, unique), `displayName`, `clientId` (usually same as providerId), `clientSecret` (temporary), operations list
     - Click **Đăng ký**
   - 2.2 **Via API**:
     ```http
     POST /api/v1/admin/providers
     Authorization: Bearer <admin-jwt>
     Content-Type: application/json

     {
       "providerId":   "ml-team-fraud",
       "displayName":  "ML Team — Fraud Scoring",
       "description":  "Real-time fraud scoring using gradient boosting model v3",
       "clientId":     "ml-team-fraud",
       "clientSecret": "temporary-placeholder",
       "operations":   ["ml.fraud.score", "ml.fraud.batchScore"],
       "timeoutMs":    5000,
       "priority":     5
     }
     ```
     Response: `{ "providerId": "ml-team-fraud", "registeredAt": "..." }`
   - 2.3 Verify: `GET /api/v1/admin/providers/ml-team-fraud` → confirm `status: "active"`

3. [Step 2 — Set Secret & Generate Bootstrap Token](#3-step-2--set-secret--generate-bootstrap-token)
   - 3.1 **Via Admin UI** (recommended):
     - Go to **Admin UI → select provider → tab "Credentials"**
     - Option A — **"Đặt secret cụ thể"**: enter desired secret → Save. Use this if provider team already has a secret they want to use.
     - Option B — **"Xoay key"**: platform generates a cryptographically random secret. The plaintext is shown **once** — copy it if using direct-secret delivery.
     - Then click **"Tạo Bootstrap Token"** → copy the token to deliver to provider team.
   - 3.2 **Via API**:
     ```http
     # Set a specific secret
     POST /api/v1/admin/providers/ml-team-fraud/credentials/set
     Authorization: Bearer <admin-jwt>
     { "newSecret": "chosen-secret-value" }

     # Or rotate (platform generates random secret, returns plaintext once)
     POST /api/v1/admin/providers/ml-team-fraud/credentials/rotate
     Authorization: Bearer <admin-jwt>

     # Generate bootstrap token
     POST /api/v1/admin/providers/ml-team-fraud/bootstrap-token/regenerate
     Authorization: Bearer <admin-jwt>
     # → { "providerId": "ml-team-fraud", "bootstrapToken": "abc123..." }
     ```
   - 3.3 **Deliver to provider team** — send only:
     - `bootstrapToken` (used once at provider startup to fetch the secret automatically)
     - `HDOS_HOST` (IP/hostname of the HDOS platform)
     - `tokenEndpoint`: `http://{HDOS_HOST}:5000/api/v1/providers/token`
     - `bridgeGrpcUrl`: `http://{HDOS_HOST}:5400`

     **Do NOT send `clientSecret` directly.** The provider calls the bootstrap API on startup:
     ```
     POST /api/v1/providers/bootstrap
     { "clientId": "ml-team-fraud", "bootstrapToken": "abc123..." }
     → { "clientSecret": "..." }
     ```
   - 3.4 If you ever need to verify the currently stored secret: `GET /api/v1/admin/providers/ml-team-fraud/credentials/reveal`
   - 3.5 Confirm receipt: provider team acknowledges in your ticketing system

4. [Step 3 — Register Provider's Operations](#4-step-3--register-providers-operations)
   - 4.1 One call per operation pattern
     ```http
     POST /api/v1/admin/operations
     Authorization: Bearer <admin-jwt>
     Content-Type: application/json

     {
       "operationPattern": "ml.fraud.score",
       "handlerType": "external",
       "providerId": "ml-team-fraud",
       "schemaVersion": "1.0.0",
       "paramsSchema": {
         "$schema": "https://json-schema.org/draft/2020-12/schema",
         "type": "object",
         "required": ["txnId", "features"],
         "properties": {
           "txnId": { "type": "string" },
           "features": { "type": "object" }
         }
       },
       "payloadSchema": {
         "$schema": "https://json-schema.org/draft/2020-12/schema",
         "type": "object",
         "required": ["score"],
         "properties": {
           "score": { "type": "number", "minimum": 0, "maximum": 1 },
           "explanation": { "type": "string" }
         }
       },
       "timeoutMs": 1000,
       "cacheable": false,
       "idempotent": true
     }
     ```
   - 4.2 Verify: `GET /api/v1/admin/operations?providerId=ml-team-fraud`
   - 4.3 Common mistakes: `paramsSchema` and `payloadSchema` must be valid JSON Schema draft 2020-12

5. [Step 4 — Verify Provider Connectivity](#5-step-4--verify-provider-connectivity)
   - 5.1 Ask provider team to obtain a token and confirm (do NOT do this yourself — you would expose the secret):
     ```sh
     curl -s -X POST https://platform/api/v1/providers/token \
       -H "Content-Type: application/json" \
       -d '{"clientId":"ml-team-fraud-c83hf","clientSecret":"...","grantType":"client_credentials"}' \
       | jq .
     ```
   - 5.2 Test gRPC connectivity

     Because `OperationProvider/Connect` is a **bidirectional streaming RPC**, a plain `grpcurl` invocation will hang. Use one of these three approaches instead:

     **Option A — Verify reflection only** (fastest sanity check):
     ```sh
     grpcurl provider-bridge.platform:443 list
     # Expected output:
     #   grpc.reflection.v1alpha.ServerReflection
     #   reporting.provider.v1.OperationProvider

     grpcurl provider-bridge.platform:443 describe reporting.provider.v1.OperationProvider
     # Expected output: full service descriptor
     ```

     **Option B — Test the Hello/Welcome handshake** (requires a valid provider JWT):
     ```sh
     # Send Hello via stdin; reads Welcome (or rejection) from stdout
     echo '{"hello":{"providerId":"ml-team-fraud","version":"1.0.0","supportedOperations":["ml.fraud.score"]}}' \
       | grpcurl -H "authorization: Bearer <jwt>" \
           -d @ \
           provider-bridge.platform:443 \
           reporting.provider.v1.OperationProvider/Connect
     # Expected: { "welcome": { "sessionId": "...", "maxConcurrentRequests": 50, "heartbeatIntervalSeconds": 30 } }
     ```
     This sends one message and reads the server's first response. The stream will hang after Welcome (waiting for more provider messages) — terminate with Ctrl+C, that is expected.

     **Option C — Use the platform's connectivity probe** (recommended for routine ops):
     ```sh
     curl -X POST https://platform/api/v1/admin/providers/ml-team-fraud/probe \
       -H "Authorization: Bearer <admin-jwt>"
     ```
     The platform performs the handshake itself using a synthetic JWT scoped to the providerId and reports back:
     ```json
     {
       "tlsHandshake": "ok",
       "jwtAccepted": "ok",
       "welcomeReceived": "ok",
       "latencyMs": 47
     }
     ```
     No provider JWT needed. This is what monitoring uses. See also `docs/PROTOCOL.md` §8.7 for the endpoint reference.
   - 5.3 Monitor audit log for first `token_minted` event:
     ```sql
     SELECT * FROM provider_credentials_audit
     WHERE provider_id = 'ml-team-fraud'
     ORDER BY at DESC LIMIT 10;
     ```

6. [Monitoring the First Hour](#6-monitoring-the-first-hour)
   - 6.1 Health endpoint — poll every 30s during initial onboarding
     ```http
     GET /api/v1/admin/providers/ml-team-fraud/health
     ```
     Expected: `{ "circuit": "closed", "lastSeenAt": "...", "inFlightCount": 0, "connectedInstances": 1 }`
   - 6.2 Metrics dashboard — Grafana: `Providers > ml-team-fraud`
     - Error rate: should be < 1% after warm-up
     - p95 latency: should be < registered `timeoutMs`
     - Circuit state: should be `closed`
   - 6.3 Alert thresholds to watch in first 30 minutes

     | Signal | Threshold | Action |
     |---|---|---|
     | Auth failures in audit log | > 5 in 1 min | Call provider team; possible secret issue |
     | Circuit state = `open` | Any | Suspend provider, investigate |
     | `lastSeenAt` stale > 60s | — | Provider may have crashed; check their logs |
     | Error rate | > 10% | Check provider logs; consider suspend |

   - 6.4 After 1 hour clean: remove escalation watch; standard Grafana alert rules apply

7. [Credential Rotation](#7-credential-rotation)
   - 7.1 When to rotate
     - Every 90 days (recommended policy)
     - When a team member who knew the secret leaves the provider team
     - On any suspected credential exposure
   - 7.2 Rotation procedure (Bootstrap Token method — zero-downtime)
     1. Notify provider team T-24h: "rotating credentials on [date/time]"
     2. In Admin UI → Credentials tab: click **"Xoay key"** (or use API `credentials/rotate`)
        - New secret is stored and encrypted in DB
     3. Click **"Tạo Bootstrap Token"** → send new token to provider team
     4. Provider team: update `HDOS_BOOTSTRAP_TOKEN` in their `.env` + restart service
        - On restart, provider calls bootstrap API → fetches new secret automatically
     5. Old secret remains valid for **60 seconds** (grace period for in-flight auth)
     6. Confirm new `token_minted` event in audit log
   - 7.3 Emergency rotation (suspected compromise)
     - Skip T-24h notice; call provider team directly
     - Consider temporary suspend (§9) while coordinating
     - Regenerate both secret and bootstrap token in Admin UI

8. [Emergency Credential Revocation](#8-emergency-credential-revocation)
   - 8.1 When to use: active credential compromise; provider misbehaving (sending cross-tenant data, DDoS-like behaviour)
   - 8.2 Steps
     1. `POST /api/v1/admin/providers/ml-team-fraud/credentials/revoke`
     2. All gRPC streams for this provider close within **5 seconds**
     3. In-flight requests: fail with `PROVIDER_DISCONNECTED` → DLQ
     4. Confirm in audit log: `action = "revoked"`
     5. Monitor: `GET /health` → `connectedInstances: 0`
   - 8.3 Impact on clients: operations served by this provider fail with `PROVIDER_UNAVAILABLE`
   - 8.4 Resolution path: investigate → rotate credentials (§7) → deliver new secret → provider restarts
   - 8.5 Do NOT use revocation as routine rotation — it has immediate service impact

9. [Suspending a Misbehaving Provider](#9-suspending-a-misbehaving-provider)
   - 9.1 Suspension pauses the provider without changing credentials — use when you need to stop traffic temporarily while investigating
   - 9.2 `POST /api/v1/admin/providers/ml-team-fraud/suspend`
     - In-flight requests complete normally
     - New requests fail with `PROVIDER_SUSPENDED` immediately
     - Provider's gRPC connection remains open (they stay connected, just get no new work)
   - 9.3 Resume: `POST /api/v1/admin/providers/ml-team-fraud/resume`
   - 9.4 Suspension does NOT reset the circuit breaker — if you resume after fixing a bug, manually close the circuit via metrics reset or wait for `breakDurationSeconds`

10. [Appendix A — CA Certificate Download](#10-appendix-a--ca-certificate-download)
    - Download at: `GET /api/v1/admin/ca-cert` (admin auth required)
    - Format: PEM
    - Used by provider teams to validate Bridge TLS

11. [Appendix B — Bruno Collection Reference](#11-appendix-b--bruno-collection-reference)
    - `api/bruno-collection/providers/01-register-provider.bru`
    - `api/bruno-collection/providers/02-register-operations.bru`
    - `api/bruno-collection/providers/03-obtain-token.bru`
    - `api/bruno-collection/providers/04-health-check.bru`
    - `api/bruno-collection/providers/05-rotate-credentials.bru`
    - `api/bruno-collection/providers/06-revoke-credentials.bru`
    - `api/bruno-collection/providers/07-suspend-resume.bru`
    - `api/bruno-collection/providers/08-decommission.bru`

12. [Decommissioning a Provider](#12-decommissioning-a-provider)
    - 12.1 Coordinate with client teams: identify all dashboards using this provider's operations
    - 12.2 Deprecate operations (clients begin receiving deprecation warnings)
      ```http
      PUT /api/v1/admin/operations/ml.fraud.score
      { "status": "deprecated", "deprecationMessage": "Use ml.fraud.score.v2 instead" }
      ```
    - 12.3 Wait 90 days (or agreed migration window); monitor usage via metrics
    - 12.4 Disable operations when usage reaches 0
      ```http
      PUT /api/v1/admin/operations/ml.fraud.score { "status": "disabled" }
      ```
    - 12.5 Suspend provider: `POST /admin/providers/ml-team-fraud/suspend`
    - 12.6 Revoke credentials: `POST /admin/providers/ml-team-fraud/credentials/revoke`
    - 12.7 Delete provider: `DELETE /api/v1/admin/providers/ml-team-fraud`
    - 12.8 Clean operation registry:
      ```http
      DELETE /api/v1/admin/operations?providerId=ml-team-fraud
      ```
    - 12.9 Confirm Grafana shows zero traffic for the provider for 24h before closing the ticket
