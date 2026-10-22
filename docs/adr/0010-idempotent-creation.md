# ADR-0010: New code for every create; idempotency keys for retries

**Status:** Proposed · **Date:** 2026-10-21 · **Related:** ADR-0004, ADR-0005, Assumption A-23

## Context
Two questions to settle:
- Should the same long URL submitted twice get the same code?
- How do we stop a client's retries from creating duplicate links?

## Decision
- **Every create returns a new code** (A-23). Separate links keep analytics and expiry separate.
- **Optional `Idempotency-Key` header**, scoped per API key, kept for 24 h. In **one transaction on the primary**:
  1. `INSERT INTO idempotency_keys … ON CONFLICT DO NOTHING`
  2. insert the link
  3. store the response
- If the key already exists:
  - same request hash → return the stored response,
  - different hash → `422`.
- A concurrent duplicate request waits on the unique index until the first transaction commits, then gets the stored response.

## Alternatives considered
| Option | Why not chosen |
|---|---|
| Deduplicate by long URL | Mixes analytics; ignores differences in requested options; reveals what others have shortened. The engineer chose to keep A-23. |
| No idempotency | Retries after a timeout create duplicate links |

## Consequences
A cleanup job deletes idempotency keys older than 24 h.

## History
- 2026-10-21 · Proposed · Single-transaction design on PostgreSQL.
