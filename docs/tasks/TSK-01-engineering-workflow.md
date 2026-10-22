# TSK-01: Engineering workflow

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Publish, workflow and platform |
| **Risk class** | Medium (ADR-0012 §4) |
| **Depends on** | TSK-00 (repository published) |
| **Estimate** | 2 h (`04-task-breakdown.md`) |
| **Branch** | `task/TSK-01-engineering-workflow` |
| **Status** | Approved by the engineer on 2026-10-22 |

> This is the first task spec. The task-spec template is one of this task's deliverables, so the spec is
> written in the planned format, and the template is extracted from it.

## 1. Intent
Make the engineer-led, AI-assisted workflow (ADR-0001, ADR-0012) **concrete and repeatable** before any
code is written. Every later task then starts from a spec, gives the AI the same project context, uses
versioned prompts, and ends with a PR checklist that provides evidence of review.

## 2. Technical context
- Decisions: ADR-0003 (C# style), ADR-0009 (layering rules), ADR-0011 (confidentiality, secure AI usage), ADR-0012 (workflow, risk classes, checklist, gates, who commits what).
- Task breakdown: the definition of done (`04` §2), including a CHANGELOG entry per change (G4) and prompt iterations for high-risk tasks (G1).
- Traceability matrix gaps G1 and G4 (requirements §6.8) are closed by this task.
- No `gh` CLI, so PRs are opened on github.com.

## 3. Scope
**In scope:**

| # | File | Purpose |
|---|---|---|
| 1 | `CLAUDE.md` | Project context for the AI: purpose, document map, architecture and style rules, workflow rules (branch per task, co-author line, never push `main`, never read or commit the brief, no secrets), definition of done, common commands |
| 2 | `docs/templates/task-spec.md` | The task-spec template: the same structure as this file |
| 3 | `docs/ai-usage/prompts/` | **Prompt library with version history.** A `README.md`, plus `implement.md`, `write-tests.md`, `debug.md`, `refactor.md`, `document.md` and `review.md`. Each file has a version header and a revision log (v1 → v2 …, and why it changed). |
| 4 | `.github/pull_request_template.md` | The PR template with the full ADR-0012 checklist, including the AI disclosure and the high-risk ownership statement |
| 5 | `.github/CODEOWNERS` | `* @rajeshj18`, so the engineer owns every path |
| 6 | `.gitattributes` | Consistent line endings: LF for source and docs, CRLF for `*.ps1`/`*.cmd`, binary files marked as binary |
| 7 | `LICENSE` | MIT, © 2026 Rajesh Jagadeesan |
| 8 | `CHANGELOG.md` | "Keep a Changelog" format, with an `Unreleased` section and an entry for this task |
| 9 | `docs/ai-usage/traceability-log.md` | Entries **TSK-00** (publish, which had no PR) and **TSK-01**, with actual hours |
| 10 | `docs/03-architecture.md` §8 | The workflow-component table points to the real files |

**Out of scope:**
- the CI pipeline and branch protection (TSK-04),
- the solution and code (TSK-02),
- the merge-commit-only repository setting (the engineer does this in GitHub settings).

## 4. Constraints
- Only the C# style rules in ADR-0003 and the layering rules in ADR-0009 go into `CLAUDE.md`. It summarizes and links; it doesn't redefine anything.
- `CLAUDE.md` must not quote or reference the assignment brief's content, company or classification (ADR-0011).
- The prompts must be generic and reusable, with no secrets or real data.
- Renormalizing with `.gitattributes` must not change file content beyond line endings.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | All 10 deliverables in §3 exist | PR file list |
| AC2 | The PR template contains **every** ADR-0012 checklist item | Engineer compares it with ADR-0012 §4 |
| AC3 | `CLAUDE.md` covers the workflow rules, the ADR-0009 dependency rules, the ADR-0003 style rules, the definition of done, and links to the docs | Engineer review |
| AC4 | Every prompt file has a version header and a revision log | Review |
| AC5 | `git add --renormalize .` produces no content changes other than line endings | Diff check, recorded in the PR |
| AC6 | No secrets, no brief content, no Gmail address in the diff | Manual scan (gitleaks arrives with TSK-04) |
| AC7 | `CHANGELOG.md` has an `Unreleased` entry for TSK-01 | Review |
| AC8 | This PR itself uses the new PR template, and is merged by the engineer **with a merge commit** | PR on GitHub |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Draft all files | ✅ | Reviews |
| Extract the template from this spec | ✅ | Reviews |
| Prompt library v1 | ✅ | Reviews; asks for changes, which are recorded as v2 in the revision log |
| PR description using the new template | ✅ drafts | Completes the checklist and merges |
| Log entries | ✅ drafts | Confirms the actual hours |

**Tool:** Claude Code (Claude Opus 5.5). Prompt iterations aren't required for a Medium-risk task, but any revision to the prompt library is recorded in its revision log.

## 7. Risks
| Risk | Mitigation |
|---|---|
| `CLAUDE.md` drifts from the ADRs | It links to the ADRs instead of restating them; the PR checklist item "docs/ADRs updated" |
| Line-ending renormalization touches many files | Check with AC5 before committing |
