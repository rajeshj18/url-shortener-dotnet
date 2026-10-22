# ADR-0012: Engineer-led workflow and risk-based sign-off

**Status:** Proposed · **Date:** 2026-10-21 · **Related:** ADR-0001, ADR-0011

## Context
The brief asks for engineer-led execution, not autonomous orchestration: AI assists *within* tasks, and
the engineer approves every output and signs off on high-impact changes.

## Decision
- **The engineer approves the task breakdown.** Each task starts from a task spec covering intent, constraints, acceptance criteria and technical context.
- **One task per branch and PR.** AI helps with implementation, tests, debugging, docs and the PR description. CI gates run, and the AI does a self-review. **The engineer reviews and merges.** No multi-agent orchestration.
- **Every task gets a traceability-log entry:**
  - intent,
  - tool and model,
  - prompt summary,
  - whether the output was accepted, edited or rejected, and why,
  - which gates passed,
  - a link to the PR.

  Entries are append-only and record only things that actually happened.

| Risk class | Examples | Sign-off |
|---|---|---|
| High | Schema, public API contract, consistency logic, ID generation, security, CI | Explicit checklist in the PR plus a log entry |
| Medium | New use case or endpoint, dependency upgrade | Engineer reviews the PR |
| Low | Docs, tests, refactors covered by tests | Engineer reviews the PR (light checklist) |

## Consequences
GitHub doesn't let you approve your own PR. In this one-person repository, sign-off is therefore the completed checklist plus the engineer's merge. This limitation is stated openly.

## History
- 2026-10-21 · Proposed.
