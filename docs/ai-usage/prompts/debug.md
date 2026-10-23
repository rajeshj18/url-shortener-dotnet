# Prompt: debug

**Version:** 2 · **Last changed:** 2026-10-22 · **Use for:** diagnosing a failing test, build or runtime error

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
Check the REAL exit code of the command under test, never the exit code of a pipe
(`cmd | tail` reports tail's status). Run it without the pipe, or read ${PIPESTATUS[0]}.
```

## Expected output
The root cause with evidence, a minimal fix, a test proving the fix, and a note for the traceability log (debugging sessions are recorded).

## Revision log
| Version | Date | Change | Why |
|---|---|---|---|
| 1 | 2026-10-22 | Initial version | TSK-01 |
| 2 | 2026-10-22 | Added: check the real exit code, not the pipe's | False-positive results in TSK-03 (smoke script piped to `tail`) and TSK-04 (`dotnet format` piped to `tail`) |
