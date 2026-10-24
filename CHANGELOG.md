# Changelog

All notable changes to this project are recorded here, following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/).
Each entry names the task (`TSK-xx`) that made the change.

## [Unreleased]

### Changed (comparison branch only, TSK-SIMPLE; never merged to `main`)
- Application and Infrastructure replaced by one feature-organised **Core** project with concrete services (`LinkService`, `RedirectService`, `StatsService`, `CodeAllocator`, `ClickPublisher`, `ClickConsumer`); 0 own interfaces instead of 14. Domain, API contract, `links` schema and error codes unchanged. See [docs/simplified-comparison.md](docs/simplified-comparison.md).
- **Analytics (FR-07, NFR-08):** Redirect publishes `ClickEvent v1` to Kafka without blocking; the AnalyticsWorker keeps hourly, referrer and device rollups exactly once (offsets in the same transaction); `GET /api/v1/urls/{code}/stats` for the owner (`stats.range` for bad ranges). Expand-only migration `AddAnalytics` (`analytics` schema, role `urlshortener_worker`); Redpanda and the worker in compose.

### Added
- Close-out documents (TSK-QW): [final engineering summary](docs/08-final-summary.md), [testing strategy](docs/05-testing-strategy.md), [risks and trade-offs](docs/06-risks-tradeoffs.md), [scenario 2 write-up](docs/scenarios/02-brownfield.md); the project status marks TSK-16 onward as descoped.
- JSON console logging with UTC timestamps in all three services, as ADR-0013 specifies (TSK-QW).
- **Link expiry** (TSK-14, FR-04): optional `expiresAt` on create (future, at most 10 years ahead: `expiry.past`, `expiry.too-far`); after it, redirects answer `410` like deleted links; `expiresAt` in the create and metadata responses and in the idempotency hash. Expand-only migration `AddLinkExpiry` (nullable `expires_at`; `redirect_lookup` gains the column). `expiry.unsupported` is no longer emitted.
- Scenario 1 (greenfield) write-up: ask → normalization → decomposition → AI-assisted execution → validation → outcome, with estimates vs actual hours ([docs/scenarios/01-greenfield.md](docs/scenarios/01-greenfield.md)); requirements matrix updated (TSK-13).
- **v1 load test and CI performance gate** (TSK-12, ADR-0006).
  - k6 scripts (`tests/load/`): redirect, hot key, scan, create, and a 30 s CI smoke; each warms up for 15 s and measures against the SLO thresholds.
  - `scripts/load-test.sh` / `.ps1` run them in `grafana/k6:2.3.0` on the compose network.
  - Results: redirect p99 3.8 ms at 1,500/s, create p99 7.1 ms at 600/s, 0% errors ([docs/perf/v1-load-test.md](docs/perf/v1-load-test.md)). ADR-0006: no cache for v1.
  - CI job `perf-smoke` (compose stack + k6 smoke on every PR).
  - Metric `redirect_primary_fallback` (`outcome=granted|denied`) for the fallback budget.
- **Resilience** (TSK-11, LLD §6.6–§6.7).
  - Database timeouts on every connection (connect 2 s, command 1 s; `statement_timeout = 2s` for the Api writer); request timeouts (Api 3 s, Redirect 500 ms) → `503` + `Retry-After`.
  - Circuit breakers (Polly.Core 8.8.0): `links-primary` in the Api, `redirect-primary-fallback` in Redirect; pool cleared on open; `circuit_state` / `circuit_transitions` metrics. While the fallback breaker is open, Redirect keeps serving replica hits and answers misses `503` + `Retry-After: 1`.
  - Retry rule: one retry for failures before `COMMIT`; a failure during `COMMIT` is retried only with an `Idempotency-Key`; never while the breaker is open or after cancellation. Each attempt runs in a fresh scope.
  - Idempotency-key cleanup every 5 minutes under an advisory lock, in batches.
  - `Resilience:*` configuration. Tests: 18 unit and 5 integration tests, including fault injection (a paused PostgreSQL container).
- **Metadata and delete: `GET` / `DELETE /api/v1/urls/{code}`** (TSK-10, LLD §6.3).
  - Owner only; another client's link and an unknown code give the same `404 link.not-found`.
  - Soft delete, `204`, idempotent (keeps the first deletion time); the short URL then answers `410`, and the alias can't be created again (A-25). The owner still sees a deleted link, with `deletedAt` (D1).
  - OpenAPI document regenerated. Tests: 7 unit and 7 API tests (across Api and Redirect).
- **Redirect: `GET` / `HEAD /{code}`** in the Redirect service (TSK-09, LLD §6.2, ADR-0007).
  - `302` + `Location` + `Cache-Control: no-store`; `404` (`no-store`) for unknown or malformed codes, without a database call for malformed ones; `410` (`public, max-age=86400`) for deleted links.
  - Replica lookup with a capped primary fallback (token bucket, `Redirect:PrimaryFallbackPerSecond` = 500) for read-your-writes.
  - `ClickEvent v1` (UUIDv7 `eventId`, referrer host only, device class) for `GET` from non-bots; `HEAD` and bots get no event. Stage 1 discards events and counts them (`click_events_discarded`, meter `UrlShortener`).
  - Tests: 42 unit and 10 API tests (create through the Api, then redirect through the Redirect service).
- **Create short links: `POST /api/v1/urls`** (TSK-08, LLD §6.1, ADR-0010).
  - Generated 7-character code or custom alias; `201` + `Location` + body; `409` for a taken alias (no read before the insert, so concurrent requests never get `500`).
  - API-key authentication (`X-Api-Key`, SHA-256 hashes in configuration, constant-time comparison) as ASP.NET Core authentication middleware.
  - Idempotency: `Idempotency-Key` (1–128 printable ASCII) scoped per client; replay returns the stored body with `Idempotent-Replayed: true`; a different request with the same key → `422`; keys honoured for 24 h; only successful creates are stored.
  - RFC 7807 errors with `traceId` and stable codes (`errors: [{ code, message }]`); `IdSpaceExhaustedException` → `503` + `Retry-After: 5`.
  - Self-link check (`url.self-link`); `expiresAt` rejected with `expiry.unsupported` until expiry exists (TSK-14).
  - OpenAPI document (Swashbuckle 10.2.3) committed as `docs/api/openapi-v1.json` with a drift test; Swagger UI at `/swagger` when `OpenApi:EnableUi` is true (on in compose).
  - Configuration `PublicBaseUrl`, `ApiKeys:Clients`, `OpenApi:EnableUi`; local dev client via `DEV_CALLER_HASH`.
  - Tests: 58 unit and 22 API tests, including a 12-way alias race and 10 identical requests with one key.
- Short-code generation (TSK-07, ADR-0005).
  - Ports `IShortCodeGenerator`, `IIdRangeLeaser`, `IdRange`, `IdSpaceExhaustedException`.
  - `Base62Encoder` (fixed 7 characters) and `FeistelPermutation` (4 HMAC-SHA256 rounds over 42 bits, cycle-walking into 62⁷, with an inverse).
  - `ShortCodeGenerator`: leases from memory under a lock, pre-fetches the next lease at 50%.
  - `PostgresIdRangeLeaser`: one atomic `UPDATE … RETURNING` per lease; block switch under a per-region advisory lock.
  - `ShortCodeStartupCheck`: the Api refuses to start without an active block for its region, or with blocks of another region.
  - Migration `SeedBlockB001`; block registry `config/id-blocks.json` with a CI check (`scripts/ci/check-id-blocks.py`, 18 script tests).
  - Configuration `ShortCode:*`; the Feistel key is required (≥ 32 characters) and comes from `SHORTCODE_FEISTEL_KEY` locally.
  - Tests: Feistel round-trip over 100k IDs and no duplicates over 1M; 50×2,000 concurrent codes; concurrent leases on PostgreSQL never overlap; block switch, including a race; startup check. Benchmark: ~2.1 µs per code.
- Persistence for the `links` database (TSK-06).
  - EF Core migration `InitialLinksSchema`: `short_urls`, `id_blocks` (one active block per region), `idempotency_keys`.
  - The `redirect_lookup` view, and least-privilege grants.
  - Write repository on the primary; plain-Npgsql redirect lookup on the replicas.
  - Database readiness checks.
  - `migrate` job (an EF bundle for x64 and arm64) that the services wait for.
  - A local roles init script.
  - 34 Testcontainers integration tests, including denial tests for the Redirect and Api roles.
- Domain model (TSK-05): `ShortUrl`, `ShortCode`, `CustomAlias`, `LongUrl`, `LinkState`, and `DomainValidationException` with stable error codes. Alias, URL and owner rules; idempotent soft delete. 74 unit tests; Domain line coverage 100%.
- CI quality gates (TSK-04):
  - `ci.yml`, with the jobs `build-test`, `codeql`, `secrets` and `dependencies`;
  - a test-metrics gate: failed = 0, skipped = 0, coverage ≥ 80% on Domain/Application;
  - test results posted on PRs;
  - `scheduled.yml`, with a daily dependency scan and a weekly CodeQL run;
  - Dependabot;
  - all actions pinned to commit SHAs;
  - `.gitleaksignore` with one reviewed false positive;
  - a CI badge in the README.
- Local stack: `docker-compose.yml` with PostgreSQL 16.15, `grafana/otel-lgtm` 0.37.0, Api and Redirect.
  - Multi-stage Alpine Dockerfiles for all 3 services, running as the non-root `app` user, with health checks.
  - `.dockerignore` keeping the brief, secrets and build output out of images.
  - `.env.example`.
  - Smoke scripts (`scripts/compose-smoke.sh` / `.ps1`).
  - README "Run locally".
  (TSK-03)
- Solution skeleton: 10 projects (3 libraries, 3 services, 4 test/benchmark projects).
  - Strict shared build settings: .NET 8, C# 11, warnings as errors, `AnalysisMode=All`, NuGet audit; central package versions; style rules enforced as build errors.
  - Exception-handling middleware returning RFC 7807 responses with a `traceId`.
  - `/health/live` and `/health/ready` in every service, and OpenTelemetry wiring (off when no endpoint is configured).
  - Architecture tests for every ADR-0009 rule.
  (TSK-02)
- Engineering workflow: `CLAUDE.md` (AI project context), task-spec template, versioned prompt library, PR template with the ADR-0012 checklist, `CODEOWNERS`, `.gitattributes`, MIT `LICENSE`, and this changelog. (TSK-01)
- Design documentation: requirements with a traceability matrix, HLD, LLD, 13 accepted ADRs, assumptions register, task breakdown. (Design phase, before TSK-00)

### Changed
- Integration tests share one in-memory Api host (`PostgresFixture.SharedApi`): more than 20 hosts in one process trip EF Core's `ManyServiceProvidersCreatedWarning` (TSK-08).
- The Api test host captures logs through DI, so tests can assert what is never logged (TSK-08).
- The Api needs its database to start (short-code startup check). The health test for a lost database now drops the database after startup; integration tests that change `id_blocks` use scratch databases (TSK-07).
- CI gate: count Cobertura lines once per class (lines are listed under both `<method>` and `<class>`). Coverage counts were doubled; the percentages were unaffected (TSK-05).
- Dependabot ignores FluentAssertions ≥ 8 (commercial licence); 7.x stays (TSK-04 follow-up).
- CI: gitleaks scans only the history being merged; runners pinned to `ubuntu-24.04`; the gate emits annotations (TSK-04).
- `.editorconfig` naming: constants and static readonly fields are PascalCase; only private instance fields use `_camelCase`. The previous rule contradicted the code and was only caught by `dotnet format` (TSK-04).

### Fixed
- A stored row that breaks a domain rule answers `500` (logged, naming the record and the rule), not `400`, on the metadata and redirect paths (TSK-QW).
- `401` responses now carry `WWW-Authenticate: ApiKey header="X-Api-Key"`, which RFC 9110 §15.5.2 requires (TSK-15).
- Every problem response's `traceId` is the 32-hex trace ID; framework-generated problems (`401`, unknown-code `404`) used to carry the full `traceparent`, which didn't match the logs (TSK-15).
- Docs: the HLD §3.2 redirect diagram failed to render on GitHub (a `;` inside a Mermaid message); HLD, LLD and README brought in line with what stage 1 built (DOC-01).
- **Connection pools are capped** (TSK-11b, LLD §6.7): Api writer 30, Redirect replicas 30, Redirect fallback 10 (`Resilience:*MaxPoolSize`). Uncapped pools (Npgsql's default of 100 each) exhausted PostgreSQL's 100 connections under sustained load, found by the TSK-12 load test: at 1,500 redirects/s, p99 went from 827 ms with 12% errors to 3.8 ms with none.
