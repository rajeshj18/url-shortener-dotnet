# TSK-04: CI pipeline and branch protection

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Publish, workflow and platform |
| **Risk class** | **High** (CI pipeline and branch protection, ADR-0012 §4). Requires the ownership statement and logged prompt iterations. |
| **Depends on** | TSK-02, TSK-03 |
| **Estimate** | 2.5 h (re-estimated: ~1.1 h, `04` §5.1) |
| **Branch** | `task/TSK-04-ci-pipeline` |
| **Status** | Approved by the engineer on 2026-10-22, with additions: order of steps, and what triggers CI (§6) |

## 1. Intent
Make the quality gates **automatic and impossible to bypass**. Every PR is built, analyzed, tested and
scanned in CI. The **test metrics** (total, passed, failed and skipped, plus coverage) are posted on the PR.
`main` accepts changes only through a PR whose required checks are green (ADR-0012 §5–6; definition of done in `04` §2).

## 2. Technical context
- **ADR-0012 §5:** these gates run on every push to a PR (stage 1 core set): build with warnings as errors, analyzers, formatting, unit/architecture/integration tests, coverage, CodeQL, gitleaks, SCA. Trivy and ZAP come in TSK-24; the k6 smoke in TSK-12.
- **ADR-0012 §6:** branch protection, with a PR required, required checks, up-to-date branches, no force-push or deletion, merge commits only.
- **`04` §2:** the test-metrics gate is **failed = 0, skipped = 0, coverage ≥ 80% on Domain and Application**.
- **ADR-0011:** secret scanning and the dependency audit.
- No `gh` CLI, so **branch protection is configured by the engineer in GitHub Settings**, following exact steps in the PR. CI results are checked through GitHub's public API.

**Versions (latest releases, checked today), pinned to full commit SHAs:**

| Tool | Version |
|---|---|
| `actions/checkout` | v7.0.1 |
| `actions/setup-dotnet` | v6.0.0 |
| `actions/upload-artifact` | v7.0.2 |
| `github/codeql-action` | v4.39.1 |
| `EnricoMi/publish-unit-test-result-action` | v2.25.0 |
| `danielpalme/ReportGenerator-GitHub-Action` | 5.5.11 |
| gitleaks CLI | v8.30.1, downloaded and checked against the release checksum (MIT; avoids the action's licence-key requirement for organisations) |

## 3. Scope
**In scope:**

| # | File / component | Purpose |
|---|---|---|
| 1 | `.github/workflows/ci.yml` | On `pull_request` and pushes to `main`. Jobs: **`build-test`** (restore with NuGet audit → `dotnet format --verify-no-changes` → build → tests with TRX results and coverage → test-results report → test and coverage gate), **`codeql`** (C#), **`secrets`** (gitleaks across the full history), **`dependencies`** (vulnerable-package scan) |
| 2 | `.github/workflows/scheduled.yml` | **Daily** dependency scan of `main` and **weekly** CodeQL, because new vulnerabilities are published even when our code doesn't change |
| 3 | `scripts/ci/check-test-results.py` | The **gate**: reads the TRX files and fails if **failed > 0 or skipped > 0**. Reads the Cobertura coverage and fails if **Domain or Application line coverage < 80%**. Writes a summary table for the run. Layers with no code yet are reported as "n/a", not as failures. |
| 4 | Test-results report on the PR | Check run and comment: total, passed, failed and skipped per suite, plus the change against `main` (via the publish-unit-test-result action) |
| 5 | Coverage report | ReportGenerator summary in the run, uploaded as an artifact |
| 6 | `.github/dependabot.yml` | Weekly updates for GitHub Actions pins and NuGet packages |
| 7 | **Least-privilege permissions** | `contents: read` by default. Write access is granted only where needed: `checks`/`pull-requests` for the report, `security-events` for CodeQL. |
| 8 | Branch protection (**ruleset** on `main`) | Configured by the engineer in GitHub following the PR's steps: PR required (0 approvals), required checks = the 4 CI jobs, branch up to date, no force-push or deletion, **no bypass**. Rulesets can be read through the public API, so the configuration can be verified. |
| 9 | README badge, `CLAUDE.md`, `CHANGELOG.md`, log | Documentation. The log includes **prompt iterations**, because this is a high-risk task (G1). |

**Out of scope:**
- Trivy, ZAP and the daily/nightly scans of images (TSK-24),
- compose smoke and k6 in CI (TSK-12),
- deployment.

## 4. Constraints
- **Every third-party action pinned to a full commit SHA**, with a comment naming its version. No `@v4`-style tags, which can be moved (supply-chain safety).
- No secrets are needed by the workflows. The default `GITHUB_TOKEN` is used with minimal permissions.
- The gate logic lives in a **versioned script in the repository**, not in long inline shell, so it can be reviewed and reused.
- **Required check names stay stable**, because the branch-protection ruleset refers to them by name.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | All 4 jobs run on this PR and are **green** | The PR's checks |
| AC2 | The PR shows the **test metrics**: per suite, total, passed, failed and skipped | Check run and comment on the PR |
| AC3 | **The gates actually fail** (on a throwaway proof PR that is never merged): **(a)** a failing test, **(b)** a skipped test, **(c)** coverage below 80% (an uncovered class in Domain), **(d)** a formatting violation, **(e)** a fake secret, **(f)** a known-vulnerable package. Each one turns the right job **red**. | Links to the red runs, recorded in the PR and the log |
| AC4 | Branch protection is active: the ruleset on `main` requires the 4 checks and a PR, with no bypass. A PR with a failing check shows **merging blocked**. | Ruleset read through the public API, plus the proof PR's merge box |
| AC5 | All actions pinned to SHAs; permissions least-privilege; Dependabot configured | Review of the workflow files |
| AC6 | The daily and weekly scheduled workflows exist, and each passes one manual run | A run via `workflow_dispatch` |
| AC7 | Docs: README CI badge; `CLAUDE.md` (CI rules); CHANGELOG; log entry with **prompt iterations** and actual hours, recorded before merge | Review |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Workflows, gate script, Dependabot config | ✅ (`implement.md`) | Reviews every line (high risk) |
| Push, watch the CI runs through the public API, fix failures | ✅ (`debug.md`) | — |
| Proof PR for AC3: create the breaking commits | ✅ | **Opens it as a draft** on GitHub, confirms the red checks, closes it **without merging** |
| Ruleset steps | ✅ writes them | **Configures them in GitHub Settings** |
| Self-review and PR description | ✅ (`review.md`) | Completes the checklist, including the **ownership statement**; gives actual hours before merging |

**Tool:** Claude Code (Claude Opus 5.5). Prompt iterations (v1 → v2 …, and why) are recorded in the log entry.

**Order of steps** (engineer clarification at approval):
1. Push this branch and open the TSK-04 PR, so the checks run once and their **names exist** (GitHub only offers checks that have run).
2. The **engineer configures the ruleset**, requiring those checks.
3. Open the **proof PR as a draft**. Its red checks now show **"merging blocked"**, because the ruleset is active. It is then **closed without merging**.
4. Merge the green TSK-04 PR.

**The CI never closes or rejects a PR.** It marks checks red, and the ruleset makes GitHub **block the merge**.
The PR stays open so a fix can be pushed. Closing a PR is a human decision, used when a change is abandoned.

**What triggers CI:**

| Trigger | Re-runs tests? |
|---|---|
| New commit pushed to the PR branch (the normal fix loop) | ✅ automatically |
| PR opened or reopened | ✅ automatically |
| "Update branch" (merges `main` into the PR branch → new commit) | ✅ automatically, against the latest `main` |
| Merge to `main` | ✅ automatically |
| "Re-run jobs" button | ✅ same commit. For flaky infrastructure only, **never** to get a failing test to pass. |
| Manual run (`workflow_dispatch`) | ✅ on demand |
| Editing the PR title, description or checklist | ❌ |
| Scheduled runs | ❌ tests. Only the dependency scan (daily) and CodeQL (weekly). |


## 7. Risks
| Risk | Mitigation |
|---|---|
| Check names in the ruleset don't match the job names, so merges are blocked for ever | Fixed job names, listed in the ruleset steps; verified via AC4 before closing the task |
| The engineer gets locked out by "no bypass" if CI itself is broken | Rule fixes happen through PRs, and the engineer can always edit the ruleset as repository admin |
| Integration tests need Docker in CI (from TSK-06) | GitHub's `ubuntu-latest` runners include Docker |
| The vulnerable-package proof (AC3f) pulls an old package | Only on the throwaway proof branch, which is never merged; the branch is deleted afterwards |
| API rate limit (60 requests/hour without authentication) when watching runs | Poll sparingly |
