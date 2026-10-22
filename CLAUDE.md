# CLAUDE.md: project context for AI assistants

This file gives an AI assistant (Claude Code) the context and rules for working in this repository.
It **summarizes and links**. The ADRs remain the source of truth, and if they disagree with this file,
the ADR wins and this file must be corrected.

## Project
A URL shortener service (.NET 8) plus the engineer-led, AI-assisted workflow used to build it (ADR-0001).
**The engineer leads and owns every output; the AI assists within tasks** (ADR-0012).

## Where things are
| Need | Read |
|---|---|
| What to build | `docs/01-requirements.md` (FR/NFR, traceability matrix) |
| How it fits together | `docs/03-architecture.md` (HLD) |
| Details: API, schema, algorithms, resilience | `docs/02-system-design.md` (LLD) |
| Why | `docs/adr/` (13 accepted ADRs) |
| Assumptions | `docs/assumptions.md` |
| Current plan and the task you're on | `docs/04-task-breakdown.md`, `docs/tasks/TSK-xx-*.md` |
| Prompts | `docs/ai-usage/prompts/` |
| What the AI did before | `docs/ai-usage/traceability-log.md` |

## Workflow rules (ADR-0012)
- **Only work on an approved task spec** (`docs/tasks/TSK-xx-*.md`). If something isn't in the spec, ask; don't expand the scope.
- **One task per branch:** `task/TSK-xx-<name>`. **Never commit or push to `main`.** The engineer merges PRs, using merge commits only.
- Every AI-made commit ends with: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Never author an ADR acceptance or a sign-off commit.** Those are the engineer's own commits.
- Write tests **in the same task** as the code. Run build and tests locally before pushing.
- Draft the PR description from `.github/pull_request_template.md`, including an honest AI disclosure.
- Add a `TSK-xx` entry to the traceability log. It is **append-only and records only things that actually happened**. For **high-risk** tasks, record the prompt iterations (v1 → v2 …, and why).
- Add a `CHANGELOG.md` entry for every user-visible change.

## Confidentiality and secure AI usage (ADR-0011)
- **Never read, quote or commit the assignment brief** (any `*.pdf` in the project folder), and never name the company or its classification marking.
- **No secrets anywhere:** not in code, docs, prompts, tests or logs. Local development uses `.env.example` with throwaway values; real values come from the environment or a secret store.
- Synthetic data only. Never log full long URLs, IP addresses or API keys.

## Architecture rules (ADR-0009). Architecture tests enforce them.
| Project | May reference | Must not reference |
|---|---|---|
| `UrlShortener.Domain` | Nothing (plain C#) | Any framework |
| `UrlShortener.Application` | Domain | Infrastructure, EF Core, Npgsql, Kafka |
| `UrlShortener.Infrastructure` | Application, Domain | Any service project |
| Services (`Api`, `Redirect`, `AnalyticsWorker`) | All three libraries; `Program` is the composition root | Each other; controllers use Application interfaces only |

- Each schema has one owner: the Api owns `links`, the Worker owns `analytics`. Other services use read-only database users. Redirect reads only the `redirect_lookup` view.
- **Services never run migrations at startup.** Migrations ship as EF Core bundles and run before deployment.
- Changes to the schema, the `ClickEvent` contract or the API must be **backward-compatible** (expand, then contract; optional event fields only; `/api/v1`).

## C# style (ADR-0003)
- `net8.0`, `LangVersion 11`, nullable reference types enabled, warnings treated as errors, `AnalysisMode=All`.
- **Use:**
  - controllers
  - an explicit `Program.Main`
  - **block-scoped namespaces**
  - interfaces with constructor injection
  - encapsulated domain classes with guard clauses
  - `async`/`await` with `CancellationToken`
  - `record` **only** for DTOs and messages
- **Avoid:**
  - top-level statements
  - file-scoped namespaces
  - primary constructors
  - collection expressions
  - long pattern-matching chains

## Definition of done (`docs/04-task-breakdown.md` §2)
- Unit and integration tests are written with the code.
- CI gates are green: **failed = 0, skipped = 0, coverage ≥ 80%** on Domain and Application.
- Docs, ADRs and assumptions are updated if behavior changed.
- The CHANGELOG has an entry.
- The PR checklist is complete.
- The log entry records actual hours.

## Commands
```powershell
dotnet build -warnaserror        # build (TSK-02 onwards)
dotnet test                      # unit + architecture + integration (Docker required for integration)
docker compose up -d             # local stack (TSK-03 onwards)
git switch -c task/TSK-xx-name   # start a task branch
```
