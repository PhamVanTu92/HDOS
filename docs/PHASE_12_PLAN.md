# PHASE_12_PLAN.md — Validation, Load Testing & Deliverables
> Status: PLACEHOLDER | Author: Claude Sonnet 4.6 | Date: 2026-05-19

This file is pre-created to capture mandatory checklist items that must be completed in Phase 12 but whose context was established in earlier phases.

---

## Mandatory carry-forward items

### Phase 3 deferred integration tests

These tests were written in Phase 3 but could not be executed because Docker was unavailable in the Phase 3 development environment. They MUST be run on a Docker-enabled machine before Phase 12 is declared complete.

- [ ] Re-run Phase 3 deferred integration tests on Docker-enabled environment:
  - [ ] **T7**: `Providers.Tests.Registry.ProviderRegistryTests.T7_RedisPubSubTriggersReload`
        — Publishes `"all"` to `operation-registry:updated` Redis channel; asserts `ReloadAsync` called within 500ms
  - [ ] **T8**: `Providers.Tests.Registry.OperationRegistryReloadTests.T8_InvalidSchemaInDb_GracefulSkip_ValidRegistrationsReachable`
        — Seeds one valid + one invalid `params_schema`; asserts reload does not throw; asserts valid registration reachable; asserts invalid registration has `CompiledSchema = null`
  - Both must produce `Test Run Successful` output with 0 failures
  - If either fails, fix immediately — do not declare Phase 12 done with regressions
  - Run command: `dotnet test tests/Providers.Tests/ --filter "RequiresDocker=true"`

### Phase 6 deferred: validation result caching measurement

Per `PHASE_3_PLAN.md §6.4`: if NBomber load tests reveal p95 `JsonSchemaParamsValidator.ValidateAsync` latency > 10ms, add `ConcurrentDictionary<(string, string), ValidationResult>` cache in `Shared/Caching/ValidationCache.cs`. Do NOT add this cache before measurement.

- [ ] Run NBomber scenarios targeting operations with non-trivial `params_schema`
- [ ] Record p50/p95/p99 of `ValidateAsync` latency
- [ ] If p95 > 10ms: implement `ValidationCache` and re-run
- [ ] If p95 ≤ 10ms: document result and close this item

---

## Full Phase 12 scope (to be expanded when planned)

This section will be expanded when Phase 12 is formally planned. Placeholder sections:

- Load testing with NBomber
- End-to-end integration test suite
- Performance baselines
- Security review
- Documentation completeness audit
- Phase 3 deferred tests (above)
- Deployment readiness checklist
