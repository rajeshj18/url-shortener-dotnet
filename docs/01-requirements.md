# Requirements

> Status: draft for engineer review. Requirements are restated in our own words (ADR-0011). Where the
> brief was silent or ambiguous, we recorded an assumption in [`assumptions.md`](assumptions.md).
> Targets are detailed in the [SLO/SLA section](02-system-design.md#2-service-levels-slo--sla).

## 1. Problem statement

Build a URL-shortening service that turns long URLs into short codes that can't be enumerated in order, and redirects visitors
to the original URL. It must also give link owners click analytics.

The service must handle **100M new links per day and 10× as many redirects**. Redirects must be fast and
highly available. No acknowledged write may be lost. A short code must always identify exactly one link.

The service is to be built with an engineer-led, AI-assisted workflow that is itself part of the deliverable (ADR-0001).

## 2. Functional requirements

| ID | Requirement | Notes |
|---|---|---|
| FR-01 | **Shorten:** create a short link for a valid long URL and return the code and short URL | A-20, A-21 |
| FR-02 | **Custom alias:** optionally use a caller-chosen alias. Return `409` if it is taken, and `400` if it has the shape reserved for generated codes (exactly 7 Base62 characters). | A-21, ADR-0005 |
| FR-03 | **Redirect:** `GET /{code}` returns `302` to the long URL; `404` if unknown; `410` if deleted or expired | ADR-0007 |
| FR-04 | **Expiry:** optionally set an expiry time. Expired links return `410`. | A-22 |
| FR-05 | **Delete:** the owner can delete a link (soft delete; returns `410` afterwards) | A-25 |
| FR-06 | **Metadata:** the owner can retrieve a link's details | A-24 |
| FR-07 | **Analytics:** the owner can see total clicks, an hourly/daily time series, top referrer hosts and device class | A-31, ADR-0008 |
| FR-08 | **Safe retries:** a create sent again with the same `Idempotency-Key` **within 24 h** returns the original result instead of creating a duplicate; reusing the key for a different request returns `422` | ADR-0010, LLD §6.1 |
| FR-09 | **Authentication and ownership:** management endpoints need an API key; only the owner can view stats, read metadata or delete | A-24 |
| FR-10 | **URL validation:** only absolute `http`/`https` URLs up to 2,048 characters; no embedded credentials; no links back to our own domain | A-20, LLD §7 |

## 3. Non-functional requirements

| ID | Category | Requirement | Target / measure | Verified by |
|---|---|---|---|---|
| NFR-01 | Scalability | Handle the projected load | 100M creates/day (~3.5K/s peak), 1B redirects/day (~35K/s peak); scale out without redesign | Load test plus extrapolation; capacity estimates |
| NFR-02 | Performance | Low latency | Redirect p99 ≤ 50 ms; create p99 ≤ 200 ms | k6 load test; latency dashboards |
| NFR-03 | Availability | Redirects stay up through node and zone failures | Redirect 99.95% SLO; create/delete 99.9% SLO | Failover drill; SLO dashboards |
| NFR-04 | Durability | No acknowledged write is lost on a node or zone failure | Recovery point = 0 (synchronous standby); region disaster: recovery time ≤ 4 h, data loss ≤ 15 min | Failover drill; backup and restore test |
| NFR-05 | **Data integrity** | **A short code is unique across all regions and for all time.** One code always identifies exactly one link, in every region, and is **never reused** (not even after deletion or expiry). | Zero duplicate codes | Non-overlapping region blocks checked by CI on the block registry; primary-key constraint; property tests that the permutation is one-to-one; startup check on region blocks (ADR-0005) |
| NFR-06 | Consistency | Deletes and expiries take effect quickly everywhere | 99% within 5 s (SLO); all within 60 s (SLA) | Delete propagation metric; integration test |
| NFR-07 | Consistency | Read-your-writes for new links | A newly created link redirects immediately | Integration test (create, then redirect at once) |
| NFR-08 | Analytics freshness and accuracy | Stats are up to date and each click is counted once | Stats lag p99 ≤ 60 s; ≥ 99.9% of clicks captured; each event counted exactly once | Worker integration test with replayed events; freshness metric |
| NFR-09 | Security | Codes can't be **enumerated in order**, and **random guessing** is limited. A random 7-character guess hits a real link with probability equal to the share of the code space in use (~1% after 1 year, ~5% after 5 years), so short codes aren't secrets. | Keyed permutation; per-client rate limits and abuse detection on redirect misses; validation rules | Unit tests; ambiguous scenario |
| NFR-10 | Privacy | Collect as little personal data as possible | No IP addresses or full User-Agents stored; no personal data in logs | Code review; log checks |
| NFR-11 | Observability | Every request can be traced from start to finish; SLOs are measured | Traces across HTTP and Kafka; SLO dashboards and burn-rate alerts | ADR-0013; dashboards in the repo |
| NFR-12 | Maintainability | Modular, testable code | Clean Architecture enforced by architecture tests; analyzers with warnings as errors | CI quality gates |
| NFR-13 | Portability / local run | Runs end to end locally on open-source images | One `docker compose up`, ~3 GB RAM | Clean-clone run before submission |
| NFR-14 | Secure AI usage | No secrets or confidential material in the repo; AI output goes through the same gates as any other code | Secret scanning, dependency audit, CodeQL | ADR-0011; CI |

## 4. Out of scope for v1

- Multi-region deployment (planned for a later release; NFR-05 already applies across regions)
- End-user accounts or UI
- Editing a link's target
- QR codes, custom domains, geo/device routing, link previews, bulk import
- Unique-visitor counts

See A-17, A-26, A-27 and A-32.

## 5. Ambiguities

All ambiguities and how we resolved them are recorded in the [assumptions register](assumptions.md).
The most significant one, what "system" means in the brief, is decided in ADR-0001.

## 6. Traceability to the brief

*To follow:* a matrix linking each section of the brief to the artifact that addresses it.
