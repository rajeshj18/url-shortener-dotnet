# URL Shortener: AI-Assisted Engineering

[![CI](https://github.com/rajeshj18/url-shortener-dotnet/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/rajeshj18/url-shortener-dotnet/actions/workflows/ci.yml)

A URL shortener service (core APIs, analytics, reliability) built with **.NET 8**, together with the
**AI-assisted engineering workflow** used to build it. Both are deliverables
([ADR-0001](docs/adr/0001-deliverable-scope-product-and-workflow.md)).

> **Current status: build phase, stage 1.**
> - The design is complete: requirements, HLD, LLD and 13 accepted ADRs.
> - The solution skeleton, the shared middleware and health endpoints, and the local Docker stack are in place (TSK-01 to TSK-03).
> - The domain model, persistence and short-code generation are in place (TSK-05 to TSK-07).
> - **Create, redirect, metadata and delete work** (`POST /api/v1/urls`, TSK-08; `GET /{code}`, TSK-09; `GET`/`DELETE /api/v1/urls/{code}`, TSK-10). They have timeouts, retries and circuit breakers (TSK-11), and a v1 load test with a k6 smoke gate in CI (TSK-12). Scenario 1's write-up follows in TSK-13; see the [task breakdown](docs/04-task-breakdown.md).

## Run locally

**Prerequisites:**
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) running, with about 2 GB free memory.
- To build and test outside Docker: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), version 8.0.424 or a later 8.0 SDK.

**Start the stack** (the services are built from source; the first run takes ~2 min for image downloads and builds).
A one-off `migrate` job applies the database schema first. `api` and `redirect` start only after it succeeds.

> **Upgrading from an earlier checkout?** If your `.env` predates TSK-06, add the three `LINKS_*_PASSWORD` lines
> from `.env.example`. Then run `docker compose down -v` **once**, because the database roles are created only on
> a fresh data volume. This deletes your local database.
> If it predates TSK-07, also add `SHORTCODE_FEISTEL_KEY` (at least 32 characters); compose refuses to start the Api
> without it. No volume reset is needed for this one.
> If it predates TSK-08, also add `DEV_CALLER_HASH` from `.env.example` (the local API client).

*Windows (PowerShell):*
```powershell
Copy-Item .env.example .env                                         # throwaway local values; .env is git-ignored
docker compose up -d --build --wait
powershell -ExecutionPolicy Bypass -File scripts\compose-smoke.ps1  # smoke test
```

*macOS / Linux / Git Bash:*
```bash
cp .env.example .env
docker compose up -d --build --wait
bash scripts/compose-smoke.sh
```

> On Windows, don't run `bash` from PowerShell. It starts WSL's bash (`C:\Windows\system32\bash.exe`), which fails
> if no Linux distribution is installed. Use the PowerShell script, or open **Git Bash**.

| What | URL |
|---|---|
| Api | http://localhost:5080/health/live |
| Redirect | http://localhost:5081/health/live |
| Grafana (traces, logs, metrics) | http://localhost:3000. See "Viewing telemetry" below. |
| **Swagger UI** (try the API in the browser) | http://localhost:5080/swagger. Click **Authorize** and enter the dev key below. |

**Viewing telemetry.** Our own dashboards arrive in TSK-22. The bundled "JVM" and "RED Metrics" dashboards
expect other metric names, so they look empty. Until then, use **Explore** (compass icon) with the time
range set to *Last 15 minutes*:

| Signal | Data source | Query |
|---|---|---|
| Traces | Tempo | Search → Service Name `urlshortener-api` → Run query |
| Metrics | Prometheus | `sum by (job) (rate(http_server_request_duration_seconds_count[1m]))` |
| Logs | Loki | `{service_name="urlshortener-api"}` |

**Build and test** (no Docker needed for the current tests):
```bash
dotnet build
dotnet test
```

**Stop and reset:**
```bash
docker compose down        # stop
docker compose down -v     # stop and delete the local database volume
```

Everything binds to `127.0.0.1` only. In stage 1 the stack is PostgreSQL (with 3 least-privilege roles), the `migrate` job, Grafana's `otel-lgtm`, Api and Redirect.
It runs on x64 and arm64 (e.g. Apple Silicon).
Kafka and the AnalyticsWorker are added in stage 2.

## Try the API

The local stack has one API client. Its **dev key is `local-dev-api-key-change-me`** (throwaway; only its SHA-256
is configured, via `DEV_CALLER_HASH` in `.env`). The easiest way is the Swagger UI at http://localhost:5080/swagger.
From a terminal:

*Windows (PowerShell):*
```powershell
$headers = @{ "X-Api-Key" = "local-dev-api-key-change-me"; "Idempotency-Key" = [guid]::NewGuid().ToString() }
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/v1/urls -Headers $headers `
  -ContentType "application/json" -Body '{"longUrl":"https://example.com/some/long/path"}'
```

*macOS / Linux / Git Bash:*
```bash
DEV_CALLER=local-dev-api-key-change-me
curl -i -X POST http://localhost:5080/api/v1/urls \
  -H "X-Api-Key: $DEV_CALLER" -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" -d '{"longUrl":"https://example.com/some/long/path"}'
```

- Add `"customAlias": "spring-sale"` for your own code. A taken alias returns `409`.
- Add `"expiresAt": "2027-01-01T00:00:00Z"` for a link that expires: afterwards it answers `410 Gone`. It must be in the future and at most 10 years ahead.
- Send the same request with the same `Idempotency-Key` again: you get the **same link** back with `Idempotent-Replayed: true`. The same key with a different body returns `422`.
- Errors are RFC 7807 problems with a stable `code` (LLD §3).
- `GET /api/v1/urls/<code>` shows your link; `DELETE /api/v1/urls/<code>` deletes it (`204`), after which the short URL answers `410 Gone`. Other clients get `404` for your links.
- **Open the returned `shortUrl`** (`http://localhost:5081/<code>`) in a browser: it redirects with `302`. `curl -I http://localhost:5081/<code>` shows the status and `Cache-Control: no-store` without following it. Unknown codes give `404`.

## Performance

The v1 load test ([results](docs/perf/v1-load-test.md)), per instance on a laptop: **redirect p99 3.8 ms at
1,500 req/s**, create p99 7.1 ms at 600 req/s, 0% errors; the database isn't the bottleneck, so v1 has no cache
(ADR-0006). Every PR also runs a 30 s k6 smoke against the compose stack (`perf-smoke`, SLO thresholds).

Run a test yourself (stack running): `bash scripts/load-test.sh redirect` (or `hot-key`, `scan`, `create`,
`smoke`), or `powershell -ExecutionPolicy Bypass -File scripts\load-test.ps1 redirect`. `RATE=` and
`DURATION=` override the defaults.

## Design at a glance

| Aspect | Choice |
|---|---|
| Scale target | 100M new links/day, 10:1 reads to writes → ~3.5K writes/s and ~35K reads/s at peak |
| Service levels | Redirect 99.95% SLO, p99 ≤ 50 ms · Create 99.9%, p99 ≤ 200 ms · Delete takes effect ≤ 5 s (99%) · Stats fresh within ≤ 60 s ([§2](docs/02-system-design.md#2-service-levels-slo--sla)) |
| Deployment | **One region across multiple availability zones.** Multi-region is planned for a later release. |
| Consistency model | Reads favor availability (read replicas); writes favor consistency (primary + synchronous standby, no data loss on failover) ([ADR-0002](docs/adr/0002-consistency-and-availability-model.md)) |
| Data store | PostgreSQL 16; Citus added when data passes the thresholds in [ADR-0004](docs/adr/0004-data-store-postgresql.md) |
| Short codes | 7 characters. Leased ID ranges + Feistel permutation + Base62, so codes never collide and can't be enumerated in order ([ADR-0005](docs/adr/0005-short-code-generation.md)) |
| Caching | Measure first. A cache is added only if load tests show it's needed ([ADR-0006](docs/adr/0006-caching-measure-first.md)) |
| Redirects | 302, with 404 for unknown codes and 410 for deleted or expired ones ([ADR-0007](docs/adr/0007-http-302-redirects.md)) |
| Analytics | Kafka → worker → rollups; each event counted exactly once ([ADR-0008](docs/adr/0008-async-analytics-pipeline.md)) |
| Observability | OpenTelemetry → Grafana (Prometheus, Tempo, Loki) ([ADR-0013](docs/adr/0013-observability-opentelemetry-grafana.md)) |
| Services | One bounded context deployed as three services (Redirect, Api, AnalyticsWorker), each with its own image, on shared Clean Architecture layers ([ADR-0009](docs/adr/0009-service-decomposition-and-layering.md)) |
| Platform | .NET 8, C# 11, classic object-oriented style ([ADR-0003](docs/adr/0003-platform-dotnet8-csharp11.md)) |
| Local stack | Stage 1: `postgres:16.15-alpine`, `grafana/otel-lgtm:0.37.0`, Api, Redirect (all open source). Stage 2 adds `apache/kafka` and the AnalyticsWorker. |

## Documentation map

| Document | What it covers | Status |
|---|---|---|
| [docs/03-architecture.md](docs/03-architecture.md) | **HLD**: components, flows, deployment, failure modes, observability, engineering workflow | Current for stage 1 (DOC-01) |
| [docs/02-system-design.md](docs/02-system-design.md) | **Capacity, SLO/SLA and LLD**: API contract, SQL schema, classes, algorithms, validation, configuration | Current for stage 1, with implementation notes per task |
| [docs/adr/](docs/adr/README.md) | 13 architecture decision records | All 13 Accepted |
| [docs/assumptions.md](docs/assumptions.md) | Assumptions register (we proceeded without external clarification) | Living document |
| [docs/scenarios/](docs/scenarios/README.md) | Greenfield, brownfield and ambiguous scenarios | Scenario 1 [done](docs/scenarios/01-greenfield.md); 2 and 3 planned |
| [docs/ai-usage/traceability-log.md](docs/ai-usage/traceability-log.md) | What the AI produced and whether it was accepted, edited or rejected | Living document |
| [docs/01-requirements.md](docs/01-requirements.md) | Problem statement, functional (FR) and non-functional (NFR) requirements, out of scope | Draft, including the traceability matrix to the brief |
| [docs/04-task-breakdown.md](docs/04-task-breakdown.md) | Staged plan (core → lean → full), definition of done incl. test-metrics gate, tasks, critical path, estimates | Approved |
| `docs/05-testing-strategy.md`, `docs/06-risks-tradeoffs.md`, `docs/08-final-summary.md` | Testing, risks and summary | Planned |

**Suggested reading order for reviewers:** this README → requirements → HLD → ADR index → LLD → assumptions → scenarios → traceability log.

## How AI is used

The engineer leads; AI assists within tasks. Each task starts from a spec, runs through CI quality gates,
and is reviewed and merged by the engineer. The AI's contribution is logged, including rejected and
revised outputs. See [ADR-0012](docs/adr/0012-engineer-led-workflow-and-sign-off.md) and
[ADR-0011](docs/adr/0011-confidentiality-and-secure-ai-usage.md) (confidentiality and secure AI usage).

**AI tool:** Claude Code (Claude Opus 5.5).

**Workflow files:**
- [CLAUDE.md](CLAUDE.md): the project rules given to the AI
- [docs/ai-usage/prompts/](docs/ai-usage/prompts/README.md): the versioned prompt library
- [docs/templates/task-spec.md](docs/templates/task-spec.md) and [docs/tasks/](docs/tasks/): task specs
- [.github/pull_request_template.md](.github/pull_request_template.md): the PR checklist
- [CHANGELOG.md](CHANGELOG.md): change history

License: [MIT](LICENSE).

## Known limitations (so far)

- **Single region.** A full region outage means a disaster-recovery restore (RTO ≤ 4 h, RPO ≤ 15 min). Multi-region is planned for a later release.
- **Storage:** one PostgreSQL node fits about 1.5–2 years of data. After that, Citus is required (ADR-0004).
- **.NET 8 support ends on 10 Nov 2026.** Upgrading to .NET 10 mostly means changing the target framework.
- Automatic database failover isn't demonstrated locally (it would need Patroni and etcd). The demo stops and restarts the primary to show the services' side of a failover.
- Production scale (35K reads/s) can't be reproduced locally. Sizing is extrapolated from measured per-node results ([load test](docs/perf/v1-load-test.md)).
- **No replica-lag monitor yet:** the LLD's `ReplicaLagMonitor` (drop replicas lagging > 2 s) isn't built, because locally the replicas are the primary. It's needed before running against real replicas.
- **Connections at scale:** instances × pool caps must stay under PostgreSQL's `max_connections` (LLD §6.7). The ~24 Redirect instances the sizing implies need a connection pooler (e.g. PgBouncer) or a higher limit; not built in v1.
- Assumptions are unconfirmed by the requester. See the [assumptions register](docs/assumptions.md).
