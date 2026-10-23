# Scenarios

The brief asks for three scenarios: greenfield, brownfield and ambiguous. Each one shows decomposition,
execution and validation. Brownfield scope covers enhancements, refactors, bug fixes, and test and
documentation improvements. No starter codebase was provided (A-02), so the brownfield scenario changes
our own scenario-1 code.

Each scenario document follows the same structure: **ask → normalization → decomposition → AI-assisted execution → validation → outcome**.

| Scenario | Content |
|---|---|
| **1. Greenfield** ([write-up](01-greenfield.md)) | Shorten, redirect, custom alias, metadata and delete, idempotent retries, persistence, resilience, load test. No cache (ADR-0006, measured). Expiry moved to scenario 2 (TSK-14), as in the task breakdown. |
| **2. Brownfield** ([write-up](02-brownfield.md)) | Built: link expiry as an expand-only enhancement (TSK-14); three real defects fixed, each reproduced by a failing test first (TSK-15, TSK-QW); test and documentation improvements (TSK-QW). **Descoped:** the validation refactor (TSK-16). The planned analytics and cache enhancements weren't built (analytics is stage 2; ADR-0006 showed no cache is needed). |
| **3. Ambiguous** | *"Make it secure so people can't abuse it"* → clarifying questions → assumptions → measurable requirements: rate limits derived from the capacity estimates, URL threat checking behind an interface, blocking redirect loops, codes that can't be enumerated in order, and limits on random guessing (a guess hits a real link ~1% of the time after 1 year and ~5% after 5 years). **Descoped** (stage 1 closed after TSK-15); no write-up. |

**Note on (c):** the defect is chosen when TSK-15 starts, from what the merged code actually contains. No
bug is planted for the exercise.

Scenario 1 is merged before scenario 2 starts, so the git history shows the brownfield work happening on existing code.
