## Task
- **Task:** TSK-xx · spec: `docs/tasks/TSK-xx-<name>.md`
- **Risk class:** High / Medium / Low (ADR-0012 §4)
- **Summary:** <what changed and why>

## AI disclosure
- **Tool / model:** Claude Code (Claude Opus 5.5)
- **AI generated:** <files / parts>
- **Engineer edited or rejected:** <what, and why>
- **Prompt iterations** (required for High risk): <link to the log entry / prompt revisions>

## Checklist (completed by the engineer)
- [ ] Backward compatible: DB schema (expand/contract), `ClickEvent` (optional fields only), public API (`/api/v1`). Or N/A.
- [ ] Tests added or updated, and passing (unit / integration / architecture). Failed = 0, skipped = 0.
- [ ] All CI gates green
- [ ] Security considered: input validation, secrets, permissions, logging (ADR-0011)
- [ ] Docs, ADRs and assumptions updated if behavior or decisions changed
- [ ] `CHANGELOG.md` entry added (user-visible changes)
- [ ] Traceability-log entry `TSK-xx` added, with actual hours
- [ ] **High risk only:** I have reviewed every changed line and accept ownership.

**Merge with "Create a merge commit"** (no squash or rebase, ADR-0012).
