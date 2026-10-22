# ADR-0012: Engineer-led workflow and risk-based sign-off

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0001, ADR-0009, ADR-0011, NFR-12, NFR-14, [HLD §8](../03-architecture.md#8-engineering-workflow-how-the-system-is-built)

## Context
The brief asks for engineer-led execution, not autonomous orchestration: AI assists *within* tasks, and
the engineer approves every output and signs off on high-impact changes. The evidence for this has to be
visible in the repository, mainly in the git history, and enforced by tooling rather than by good intentions.

## Decision

### 1. Two phases
| Phase | How changes land |
|---|---|
| **Design** (before the repository is on GitHub) | Direct commits to `main`. AI-prepared commits carry `Co-Authored-By: Claude Opus 5.5`. **Each ADR is accepted in a separate commit authored by the engineer**, with `Signed-off-by`. |
| **Build** (from publication onwards) | **One task per branch and PR.** No direct pushes to `main` (blocked by branch protection). |

### 2. Task flow (build phase)
1. The **engineer approves the task breakdown** (`docs/04-task-breakdown.md`). The AI may draft it.
2. Each task has a **spec** in `docs/tasks/TSK-xx-<name>.md`, from the template `docs/templates/task-spec.md`. It records intent, constraints, acceptance criteria, technical context and risk class.
3. Branch `task/TSK-xx-<name>`. The AI helps with implementation, tests, debugging, docs and the PR description, then runs a self-review. No multi-agent orchestration is used.
4. CI gates run (§5). The engineer reviews, fills in the checklist, and **merges with a merge commit**, never squash or rebase, so the AI's commits and the engineer's commits stay distinguishable.
5. A **traceability-log entry** `TSK-xx` (matching the spec ID) records: intent, tool and model, prompt summary, whether the output was accepted, edited or rejected and why, which gates passed, and the PR link. `PLAN-xx` IDs are used for design-phase entries. The log is append-only and records only things that actually happened.

### 3. Who commits what
| Action | Who | Evidence in git |
|---|---|---|
| Code and docs on a feature branch | AI, directed by the engineer | Commits with `Co-Authored-By: Claude Opus 5.5` |
| Review fixes | Engineer | The engineer's own commits, without an AI line |
| Merge a PR | **Engineer** | A merge commit authored by the engineer |
| Accept an ADR | **Engineer** | The engineer's commit with `Signed-off-by` |

### 4. Risk classes and sign-off
| Risk | Examples | Sign-off |
|---|---|---|
| **High** | DB schema and migrations · database users and permissions · public API contract · `ClickEvent` schema · consistency, caching and failover logic · ID generation and the **ID block registry** · the Feistel key, secrets and auth · CI pipeline and branch protection | Full checklist, including the explicit ownership statement, plus a log entry |
| **Medium** | New use case or endpoint behind an existing contract · resilience settings · dependency upgrades · Docker/compose changes | Full checklist |
| **Low** | Docs, tests, refactors covered by tests, logging wording | Checklist; compatibility items marked N/A |

**PR checklist** (the source for `.github/pull_request_template.md`):
- Task ID, spec link, risk class
- **AI disclosure:** tool and model; what the AI generated; what the engineer edited or rejected, and why
- Backward compatible: DB schema (expand/contract), `ClickEvent` (optional fields only), public API
- Tests added or updated, and passing
- All CI gates green
- Security considered: input validation, secrets, permissions
- Docs, ADRs and assumptions updated if behavior or decisions changed
- Traceability entry added
- *High risk:* "I have reviewed every changed line and accept ownership."

### 5. Quality gates: what runs, and when
| When | What runs |
|---|---|
| **Locally, before committing** | Build, unit and architecture tests. Optional pre-commit hook: gitleaks. |
| **Every push to a PR branch** (required before merge; target ≤ 15 min, independent jobs in parallel) | Build with warnings as errors · NetAnalyzers + `dotnet format --verify-no-changes` · **unit, architecture and integration tests** (Testcontainers) · coverage (Coverlet, ≥ 80% on Domain and Application) · **SAST:** CodeQL · **secret scanning:** gitleaks · **dependency scanning (SCA):** NuGet Audit + `dotnet list package --vulnerable` · build images + **container scanning:** Trivy · start the compose stack → smoke test + **k6 performance smoke** (p99 within the SLOs) + **DAST:** OWASP ZAP baseline (passive) |
| **After merging to `main`** | The same pipeline again, then publish the images |
| **Daily** (scheduled) | SCA and Trivy on `main`, because new vulnerabilities are published even when our code doesn't change |
| **Weekly / nightly** (scheduled) | CodeQL with the latest rules · **ZAP full scan (active DAST)** against the compose stack |
| **Before release / submission** | Full ZAP active scan · full k6 load test (per-node throughput, to confirm the capacity figures) |

- A PR fails on any build warning, test failure, coverage below threshold, a secret, a vulnerable package, a high-severity CodeQL, Trivy or ZAP finding, or a p99 over the SLO.
- **ZAP tuning:** redirecting (`302` from `/{code}`) is the product's purpose, so the "open redirect" rule is tuned for that endpoint. The validation rules (LLD §7) are what's actually under test.
- Secret scanning and dependency scanning implement ADR-0011.

### 6. Mechanical enforcement
**Branch protection on `main`** (build phase):
- a PR is required before merging, with 0 approvals (one-person repository),
- every gate in §5 is a required status check,
- branches must be up to date before merging,
- no force-pushes and no deletion,
- **only merge commits** are allowed.

## Alternatives considered
| Option | Why not chosen |
|---|---|
| Squash merges | Would collapse the AI's commits and the engineer's commits into one, losing the evidence of who did what |
| Autonomous multi-agent build | Contradicts the brief (engineer-led; AI within tasks) |
| Every scan on every PR, including the full ZAP active scan | An active scan takes 15–60 min. It runs nightly and before release instead. |
| Review discipline without branch protection | Not enforceable; one direct push would break the audit trail |

## Consequences
- **GitHub doesn't allow approving your own PR.** In this one-person repository, sign-off is the completed checklist plus the engineer's merge commit. This limitation is stated openly.
- The design phase has no PRs; its evidence is the propose/accept commit pairs and the `PLAN-xx` log entries.
- The PR template, task-spec template, CI workflows and branch protection are **created in the workflow setup phase**, as a high-risk change.

## History
- 2026-10-21 · Proposed.
- 2026-10-22 · Proposed · Revised after a consistency review: design and build phases (matching how the design phase actually ran); merge commits only; who commits what; full risk classes (block registry, `ClickEvent`, database users, secrets); defined PR checklist; quality gates with when each runs, adding container scanning (Trivy) and DAST (ZAP baseline on PRs, full scan nightly and before release); branch protection; task-spec location and ID scheme.
- 2026-10-22 · **Accepted** · Engineer signed off.
