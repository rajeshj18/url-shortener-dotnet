# TSK-06: Persistence (links database)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | **High** (DB schema and migrations, database users and permissions, ADR-0012 §4). Requires the ownership statement and logged prompt iterations. |
| **Depends on** | TSK-03, TSK-05 |
| **Estimate** | 3 h (re-estimated: ~1.35 h, `04` §5.1) |
| **Branch** | `task/TSK-06-persistence` |
| **Status** | Approved by the engineer on 2026-10-23, with R1–R5. Clarified at approval: number of database users and how the view limits columns (see §9). |

## 1. Intent
Store and load the domain model in the PostgreSQL `links` database:
- correct, versioned **migrations**, run as a one-off step **before** the services start (ADR-0009);
- **least-privilege database users**, so the Redirect service can only read its view;
- separate **write (primary)** and **read (replica)** paths (ADR-0002, LLD §4.3).

This is the first code tested against a **real PostgreSQL** (Testcontainers).

## 2. Technical context
- **LLD §4.1 / 4.3 / 4.4:** tables, the `redirect_lookup` view, statement routing, database users.
- **ADR-0004:** PostgreSQL 16, EF Core 8 + Npgsql, expand/contract migrations, primary and replica connections.
- **ADR-0009:**
  - the Api **owns** the `links` schema;
  - Redirect reads **only** `redirect_lookup` as a read-only user;
  - services **never migrate at startup**; migrations ship as an EF Core bundle run by a one-off `migrate` step (moved here from TSK-03).
- **LLD §6.7:** named data sources `LinksPrimary` (`Target Session Attributes=read-write`) and `LinksReplicas` (`prefer-standby`, `Load Balance Hosts=true`).
- **TSK-05:** the domain types (`ShortUrl`, `ShortCode`, `LongUrl`, …), which have private constructors and no EF attributes.

**Versions (latest .NET 8-compatible, checked today):** `Microsoft.EntityFrameworkCore` 8.0.32, `.Design` 8.0.32, `dotnet-ef` 8.0.32, `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11, `Testcontainers.PostgreSql` 4.16.0.

## 3. Refinements of the LLD (decisions for the engineer)
| # | LLD says | Proposed | Why |
|---|---|---|---|
| **R1** | `short_urls` has `expires_at` and an archiving index | **Leave out `expires_at` and its index.** TSK-14 adds them as an expand-only migration. | Expiry is the brownfield enhancement. Adding the column now leaves TSK-14 nothing real to change (same reasoning as TSK-05). |
| **R2** | One `IUrlRepository` with `FindForRedirectAsync` returning a full `ShortUrl` | **Two interfaces (interface segregation, SOLID):** `IShortUrlRepository` (write side and owner reads, primary) and `IRedirectLookup` (read side, replicas, with primary fallback). The latter returns a small **`RedirectTarget`** read model (code, target URL, state). | The `redirect_lookup` view deliberately leaves out `owner_id` and `created_at` (ADR-0009), so a full `ShortUrl` **can't** be built from it. Two interfaces also make the Redirect service's narrower rights visible in the code. |
| **R3** | 4 database users, created somewhere | **Roles** are created by the platform: a local init script (`docker/postgres/init/`), or IaC in production, with passwords from `.env` or the secret store. **Grants** live in the EF migrations, so permissions are versioned with the schema. The `worker` role arrives in stage 2. | Creating a role needs a password (a secret), which can't go into a migration file. Grants are schema-level and contain no secrets. |
| **R4** | `id_blocks` exists | Table and constraints here; **seeding block `B-001` and the block registry** in TSK-07 | Short-code generation owns its data |
| **R5** | Statement routing | **EF Core for writes and migrations; plain Npgsql for the redirect read path** (a single `SELECT` on the view through `NpgsqlDataSource`) | The read path runs ~35K/s at peak, and needs no change tracking or object mapping. EF stays where it adds value. |

`idempotency_keys` (the table) is created here, so the schema is complete. Its store is implemented in TSK-08.

## 4. Scope
**In scope:**

| # | Component | Purpose |
|---|---|---|
| 1 | Application ports | `IShortUrlRepository` (`TryInsertAsync` → `false` on a duplicate code; `FindByCodeAsync`; `SaveDeletionAsync`), `IRedirectLookup` (`FindAsync` on replicas, `FindOnPrimaryAsync`), `RedirectTarget` |
| 2 | Domain addition | `ShortUrl.Rehydrate(...)`, which rebuilds a stored link through the **same validated value objects**. No EF types reach Domain. |
| 3 | `LinksDbContext` + `ShortUrlRecord` (Infrastructure) | A separate persistence model, mapped to and from the domain by an explicit mapper. The domain stays free of EF. |
| 4 | Migration `InitialLinksSchema` | `short_urls` (PK `code`, `varchar(30)`); `id_blocks` (with its CHECK constraint and the **partial unique index of one active block per region**, ADR-0005); `idempotency_keys`; the **`redirect_lookup` view**; **grants** (`api`: read/write on the tables; `redirect`: `SELECT` on the view **only**) |
| 5 | Repositories | `EfShortUrlRepository` (primary) and `NpgsqlRedirectLookup` (replicas plus primary) |
| 6 | Composition | `AddLinksPersistence(...)` registers the named data sources and the DbContext; readiness health checks (`SELECT 1`, tagged `ready`) on the primary (Api) and the replicas (Redirect) |
| 7 | Migration bundle and `migrate` service | `.config/dotnet-tools.json` (`dotnet-ef` 8.0.32); `docker/migrate/Dockerfile` builds the `efbundle`; compose service `migrate` runs it as the **migrator** user; `api` and `redirect` wait for it with `service_completed_successfully` |
| 8 | Local roles | `docker/postgres/init/01-roles.sh` creates `urlshortener_migrator` / `_api` / `_redirect` from `.env`; `.env.example` gains their throwaway passwords |
| 9 | Integration tests (Testcontainers) | A real PostgreSQL 16.15: migrations apply cleanly; insert, find and delete round-trip; **a duplicate code makes `TryInsertAsync` return `false`**; **permissions: `redirect` can `SELECT` the view but is denied the table and any write, and `api` is denied DDL** |
| 10 | Docs | CHANGELOG; LLD §4/§5 updated for R1, R2 and R5; log entry with **prompt iterations** (high risk) |

**Out of scope:**
- seeding the ID block and generating codes (TSK-07),
- the API endpoints and the idempotency store (TSK-08),
- the redirect endpoint and its fallback token bucket (TSK-09),
- the analytics database (stage 2),
- a read-replica container (optional, later).

## 5. Constraints
- **Domain gets no EF dependency** (the architecture tests prove it). Application defines the ports; Infrastructure implements them.
- **Migrations are expand-only in stage 1.** Every migration must be safe while the previous service version is still running.
- No secrets in migrations, code or images. Connection strings come from configuration and environment only (ADR-0011).
- Services **never** call `Database.Migrate()`.
- SOLID and the middleware rules apply, as before.

## 6. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Applying the migration to an empty database creates exactly the LLD §4.1 schema (minus R1) | Integration test checks tables, view, constraints and the partial index |
| AC2 | Insert, find and delete round-trip through `IShortUrlRepository`, with every domain field preserved (UTC times, alias vs generated) | Integration tests |
| AC3 | **A duplicate code → `TryInsertAsync` returns `false`**, no exception (protects the TSK-08 alias flow) | Integration test |
| AC4 | `IRedirectLookup` returns the target and state for active and deleted links, and `null` for unknown codes, **connecting as the `redirect` user** | Integration tests |
| AC5 | **Least privilege:** as `redirect`, `SELECT` on `short_urls` and any `INSERT` are **denied** (SQLSTATE `42501`). As `api`, `CREATE TABLE` is **denied**. | Integration tests |
| AC6 | `docker compose up --wait`: `migrate` exits `0` before `api`/`redirect` start, and their `/health/ready` includes the database | Smoke script, plus `docker compose ps -a` |
| AC7 | A second `migrate` run is a no-op (idempotent) | Run twice, exit `0` |
| AC8 | All CI gates are green; the integration tests run in CI (Docker on the runner) | PR checks |
| AC9 | Docs updated (LLD R1, R2, R5); CHANGELOG; log entry with prompt iterations and actual hours **given when the PR is opened** | Review |

## 7. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Ports, mapper, DbContext, migration, repositories | ✅ (`implement.md`) | **Reviews every line** (high risk), especially the migration SQL and the grants |
| Testcontainers tests, including the permission tests | ✅ (`write-tests.md`) | Checks that AC5 really proves least privilege |
| Bundle, `migrate` service, init script | ✅ | Runs `docker compose up` (AC6) |
| Self-review and PR description | ✅ (`review.md`) | Ownership statement; gives hours when opening the PR |

**Tool:** Claude Code (Claude Opus 5.5). Prompt iterations are recorded in the log entry.

## 8. Risks
| Risk | Mitigation |
|---|---|
| A grant mistake gives Redirect too much access | AC5 tests the **denials**, not only the allowed actions |
| Migrations drift from the model | A test fails if EF reports pending model changes |
| Testcontainers needs Docker: slower tests, flakiness | One container per test class (a fixture); GitHub's runners include Docker |
| Roles don't exist when the grants run | The migration fails loudly if a role is missing. The init script runs first locally; the fixture creates the roles in tests. |

## 9. Clarifications at approval
- **Database users:** 3 in stage 1 (`urlshortener_migrator`, `urlshortener_api`, `urlshortener_redirect`), and `urlshortener_worker` from stage 2. Each is used by exactly one component. The superuser is only used once, by the init script, to create them.
- **Column restriction:** Redirect has **no rights on `short_urls`**, only `SELECT` on the `redirect_lookup` view. A view runs with its **owner's** privileges and exposes only the columns it defines. **The view must not use `security_invoker = true`** (PostgreSQL 15+), or it would run with the caller's rights and the protection would be lost. AC5 tests the denial. A view was chosen over column-level grants because it is a stable contract (ADR-0009).
