# Final engineering summary

A URL shortener (.NET 8, PostgreSQL) built **engineer-led and AI-assisted**: the engineer approved every spec and
merged every pull request; Claude Code (Claude Opus 5.5) wrote code, tests and documentation within those tasks.
This summary covers the plan and its rationale, the artifacts, risks and validation, assumptions, and limitations,
as of 2026-10-23, when the build stopped after TSK-15 (plus the close-out task TSK-QW).

## 1. Plan and rationale
- **Normalize first.** The ask became functional and non-functional requirements with measurable targets ([requirements](01-requirements.md)), 33 recorded assumptions ([register](assumptions.md)) instead of open questions, and 13 ADRs, each with alternatives and the engineer's sign-off ([ADRs](adr/README.md)).
- **Design before code.** [HLD](03-architecture.md) (components, flows, deployment, failure modes) and [LLD](02-system-design.md) (capacity, SLOs, API contract, schema, algorithms, resilience, configuration).
- **Staged delivery.** Core first ([task breakdown](04-task-breakdown.md)): each stage complete on its own. One task per spec, branch, PR and log entry; tests in the same task; CI gates block merges.
- **Key choices:** availability for reads and consistency for writes; PostgreSQL; collision-free, non-enumerable short codes; `302` redirects; idempotent creates; no cache until measured; OpenTelemetry. Trade-offs: [risks and trade-offs](06-risks-tradeoffs.md) §1.

## 2. What was built
| Area | Delivered | Tasks / PRs |
|---|---|---|
| Platform | Clean Architecture solution (Domain, Application, Infrastructure, three services) with architecture tests; strict build; local Docker stack | TSK-02, 03 (#3, #4) |
| CI and workflow | Required gates: build-test with a test-metrics gate, CodeQL, gitleaks, dependency audit, `perf-smoke`; pinned actions; branch ruleset; prompt library; PR template; traceability log | TSK-01, 04, 12 (#1–#9, #19) |
| Persistence | `links` schema, least-privilege roles, migration bundle, redirect view | TSK-06 (#11) |
| Short codes | Leased ID ranges, keyed Feistel permutation, Base62, region blocks, startup check | TSK-07 (#12) |
| API | Create (generated code or alias, API keys, idempotency, OpenAPI/Swagger), redirect (`302`/`404`/`410`, click events), metadata and delete | TSK-08, 09, 10 (#13, #15, #16) |
| Reliability | Timeouts, retry rule by commit phase, circuit breakers, idempotency-key cleanup, pool caps | TSK-11, 11b (#17, #18) |
| Performance | k6 load test and results; no cache (ADR-0006) | TSK-12 (#19) |
| Brownfield | Link expiry; three real defects fixed with failing tests first; JSON console logging | TSK-14, 15, QW (#24, #25, this PR) |
| Write-ups | [Scenario 1](scenarios/01-greenfield.md), [scenario 2](scenarios/02-brownfield.md), [testing strategy](05-testing-strategy.md), [risks and trade-offs](06-risks-tradeoffs.md), this summary | TSK-13, QW |

## 3. Artifacts
- **Code:** `src/` (6 projects), `tests/` (unit, architecture, integration, benchmarks), `tests/load/` (k6).
- **API and schema:** [`docs/api/openapi-v1.json`](api/openapi-v1.json) (drift-tested); EF migrations in `src/UrlShortener.Infrastructure/Persistence/Links/Migrations`; [LLD §3–4](02-system-design.md).
- **Evidence:** 436 tests; [load-test results](perf/v1-load-test.md); every PR's CI runs on GitHub; the [traceability log](ai-usage/traceability-log.md) (54 rows: what the AI produced, and what was accepted, edited or rejected, with rationale).

## 4. Validation and risk control
- **Tests:** 436 (308 unit, 19 architecture, 109 integration on real PostgreSQL), 0 failed, 0 skipped; Domain 100% and Application 97.4% line coverage. Property, concurrency, fault-injection, least-privilege, migration, contract and never-logged tests ([testing strategy](05-testing-strategy.md)).
- **Tests that can fail:** mutation checks on every high-risk task, every deliberate bug caught; bug fixes shown red before green.
- **Performance:** redirect p99 3.8 ms at 1,500/s per instance, create p99 7.1 ms at 600/s, 0% errors; `perf-smoke` on every PR, proven to fail on a breached threshold.
- **Security:** CodeQL, gitleaks, dependency audit; least-privilege database roles tested as denials; API keys stored only as hashes; no secrets, long URLs or IPs in logs.
- **Setup, verified on a clean clone** (2026-10-23): a fresh clone of this branch, `.env` from `.env.example`, a fresh database: `docker compose up --build --wait` in 63 s; smoke test passed; the README's "Try the API" steps all passed (`201`, `302`, `200`, Swagger, `401` with `WWW-Authenticate`).
- **Risks and failure scenarios:** [risks and trade-offs](06-risks-tradeoffs.md) §2–3.

## 5. How the AI was used, and controlled
- **Within approved tasks only:** specs with intent, constraints, acceptance criteria and decisions; the engineer approved each before work began, and the AI declined to approve or merge its own work when asked to speed things up.
- **Across the lifecycle:** implementation, debugging (e.g. the connection-exhaustion diagnosis, where the first hypothesis was wrong), test generation, documentation, and review preparation (PR descriptions, self-review). Refactoring was planned (TSK-16) but descoped.
- **Disciplined prompting:** a versioned [prompt library](ai-usage/prompts/README.md); prompt iterations recorded for every high-risk task.
- **What went wrong, and what caught it:** analyzer findings, a test that tested nothing, a Linux-only failure, an existing test catching a new bug, the load test exposing uncapped pools; all in the [log](ai-usage/traceability-log.md) and the scenario write-ups.
- **Secure AI usage:** [ADR-0011](adr/0011-confidentiality-and-secure-ai-usage.md); the assignment brief is never committed or quoted.

## 6. Assumptions
33 assumptions, each with a risk and a fallback, none confirmed by the requester ([register](assumptions.md)). The
most consequential: API keys instead of user accounts (A-24); a new code for every create (A-23); codes never
reused (A-25); links don't expire by default (A-22).

## 7. Limitations and what wasn't built
The build stopped after TSK-15 by the engineer's decision; the remaining plan was **descoped**:

| Not built | Consequence |
|---|---|
| Scenario 3 (ambiguous): rate limits, threat checks (TSK-19, 20) | One of the three scenarios is missing; abuse protection is limited to API keys, non-enumerable codes and the fallback budget |
| Validation refactor (TSK-16) | No refactor in scenario 2 |
| Analytics pipeline (stage 2, FR-07) | Click events are built and counted but discarded |
| Dashboards, the ID-usage alert, demo scripts (TSK-21, 22) | Telemetry is viewed through Grafana Explore |
| Trivy, ZAP, Stryker, a Mermaid render check | Fewer automated gates than ADR-0012 lists |
| Replica-lag monitor; a connection pooler for scale | Needed before running with real replicas or at peak scale |

Other limitations: single region; sizing extrapolated from one laptop; .NET 8 support ends on 10 Nov 2026; EF
logs every database command at Information level.

## 8. Effort
Engineer time reported when each PR was opened (the AI's own working time isn't measured): **≈ 7.3 h** for TSK-00
to TSK-15, against **32 h** estimated for the same tasks; per task in the [log](ai-usage/traceability-log.md) and
the scenario write-ups.
