# Prompt: write tests

**Version:** 1 · **Last changed:** 2026-10-22 · **Use for:** generating unit and integration tests

## Prompt
```
Write tests for <class / endpoint / behaviour> in TSK-<xx>.

Cover:
- every acceptance criterion in docs/tasks/TSK-<xx>-<name>.md that this code affects,
- the rules in <LLD / ADR section>, including edge cases and failure paths
  (invalid input, conflicts, timeouts, concurrency where relevant),
- unit tests for logic (xUnit + FluentAssertions, no I/O),
- integration tests against real PostgreSQL/Kafka with Testcontainers where the behaviour depends on them.

Rules:
- One behaviour per test. Names describe the behaviour: Method_condition_expectedResult.
- No [Skip], no sleeps for timing. Use deterministic clocks and IDs.
- Synthetic data only.
List which acceptance criteria each test proves.
```

## Expected output
Test classes, plus a table mapping each test to the acceptance criteria it proves.

## Revision log
| Version | Date | Change | Why |
|---|---|---|---|
| 1 | 2026-10-22 | Initial version | TSK-01 |
