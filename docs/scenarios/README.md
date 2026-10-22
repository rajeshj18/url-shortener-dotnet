# Scenarios

The brief asks for three scenarios: greenfield, brownfield and ambiguous. Each one shows decomposition,
execution and validation. Brownfield scope covers enhancements, refactors, bug fixes, and test and
documentation improvements. No starter codebase was provided (A-02), so the brownfield scenario changes
our own scenario-1 code.

Each scenario document follows the same structure: **ask → normalization → decomposition → AI-assisted execution → validation → outcome**.

| Scenario | Content |
|---|---|
| **1. Greenfield** | Shorten, redirect, custom alias, expiry and delete, persistence. No cache (ADR-0006). |
| **2. Brownfield** | (a) **Enhancement:** click analytics added to the redirect path (Kafka → worker → rollups, ADR-0008). (b) **Enhancement:** a cache added as a decorator, if the load test justifies it (ADR-0006). (c) **Bug fix:** the custom-alias race in v1's read-then-insert. Two concurrent requests for the same alias both pass the check, and the second hits a primary-key violation, returning **500 instead of 409**. A concurrency test finds it; the fix is `INSERT … ON CONFLICT DO NOTHING` mapped to 409. (d) **Refactor:** URL validation pulled out into a set of separate rules, ahead of scenario 3. (e) **Test and documentation improvement:** mutation testing (Stryker.NET), filling coverage gaps, completing the OpenAPI docs. |
| **3. Ambiguous** | *"Make it secure so people can't abuse it"* → clarifying questions → assumptions → measurable requirements: rate limits derived from the capacity estimates, URL threat checking behind an interface, blocking redirect loops, codes that can't be enumerated in order, and limits on random guessing (a guess hits a real link ~1% of the time after 1 year and ~5% after 5 years). |

**Note on (c):** v1 uses a simple read-then-insert as a genuine first iteration. The scenario document
says so openly. It is not presented as a bug found in production.

Scenario 1 is merged before scenario 2 starts, so the git history shows the brownfield work happening on existing code.
