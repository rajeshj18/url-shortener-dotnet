# TSK-SIMPLE: Simplified rewrite (comparison branch)

| | |
|---|---|
| **Status** | Approved by the engineer in chat, 2026-10-24 ("rest go as planned"; purpose: comparison branch) |
| **Branch** | `task/TSK-SIMPLE-simplified-rewrite`, from `task/TSK-QW-quick-wins` |
| **Merge** | **Never merged to `main`.** It exists to compare a simpler structure with the SOLID one on `main`. |
| **Risk** | High (rewrites every layer). Same behaviour and contract, proven by the HTTP-level tests. |

## Goal
Implement the same FRs and NFRs with **less ceremony**: keep the domain model (DDD) and the behaviour, and
drop the abstractions that exist only for SOLID (ports with one implementation, decorators, mappers, one
extension class per feature).

## Decisions (engineer, 2026-10-24)
1. Base branch: `task/TSK-QW-quick-wins` (JSON logs and the stored-data `500` fix included).
2. Analytics transport: Kafka (Redpanda locally), as ADR-0008.
3. Three deployables are kept: Api, Redirect, AnalyticsWorker.
4. Comparison branch only. No ADR is drafted. Docs on `main` are not rewritten. This branch adds one
   comparison document and a README note.

## Structure
| Before (SOLID, `main`) | After (this branch) |
|---|---|
| Domain, Application, Infrastructure | **Domain** (unchanged, plain C#) and **Core** (feature folders) |
| 11 port interfaces, handler interfaces, resilience decorators | Concrete services: `LinkService`, `RedirectService`, `StatsService`, `CodeAllocator`, `ClickPublisher`, `ClickConsumer` |
| Persistence records plus a mapper class | Records stay (EF migrations need them). The service converts rows in a private method |
| Savepoints on the unit-of-work port | A savepoint inside `LinkService` |
| Five exception handlers | One `ErrorResponses` exception handler |
| One `AddXxx` class per feature | One `AddApiCore` / `AddRedirectCore` / `AddWorkerCore` |

## Scope
- FR-01 to FR-06 and FR-08 to FR-10: the same behaviour, contract and error codes as `main`.
- **FR-07 and NFR-08 (new):** Kafka click events, a worker that keeps hourly rollups exactly once (offsets in the
  same database transaction, LLD §6.5), and `GET /api/v1/urls/{code}/stats` for the owner (LLD §3).
- NFRs: the resilience, idempotency, ID-range and observability behaviour of `main` is kept.
- Deviation from the LLD, chosen for simplicity: the analytics tables live in an `analytics` **schema** created
  by the same migration bundle, not in a separate database with its own bundle. The worker still uses its own
  connection string (`ConnectionStrings:Analytics`), so production could point it elsewhere.

## Tests
- Kept: Domain unit tests, Feistel and Base62 tests, and the HTTP-level integration tests with unchanged
  expectations.
- Rewritten: tests that used fake ports now run against the concrete services and a real database.
- Rewritten: architecture tests (Domain has no dependencies; Core references no service; services don't
  reference each other; controllers don't use EF Core or Npgsql).
- New: analytics tests (rollups, exactly-once replay, stats endpoint, ownership, publisher never blocks).
- Coverage gate: Domain and Core at 80% or more.

## Out of scope
NFR-03 and NFR-04 (synchronous standby, multiple regions, failover drill) are infrastructure, not code, the
same as on `main`.
