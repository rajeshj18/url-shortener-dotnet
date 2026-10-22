# AI Traceability Log

This log is append-only. Each row records something that actually happened. Corrections are added as new rows.
**Tool used for all entries unless stated otherwise:** Claude Code (Claude Opus 5.5).

| ID | Date | Task / intent | AI contribution | Result | Rationale / engineer decision | Approved in |
|---|---|---|---|---|---|---|
| PLAN-01 | 2026-10-21 | Analyze the brief; draft functional/non-functional requirements, capacity estimates, architecture | Drafted the plan | Accepted with changes | Engineer chose 100M writes **per day**, .NET 8, C# 11 in a classic OOP style | Planning session |
| PLAN-02 | 2026-10-21 | Review an externally suggested log format, ambiguous-scenario template and secure-AI statement | Critique | Edited | Adopted the structure. Rejected the example row (wrong model name, design that contradicted ours), the invented tool `dotnet audit`, and the unfounded rate-limit number | Planning session |
| PLAN-03 | 2026-10-21 | Self-review of the plan | Found that the in-process `Channel<T>` can't feed a separate worker process | **Rejected** own proposal | Replaced with Valkey Streams (ADR-0008) | Planning session |
| PLAN-04 | 2026-10-21 | Check the plan against the brief | Found gaps: the "system" ambiguity, a missing refactor, missing test/doc improvements, and the performance gate | Accepted | Engineer chose to proceed on documented assumptions rather than ask the interviewer | Planning session |
| PLAN-05 | 2026-10-21 | Scenario selection | First proposal made analytics the ambiguous scenario | **Revised** | The brief lists analytics as core scope. Ambiguous scenario became abuse prevention | Planning session |
| PLAN-06 | 2026-10-21 | Compare with a widely circulated ZooKeeper + Snowflake + MD5 design | Showed that truncating the hash brings collisions back | Accepted | Added to ADR-0005's alternatives | Planning session |
| PLAN-07 | 2026-10-21 | Consistency model | Analyzed how the design changes under AP vs CP | Accepted | Engineer chose availability first as the primary design; consistency first documented as the alternate (ADR-0002) | Planning session |
| PLAN-08 | 2026-10-21 | Is a cache needed? | Pointed out that "reads must be served from cache" was overstated; proposed "measure first" | Proposed | Waiting for the engineer's decision (ADR-0006) | — |
| PLAN-09 | 2026-10-21 | Consolidate the ADRs | Cut 16 ADRs to 12: removed non-decisions, merged the CP/AP pair, removed repeated content | Proposed | Requested by the engineer | — |
| PLAN-10 | 2026-10-21 | Settle PLAN-08 (cache) | — | Accepted | Engineer chose **measure first**; ADR-0006 Accepted | Planning session |
| PLAN-11 | 2026-10-21 | Settle the C# style open point | — | Accepted | Engineer kept `record` types and nullable annotations (ADR-0003) | Planning session |
| PLAN-12 | 2026-10-21 | Where decision history lives | Initially proposed keeping it in git history only | **Revised** | Engineer pointed out that AI tools read files directly, so a History section was added to each ADR; git keeps the full diffs | Planning session |
| PLAN-13 | 2026-10-21 | Write the HLD and LLD | Drafted both; found that generated codes and aliases could collide (Cassandra-specific) and that the analytics rollup design lost counts across regions | Accepted | Both issues became moot after the move to PostgreSQL in a single region (PLAN-15) | Planning session |
| PLAN-14 | 2026-10-21 | Return the existing code when the same URL is submitted again (A-23) | Proposed reusing codes per owner when the options match | **Rejected** | Engineer kept A-23: every create returns a new code | Planning session |
| PLAN-15 | 2026-10-21 | Justify Cassandra and Valkey; compare with PostgreSQL / MySQL | Compared options, including a single PostgreSQL with replicas and the role of Citus | Accepted | Engineer moved multi-region to a later release and chose **PostgreSQL for v1**, with Citus added when thresholds are reached (ADR-0002, ADR-0004) | Planning session |
| PLAN-16 | 2026-10-21 | Define SLAs and SLOs | Proposed SLIs, SLOs, SLAs and error budgets; clarified that the ~30 s failover covers node and availability-zone failures, not a full region outage | Accepted | Engineer accepted the targets (A-19) | Planning session |
| PLAN-17 | 2026-10-21 | Is Kafka needed? | Compared in-memory batching, Kafka and Valkey Streams | Accepted | Engineer chose **Kafka** (ADR-0008) | Planning session |
| PLAN-18 | 2026-10-21 | Observability | Proposed OpenTelemetry with the Grafana stack (`grafana/otel-lgtm` locally) | Accepted | Engineer chose Grafana (ADR-0013) | Planning session |
