# Prompt: implement

**Version:** 1 · **Last changed:** 2026-10-22 · **Use for:** implementing a task from its approved spec

## Prompt
```
Implement TSK-<xx> as specified in docs/tasks/TSK-<xx>-<name>.md.

Context: follow CLAUDE.md and the ADRs and LLD sections listed in the spec's "Technical context".
Scope: only what the spec's "In scope" table lists. If something seems missing or wrong in the spec,
stop and ask instead of expanding the scope.

Constraints:
- Architecture rules (ADR-0009) and C# style (ADR-0003) as in CLAUDE.md.
- Write the unit and integration tests in the same change (definition of done).
- No secrets and no real data. Backward-compatible schema, event and API changes.

Work in small, reviewable steps. After each step: build, run tests, then commit on the task branch.
End by listing each acceptance criterion with how it is now met, plus anything left open.
```

## Expected output
Code and tests on the task branch, in small commits, plus a list mapping each acceptance criterion to its evidence.

## Revision log
| Version | Date | Change | Why |
|---|---|---|---|
| 1 | 2026-10-22 | Initial version | TSK-01 |
