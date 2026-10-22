# ADR-0001: Deliver both the URL shortener and the AI-assisted engineering workflow

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0012, Assumption A-01

## Context
The brief is titled *"Build an AI-Assisted Software Engineering System – URL Shortener"*. It asks for an
architecture overview covering "components, tools, execution approach, control flow". That reads as the
**engineering workflow**, not only the service. The brief also clearly requires a working URL shortener.

## Decision
Deliver both:
1. **Product:** a URL shortener with core APIs, analytics and reliability features.
2. **Workflow:** the system used to build it:
   - task-spec template
   - AI project context file
   - prompt library
   - CI quality gates
   - PR template with sign-off
   - traceability log

The architecture overview covers both. The workflow part shows the flow:
requirement → task spec → AI prompt → output → gates → human review → merge.

## Alternatives considered
| Option | Why not chosen |
|---|---|
| Product only | Likely misses the "system / tools / control flow" emphasis in the brief |
| Workflow only | The brief explicitly requires a runnable URL shortener |

## Consequences
The workflow artifacts are used for every task, so the evidence builds up as we work rather than being written at the end.

## History
- 2026-10-21 · Accepted · Engineer chose to cover both readings of "system" (Assumption A-01) instead of asking the interviewer.
