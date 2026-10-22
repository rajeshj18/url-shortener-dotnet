# ADR-0011: Confidentiality and secure AI usage

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0012, Assumption A-03

## Context
- The assignment brief carries an internal classification marking, and the repository will be public.
- **The brief was given to an AI tool (Claude Code) for requirements analysis.** We state this openly rather than claiming otherwise.

## Decision
1. The brief is never committed (`.gitignore` excludes `*.pdf`). Requirements are restated in our own words, with no company name or classification marking.
2. No secrets in prompts or in the repository:
   - local development uses `.env.example` with throwaway credentials,
   - `.env` is git-ignored,
   - keys such as the Feistel key and API keys come from configuration or a secret store.
3. Only synthetic data in prompts, fixtures and logs. IP addresses are not collected in click events (ADR-0008) and are never logged (ADR-0013).
4. **CI enforcement:**
   - gitleaks (secret scanning),
   - NuGet Audit plus `dotnet list package --vulnerable --include-transitive`,
   - NetAnalyzers (`AnalysisMode=All`, warnings as errors),
   - CodeQL.
5. AI output goes through the same gates and human review as any other code (ADR-0012). The compliance statement names only tools that are actually configured.

## Consequences
A secret-scan finding blocks CI. Before every push, we check that no PDF or `.env` file is staged.

## History
- 2026-10-21 · Accepted · Engineer.
