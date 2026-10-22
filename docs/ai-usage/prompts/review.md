# Prompt: review (AI self-review and PR description)

**Version:** 1 · **Last changed:** 2026-10-22 · **Use for:** AI self-review before the engineer's review; drafting the PR description

## Prompt
```
Review the changes on this branch for TSK-<xx> before the engineer reviews them.

Check:
- Each acceptance criterion in the spec: met? Where is the evidence?
- Correctness: edge cases, error handling, concurrency, cancellation, the resilience rules (LLD §6.7).
- Security: input validation, secrets, permissions, what gets logged (ADR-0011).
- Compatibility: schema (expand/contract), ClickEvent, API.
- Architecture (ADR-0009) and style (ADR-0003).
- Tests: meaningful assertions, no skips; is anything not covered?
- Docs, CHANGELOG and the traceability entry are up to date.

Then draft the PR description using .github/pull_request_template.md.
The AI disclosure must be honest: what the AI generated, and what is still for the engineer to
verify or decide. Don't tick checklist items on the engineer's behalf.
List the findings by severity before the PR description.
```

## Expected output
Findings ranked by severity, then a PR description draft with the checklist left for the engineer to complete.

## Revision log
| Version | Date | Change | Why |
|---|---|---|---|
| 1 | 2026-10-22 | Initial version | TSK-01 |
