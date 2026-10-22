# Prompt: document

**Version:** 1 · **Last changed:** 2026-10-22 · **Use for:** writing or updating docs, ADRs, the CHANGELOG

## Prompt
```
Update the documentation for TSK-<xx>: <what changed>.

- Find every document affected (requirements, HLD, LLD, ADRs, assumptions, README, scenario docs,
  OpenAPI descriptions, CHANGELOG) and list them before editing.
- Accepted ADRs: never edit the decision text. Add a History line, or propose a superseding ADR.
- Keep documents consistent with each other. Search for stale references (names, numbers, endpoints).
- Plain, precise language. Describe the brief in our own words only (ADR-0011).
- CHANGELOG: add an entry under "Unreleased" (Added / Changed / Fixed / Removed).
Report the files changed and any inconsistencies you found but didn't fix.
```

## Expected output
Consistent doc updates, a CHANGELOG entry, and a list of the files touched.

## Revision log
| Version | Date | Change | Why |
|---|---|---|---|
| 1 | 2026-10-22 | Initial version | TSK-01 |
