# Testing strategy

How the URL shortener is tested, which gates enforce it, and what isn't tested. Written at the end of stage 1
(TSK-QW), from the code and CI as they are; numbers were measured on 2026-10-23.

## 1. Principles
- **Tests are written in the same task as the code**, never as a later task ([definition of done](04-task-breakdown.md) §2).
- **The database is real.** Integration tests run against PostgreSQL 16.15 in a container (Testcontainers), with the same roles script and migrations as production. Nothing that touches SQL is mocked.
- **Prove that the tests can fail.** High-risk tasks applied deliberate bugs (mutation checks), and bug fixes start with a failing test.
- **Gates, not good intentions.** CI blocks a merge on a failed or skipped test, a coverage drop, or a failed security or performance check.

## 2. Test suites

| Suite | Tests | What it covers | Tools |
|---|---|---|---|
| `UrlShortener.UnitTests` | 308 | Domain rules (codes, aliases, URLs, expiry, state), use cases with in-memory fakes, Base62/Feistel, resilience rules, handlers | xUnit 2.9.3, FluentAssertions 7.2.2 |
| `UrlShortener.ArchitectureTests` | 19 | ADR-0009's layering: Domain references nothing, Application no infrastructure, services not each other, controllers only Application | NetArchTest, project-file parsing |
| `UrlShortener.IntegrationTests` | 109 | The HTTP API of both services, persistence, migrations, database roles, resilience under faults | Testcontainers (PostgreSQL), `WebApplicationFactory` |
| `UrlShortener.Benchmarks` | — | Short-code generation cost (~2.1 µs per code) | BenchmarkDotNet |
| `tests/load/` | 5 scripts | Latency at load, hot key, scan, creates; the CI smoke | k6 2.3.0 |

**Total: 436 tests**, 0 failed, 0 skipped.

**Coverage** (unit tests): Domain **100%**, Application **97.4%** (the gate is ≥ 80%). Infrastructure is 33% from
unit tests alone; most of it is exercised by the integration tests instead, which the gate doesn't measure.

## 3. Techniques, and where they are used

| Technique | What it proves | Example |
|---|---|---|
| **Property-style tests** (seeded loops) | An invariant over many inputs | No duplicate codes across 1M consecutive IDs; Feistel round-trip over 100k IDs (TSK-07) |
| **Concurrency tests** | Correctness under races | Concurrent ID leases never overlap; 12 simultaneous requests for one alias → one `201`, eleven `409`; 10 identical requests with one idempotency key → one link (TSK-07, TSK-08) |
| **Fault injection** | Behaviour during outages | A paused PostgreSQL container → `503` within the timeout, recovery without a restart; a dead primary → the breaker opens and redirects keep serving (TSK-11) |
| **Least-privilege denials** | Roles can't do what they shouldn't | The Redirect role can read only the view; the Api role has no DDL (SQLSTATE 42501, TSK-06) |
| **Migration tests** | Schema changes are safe | Expected schema; expand-only expiry migration rolled back and forward on a scratch database (TSK-06, TSK-14) |
| **Contract tests** | The API doesn't drift | Committed OpenAPI document compared with the generated one; error codes and problem format across every code path (TSK-08, TSK-15) |
| **Never-logged tests** | Sensitive data stays out of logs | API keys and long URLs absent from captured logs (TSK-08, TSK-QW) |
| **Mutation checks** | The tests would catch a real bug | Each high-risk task applied 3 deliberate bugs and ran its tests (TSK-07, 08, 09, 11, 14), and TSK-10 one (the owner check); every one was caught |
| **Failing test first** | A bug fix really fixes the bug | TSK-15: the PR's first CI run was red on the new tests; TSK-QW #7: shown red locally |
| **Load tests** | Latency SLOs at load | Redirect p99 3.8 ms at 1,500/s, create p99 7.1 ms at 600/s ([results](perf/v1-load-test.md)) |
| **Clean-clone run** | The README works on a new machine | Fresh clone, `.env` from the example, fresh database: stack up in 63 s, smoke and "Try the API" pass (TSK-QW) |

**Test isolation:** one PostgreSQL container per test run; tests that change shared state (ID blocks, dropped
databases) use their own **scratch database**; integration tests share one Api host, because EF Core refuses more
than 20 internal service providers per process (TSK-08).

## 4. CI gates (every pull request)

| Job | Gate | Fails on |
|---|---|---|
| `build-test` | Build with warnings as errors, `AnalysisMode=All`; `dotnet format`; all three test suites; the test-metrics gate; the ID-block registry check | Any warning, format drift, failed or skipped test, Domain/Application coverage < 80%, overlapping ID blocks |
| `codeql` | CodeQL `security-and-quality` | Security alerts (notes are fixed in the PR) |
| `secrets` | gitleaks 8.30.1 over the PR's history | Any finding (false positives go into `.gitleaksignore` by fingerprint, after review) |
| `dependencies` | NuGet audit + `dotnet list package --vulnerable` | Any known vulnerability |
| `perf-smoke` | Compose stack + k6 smoke (15 s warm-up, 30 s measured) | Redirect p99 ≥ 50 ms or create p99 ≥ 200 ms; proven to fail on an impossible threshold (PR #20) |

All five are required checks on `main`. Actions are pinned to commit SHAs and runners to `ubuntu-24.04`.

## 5. What isn't tested, and why

| Not tested | Why | Mitigation |
|---|---|---|
| Real replication and automatic failover | Locally the "replicas" are the primary; Patroni/etcd aren't part of the prototype | Fault injection covers the services' side; the replica-lag monitor isn't built (README limitation) |
| Production scale (35K redirects/s) | One laptop shares CPU between the load generator and the services | Per-instance numbers, extrapolated with a stated method ([load test](perf/v1-load-test.md)) |
| Analytics pipeline (Kafka, worker) | Not built (stage 2 descoped) | Click events are built and counted by a no-op publisher |
| Container image scanning, DAST | Planned for stage 2 (Trivy, ZAP), not built | CodeQL, gitleaks and the dependency audit run on every PR |
| Automated mutation testing | Stryker was planned for stage 3 | Manual mutation checks on every high-risk task |
| Infrastructure coverage as a gate | Report only (TSK-17 D1, not built) | Integration tests exercise it; not merged into the gate |
