# Pbg.Logging Release Plan

## Goal
Deliver a production-ready logger package with strong reliability, privacy controls, and operational observability.

## Release tracks

### v0.2.0 (Hardening Baseline) — 2 weeks
**Objectives**
- Fix cancellation and shutdown-drain behavior.
- Introduce `IHttpClientFactory` and configurable resilience policies.
- Lock down a minimal mandatory schema (6 core fields) and move the rest behind optional toggles.
- Add tests for options validation, filtering, and queue processing basics.

**Deliverables**
- Stable send loop that respects host shutdown.
- Documented retry/timeout defaults.
- Initial CI checks (`dotnet build`, `dotnet test`).

**Exit criteria**
- No lost logs on graceful shutdown in integration test scenario.
- Retry behavior verified with transient failure test server.

### v0.3.0 (Security & Schema) — 2 weeks
**Objectives**
- Add opt-in request/response body logging and masking/scrubbing hooks.
- Extend schema with `ResponseHeaders`, `CategoryName`, `EventId`, and request id.
- Add payload size limits and truncation strategy.

**Deliverables**
- Redaction middleware/options with examples.
- Backward-compatible schema evolution notes.

**Exit criteria**
- Security review sign-off for default-safe config.
- Contract tests validating enriched log payloads.

### v0.4.0 (Operational Excellence) — 2–3 weeks
**Objectives**
- Add internal telemetry (queue depth, dropped count, send latency).
- Add fallback sink strategy and circuit breaker behavior.
- Publish production deployment playbook.

**Deliverables**
- Health counters/diagnostics endpoints or hooks.
- Runbook for incident handling and endpoint outages.

**Exit criteria**
- Load test target sustained without memory growth regression.
- Observable alerting signals available for operations team.

### v1.0.0 (General Availability)
**Objectives**
- API stabilization, semantic versioning policy, migration guide.
- Multi-target LTS frameworks and package metadata polish.
- Final docs and examples for Web API, Worker Service, and Console.

**Go/No-Go checklist**
- Reliability SLOs met in soak tests.
- Security/privacy defaults validated.
- Performance benchmark baseline published.
- Backward compatibility reviewed and documented.

## Cross-cutting quality gates (every release)
- Build/test in CI on clean environment.
- Changelog + upgrade notes.
- Sample app smoke test.
- NuGet package validation (symbols, readme, license, tags).
