# TSK-13: Scenario 1 write-up (greenfield)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | Low |
| **Depends on** | TSK-12 |
| **Estimate** | 1 h |
| **Branch** | `task/TSK-13-scenario-1` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-09 to TSK-13) |

## 1. Intent
Tell the greenfield story end to end so a reviewer can follow how the ask became working, tested software
with AI assistance, and where the engineer steered.

## 2. Technical context
- `docs/scenarios/README.md` (common structure: **ask → normalization → decomposition → AI-assisted execution → validation → outcome**).
- Sources, all real: task specs TSK-05 to TSK-12, their PRs, the traceability log entries (including what the AI got wrong and the engineer's decisions), CI test metrics, the TSK-07 benchmark, the TSK-12 load-test results.

## 3. Scope
| # | Component | Purpose |
|---|---|---|
| 1 | `docs/scenarios/01-greenfield.md` | The write-up: the ask in our words; normalization into FR/NFR and assumptions; decomposition into tasks (with the dependency graph); execution per task (AI contribution, prompt iterations for high-risk tasks, what was corrected, decisions such as TSK-08 D2); validation (tests, gates, mutation check, benchmark, load test); outcome against FR/NFR and the SLOs; estimates vs actual hours |
| 2 | `docs/scenarios/README.md` | Link the write-up. Correct scenario 1's row: expiry moved to the brownfield scenario (TSK-14), as the task breakdown says. |
| 3 | Requirements traceability matrix | Mark FR/NFR met by stage 1 so far, with evidence links |
| 4 | Log, CHANGELOG | As usual |

**Out of scope:** scenario 2 and 3 write-ups (TSK-18, TSK-20); the final summary (TSK-22).

## 4. Constraints
- Every claim links to evidence (PR, test, log row, result file). No invented numbers.
- Honest about misses: bugs the AI introduced, things the engineer caught, deviations from specs.
- Never quote the assignment brief or name the company (ADR-0011).

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | All six sections present, each with evidence links | Review |
| AC2 | Estimate vs actual table for TSK-05 to TSK-12 matches the log | Review against the log |
| AC3 | Scenarios README consistent with the task breakdown | Review |
| AC4 | Links resolve | Link check (CI or local script) |
| AC5 | Log entry with hours | Review |

## 6. AI usage plan
AI drafts from the repository's own records; the engineer reviews tone, accuracy and emphasis, and may rewrite parts in their own words. Hours given when the PR is opened.

## 7. Risks
| Risk | Mitigation |
|---|---|
| The write-up overstates what the AI did, or hides corrections | Built only from the log and PRs; the engineer reviews |
