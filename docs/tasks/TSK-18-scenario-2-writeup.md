# TSK-18: Scenario 2 write-up (brownfield)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 2, brownfield |
| **Risk class** | Low |
| **Depends on** | TSK-14 to TSK-17 |
| **Estimate** | 1 h |
| **Branch** | `task/TSK-18-scenario-2` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-14 to TSK-18) |

## 1. Intent
Show how changes to existing code were planned and validated: an enhancement, a bug fix, a refactor and a
test/documentation improvement, each with its impact analysis.

## 2. Technical context
Same structure as scenario 1 (`docs/scenarios/README.md`): **ask → normalization → decomposition → AI-assisted
execution → validation → outcome**, built only from the repository's records (specs TSK-14 to TSK-17, their PRs,
log rows, CI runs, including the deliberately red first run of TSK-15).

## 3. Scope
| # | Component | Purpose |
|---|---|---|
| 1 | `docs/scenarios/02-brownfield.md` | The write-up, with one **impact analysis** per change: what was touched, what wasn't, compatibility (expand-only migration, stable error codes), how regressions were ruled out (characterization tests, the failing-test-first commit, the drift test) |
| 2 | `docs/scenarios/README.md` | Scenario 2's row updated to what was actually built (the cache enhancement wasn't needed, ADR-0006; analytics is stage 2) |
| 3 | Requirements matrix | FR-04 and the brownfield rows |
| 4 | Log, CHANGELOG | As usual |

**Out of scope:** scenario 3 (TSK-19, TSK-20).

## 4. Constraints
- Every claim links to evidence; no invented numbers; honest about corrections.
- Never quote the assignment brief or name the company (ADR-0011).

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Six sections, with an impact analysis per change and evidence links | Review |
| AC2 | Estimate vs actual for TSK-14 to TSK-17 matches the log | Review |
| AC3 | Links resolve | Local link check |
| AC4 | Log entry with hours | Review |

## 6. AI usage plan
AI drafts from the records; the engineer reviews tone and accuracy, and gives hours when opening the PR.

## 7. Risks
| Risk | Mitigation |
|---|---|
| The write-up overstates or hides corrections | Built from the log and the PRs; the engineer reviews |
