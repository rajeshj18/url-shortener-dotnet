# ADR-0006: Caching: measure first, add only when load tests justify it

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0002, ADR-0004, ADR-0007

## Context
- Redirects are served by PostgreSQL read replicas (ADR-0002). A primary-key lookup on a hot row is served from memory in about a millisecond.
- **Every replica holds all the data**, so a viral link's load spreads across all replicas instead of concentrating on a few nodes.
- The open question is **cost and latency at peak** (how many replicas we need), not correctness.
- A cache adds a component, new failure modes, and staleness on delete.

## Decision
1. **v1 has no cache.**
2. **Pass/fail criteria, fixed before the load test:**
   - redirect p99 ≤ 50 ms at target load per replica,
   - replica CPU < 70% at peak with the planned replica count,
   - in a hot-key test (30% of traffic on one code), p99 still ≤ 50 ms,
   - in a scan test (random non-existent codes), the primary fallback stays within its token-bucket limit.
3. **If any criterion fails, add a cache:**
   - a decorator (`CachedUrlRepository` wrapping `IUrlRepository`) behind a feature flag,
   - Valkey (BSD licence), **TTL ≤ 30 s**,
   - **purge on delete** (synchronous, best effort), so the 5 s delete SLO normally holds and the TTL limits staleness to within the 60 s SLA if a purge fails,
   - short-lived caching of "not found" results,
   - single-flight on misses (one database read per key at a time),
   - read the database directly if Valkey is down,
   - **no per-node in-process cache**, because a delete can't purge it on every node.
4. Re-run the same test with the cache on and record the before/after numbers here.

## Alternatives considered
| Option | Why not chosen |
|---|---|
| Cache from v1 | Adds complexity before there's evidence it's needed |
| CDN / edge caching of redirects | Clicks would skip our servers, breaking analytics (ADR-0007) |
| Redis | License changed (RSAL/SSPL/AGPL). Valkey works with the same client. |
| More read replicas instead of a cache | **The default first lever.** A cache is chosen only if it's cheaper, or the only way to meet p99. |

## Consequences
The cache, if needed, becomes a brownfield enhancement with measured before/after results.

## History
- 2026-10-21 · Proposed · First drafted as a Valkey cache from v1 with synchronous invalidation.
- 2026-10-21 · Revised to "measure first" after the engineer asked whether a cache is needed at all.
- 2026-10-21 · **Accepted** · Engineer chose measure first.
- 2026-10-21 · Context updated for PostgreSQL read replicas (hot keys spread across all replicas); "more replicas" added as the first lever.
- 2026-10-23 · Naming note (TSK-06 R2, no change to the decision) · The repository was split by interface segregation. If the cache is added, it decorates **`IRedirectLookup`** (`CachedRedirectLookup`) instead of `IUrlRepository`, which no longer exists. See LLD §5.
