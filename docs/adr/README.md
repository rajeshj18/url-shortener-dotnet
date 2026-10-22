# Architecture Decision Records

Each ADR records one significant decision: its context, the alternatives we considered, and the consequences.
Analysis that isn't a decision lives in the design docs (capacity and SLOs are in
[`02-system-design.md`](../02-system-design.md)). How AI was used is recorded in
[`ai-usage/traceability-log.md`](../ai-usage/traceability-log.md).

**Status:** *Proposed* (waiting for the engineer's sign-off) → *Accepted*, or *Rejected* / *Superseded by ADR-XXXX*.
Once an ADR is Accepted, its decision text is never edited. A changed decision gets a new ADR that supersedes the old one.

| # | Decision | Status |
|---|---|---|
| [0001](0001-deliverable-scope-product-and-workflow.md) | Deliver both the URL shortener and the AI-assisted engineering workflow | Accepted |
| [0002](0002-consistency-and-availability-model.md) | Single region: reads favor availability (replicas), writes favor consistency (primary + synchronous standby) | Accepted |
| [0003](0003-platform-dotnet8-csharp11.md) | .NET 8, C# 11, classic object-oriented style | Accepted |
| [0004](0004-data-store-postgresql.md) | PostgreSQL for v1; Citus when thresholds are reached; multi-region in a later release | Accepted |
| [0005](0005-short-code-generation.md) | Short codes: range lease + Feistel permutation + Base62 | Accepted |
| [0006](0006-caching-measure-first.md) | Caching: measure first, add only when load tests justify it | Accepted |
| [0007](0007-http-302-redirects.md) | HTTP 302 redirects | Accepted |
| [0008](0008-async-analytics-pipeline.md) | Asynchronous analytics pipeline with Kafka | Accepted |
| [0009](0009-service-decomposition-and-layering.md) | Three services of one bounded context, Clean Architecture layering | Accepted |
| [0010](0010-idempotent-creation.md) | New code for every create; idempotency keys for retries | Accepted |
| [0011](0011-confidentiality-and-secure-ai-usage.md) | Confidentiality and secure AI usage | Accepted |
| [0012](0012-engineer-led-workflow-and-sign-off.md) | Engineer-led workflow, risk-based sign-off, quality gates and when they run | Accepted |
| [0013](0013-observability-opentelemetry-grafana.md) | Observability with OpenTelemetry and the Grafana stack | Accepted |

Template: **Status · Date · Related** → Context → Decision → Alternatives considered → Consequences → **History**.

Each ADR ends with a short **History** section (date · status · what changed · who decided). Status
changes can then be read straight from the file, by people and by AI tools, without searching git history.
Git keeps the full diffs.
