# ADR-0009: Three hosts, Clean Architecture layering

**Status:** Proposed · **Date:** 2026-10-21 · **Related:** ADR-0003, ADR-0004, ADR-0008

## Context
Reads are 10× writes and have tighter latency targets. We want independent scaling without separate codebases.

## Decision
| Host | Responsibility | Data access |
|---|---|---|
| `UrlShortener.Redirect` | `GET /{code}` only | `links` read replicas (falls back to the primary on a miss); produces to Kafka |
| `UrlShortener.Api` | Create, get, delete, stats; API-key auth | `links` primary; reads `analytics` for stats |
| `UrlShortener.AnalyticsWorker` | Consumes click events and writes rollups | Consumes from Kafka; writes `analytics` |

- The hosts share the layers **Domain ← Application ← Infrastructure**.
- Domain has no framework dependencies.
- Layering is enforced by architecture tests (NetArchTest) in CI.

## Alternatives considered
| Option | Why not chosen |
|---|---|
| Single host | Reads can't be scaled separately from writes |
| Separate repositories / microservices | Too much overhead; domain rules would be duplicated |

## Consequences
The hosts share schemas, so schema changes must be backward-compatible (expand, then contract).

## History
- 2026-10-21 · Proposed.
