# URL Shortener: AI-Assisted Engineering

A URL shortener service (core APIs, analytics, reliability) built with **.NET 8**, together with the
**AI-assisted engineering workflow** used to build it. Both are deliverables
([ADR-0001](docs/adr/0001-deliverable-scope-product-and-workflow.md)).

> **Current status: design phase.** The requirements, decisions, HLD and LLD are drafted and under engineer
> review. No code has been written yet. Setup instructions will be added when the code skeleton exists.

## Design at a glance

| Aspect | Choice |
|---|---|
| Scale target | 100M new links/day, 10:1 reads to writes → ~3.5K writes/s and ~35K reads/s at peak |
| Service levels | Redirect 99.95% SLO, p99 ≤ 50 ms · Create 99.9%, p99 ≤ 200 ms · Delete takes effect ≤ 5 s (99%) · Stats fresh within ≤ 60 s ([§2](docs/02-system-design.md#2-service-levels-slo--sla)) |
| Deployment | **One region across multiple availability zones.** Multi-region is planned for a later release. |
| Consistency model | Reads favor availability (read replicas); writes favor consistency (primary + synchronous standby, no data loss on failover) ([ADR-0002](docs/adr/0002-consistency-and-availability-model.md)) |
| Data store | PostgreSQL 16; Citus added when data passes the thresholds in [ADR-0004](docs/adr/0004-data-store-postgresql.md) |
| Short codes | 7 characters. Leased ID ranges + Feistel permutation + Base62, so codes never collide and can't be guessed ([ADR-0005](docs/adr/0005-short-code-generation.md)) |
| Caching | Measure first. A cache is added only if load tests show it's needed ([ADR-0006](docs/adr/0006-caching-measure-first.md)) |
| Redirects | 302, with 404 for unknown codes and 410 for deleted or expired ones ([ADR-0007](docs/adr/0007-http-302-redirects.md)) |
| Analytics | Kafka → worker → rollups; each event counted exactly once ([ADR-0008](docs/adr/0008-async-analytics-pipeline.md)) |
| Observability | OpenTelemetry → Grafana (Prometheus, Tempo, Loki) ([ADR-0013](docs/adr/0013-observability-opentelemetry-grafana.md)) |
| Services | Redirect, Api and AnalyticsWorker on shared Clean Architecture layers ([ADR-0009](docs/adr/0009-service-decomposition-and-layering.md)) |
| Platform | .NET 8, C# 11, classic object-oriented style ([ADR-0003](docs/adr/0003-platform-dotnet8-csharp11.md)) |
| Local stack | `postgres:16-alpine`, `apache/kafka`, `grafana/otel-lgtm` (all open source, ~3 GB RAM) |

## Documentation map

| Document | What it covers | Status |
|---|---|---|
| [docs/03-architecture.md](docs/03-architecture.md) | **HLD**: components, flows, deployment, failure modes, observability, engineering workflow | Draft |
| [docs/02-system-design.md](docs/02-system-design.md) | **Capacity, SLO/SLA and LLD**: API contract, SQL schema, classes, algorithms, validation, configuration | Draft |
| [docs/adr/](docs/adr/README.md) | 13 architecture decision records | 8 Accepted, 5 Proposed |
| [docs/assumptions.md](docs/assumptions.md) | Assumptions register (we proceeded without external clarification) | Living document |
| [docs/scenarios/](docs/scenarios/README.md) | Greenfield, brownfield and ambiguous scenarios | Planned |
| [docs/ai-usage/traceability-log.md](docs/ai-usage/traceability-log.md) | What the AI produced and whether it was accepted, edited or rejected | Living document |
| [docs/01-requirements.md](docs/01-requirements.md) | Problem statement, functional (FR) and non-functional (NFR) requirements, out of scope | Draft (traceability matrix to follow) |
| `docs/04-task-breakdown.md` | Task breakdown, dependencies, critical path | Planned |
| `docs/05-testing-strategy.md`, `docs/06-risks-tradeoffs.md`, `docs/08-final-summary.md` | Testing, risks and summary | Planned |

**Suggested reading order for reviewers:** this README → requirements → HLD → ADR index → LLD → assumptions → scenarios → traceability log.

## How AI is used

The engineer leads; AI assists within tasks. Each task starts from a spec, runs through CI quality gates,
and is reviewed and merged by the engineer. The AI's contribution is logged, including rejected and
revised outputs. See [ADR-0012](docs/adr/0012-engineer-led-workflow-and-sign-off.md) and
[ADR-0011](docs/adr/0011-confidentiality-and-secure-ai-usage.md) (confidentiality and secure AI usage).

**AI tool:** Claude Code (Claude Opus 5.5).

## Known limitations (so far)

- **Single region.** A full region outage means a disaster-recovery restore (RTO ≤ 4 h, RPO ≤ 15 min). Multi-region is planned for a later release.
- **Storage:** one PostgreSQL node fits about 1.5–2 years of data. After that, Citus is required (ADR-0004).
- **.NET 8 support ends on 10 Nov 2026.** Upgrading to .NET 10 mostly means changing the target framework.
- Production scale (35K reads/s) can't be reproduced locally. Sizing is extrapolated from measured per-node results.
- Assumptions are unconfirmed by the requester. See the [assumptions register](docs/assumptions.md).
