# Risks and trade-offs

The key design trade-offs, the risk register, failure scenarios with their guardrails, and the limitations, as
of the end of stage 1 (TSK-QW). Details live in the [ADRs](adr/README.md), the task specs' risk tables and the
[traceability log](ai-usage/traceability-log.md); this document brings them together.

## 1. Key trade-offs

| Decision | Chosen | Given up | Why | ADR |
|---|---|---|---|---|
| Consistency vs availability | Reads favour **availability** (read replicas); writes favour **consistency** (primary + synchronous standby) | Strictly up-to-date reads everywhere | Redirects are 10× creates and must survive failures; a new link must not be lost. Read-your-writes is restored with a capped primary fallback | [0002](adr/0002-consistency-and-availability-model.md) |
| Data store | PostgreSQL, Citus later | A distributed store (e.g. Cassandra) from day one | One node holds ~1.5–2 years of data; transactions make idempotency and ID leases simple | [0004](adr/0004-data-store-postgresql.md) |
| Short codes | Leased ID ranges + keyed Feistel permutation + Base62 | Random codes (collisions to retry), hashes (collisions, enumerable) | No collision is possible by construction; codes aren't enumerable; ~2.1 µs per code. The key must never change | [0005](adr/0005-short-code-generation.md) |
| Caching | None in v1, **measured** | Lower database load at peak | The load test showed the database isn't the bottleneck (~0.2 ms CPU per lookup vs ~1 ms in the app) | [0006](adr/0006-caching-measure-first.md) |
| Redirect status | `302` + `no-store` | Fewer requests (a cached `301`) | Every click reaches us (analytics) and deletes take effect at once | [0007](adr/0007-http-302-redirects.md) |
| Analytics | Asynchronous via Kafka | Simplicity | A redirect must never wait for analytics. **Not built** (stage 2) | [0008](adr/0008-async-analytics-pipeline.md) |
| Services | One bounded context, three services on shared layers | Fewer deployables | Redirect scales and fails separately from the Api; least-privilege database roles per service | [0009](adr/0009-service-decomposition-and-layering.md) |
| Duplicate creates | A new code for every create; optional `Idempotency-Key` (24 h) | Deduplicating by long URL | Separate links keep analytics and expiry separate; keys make retries safe. Costs up to ~50 GB of keys at 100M/day | [0010](adr/0010-idempotent-creation.md) |
| Workflow | Engineer-led; AI assists within approved tasks | Speed of autonomous AI | Every output reviewed and merged by the engineer; GitHub can't approve one's own PR, so the merge commit is the sign-off | [0012](adr/0012-engineer-led-workflow-and-sign-off.md) |
| Observability | OpenTelemetry → Grafana (Prometheus, Tempo, Loki) | Serilog + Seq | One SDK for traces, metrics and logs | [0013](adr/0013-observability-opentelemetry-grafana.md) |

## 2. Risk register

| Risk | Likelihood / impact | Guardrail in place | Residual risk |
|---|---|---|---|
| Duplicate short codes | Low / high | Feistel is a bijection; non-overlapping leases (atomic `UPDATE … RETURNING`); property and concurrency tests; mutation checks | Changing the Feistel key would break uniqueness: documented "never change", startup check, key backup required |
| Lost links on failover | Low / high | Synchronous standby (ADR-0002); the commit-phase retry rule never retries an unknown commit without a key | Failover itself isn't demonstrated locally |
| Retries creating duplicates | Medium / medium | Idempotency keys with a normalized request hash; one transaction for key, link and response | Clients that send no key can duplicate on an unknown commit (documented: `503`) |
| Connection exhaustion | Medium / high | Pool caps (TSK-11b) keep instances × pools under `max_connections` | At peak (~24 Redirect instances) caps exceed 100: **a pooler (PgBouncer) is needed**, not built |
| ID space exhaustion | Low / high | Region blocks with a runbook; `IdSpaceExhaustedException` → `503` | **The 80% usage alert isn't built** (planned for TSK-22) |
| Abuse: spam links, scanning, guessing | Medium / medium | API keys; codes not enumerable; scans can't overload the primary (fallback budget); self-links rejected | **No rate limits or threat checks** (scenario 3, TSK-19, not built); a random guess hits ~1% after a year |
| Stale reads after delete or expiry | Medium / low | `410` checked on the replica, primary fallback for misses | **No replica-lag monitor** (LLD §4.3, not built): a lagging replica can serve a deleted link |
| Corrupt stored data | Low / medium | Rehydration through the domain's rules; a broken row answers `500` and is logged (TSK-QW #7) | — |
| Sensitive data in logs | Low / high | Long URLs, IPs and API keys never logged; tests check it; JSON console logs | EF logs every command at Information (parameters masked): noisy at scale |
| Supply chain and secrets | Low / high | Pinned actions, NuGet audit, gitleaks, CodeQL, Dependabot | No container image scanning (Trivy, stage 2) |
| Platform end of life | Certain / medium | Upgrade path is mostly the target framework | .NET 8 support ends on 10 Nov 2026 |

## 3. Failure scenarios and what the system does

| Scenario | Behaviour | Verified by |
|---|---|---|
| Primary down or failing over | Api: fast `503` + `Retry-After: 5`, breaker opens, pool cleared, recovers on its own. Redirect: existing links keep redirecting from replicas; misses `503` + `Retry-After: 1` | Fault-injection tests (TSK-11), compose stop/start |
| Database slow | Request timeouts (Api 3 s, Redirect 500 ms) → `503`; statement timeout 2 s on the server | TSK-11 tests |
| Load spike | Pools capped, requests queue for a connection within their timeout instead of exhausting PostgreSQL | TSK-12 load test (before and after TSK-11b) |
| Region misconfigured, or no ID block | The Api refuses to start | Startup-check tests (TSK-07) |
| ID block runs out | Switches to the next reserved block under an advisory lock; none left → `503` | TSK-07 tests |
| Rolling deploy with a schema change | Expand-only migrations run first; old instances never read new columns | TSK-14 migration rollback test |
| Corrupt row | `500`, logged with the record and the rule, never the stored values | TSK-QW #7 tests |

## 4. Limitations

Stage 1 stopped after TSK-15, by the engineer's decision; the rest of the plan was **descoped**:

- **Not built:** the validation refactor (TSK-16), test and doc improvements beyond TSK-QW (TSK-17), the ambiguous scenario with rate limits and threat checks (TSK-19, TSK-20), demo scripts (TSK-21), dashboards and the ID-usage alert (TSK-22), analytics (stage 2), extra CI gates (Trivy, ZAP), Stryker.
- **Single region**; a region outage means a disaster-recovery restore.
- **Sizing is extrapolated** from one laptop, with the load generator on the same machine.
- **Not demonstrated locally:** automatic database failover, real replicas.
