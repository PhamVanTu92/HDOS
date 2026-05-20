# Ship Status — Reporting Platform

## Verified in dev environment (no Docker)
- 240 tests passing, 0 failing
- 0 High+ CVE vulnerabilities (20 projects)
- Clean build, 0 warnings, 0 errors, -warnaserror
- All documentation complete

## Pending verification (requires Docker)
These 3 ship-gate items + 5 skipped integration tests require a Docker-enabled
environment (CI or local Docker Desktop). Run before production deployment:

### Skipped tests (5) — un-skip and run with Docker:
| Test | Verifies | Command |
|------|----------|---------|
| T7 | Provider registry Redis pub/sub reload | `dotnet test --filter T7` |
| T8 | Operation registry invalid-schema graceful skip | `dotnet test --filter T8` |
| IN12 | event_subscriptions FK CASCADE | `dotnet test --filter IN12` |
| SI1 | Provider Bridge connectivity (bridge + infra required) | `dotnet test --filter SI1` |
| SI2 | External provider adapter gRPC E2E | `dotnet test --filter SI2` |

> **PH4** (DashboardResolver real Postgres query — §12.1c) was planned but never
> implemented: no stub exists in `tests/Resolver.Tests/`. Resolver.Tests runs 24/24
> with mock-based unit tests only. PH4 body must be written in a Docker-enabled
> environment (add `PostgreSqlContainer`, write the query integration test, verify).

### Docker-gated ship criteria (3):
1. `docker compose up -d && docker compose ps` → all 10 services healthy
2. `scripts/smoke-tests.sh` → 5/5 scenarios pass
3. §12.2 scenario 2 (external provider → SignalR fan-out E2E) explicitly passing

## How to complete verification
On a Docker-enabled machine:
```bash
docker compose build
docker compose up -d
docker compose ps                           # verify 10 healthy
dotnet test --filter "RequiresDocker=true"  # un-skips the 5 deferred
bash scripts/smoke-tests.sh                 # 5/5 scenarios
```
