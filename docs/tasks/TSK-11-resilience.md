# TSK-11: Resilience (timeouts, retries, circuit breakers) and idempotency-key cleanup

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | **High** (behaviour during failover; a wrong retry could create a duplicate link) |
| **Depends on** | TSK-08, TSK-09 |
| **Estimate** | 3 h |
| **Branch** | `task/TSK-11-resilience` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-09 to TSK-13) |

## 1. Intent
When the primary fails over, the services answer fast `503`s with `Retry-After` instead of hanging, retry
only where a retry can't change the outcome, and recover on their own. Redirects keep working from the
replicas throughout. The idempotency table is kept to its 24 h window.

## 2. Technical context
- **LLD §6.7** (connection routing, the timeout table, the retry table, circuit-breaker settings, cancellation, failover sequence), **§6.6** (housekeeping under `pg_try_advisory_lock`), §8 (`Resilience:*`).
- **ADR-0002** (synchronous standby: a committed create survives failover), **ADR-0010** (why a create with an `Idempotency-Key` can be retried even when the commit outcome is unknown), **ADR-0013** (`circuit_state` metric).
- **Existing code:** `CreateShortUrlHandler` + `IUnitOfWork` (TSK-08), delete (TSK-10), `ResolveShortUrlHandler` with the primary fallback (TSK-09), problem handlers (TSK-08).

## 3. Scope
| # | Component | Purpose |
|---|---|---|
| 1 | Npgsql timeouts | Connect 2 s, command 1 s, and a server-side `statement_timeout` of 2 s for the Api role (see D1) |
| 2 | Request-timeout middleware | Api 3 s, Redirect 500 ms → `503` + `Retry-After` (ASP.NET Core `RequestTimeouts`) |
| 3 | `TransientDatabaseErrors` classifier | Connection failures, timeouts, "in recovery" / admin shutdown, serialization and deadlock SQLSTATEs → transient; everything else not |
| 4 | Polly v8 pipelines (`Microsoft.Extensions.Resilience`) | Api → primary: timeout → retry → breaker. Redirect → primary fallback: breaker only. Settings from LLD §6.7 (≥ 50% failures in 10 s, ≥ 20 calls, open 5 s ± 1 s). On open: clear the Npgsql pools. |
| 5 | Create retry rule (decorator on `IUnitOfWork`) | Failure **before** commit → retry once; failure **during** commit → retry once only with an `Idempotency-Key`, else `503` |
| 6 | Delete retry | Once (idempotent) |
| 7 | Error mapping | Transient error after retries, or `BrokenCircuitException` → `503` + `Retry-After: 5` (Api) / `1` (Redirect replica miss while the breaker is open) |
| 8 | `IdempotencyKeyCleanupService` (`BackgroundService` in the Api) | Every 5 min, under `pg_try_advisory_lock`, delete rows older than 24 h in batches of ≤ 50K; metrics rows deleted and duration |
| 9 | Metrics | `circuit_state` per dependency, breaker open/close events (OpenTelemetry meter) |
| 10 | Docs | LLD §6.7 implementation notes, CHANGELOG, log |

**Out of scope:** automatic database failover locally (Patroni; README limitation); the failover demo script (TSK-21); dashboards (TSK-22/27).

## 4. Constraints
- Never retry `4xx` outcomes, never retry while the breaker is open, never retry a create whose commit outcome is unknown **without** a key.
- An open breaker doesn't make the instance not-ready (LLD §6.7).
- Policies and thresholds come from configuration (`Resilience:*`) with the LLD values as defaults.
- Cancellation from the client still rolls back (already passed through; a test proves it).

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Retry rule: before-commit failure → 1 retry; during-commit with key → 1 retry and **no duplicate**; during-commit without key → `503`, no retry | Unit tests with a fake unit of work |
| AC2 | Breaker opens after the configured failures, rejects fast while open, half-opens after the break, closes on success | Unit tests with a fake `TimeProvider` |
| AC3 | **Fault injection:** with the database paused, create returns `503` + `Retry-After` within the request timeout; after resume, creates succeed again without restart | Integration test (see D2) |
| AC4 | Redirect: replica path keeps serving while the primary fallback's breaker is open; a replica miss then returns `503` + `Retry-After: 1` | Unit + integration test |
| AC5 | Cleanup: rows > 24 h deleted, newer kept; two instances at once → only one runs the batch | Integration test |
| AC6 | Request timeout → `503` (Api 3 s, Redirect 500 ms) | API test with an artificially slow fake |
| AC7 | Gates green; coverage ≥ 80%; docs; log with prompt iterations and hours | CI and review |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Classifier, pipelines, decorators, cleanup service | ✅ (`implement.md`) | Reviews every line (high risk), especially the commit-phase rule |
| Unit and fault-injection tests | ✅ (`write-tests.md`) | Checks AC1/AC3 prove "no duplicate, fast 503, self-recovery" |
| Self-review, PR | ✅ (`review.md`) | Ownership statement; hours |

## 7. Risks
| Risk | Mitigation |
|---|---|
| A retry creates a duplicate link | Commit-phase rule + idempotency key; AC1 asserts a single row |
| Breakers flap or all instances leave the load balancer | Randomised break time; readiness ignores breaker state |
| Fault tests are slow or flaky | Dedicated container for fault tests; generous but bounded waits |

## 8. Decisions for the engineer
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | How to set `statement_timeout = 2s` for the Api role | (a) in the connection string (`Options=-c statement_timeout=2000`); (b) `ALTER ROLE` in the roles script (needs a superuser, applies only to new volumes) | **(a).** Versioned with the app's configuration and needs no superuser. |
| D2 | Fault injection | (a) pause/unpause a **dedicated** PostgreSQL container (Testcontainers); (b) a Toxiproxy container in front of the database | **(a).** Realistic (connections hang, like a failed node) with no extra component; the dedicated container keeps the shared tests unaffected. |
