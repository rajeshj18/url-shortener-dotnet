# ADR-0009: Three services, Clean Architecture layering

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0002, ADR-0003, ADR-0004, ADR-0007, ADR-0008, ADR-0013, NFR-12

## Context
Reads are 10× writes and have tighter latency targets. We want reads and writes to scale and deploy
independently, without keeping separate codebases. ("Service" here means a separately deployable .NET
application. In code, .NET calls it a *host*.)

## Decision
**The URL Shortener is one bounded context, deployed as three services.** They scale and deploy
independently, but they share one domain and, by design, the `links` database. They are **not
microservices in the database-per-service sense**: this is a read/write split of one domain, in a light
CQRS style.

| Service | Container image | Responsibility | Data access |
|---|---|---|---|
| **Redirect** | `urlshortener-redirect` | `GET` and `HEAD /{code}` (ADR-0007) | Reads the `redirect_lookup` view on `links` read replicas (falls back to the primary on a miss), as a **read-only** database user; produces to Kafka |
| **Api** | `urlshortener-api` | Create, get, delete, stats; API-key auth | **Owns** the `links` schema; writes on the primary; reads `analytics` for stats as a **read-only** user |
| **AnalyticsWorker** | `urlshortener-worker` | Consumes click events and writes rollups | Consumes from Kafka; **owns** the `analytics` schema |

All three expose `/health/live` and `/health/ready`.

### Shared databases, with rules
- **Each schema has exactly one owner** (`links`: Api; `analytics`: AnalyticsWorker). Only the owner's migrations change it.
- **Other services read through a contract and a read-only database user.** Redirect may only `SELECT` from the `redirect_lookup` view (`code`, `long_url`, `expires_at`, `deleted_at`). The Api can then reshape `short_urls` freely, as long as the view keeps returning the same columns.
- **Schema changes are expand-then-contract**, so a service running the previous version keeps working.

### Migrations: one job, run before deployment
- No service migrates its schema at startup. That would race when many instances start together, and it would need schema-change (DDL) rights.
- CI builds **EF Core migration bundles** (`efbundle`, one per database). The pipeline runs them **once**, before rolling out services. Locally, docker-compose runs them as a one-off `migrate` step.
- Only the migration job's database user has DDL rights. Service users can only read and write data.

### Deploying services independently
CI builds three images per commit, all tagged with the commit SHA. Each service is rolled out on its own.

| Change | Rebuilt images | Deployment order | Effect on other services |
|---|---|---|---|
| One service's code only | That service | That service | None |
| Shared libraries | All three | Any service, at any time | None, if backward-compatible (each image contains its own copy of the libraries) |
| Database schema | — | Migration (expand) first, then the owning service | None (expand-then-contract) |
| `ClickEvent` format | Redirect, Worker | Worker first, then Redirect | None. Only optional fields may be added, consumers ignore unknown fields, and a breaking change uses a new topic (`click-events.v2`). |

### Layering (enforced in CI by architecture tests, NetArchTest)
| Project | May reference | Must not reference |
|---|---|---|
| Domain | Nothing (plain C#) | Any framework (ASP.NET Core, EF Core, Npgsql, Kafka) |
| Application | Domain | Infrastructure, EF Core, Npgsql, Kafka |
| Infrastructure | Application, Domain | Any service project |
| Services (Api, Redirect, Worker) | All three libraries. `Program` is the composition root. | Each other. Controllers use Application interfaces, never Infrastructure classes directly. |

## Alternatives considered
| Option | Why not chosen |
|---|---|
| Single service | Reads can't scale or deploy separately from writes |
| One image, role chosen by configuration (`--role=redirect`) | Every instance would carry all dependencies, and Redirect, the most exposed service, would include write and stats code paths and more secrets. It would also start more slowly. The only gain is one build instead of three. |
| Microservices with a database each (Redirect keeps its own copy of the links, fed by events) | Read-your-writes (NFR-07) and the 5 s delete target (NFR-06) would also depend on event delivery. More infrastructure for one team and one domain. |
| Separate repositories per service | Domain rules would be duplicated, with more overhead |
| Each service migrates at startup | Races between instances; services would need DDL rights |

## Consequences
- The schemas, and the `ClickEvent` contract, must stay backward-compatible. This is a review item in the PR checklist (ADR-0012).
- The database is shared, so a heavy query from one service can affect the others. The routing in LLD §4.3 (replicas for redirects, a separate `analytics` database) limits this.

## History
- 2026-10-21 · Proposed.
- 2026-10-22 · Proposed · Revised after a consistency review: one bounded context with three services (not microservices); one owner per schema with read-only users and the `redirect_lookup` view; migration bundle run before deployment; rules for independent deployment and event compatibility; `GET`/`HEAD`; health endpoints; explicit dependency rules; rejected one-image-with-roles alternative; "host" renamed to "service".
- 2026-10-22 · **Accepted** · Engineer signed off.
