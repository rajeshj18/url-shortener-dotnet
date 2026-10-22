# ADR-0008: Asynchronous analytics pipeline with Kafka

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0002, ADR-0004, ADR-0009, ADR-0013

## Context
About 1B clicks/day (~35K/s at peak, ~100 GB/day of raw events). Recording clicks must not slow redirects,
and must not add write load to the `links` primary, because the resulting replica lag would endanger the
5 s delete SLO. Stats may lag by up to 60 s (SLO).

## Decision
- **Redirect publishes** a `ClickEvent v1` to the Kafka topic `click-events.v1`.
  - **Key = `code`**, so all events for a code go to one partition and one consumer.
  - The `IClickEventPublisher.TryPublish` method uses the Kafka client's non-blocking `Produce`, with an idempotent producer and `acks=all`.
  - If the client's local queue is full, the event is **dropped and counted**. A redirect never waits on Kafka.
- **Kafka retains raw events for 30 days** (A-33). Kafka is the raw-event store; there is no raw-event table in a database.
- **AnalyticsWorker** consumes in batches, aggregates by `(code, hour)` and dimension, and in **one transaction on the `analytics` database**:
  1. adds the counts to the rollup rows (`clicks = clicks + n`),
  2. records the next offset for that partition.

  On restart, the worker resumes from the offset stored in the database. Re-delivered events are skipped, so **each event is counted once**.
- **Privacy:** events carry only the referrer host and the device class. Bots are filtered out before publishing. IP addresses are not included (A-32).
- Trace context is carried in Kafka message headers (ADR-0013).
- Locally: `apache/kafka`, single node in KRaft mode (no ZooKeeper). Production: a 3+ broker cluster, replication factor 3, spread across availability zones (MSK, Confluent Cloud, or Azure Event Hubs with the Kafka API).

## Alternatives considered
| Option | Why not chosen |
|---|---|
| Buffer in memory and batch-write from Redirect nodes | Loses buffered clicks on a crash; no raw retention or replay; write load grows with node count (~10–20K updates/s, because most codes get very few clicks) |
| Valkey Streams | Retention is limited by memory, and it differs from the production path (Kafka) |
| Synchronous insert per click | Adds latency and primary load at 35K/s |

## Consequences
- **Replay:** reset the stored offsets to recompute stats (for example after an aggregation bug).
- **Other consumers can read the same stream later**, such as abuse detection in the ambiguous scenario.
- Kafka uses about 1 GB RAM locally. Monitored metrics: consumer lag, dropped events, and stats freshness.

## History
- 2026-10-21 · Proposed · In-process `Channel<T>` rejected in AI self-review, because it can't reach a separate worker process.
- 2026-10-21 · **Accepted** · Engineer chose Kafka over in-memory batching and Valkey Streams.
