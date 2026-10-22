# Prompt library

Reusable, **versioned** prompts for AI-assisted work in this repository (ADR-0012). Each prompt file has
a version header and a **revision log** that records every change and why. That is the evidence of
"disciplined prompting with iterative refinement" (requirements §6.3, item 4c).

| Prompt | Use it for |
|---|---|
| [implement.md](implement.md) | Implementing a task from its approved spec |
| [write-tests.md](write-tests.md) | Generating unit and integration tests |
| [debug.md](debug.md) | Diagnosing a failing test, build or runtime error |
| [refactor.md](refactor.md) | Changing structure without changing behavior |
| [document.md](document.md) | Writing or updating docs, ADRs, the CHANGELOG |
| [review.md](review.md) | AI self-review before the engineer's review; drafting the PR description |

## How to use a prompt
1. Copy the prompt and fill in the `<placeholders>`, mainly the task ID and the files involved.
2. The AI assistant also reads `CLAUDE.md`, so the project rules don't need repeating.
3. **If a prompt needed changing to get a good result**, update the file:
   - increase the version,
   - add a revision-log row (what changed, why, which task showed the need).

   For **high-risk tasks**, also record the iterations in that task's traceability-log entry.

## Rules
- No secrets, real data or brief content in prompts (ADR-0011).
- Prompts state the **intent, constraints and acceptance criteria**. They don't dictate the solution where judgment is needed.
