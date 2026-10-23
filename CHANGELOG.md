# Changelog

All notable changes to this project are recorded here, following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/).
Each entry names the task (`TSK-xx`) that made the change.

## [Unreleased]

### Changed
- The Api needs its database to start (short-code startup check). The health test for a lost database now drops the database after startup; integration tests that change `id_blocks` use scratch databases (TSK-07).
- CI gate: count Cobertura lines once per class (lines are listed under both `<method>` and `<class>`). Coverage counts were doubled; the percentages were unaffected (TSK-05).
- Dependabot ignores FluentAssertions ≥ 8 (commercial licence); 7.x stays (TSK-04 follow-up).
- CI: gitleaks scans only the history being merged; runners pinned to `ubuntu-24.04`; the gate emits annotations (TSK-04).
- `.editorconfig` naming: constants and static readonly fields are PascalCase; only private instance fields use `_camelCase`. The previous rule contradicted the code and was only caught by `dotnet format` (TSK-04).

### Added
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
