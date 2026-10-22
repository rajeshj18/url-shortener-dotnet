# Prompt: refactor

**Version:** 1 · **Last changed:** 2026-10-22 · **Use for:** changing structure without changing behavior

## Prompt
```
Refactor <component> for TSK-<xx>: <goal, e.g. "extract URL validation into IUrlValidationRule classes">.

Rules:
1. First add characterization tests that pin down the current behaviour (if not already covered),
   and commit them on their own.
2. Refactor in small steps. All tests must pass after every step, and no test may be changed
   to make it pass.
3. No behaviour change: same API responses, same database writes, same events.
4. Keep the ADR-0009 dependency rules and the ADR-0003 style.
Report: what moved where, why, and the evidence that behaviour is unchanged (the same tests before and after).
```

## Expected output
A characterization-test commit, then refactoring commits with all tests passing at each step, plus a summary.

## Revision log
| Version | Date | Change | Why |
|---|---|---|---|
| 1 | 2026-10-22 | Initial version | TSK-01 |
