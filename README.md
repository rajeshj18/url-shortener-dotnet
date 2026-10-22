# URL Shortener: AI-Assisted Engineering

A URL shortener service (core APIs, analytics, reliability) built with **.NET 8**, together with the
**AI-assisted engineering workflow** used to build it. Both are deliverables
([ADR-0001](docs/adr/0001-deliverable-scope-product-and-workflow.md)).

> **Current status: build phase, stage 1.**
> - The design is complete: requirements, HLD, LLD and 13 accepted ADRs.
> - The solution skeleton, the shared middleware and health endpoints, and the local Docker stack are in place (TSK-01 to TSK-03).
> - The URL-shortening features follow from TSK-05; see the [task breakdown](docs/04-task-breakdown.md).

## Run locally

**Prerequisites:**
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) running, with about 2 GB free memory.
- To build and test outside Docker: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), version 8.0.424 or a later 8.0 SDK.

**Start the stack** (the services are built from source):
```bash
cp .env.example .env                    # throwaway local values; .env is git-ignored
docker compose up -d --build --wait     # first run takes ~2 min (image downloads + build)
bash scripts/compose-smoke.sh           # or: pwsh scripts/compose-smoke.ps1
```

| What | URL |
|---|---|
| Api | http://localhost:5080/health/live |
| Redirect | http://localhost:5081/health/live |
| Grafana (traces, logs, metrics) | http://localhost:3000. In **Explore → Tempo**, search for `service.name = urlshortener-api`. |

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

Everything binds to `127.0.0.1` only. In stage 1 the stack is PostgreSQL, Grafana's `otel-lgtm`, Api and Redirect.
Kafka and the AnalyticsWorker are added in stage 2.

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
| Local stack | `postgres:16-alpine`, `apache/kafka`, `grafana/otel-lgtm` (all open source, ~3 GB RAM) |

## Documentation map

| Document | What it covers | Status |
|---|---|---|
| [docs/03-architecture.md](docs/03-architecture.md) | **HLD**: components, flows, deployment, failure modes, observability, engineering workflow | Draft |
| [docs/02-system-design.md](docs/02-system-design.md) | **Capacity, SLO/SLA and LLD**: API contract, SQL schema, classes, algorithms, validation, configuration | Draft |
| [docs/adr/](docs/adr/README.md) | 13 architecture decision records | All 13 Accepted |
| [docs/assumptions.md](docs/assumptions.md) | Assumptions register (we proceeded without external clarification) | Living document |
| [docs/scenarios/](docs/scenarios/README.md) | Greenfield, brownfield and ambiguous scenarios | Planned |
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
- Production scale (35K reads/s) can't be reproduced locally. Sizing is extrapolated from measured per-node results.
- Assumptions are unconfirmed by the requester. See the [assumptions register](docs/assumptions.md).
