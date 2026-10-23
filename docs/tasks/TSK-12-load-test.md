# TSK-12: v1 load test, the cache decision (ADR-0006), and a k6 smoke gate in CI

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | Medium |
| **Depends on** | TSK-04, TSK-09, TSK-11 |
| **Estimate** | 3 h |
| **Branch** | `task/TSK-12-load-test` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-09 to TSK-13) |

## 1. Intent
Measure v1 against its latency targets, decide with evidence whether a cache is needed (ADR-0006 step 2),
and keep performance from regressing with a short k6 run on every PR.

## 2. Technical context
- **ADR-0006** (pass/fail criteria fixed before the test: redirect p99 ≤ 50 ms per replica at target load, replica CPU < 70 %, hot key 30 % of traffic, scan of random codes stays within the fallback budget; record before/after results in the ADR).
- **NFR-01/02** (scale, p99 targets), LLD §1 (capacity: ~35K redirects/s and ~3.5K creates/s at peak; 4–8 replicas "to be confirmed by the load test"), **ADR-0012 §5** (k6 performance smoke is a required gate from TSK-12).
- README limitation: production scale can't be reproduced locally; sizing is extrapolated from measured per-node results.

## 3. Scope
| # | Component | Purpose |
|---|---|---|
| 1 | `tests/load/` k6 scripts | `redirect.js` (steady load on a seeded set of codes), `create.js`, `hot-key.js` (30 % on one code), `scan.js` (random non-existent codes), shared thresholds |
| 2 | Seed script | Creates N links through the Api before a run |
| 3 | `scripts/load-test.(sh|ps1)` | Runs k6 from the `grafana/k6` container against the compose stack |
| 4 | Results | `docs/perf/v1-load-test.md`: machine, stack, rates, p50/p95/p99, errors, CPU; per-node throughput and the extrapolated replica count |
| 5 | ADR-0006 update | Results and the outcome: criteria met → no cache (TSK-30 not triggered); otherwise TSK-30 is triggered. A history entry, not a new decision. |
| 6 | CI job `perf-smoke` | Starts the compose stack on the runner, seeds, runs a 30 s k6 smoke with SLO thresholds; fails the build if p99 is exceeded |
| 7 | Proof that the gate fails | A throwaway proof PR with an impossible threshold, closed unmerged (as the TSK-04 gate proofs in PR #6) |
| 8 | Docs | README "Performance", CHANGELOG, log |

**Out of scope:** making `perf-smoke` a **required** check: that's a ruleset change the engineer makes after merge (CLAUDE.md: the ruleset names the required checks). Trivy/ZAP (TSK-24).

## 4. Constraints
- Thresholds are written before the run and not tuned afterwards to pass.
- No test hooks in product code (no "slow mode" endpoints); the failing proof uses a threshold, not a code change.
- The CI job stays well under the 15 min PR budget (ADR-0012 §5).

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | The four scripts run against the compose stack and produce k6 summaries | Local run, summaries committed in `docs/perf/` |
| AC2 | Results recorded: per scenario rate, p50/p95/p99, error rate, Api/Redirect/PostgreSQL CPU | `docs/perf/v1-load-test.md` |
| AC3 | ADR-0006 records each criterion as met / not met and the cache decision | ADR diff in the PR |
| AC4 | `perf-smoke` runs on the PR and passes; the proof PR shows it **fails** on a breached threshold | CI runs (links in the PR) |
| AC5 | Gates green; docs; log and hours | CI and review |

## 6. AI usage plan
Scripts, CI job and write-up by AI; the AI runs the load test locally and reports raw numbers. The engineer reviews the thresholds **before** the run and the conclusions after it. Hours given when the PR is opened.

## 7. Risks
| Risk | Mitigation |
|---|---|
| A laptop isn't a production node (k6, services and PostgreSQL share one machine) | State it in the results; report per-node numbers and extrapolate, don't claim production throughput |
| CI runners are noisy, so the smoke flakes | Low rate (~50 req/s) for 30 s, p99 thresholds at the SLO; re-evaluate after 10 runs |

## 8. Decisions for the engineer
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | Local run size | (a) the highest rate the laptop sustains without k6 itself saturating, for 2–5 min per scenario; (b) fixed rates from the capacity model | **(a)**, and extrapolate to production with the documented per-node method. |
| D2 | Smoke thresholds in CI | (a) the SLOs (redirect p99 ≤ 50 ms, create p99 ≤ 200 ms) at low rate; (b) looser CI-only thresholds | **(a)**, as ADR-0012 says; revisit only if it flakes, and record it in the log if we do. |
