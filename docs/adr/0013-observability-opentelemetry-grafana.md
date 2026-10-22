# ADR-0013: Observability with OpenTelemetry and the Grafana stack

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0002, ADR-0008, [SLOs](../02-system-design.md#2-service-levels-slo--sla)

## Context
- Three services plus Kafka and PostgreSQL. A single click crosses Redirect → Kafka → Worker.
- The SLOs need to be measured, and the error budget needs to be alerted on.
- The engineer wants Grafana, from open-source images that run locally.

## Decision
- **Instrumentation:** OpenTelemetry .NET SDK in all three hosts, covering **traces, metrics and logs**.
  - Libraries instrumented: ASP.NET Core, HttpClient, Npgsql (`Npgsql.OpenTelemetry`), EF Core.
  - **Across Kafka:** the W3C `traceparent` is put in the message headers. The worker's batch span **links to** the producer spans it processes.
- **Logs:** `ILogger` with the OpenTelemetry log exporter, plus JSON console output. `traceId` and `spanId` are attached automatically. `traceId` is also returned in error responses.
  **Never logged:** full long URLs (they can contain tokens), IP addresses, API keys.
- **Metrics:**
  - rate, errors and duration for each endpoint,
  - `clicks_dropped_total`,
  - Kafka consumer lag,
  - stats freshness,
  - replica lag and delete propagation time,
  - IDs remaining in the current lease.
- **Pipeline:** apps send OTLP to an OpenTelemetry Collector, which forwards to Prometheus (metrics), Tempo (traces) and Loki (logs). **Grafana** shows all three.
- **Locally:** a single **`grafana/otel-lgtm`** container that bundles Grafana, Prometheus, Tempo, Loki and the Collector. It's meant for development only; production runs the components separately or uses managed Grafana.
- **Dashboards and alerts are kept in the repo as code** (provisioned JSON):
  - SLO dashboard with error-budget burn-rate alerts,
  - one dashboard per service,
  - Kafka and PostgreSQL dashboards.
- **Sampling:** 100% locally. In production, ~5% of traces based on the parent's decision, and the Collector keeps **all errors and slow requests** (tail sampling).

## Alternatives considered
| Option | Why not chosen |
|---|---|
| .NET Aspire Dashboard | Light and .NET-focused, but no persistence, alerting or SLO dashboards |
| Jaeger + Prometheus without Grafana | Separate UIs; no single place to see everything |
| Commercial APM (Datadog, Application Insights) | Vendor lock-in; can't be fully reproduced locally |
| Serilog + Seq | Logs only; OpenTelemetry covers all three signals with one SDK |

## Consequences
- Licenses: Grafana, Loki and Tempo are AGPL-3.0, used unmodified as tools. Prometheus and the Collector are Apache-2.0.
- `grafana/otel-lgtm` uses about 1 GB RAM locally. The total local stack is roughly 3 GB.
- PostgreSQL and Kafka exporters are optional locally.

## History
- 2026-10-21 · Accepted · Engineer chose Grafana with open-source images.
