# Assumptions Register

The engineer decided **not to ask the interviewer for clarification** and to proceed on the documented
assumptions below, each designed to be cheap to reverse. They are **Unconfirmed** unless marked otherwise,
and all unconfirmed ones are listed under limitations in the final summary.

**Status values:** Unconfirmed · Confirmed · Invalidated (with a link to the follow-up change)

## A. Scope and deliverable

| ID | Assumption | Rationale | Impact if wrong | Change course by | Status |
|---|---|---|---|---|---|
| A-01 | The deliverable is **both** the URL shortener and the AI-assisted engineering workflow used to build it | The title says "…Engineering System"; the architecture deliverable mentions "tools, execution approach, control flow" | Low: extra workflow artifacts would just be surplus | Reduce the workflow docs to an appendix | Unconfirmed |
| A-02 | No starter codebase is provided. The brownfield scenario changes our own scenario 1 codebase. | No repository or code accompanies the brief | Medium: brownfield work would need redoing against their code | Apply the same brownfield process to the provided code | Unconfirmed |
| A-03 | Requirements may be restated **in our own words** in a public repository. The original brief and any company references are excluded. | The brief requires a public repository but carries an internal marking | High if wrong: confidentiality exposure | Make the repository private or remove the requirements docs | Unconfirmed |
| A-04 | Reviewers will run the prototype locally with Docker. A walkthrough session may follow. | "Runnable end-to-end" and "setup instructions" are required | Low | Add a hosted demo | Unconfirmed |
| A-05 | A .NET 8 / C# stack running locally in Docker is acceptable. Cloud deployment is covered in design docs, not actually deployed. | No technology constraint stated; ".NET Core APIs" chosen by the engineer | Medium | Add IaC and a deployment pipeline | Unconfirmed |
| A-06 | The timebox is **2–3 days**. Must-have scope comes first; nice-to-haves are cut first. | Stated in the brief | Low | Rebalance scope | Confirmed (in brief) |
| A-07 | "Build from scratch" and "complete and improve it" both apply: build v1 first (greenfield), then improve it (brownfield) | The brief uses both phrasings | Low | See A-02 | Unconfirmed |

## B. Capacity, performance and service levels

| ID | Assumption | Rationale | Impact if wrong | Change course by | Status |
|---|---|---|---|---|---|
| A-10 | **100M writes per day** (not per month) | Engineer's decision; the harder case | Low: design is oversized but still valid | Simplify | Confirmed by engineer |
| A-11 | Read:write ratio **10:1** → 1B redirects/day | Engineer's requirement | Medium: replica and cache sizing | Re-estimate | Confirmed by engineer |
| A-12 | Peak traffic = **3× average** | Typical daily-traffic pattern | Medium: capacity headroom | Adjust autoscaling targets | Unconfirmed |
| A-13 | Average record size **~500 B**, including index overhead | Conservative; typical long URLs are 100–200 B | Low | Re-estimate storage | Unconfirmed |
| A-14 | Data retention: **5 years** for links | A common planning horizon | Medium: storage sizing, timing of Citus | Re-estimate; add archiving | Unconfirmed |
| A-15 | Access follows the **80/20 rule**: the hottest 20% of URLs get most of the reads | Typical skew for link traffic | Medium: cache size if a cache is added | Resize | Unconfirmed |
| A-16 | Latency targets: redirect p99 **≤ 50 ms**; create p99 **≤ 200 ms** (server-side) | "Low latency" requirement made measurable | Medium | Re-tune | Unconfirmed |
| A-17 | **Single region, multiple availability zones.** Multi-region is **out of scope for v1** and planned for a later release (ADR-0004). | Keeps v1 simple; ID blocks per region keep the door open | Medium: a full region outage means a disaster-recovery restore (RTO ≤ 4 h) | Multi-region release | Confirmed by engineer |
| A-18 | **Delete/expiry propagation:** 99% within **5 s** (SLO), all within **60 s** (SLA), across replicas and caches | Replica lag is normally < 1 s; lagging replicas are removed from the read pool (ADR-0002) | High: security and trust (phishing takedown) | Consistency-first reads (ADR-0002, alternate design) | Unconfirmed |
| A-19 | The **SLOs and SLAs** in [system design §2](02-system-design.md#2-service-levels-slo--sla) are our own targets. The brief gives none. | "Scalable, low latency, reliable" made measurable | Medium | Re-negotiate the targets | Confirmed by engineer |

## C. Functional behavior

| ID | Assumption | Rationale | Impact if wrong | Change course by | Status |
|---|---|---|---|---|---|
| A-20 | Only `http`/`https` URLs are accepted; maximum length **2,048** characters | Security (no `javascript:`/`data:` schemes); 2,048 is a widely supported practical limit | Low | Change the validation rule | Unconfirmed |
| A-21 | Generated codes are **7 characters**, Base62; custom aliases are 4–30 characters `[A-Za-z0-9_-]`, not reserved paths, and **not exactly 7 Base62 characters** (that shape is reserved for generated codes, ADR-0005) | Key-space math in the capacity estimates | Low | Configuration change | Unconfirmed |
| A-22 | Links **never expire by default**; an optional `expiresAt` can be set | Matches common link-shortener behavior | Low | Add a default TTL | Unconfirmed |
| A-23 | Submitting the same long URL again creates a **new** code; retries are deduplicated with `Idempotency-Key` | ADR-0010 | Low | Add a deduplication option | Confirmed by engineer |
| A-24 | **No end-user accounts or UI.** Clients authenticate with **API keys**; only the key that created a link can view its stats or delete it. | Keeps scope focused on APIs | Medium | Add OAuth/OIDC | Unconfirmed |
| A-25 | Short codes are **never reused**, including after deletion or expiry | Prevents a deleted link's code from pointing somewhere new | Low | — | Unconfirmed |
| A-26 | Links cannot be edited (the target URL is immutable) | Simpler caching and replica behavior; an editable target is a known phishing pattern. Also a precondition for the later multi-region release. | Medium | Add an update endpoint with cache purge | Unconfirmed |
| A-27 | Out of scope: QR codes, custom domains, geo/device routing, link previews, bulk import, **multi-region**, unique-visitor counts | Timebox; deferred deliberately | Low | Listed in "considered and deferred" | Unconfirmed |

## D. Analytics and privacy

| ID | Assumption | Rationale | Impact if wrong | Change course by | Status |
|---|---|---|---|---|---|
| A-30 | Stats may lag by up to **60 s** (SLO) / **5 min** (SLA) | Asynchronous pipeline (ADR-0008) | Medium | Shorter batch interval | Unconfirmed |
| A-31 | Metrics: total clicks, hourly/daily time series, top referrer hosts, device class | The most common shortener analytics | Low | Add dimensions | Unconfirmed |
| A-32 | **IP addresses are not collected** in click events or logs. Unique visitors are out of scope for v1. | Data minimization and GDPR | Low | Add a salted hash with HyperLogLog later | Unconfirmed |
| A-33 | Raw click events are kept for **30 days in Kafka** (topic retention); hourly rollups for the life of the link | Cost, privacy and replay | Low | Configuration change | Unconfirmed |
| A-34 | Known bots and crawlers are excluded from counts, based on User-Agent | Data quality | Low | Better bot detection | Unconfirmed |

## E. Environment and process

| ID | Assumption | Rationale | Impact if wrong | Change course by | Status |
|---|---|---|---|---|---|
| A-40 | All infrastructure images are open source and run locally: **`postgres:16-alpine`**, **`apache/kafka`** (KRaft), **`grafana/otel-lgtm`**. Valkey only if a cache is justified (ADR-0006). | Engineer's requirement for local testing | Low: ~3 GB RAM in total | — | Confirmed by engineer |
| A-41 | GitHub Actions is used for CI | The repository is hosted on GitHub | Low | Another CI provider | Unconfirmed |
| A-42 | AI tool used: **Claude Code (Claude Opus 5.5)**. Any other tool used will be named in the traceability log. | Must be recorded accurately | — | — | Confirmed |
