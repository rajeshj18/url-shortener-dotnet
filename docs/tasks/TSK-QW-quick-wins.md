# TSK-QW: Quick wins to close the deliverable gaps (one PR)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 close-out, after the build stopped at TSK-15 |
| **Risk class** | Medium (mostly documentation; one small code change and one CI job) |
| **Depends on** | TSK-15 (merged) |
| **Estimate** | ~3–3.5 h |
| **Branch** | `task/TSK-QW-quick-wins` |
| **Status** | **Approved** by the engineer on 2026-10-23, with D1 = (b) and D2 = (b) |

## 1. Intent
The build stopped after TSK-15 (engineer's decision, 2026-10-23). A gap analysis against the brief found
deliverables that are cheap to close because their content already exists in the repository's records: the final
engineering summary, the testing approach, the risks and trade-offs, the scenario 2 write-up, an honest project
status, and one place where the code contradicts an ADR. This task closes them **in one PR, at the engineer's
request**, an explicit exception to "one task per branch and PR" (CLAUDE.md, ADR-0012), recorded in the log.

## 2. Technical context
- **Sources, all real:** specs TSK-00 to TSK-15, their PRs (#1 to #25), the traceability log, the ADRs, the assumptions register, CI runs, `docs/perf/v1-load-test.md`, the scenario 1 write-up.
- **The gap analysis** of 2026-10-23 (in the conversation): scenario 3 missing; no final summary; scenario 2 incomplete; no testing-strategy or risks documents; analytics not built; docs still list TSK-16 onward as planned; JSON console logging missing (ADR-0013).
- **ADR-0013:** "`ILogger` with the OpenTelemetry log exporter, plus JSON console output." The services use .NET's default plain-text console formatter.

## 3. Scope
One commit per item, so each can be reviewed on its own in the PR.

| # | Item | Content |
|---|---|---|
| 1 | `docs/08-final-summary.md` | Plan and rationale; artifacts (linked); risks, trade-offs and validation; assumptions; limitations; what was built and what wasn't; estimate vs actual hours across all tasks; a brief-to-evidence table. Includes the clean-clone result (#9). |
| 2 | `docs/05-testing-strategy.md` | Test layers (unit, architecture, integration with Testcontainers); property, concurrency and fault-injection tests; mutation checks; CI gates and their thresholds; the load test and the `perf-smoke` gate; what isn't tested, and why |
| 3 | `docs/06-risks-tradeoffs.md` | The key trade-offs (from the ADRs' alternatives), the risk register (from the specs' risk tables and findings such as TSK-11b), failure scenarios and their guardrails, known limitations |
| 4 | `docs/scenarios/02-brownfield.md` | Scenario 2 in the six-part structure: the enhancement (TSK-14) and the bug fix (TSK-15), each with its impact analysis and validation; the refactor (TSK-16) stated as **not done**; this task as the test-and-documentation improvement |
| 5 | Honest status | A log row recording the stop; TSK-16 onward marked **descoped** (not "planned") in the task breakdown (with a history entry), the README, the requirements matrix and the scenarios README |
| 6 | **JSON console logging** (ADR-0013) | `Logging:Console:FormatterName = json` with UTC timestamps in the Api, Redirect and AnalyticsWorker settings; a test that each host resolves the JSON formatter; a compose check that `docker compose logs api` shows one JSON object per line |
| 7 | **Stored invalid data answers `500`, not `400`** (D1) | A row that breaks a domain rule is the server's problem, not the client's. Failing test first (shown locally, D1 = b), then the fix: rehydration failures become a server error (logged, `500`) on both the metadata and the redirect paths |
| 9 | Clean-clone run | A fresh clone into a temporary folder, following only the README: `.env` from `.env.example`, `docker compose up --build --wait`, the smoke test and the "Try the API" steps; the result (pass, or what broke and was fixed) recorded in the summary |

**Out of scope:** TSK-16 to TSK-22 themselves (descoped), scenario 3, analytics, Trivy/ZAP, the replica-lag monitor; the Mermaid CI check (#8, excluded by D2 and still deferred).

## 4. Constraints
- Every claim links to evidence; no invented numbers. Figures are re-measured or extracted by script, not remembered (the TSK-13 lesson).
- Honest about what wasn't built and what the AI got wrong.
- Never quote the assignment brief or name the company or its classification marking (ADR-0011).
- No behaviour change apart from the console log format (#6). Never log long URLs, IP addresses or API keys.
- The local stack is stopped (`docker compose down`, keeping the volume) before the clean-clone run, because both use the same names and ports.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Items 1–4 exist, each section backed by evidence links | Review + local link check |
| AC2 | No document says a descoped task is planned or coming; the stop is in the log and the plan's history | `grep` for TSK-16 to TSK-22 references + review |
| AC3 | All three services write JSON console logs; a test covers it; nothing sensitive is logged | Test + compose check |
| AC4 | A stored row that breaks a domain rule → `500` (logged) on metadata and redirect; the test was committed first and shown failing locally | Integration test; its local red run recorded in the log |
| AC5 | The clean-clone run passes, or its failures are fixed and recorded | Summary section + log |
| AC6 | Gates green; CHANGELOG; log row with hours given when the PR is opened | CI and review |

## 6. AI usage plan
AI drafts every document from the repository's records, makes the small code and CI changes, and runs the clean clone. The engineer reviews accuracy, tone and emphasis of the summary (it describes our work together) and gives hours when opening the PR.

## 7. Risks
| Risk | Mitigation |
|---|---|
| The summary overstates what was built or hides corrections | Built from the log and PRs; descoped work and AI mistakes listed explicitly; engineer review |
| One problem holds up the whole PR | One commit per item; a problem item can be reverted from the branch without touching the others |
| The clean clone exposes a setup bug | That's the point: fix it in this PR and record it |

## 8. Decisions for the engineer
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | Include #7 (stored invalid data answers `400`, should be `500`)? | (a) leave out; (b) include, with the failing test shown locally only (no red CI run in a single push) | Recommended (a). **Decided: (b)** (engineer, 2026-10-23) |
| D2 | Include #8 (Mermaid render check in CI)? | (a) yes, as a separate, not-yet-required job; (b) no | Recommended (a). **Decided: (b)** (engineer, 2026-10-23): stays deferred |
