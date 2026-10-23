# Requirements

> Status: draft for engineer review. Requirements are restated in our own words (ADR-0011). Where the
> brief was silent or ambiguous, we recorded an assumption in [`assumptions.md`](assumptions.md).
> Targets are detailed in the [SLO/SLA section](02-system-design.md#2-service-levels-slo--sla).

## 1. Problem statement

Build a URL-shortening service that turns long URLs into short codes that can't be enumerated in order, and redirects visitors
to the original URL. It must also give link owners click analytics.

The service must handle **100M new links per day and 10× as many redirects**. Redirects must be fast and
highly available. No acknowledged write may be lost. A short code must always identify exactly one link.

The service is to be built with an engineer-led, AI-assisted workflow that is itself part of the deliverable (ADR-0001).

## 2. Functional requirements

| ID | Requirement | Notes |
|---|---|---|
| FR-01 | **Shorten:** create a short link for a valid long URL and return the code and short URL | A-20, A-21 |
| FR-02 | **Custom alias:** optionally use a caller-chosen alias. Return `409` if it is taken, and `400` if it has the shape reserved for generated codes (exactly 7 Base62 characters). | A-21, ADR-0005 |
| FR-03 | **Redirect:** `GET /{code}` returns `302` to the long URL; `404` if unknown; `410` if deleted or expired | ADR-0007 |
| FR-04 | **Expiry:** optionally set an expiry time. Expired links return `410`. | A-22 |
| FR-05 | **Delete:** the owner can delete a link (soft delete; returns `410` afterwards) | A-25 |
| FR-06 | **Metadata:** the owner can retrieve a link's details | A-24 |
| FR-07 | **Analytics:** the owner can see total clicks, an hourly/daily time series, top referrer hosts and device class | A-31, ADR-0008 |
| FR-08 | **Safe retries:** a create sent again with the same `Idempotency-Key` **within 24 h** returns the original result instead of creating a duplicate; reusing the key for a different request returns `422` | ADR-0010, LLD §6.1 |
| FR-09 | **Authentication and ownership:** management endpoints need an API key; only the owner can view stats, read metadata or delete | A-24 |
| FR-10 | **URL validation:** only absolute `http`/`https` URLs up to 2,048 characters; no embedded credentials; no links back to our own domain | A-20, LLD §7 |

## 3. Non-functional requirements

| ID | Category | Requirement | Target / measure | Verified by |
|---|---|---|---|---|
| NFR-01 | Scalability | Handle the projected load | 100M creates/day (~3.5K/s peak), 1B redirects/day (~35K/s peak); scale out without redesign | Load test plus extrapolation; capacity estimates |
| NFR-02 | Performance | Low latency | Redirect p99 ≤ 50 ms; create p99 ≤ 200 ms | k6 load test; latency dashboards |
| NFR-03 | Availability | Redirects stay up through node and zone failures | Redirect 99.95% SLO; create/delete 99.9% SLO | Failover drill; SLO dashboards |
| NFR-04 | Durability | No acknowledged write is lost on a node or zone failure | Recovery point = 0 (synchronous standby); region disaster: recovery time ≤ 4 h, data loss ≤ 15 min | Failover drill; backup and restore test |
| NFR-05 | **Data integrity** | **A short code is unique across all regions and for all time.** One code always identifies exactly one link, in every region, and is **never reused** (not even after deletion or expiry). | Zero duplicate codes | Non-overlapping region blocks checked by CI on the block registry; primary-key constraint; property tests that the permutation is one-to-one; startup check on region blocks (ADR-0005) |
| NFR-06 | Consistency | Deletes and expiries take effect quickly everywhere | 99% within 5 s (SLO); all within 60 s (SLA) | Delete propagation metric; integration test |
| NFR-07 | Consistency | Read-your-writes for new links | A newly created link redirects immediately | Integration test (create, then redirect at once) |
| NFR-08 | Analytics freshness and accuracy | Stats are up to date and each click is counted once | Stats lag p99 ≤ 60 s; ≥ 99.9% of clicks captured; each event counted exactly once | Worker integration test with replayed events; freshness metric |
| NFR-09 | Security | Codes can't be **enumerated in order**, and **random guessing** is limited. A random 7-character guess hits a real link with probability equal to the share of the code space in use (~1% after 1 year, ~5% after 5 years), so short codes aren't secrets. | Keyed permutation; per-client rate limits and abuse detection on redirect misses; validation rules | Unit tests; ambiguous scenario |
| NFR-10 | Privacy | Collect as little personal data as possible | No IP addresses or full User-Agents stored; no personal data in logs | Code review; log checks |
| NFR-11 | Observability | Every request can be traced from start to finish; SLOs are measured | Traces across HTTP and Kafka; SLO dashboards and burn-rate alerts | ADR-0013; dashboards in the repo |
| NFR-12 | Maintainability | Modular, testable code | Clean Architecture enforced by architecture tests; analyzers with warnings as errors | CI quality gates |
| NFR-13 | Portability / local run | Runs end to end locally on open-source images | One `docker compose up`, ~3 GB RAM | Clean-clone run before submission |
| NFR-14 | Secure AI usage | No secrets or confidential material in the repo; AI output goes through the same gates as any other code | Secret scanning, dependency audit, CodeQL | ADR-0011; CI |

## 4. Out of scope for v1

- Multi-region deployment (planned for a later release; NFR-05 already applies across regions)
- End-user accounts or UI
- Editing a link's target
- QR codes, custom domains, geo/device routing, link previews, bulk import
- Unique-visitor counts

See A-17, A-26, A-27 and A-32.

## 5. Ambiguities

All ambiguities and how we resolved them are recorded in the [assumptions register](assumptions.md).
The most significant one, what "system" means in the brief, is decided in ADR-0001.

## 6. Traceability to the brief

Each section of the brief is described in our own words (ADR-0011) and mapped to the artifacts that address it.
**Status:** ✅ done · ⚠️ gap or partial (see §6.8) · ❌ descoped (stage 1 closed after TSK-15, 2026-10-23).

### 6.1 Objective and scenario
| Brief item | Addressed by | Status |
|---|---|---|
| A working prototype that turns a requirement into a reviewable engineering outcome | Stage 1 build (TSK-02 to TSK-15, TSK-QW); PRs per task (ADR-0012); [final summary](08-final-summary.md) | ✅ |
| Show requirement understanding, task decomposition, multi-step execution, output generation and validation | §6.3 rows 1, 2, 4, 5, 6 | see below |
| Engineer-led execution accelerated by AI, not autonomous orchestration | ADR-0012; engineer-authored sign-off commits; traceability log | ✅ |
| A URL shortener with **core APIs** | FR-01 to FR-10; LLD §3; TSK-08 to TSK-10 ([scenario 1](scenarios/01-greenfield.md)) | ✅ FR-01 to FR-06 and FR-08 to FR-10 (FR-04 by TSK-14) |
| … with **analytics** | FR-07; ADR-0008; LLD §6.5; TSK-23 (stage 2) ❌ descoped: click events are built and counted, not stored | ⚠️ G3: designed, not built |
| … with **reliability features** | ADR-0002; LLD §6.6–6.7; HLD §5; TSK-11, TSK-11b; fault-injection tests | ✅ |
| Completed over 2–3 days with AI assistance, showing engineering judgment | Timebox discussion in `04-task-breakdown.md` §5; documented trade-offs (ADR alternatives) | ⚠️ G5 |

### 6.2 Scope
| Brief item | Addressed by | Status |
|---|---|---|
| Greenfield work | Scenario 1 (TSK-05 to TSK-13): [write-up](scenarios/01-greenfield.md) | ✅ |
| Brownfield: enhancements, refactors, bug fixes | [Scenario 2](scenarios/02-brownfield.md): expiry (TSK-14) ✅, three real defects (TSK-15, TSK-QW) ✅, validation refactor (TSK-16) ❌ descoped | ⚠️ refactor missing |
| Test and documentation improvements | Tests in every task; TSK-QW (testing strategy, risks, summaries, JSON logging); TSK-17 and Stryker descoped | ✅ partly |
| Well-defined and ambiguous requirements | Scenarios 1–2 (well-defined) ✅; scenario 3 (TSK-19, TSK-20) ❌ descoped | ⚠️ ambiguous scenario missing |

### 6.3 Core requirements
| # | Brief item | Addressed by | Status |
|---|---|---|---|
| 1 | Requirement understanding: interpret intent, find ambiguity, normalize | This document (§1–5); `assumptions.md`; ADR-0001 | ✅ |
| 2 | Task decomposition with dependencies and sequencing | `04-task-breakdown.md` (tasks, dependency graph, critical path), approved by the engineer | ✅ |
| 3 | Codebase reasoning for brownfield work: impacted modules, services, APIs, data flows | Brownfield impact analyses (TSK-14, TSK-15 specs; [scenario 2](scenarios/02-brownfield.md) §3; TSK-16 descoped); HLD flows; ADR-0009 dependency rules | ✅ partly (no refactor) |
| 4a | AI across implementation, debugging, refactoring, test generation, documentation, review preparation | Every task (ADR-0012 §2); PR descriptions and AI self-review; the log records debugging sessions | ✅ except refactoring (TSK-16 descoped) |
| 4b | Tasks defined with intent, constraints, acceptance criteria, technical context | Task-spec template (TSK-01); `docs/tasks/TSK-xx` | ✅ |
| 4c | **Disciplined prompting with iterative refinement** | Prompt library with version history (TSK-01); prompt iterations logged for high-risk tasks (definition of done) | ✅ (prompt iterations in every high-risk log row) |
| 4d | Traceability: generated, edited or rejected, with rationale | `ai-usage/traceability-log.md` (PLAN-01 to PLAN-26 so far; TSK-xx to follow) | ✅ |
| 4e | Quality gates: analysis, linting, tests, security, **performance** | ADR-0012 §5; TSK-04 (core gates); k6 `perf-smoke` on every PR (TSK-12); [load-test results](perf/v1-load-test.md) | ✅ (G2 resolved; `perf-smoke` becomes required when the engineer adds it to the ruleset) |
| 4f | Secure AI usage | ADR-0011; gitleaks, SCA and CodeQL in TSK-04 | ✅ |
| 4g | Human sign-off for high-impact changes | ADR-0012 §4 risk classes; signed-off ADR commits (git history) | ✅ |
| 4h | Explicit engineer ownership of correctness, maintainability, production readiness | PR checklist ownership statement (ADR-0012); merge commits by the engineer | ✅ |
| 5 | Production-quality code, API and schema definitions, unit and integration tests, documentation | LLD §3–5 (API, schema, classes); OpenAPI (TSK-08); definition of done (`04` §2) | ✅ (gaps listed in the [final summary](08-final-summary.md) §7) |
| 6 | Risks, trade-offs, failure scenarios, validation and safety guardrails | HLD §5 failure modes; ADR alternatives; LLD §6.7; [risks and trade-offs](06-risks-tradeoffs.md) (TSK-QW) | ✅ |
| 7 | Controlled oversight: engineer leads and approves all outputs | ADR-0012; branch protection (TSK-04); propose and sign-off commits | ✅ |
| 8 | Final engineering summary: plan and rationale, artifacts, risks, trade-offs, validation, assumptions, limitations | [final summary](08-final-summary.md) (TSK-QW) | ✅ |

### 6.4 Deliverables
| Brief item | Addressed by | Status |
|---|---|---|
| Working prototype, runnable end to end | `docker compose up`; clean-clone run passed (TSK-QW, [summary §4](08-final-summary.md)); demo scripts (TSK-21) ❌ descoped | ✅ |
| Architecture overview: components, tools, execution approach, control flow, key decisions | HLD (`03-architecture.md`) incl. §8 engineering workflow; ADR index | ✅ |
| Three scenarios, each showing decomposition, execution, validation | [Scenario 1](scenarios/01-greenfield.md) ✅, [scenario 2](scenarios/02-brownfield.md) ✅ (no refactor), scenario 3 ❌ descoped | ⚠️ 2 of 3 |
| Setup instructions | README "Run locally" and "Try the API", verified on a clean clone (TSK-QW) | ✅ |
| Testing approach, limitations, trade-offs | [Testing strategy](05-testing-strategy.md), [risks and trade-offs](06-risks-tradeoffs.md) (TSK-QW); definition of done (`04` §2) | ✅ |
| Publish in a new **public** GitHub repository and share the link | TSK-00 (publish); the public repository is the deliverable's home | ✅ |

### 6.5 Evaluation criteria
| Brief item | Where the evidence is |
|---|---|
| Effectiveness of AI-assisted execution | Traceability log; AI disclosure in each PR; actual vs estimated hours |
| Architecture and system design quality | HLD, LLD, 13 ADRs with alternatives |
| Depth of decomposition and execution | `04-task-breakdown.md`; one PR per task |
| Realism and quality of outputs | Running prototype; tests; load-test results (TSK-12) |
| Rigour of validation and risk management | Quality gates (ADR-0012 §5); HLD failure modes; fault-injection tests (TSK-11) |
| Clarity and defensibility of decisions | ADRs (context, alternatives, consequences, history) |
| Core principles: modular, testable, reliable, secure, scalable, safe change management | Modular and testable: ADR-0009 and architecture tests · Reliable: ADR-0002, LLD §6.7 · Secure: ADR-0011, CodeQL/ZAP, validation rules · Scalable: capacity estimates, ADR-0004/0005 · Safe change management: expand/contract migrations, branch protection, API versioning (`/api/v1`), `CHANGELOG.md` (G4 resolved) |
| Engineering judgment | "Measure first" (ADR-0006), Citus thresholds (ADR-0004), staged scope (`04` §1), rejected alternatives |

### 6.6 Expectation
| Brief item | Addressed by |
|---|---|
| Production-grade work with strong design fundamentals | HLD, LLD, ADRs; definition of done |
| AI used as an accelerator, with the engineer owning outputs and reasoning | ADR-0012; signed-off commits; log entries where the engineer revised or rejected AI proposals (e.g. PLAN-14, PLAN-24) |

### 6.7 Status summary
- **Design phase:** complete (requirements, HLD, LLD, 13 accepted ADRs, approved task breakdown).
- **Build:** stage 1 built TSK-00 to TSK-15 and TSK-QW; TSK-16 to TSK-22 were descoped on 2026-10-23.

### 6.8 Gaps found by this matrix
| ID | Gap | Action | Decision |
|---|---|---|---|
| **G1** | **Iterative prompt refinement** isn't evidenced: the log records only a prompt summary | TSK-01: prompt library with a version history. For **High-risk tasks**, the `TSK-xx` log entry records the prompt iterations (v1 → v2 → … and why each changed). Add this to the definition of done. | ✅ Approved, in `04` (TSK-01, definition of done) |
| **G2** | The **performance** gate runs in CI only from stage 2 | Move a **k6 smoke job into stage 1** (extend TSK-12 to start the compose stack in CI and run k6; ~+1 h), so all five gate types from the brief exist in stage 1 | ✅ Approved, in `04` (TSK-12) |
| **G3** | **Analytics** is built only in stage 2 | Known and accepted (`04` §1). If stage 2 isn't reached, the final summary states it is designed but not built. | Accepted as known |
| **G4** | **Safe change management** is evidenced only partly: there's no CHANGELOG | Add `CHANGELOG.md` (TSK-01). The definition of done requires an entry for every user-visible change. | ✅ Approved, in `04` (TSK-01, definition of done) |
| **G5** | The **2–3 day timebox** is exceeded | Known; stated openly in the final summary with the rationale | Accepted as known |
