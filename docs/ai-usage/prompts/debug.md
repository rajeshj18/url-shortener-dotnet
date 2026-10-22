# Prompt: debug

**Version:** 1 · **Last changed:** 2026-10-22 · **Use for:** diagnosing a failing test, build or runtime error

## Prompt
```
Diagnose this failure in TSK-<xx>:
<error message / failing test name / log excerpt, with secrets removed>

Steps:
1. Reproduce it (the exact command) and confirm you see the same failure.
2. State 1–3 hypotheses for the root cause, with the evidence for each.
3. Verify the most likely one by reading code or running a targeted check. Don't guess-fix.
4. Propose the smallest fix of the root cause (not the symptom), with a test that fails before the fix
   and passes after.
5. Report: root cause, fix, the test added, and whether anything similar may be affected elsewhere.
Don't disable or skip tests to get a green build.
```

## Expected output
The root cause with evidence, a minimal fix, a test proving the fix, and a note for the traceability log (debugging sessions are recorded).

## Revision log
| Version | Date | Change | Why |
|---|---|---|---|
| 1 | 2026-10-22 | Initial version | TSK-01 |
