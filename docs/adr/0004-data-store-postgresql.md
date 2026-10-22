# ADR-0004: PostgreSQL as the v1 data store

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0002, ADR-0005, ADR-0008, ADR-0010, [capacity](../02-system-design.md#1-capacity-estimates)

## Context
v1 is single-region (A-17). The store needs to:
- handle ~3.5K writes/s and ~35K reads/s at peak,
- enforce uniqueness,
- give the Application layer a simple programming model,
- run locally from an open-source image.

Five-year storage (~91 TB) is too much for one node, but the first ~1.5–2 years (~18 TB per year) fit comfortably.

## Decision
- **PostgreSQL 16.** Locally: `postgres:16-alpine`. In production: a managed service with zone-redundant HA (Azure Database for PostgreSQL Flexible Server, Amazon RDS/Aurora), or self-managed with Patroni.
- **Topology:**
  - one primary,
  - one **synchronous standby** in another availability zone (no data loss on failover; failover takes 30–120 s),
  - **N asynchronous read replicas** serving redirects (ADR-0002).
- **Two databases:**
  - `links`: the system of record,
  - `analytics`: rollups and Kafka offsets (ADR-0008).

  In production they are separate instances, so analytics write volume can't slow the link replicas. Locally both live in one container.
- **Access:** EF Core 8 + Npgsql.
  - Writes go through the primary connection string.
  - Redirect reads use an Npgsql multi-host connection string across the replicas (`Load Balance Hosts=true`).
  - Schema changes are EF Core migrations, applied expand-then-contract.
- **Ready for sharding later:** every query is keyed by `code`, and nothing joins across codes.
- **Add Citus when any of these is reached:**
  - links data > 10 TB,
  - sustained primary CPU > 60%,
  - backup or restore time approaching the RTO.

  We expect this around year 1.5–2. Adding Citus is then mostly `create_distributed_table` on `code`.
- **Disaster recovery:** continuous WAL archiving and daily backups to object storage in another region (RTO ≤ 4 h, RPO ≤ 15 min).

## Alternatives considered
| Option | Why not chosen |
|---|---|
| Apache Cassandra | Its strength is multi-region active-active writes, which v1 doesn't need. Heavier to run, no EF Core, and conditional writes are slow. **Candidate for the multi-region release.** |
| MySQL + Vitess | Equivalent capability. PostgreSQL has better EF Core/Npgsql support and a growth path that speaks the same protocol (Citus, YugabyteDB). |
| Citus from day one | More moving parts before the data volume needs them. Same "measure first" principle as ADR-0006. |
| DynamoDB / Cosmos DB | Vendor lock-in; can't be reproduced locally |

## Consequences
- Write availability depends on the primary, plus automatic failover (ADR-0002).
- Replica lag must be monitored (ADR-0013).
- Locally there is no replica by default. The replica connection string points at the primary. An optional compose profile adds a streaming replica for the failover demo.
- **Future multi-region options** (later release):
  - links tied to a home region on PostgreSQL (each code's ID block identifies its region, ADR-0005),
  - Cassandra,
  - distributed SQL (YugabyteDB/CockroachDB).

## History
- 2026-10-21 · Accepted · Engineer chose PostgreSQL for v1 and moved multi-region to a later release; Citus to be added when the thresholds above are reached.
