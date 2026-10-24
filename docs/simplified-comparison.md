# Simplified structure: comparison with `main`

> **Comparison branch, not for merging** (TSK-SIMPLE, approved 2026-10-24). This branch re-implements the same
> requirements without the SOLID ceremony, so the two structures can be compared side by side. Decisions and scope
> are in [the task spec](tasks/TSK-SIMPLE-simplified-rewrite.md).

## What changed
| | `main` (Clean Architecture + SOLID) | This branch (DDD, concrete services) |
|---|---|---|
| Projects | Domain, Application, Infrastructure, 3 services | **Domain**, **Core**, 3 services |
| Production C# files (without migrations) | 84 | 40 |
| Production lines (without migrations) | 5,457 | 4,698, **including analytics** (Kafka publisher, worker, stats: ~840 lines), which `main` doesn't have |
| Public interfaces of our own | 14 (11 ports, 3 handler interfaces) | **0** |
| Resilience | Decorators around handler interfaces, resolved per attempt from keyed DI scopes | `DatabaseGuard.ExecuteWithRetryAsync(...)` called inline; a fresh `DbContext` per attempt from `IDbContextFactory` |
| Persistence | Repository, unit of work, idempotency store, record mapper (4 classes, 4 ports) | `LinkService` uses the `DbContext` and SQL directly; one private method converts rows |
| Error mapping | 5 exception handlers | 1 (`ErrorResponses`) |
| Wiring | One `AddXxx` extension class per feature (6) | `CoreServices.AddApiCore` / `AddRedirectCore` / `AddWorkerCore` |

**Unchanged:** the Domain project (already plain C# value objects and an aggregate with guard clauses), the API
contract (the committed OpenAPI document only gains the stats endpoint), the `links` schema, error codes, the ID-range
and Feistel scheme, idempotency semantics, breaker and timeout values, health endpoints, telemetry.

## Reading one request
Create, on `main`: `ShortUrlsController` → `ICreateShortUrlHandler` (= `ResilientCreateShortUrlHandler`) →
`PrimaryDatabaseResilience` → `DatabaseCircuitBreaker` → new DI scope → keyed `ICreateShortUrlHandler`
(= `CreateShortUrlHandler`) → `IUnitOfWork`, `IIdempotencyStore`, `IShortCodeGenerator`, `IShortUrlRepository` →
`EfUnitOfWork`, `EfIdempotencyStore`, `ShortCodeGenerator` → `IIdRangeLeaser` → `PostgresIdRangeLeaser`,
`EfShortUrlRepository` → `ShortUrlRecordMapper`. About 14 types across 3 projects.

Create, here: `ShortUrlsController` → `LinkService.CreateAsync` → `DatabaseGuard` → `LinksDbContext` and
`CodeAllocator`. 5 types, all in `Core/Links`, `Core/Codes` and `Core/Database`.

## What was lost, honestly
| SOLID benefit on `main` | Consequence here |
|---|---|
| Use cases unit-tested with fake ports, no database (fast, isolated) | Those tests are now integration tests against PostgreSQL (Testcontainers). Slower (seconds, not milliseconds) and need Docker, but they test the real SQL, locks and transactions |
| Swap an adapter without touching the use case (e.g. a cache in front of the redirect lookup, ADR-0006) | A cache would be added inside `RedirectService`, or an interface introduced then, when the second implementation actually exists |
| Architecture tests guaranteed "use cases know no EF Core, Npgsql or Kafka" | Only the domain and the controllers are kept framework-free by the architecture tests; `Core` mixes use-case logic and data access by design |
| Timeouts tested with hanging fakes | Provoked for real: a create waits on another transaction's row lock; a redirect waits on a table lock. This exposed a **finding that also applies to `main`**: a database that accepts TCP but never answers the startup message is bounded by the 2 s connect timeout, not by the 500 ms request budget, because Npgsql doesn't abandon a connection attempt on cancellation. Still a `503`, never a hung request (test: `A_database_that_hangs_during_connect_is_bounded_by_the_connect_timeout`) |

## Added on this branch (FR-07, NFR-08)
- Redirect publishes `ClickEvent v1` to Kafka (`click-events.v1`, key = code) through `ClickPublisher`: the Kafka
  client queues locally and never blocks; a full queue drops and counts the click. Trace context in `traceparent`.
- The AnalyticsWorker consumes in batches (≤ 5 s) and adds hourly, referrer and device rollups **in the same
  transaction as the partition offsets** (LLD §6.5): records below the stored offset are skipped, so a replay after a
  crash or rebalance never counts twice. A malformed record is skipped and its offset still advances.
- `GET /api/v1/urls/{code}/stats?from=&to=&granularity=hour|day` for the owner: totals, series, top 10 referrers,
  devices, `dataFreshAsOf`. Ranges over 90 days are `400 stats.range`.
- Storage: an `analytics` schema created by an expand-only migration (`AddAnalytics`), with a new least-privilege
  role `urlshortener_worker`. **Deviation from the LLD, for simplicity:** one database and one migration bundle
  instead of a separate analytics database; the worker still has its own connection string.
- Local stack: Redpanda (Kafka API, memory capped at 512 MB) and the worker in `docker-compose.yml`.

## Tests
See the counts in the [log entry](ai-usage/traceability-log.md) (TSK-SIMPLE). In short: domain and codec unit tests
are kept as they were; the HTTP-level integration tests are kept with **unchanged expectations** (the parity proof);
handler tests on fakes became service tests on PostgreSQL; new tests cover the publisher, the consumer's exactly-once
batches, the stats endpoint, database roles, and one end-to-end path through a real broker.

**Local limitation:** Kafka's native client (librdkafka) has no Windows-on-ARM64 build. On such a machine, the tests
that create a Kafka client fail with `DllNotFoundException`; run the suite in a Linux container or on x64. CI (Linux
x64) is not affected. The Alpine images install `cyrus-sasl`, because the Alpine build of librdkafka links against
`libsasl2` (found by the compose run, not by the tests).

**Verified on the full stack** (`docker compose`, separate project): all containers healthy; a created link followed
twice by browsers, once by a bot and once with `HEAD` showed exactly 2 clicks in the stats after 3 s; logs are JSON
with no long URL, IP address or API key; the stack uses about 1.7 GB of memory.

## Verdict
For a service of this size, the simpler structure is easier to read and change: one request touches a third of the
types, and nothing is abstract without a reason. The SOLID version earns its cost when adapters really vary (caches,
several stores, several brokers) or when fast, database-free use-case tests matter more than testing the real SQL.
The decision belongs to the engineer; this branch exists to make it concrete.
