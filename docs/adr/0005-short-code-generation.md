# ADR-0005: Short codes: range lease + Feistel permutation + Base62

**Status:** Proposed · **Date:** 2026-10-21 · **Related:** ADR-0004, [capacity](../02-system-design.md#1-capacity-estimates)

## Context
**NFR-05:** a short code must be unique across all regions and for all time. A short URL contains only
the code, so a click can arrive in any region, and the code alone must identify one link.
We need ~182.5B unique codes over 5 years, generated at ~3.5K/s across many API nodes. Codes must be
7 characters, not guessable by counting up, and generated without a database round-trip for every code.

## Decision
1. **Region blocks, assigned explicitly.** A *region block* is a contiguous range of the ID space `[0, 62⁷)` owned by one **cloud deployment region** (see the LLD glossary).
   - An operator assigns blocks. There is no fixed split.
   - v1 has a single block, **B-001**: region 0 owns `[0, 440,201,825,776)`, which is about 12 years at 100M/day. The rest of the space stays unassigned.
   - Block IDs (`B-001`, `B-002`, …) are numbered across all regions, so a block number never implies a region.
   - **One Feistel key is shared by all regions.** Non-overlapping blocks plus one key together guarantee that codes are unique worldwide (NFR-05).
   - A region can be given more blocks later, and new regions get blocks from unassigned space.
   - Assignments are tracked in a version-controlled registry, and CI checks that no two blocks overlap.
2. **Range lease.** Each Api instance leases 10,000 IDs from its region's active block with one atomic `UPDATE … RETURNING`.
   - The region is set **once for the whole regional deployment** (`ShortCode:DeploymentRegionIndex`, default 0). Instances created by autoscaling inherit it and need no identity of their own.
   - An instance leases its first range **before reporting ready**, and fetches the next lease in the background when the current one is 50% used.
   - An instance **refuses to start** if its database has no active block for its region, or has blocks belonging to another region.
3. **Feistel permutation:** a keyed, 4-round Feistel network over 42 bits. Results ≥ 62⁷ are permuted again until they fall in range (*cycle-walking*). The result is a **one-to-one mapping**, so codes never collide and look random.
4. **Base62** encoding, padded to 7 characters.
5. **Custom aliases** rely on the primary key (`INSERT … ON CONFLICT DO NOTHING` → `409`).
   A generated code that happens to match an existing alias hits the same conflict and simply takes the next ID.

## Alternatives considered
| Approach | Why not chosen |
|---|---|
| Hash the URL (MD5/SHA) and truncate | Collisions, so every write needs a check-and-retry. The same URL always gets the same code. |
| Global Redis counter | A single point of failure. Redis replicates asynchronously, so after a failover the counter can go backwards and **issue duplicate IDs**. |
| ZooKeeper + Snowflake + MD5 truncated to 6 chars | **Truncating the hash brings collisions back.** 6 Base62 chars = 56.8B codes, with a 50% collision chance after ~280K URLs. The space fills in ~1.6 years, so it **can't hold 182.5B** at all. Also adds a ZooKeeper cluster and depends on clocks being correct. |
| Snowflake without truncation | 64 bits needs 11 characters |
| Random code + existence check | A database round-trip per attempt; retries increase as the space fills |
| A database sequence per code (`nextval`) | A primary round-trip for every create. Leasing gives the same guarantee with ~0.35 calls/s. |

The key difference: a hash scrambles **and compresses**, so it collides. A permutation scrambles **without compressing**, so it can't.

### Why region blocks must not overlap
A short URL contains **only the code**. It doesn't say which region created it, and a click is routed to the
**nearest** region, not the region that created the link. If two regions could issue the same ID, then
one code would map to two different links, and a visitor could be sent to the wrong (possibly malicious)
site.

Non-overlapping blocks keep the creating region recoverable from the code itself:
code → Base62 decode → Feistel inverse → ID → owning block → region.
This is what lets the multi-region release route a request, or replicate links between regions, with one
global primary key.

| Alternative for keeping regions apart | Why not chosen |
|---|---|
| Each region counts from 0 (overlapping IDs) | Duplicate codes across regions; violates NFR-05 |
| Region prefix character in the code (`e` + 6 chars) | 62⁶ = 56.8B per region, which is below the 182.5B needed for 5 years, so codes would need 8 characters. It also reveals the region. |
| Region in the path (`/eu/{code}`) | Longer URLs, and the region becomes part of the public API forever |
| A domain per region (`eu.sho.rt`) | Several domains and certificates to run; links look inconsistent |

## Consequences
- IDs left in a stopped or crashed instance's lease are never issued. That's harmless: even with heavy autoscaling churn the loss is about 1%, and codes are never reused.
- An alert fires when a region's active block is **80% used**. The response is to assign another block (LLD §6.4 runbook). At 100M/day that is about 9.6 years away in v1.
- The Feistel key is a secret that **must never change**, because changing it maps IDs to different codes.
- Tests:
  - the permutation is one-to-one (property-based),
  - encode/decode round-trips,
  - concurrent leases never overlap.

## History
- 2026-10-21 · Proposed · ZooKeeper + Snowflake + MD5 comparison added at the engineer's request; lease implemented as a single atomic SQL update.
- 2026-10-22 · Proposed · Fixed split into 8 regions replaced by explicitly assigned blocks; region set per deployment (works with autoscaling); startup check; 80% alert; setting renamed to `DeploymentRegionIndex`.
- 2026-10-22 · Proposed · Linked to NFR-05; block IDs renamed `B-001`…; one Feistel key shared by all regions; added "why blocks must not overlap" with the rejected region-in-URL alternatives.