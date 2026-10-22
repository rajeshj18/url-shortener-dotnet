# ADR-0002: Consistency and availability model (single region)

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0004, ADR-0006, ADR-0010, [SLOs](../02-system-design.md#2-service-levels-slo--sla), Assumptions A-17, A-18

## Context
v1 runs in **one region across multiple availability zones** (multi-region is a later release, A-17).
The system of record is a single PostgreSQL primary with a synchronous standby and asynchronous read
replicas (ADR-0004). Reads outnumber writes 10:1, and redirects have the strictest availability target
(99.95% SLO).

## Decision
**Reads favor availability. Writes favor consistency.**

| Concern | Behavior |
|---|---|
| **Redirect reads** | Served by **asynchronous read replicas**, so they keep working through primary failures. Replicas may be slightly stale (lag is typically < 1 s). |
| **Read-your-writes** | If a well-formed code isn't found on a replica, retry **once on the primary** before returning 404. The fallback is capped by a token bucket, so scans for non-existent codes can't overload the primary. |
| **Writes** (create, delete) | Go to the **primary** with `synchronous_commit = on` to a standby in another availability zone, so no committed write is lost. |
| **Primary failure** | The standby is promoted automatically within **30–120 s**. During that window writes return `503` with `Retry-After`, and redirects are unaffected. |
| **Uniqueness** (codes, aliases, idempotency keys) | Primary-key and unique constraints on the primary. Strongly consistent. |
| **Delete / expiry** | Soft delete (`deleted_at`) on the primary; `expires_at` is checked on every read. A replica that lags more than 2 s is removed from the read pool. Target: **99% of deletes take effect within 5 s (SLO), all within 60 s (SLA)**. |
| **Analytics** | Asynchronous and eventually consistent. Stats may lag by up to 60 s (SLO, ADR-0008). |

## Alternatives considered
| Option | Why not chosen |
|---|---|
| **Consistency-first alternate design:** all reads from the primary, or from synchronous replicas with `remote_apply` | Deletes take effect instantly. But the primary must serve ~35K reads/s, writes are slower, and redirects stop working whenever the primary does. **Kept as the alternate design.** Switching is a change to connection routing and configuration only, not to code. |
| **Multi-region active-active** (Cassandra, availability-first with lightweight transactions for aliases) | Best write availability, but much more complex. **Deferred to a later release** (ADR-0004). |

## Consequences
- **Replica lag is a first-class metric.** Lagging replicas are taken out of the read pool, and alerts fire well before the 5 s SLO is at risk.
- Failover is tested in a drill (stop the primary container and observe writes returning 503, then recovering).
- A full region outage is a disaster-recovery event, not covered by failover (RTO ≤ 4 h, RPO ≤ 15 min).

## History
- 2026-10-21 · Accepted · Engineer: single region; reads favor availability, writes favor consistency; consistency-first reads documented as the alternate design.
