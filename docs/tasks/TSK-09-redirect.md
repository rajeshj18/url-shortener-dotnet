# TSK-09: Redirect (`GET` / `HEAD /{code}`)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | **High** (the hot path: 35K/s at peak, user input in the URL, the `Location` header) |
| **Depends on** | TSK-06 (can run in parallel with TSK-08) |
| **Estimate** | 2 h |
| **Branch** | `task/TSK-09-redirect` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-09 to TSK-13) |

## 1. Intent
Resolve a short code to its long URL with a `302`, fast and read-mostly, with the caching headers and click
semantics of ADR-0007. Click events are built and handed to a publisher, which is a no-op until Kafka arrives
in stage 2, so the redirect path is already in its final shape.

## 2. Technical context
- **LLD §6.2** (resolve), §3 (contract), §6.7 (Redirect timeouts and the capped primary fallback), §8 (`Redirect:PrimaryFallbackPerSecond` = 500).
- **ADR-0007** (302 / 404 / 410 and their `Cache-Control`; `HEAD` and bots send no click event), **ADR-0002** (replicas for reads, primary fallback for read-your-writes, NFR-07), **ADR-0008** + LLD §6.5 (`ClickEvent v1` shape), **ADR-0013** (never log full long URLs).
- **Existing code:** `IRedirectLookup.FindAsync` (replicas) and `FindOnPrimaryAsync` (TSK-06), `ShortCode.TryParse` (cheap shape check, TSK-05), `RedirectTarget` with `LinkState`.
- Assumptions A-25 (codes never reused, so `410` is permanent), A-34 (bots excluded by User-Agent).

## 3. Scope
| # | Component | Purpose |
|---|---|---|
| 1 | Application: `IResolveShortUrlHandler` + `ResolveShortUrlHandler` | Shape check → replica lookup → primary fallback if the budget allows → outcome `Redirect(url)` / `NotFound` / `Gone` |
| 2 | Application: `IPrimaryFallbackBudget` + Infrastructure `TokenBucketFallbackBudget` | Caps primary lookups at `Redirect:PrimaryFallbackPerSecond` per instance (`System.Threading.RateLimiting`), so scans of random codes can't overload the primary |
| 3 | Application: `ClickEvent` (record, v1), `IClickEventPublisher.TryPublish` (sync, never blocks) | The contract of LLD §6.5 |
| 4 | Infrastructure: `NoOpClickEventPublisher` | Discards the event and counts it (`click_events_discarded_total`) so stage 1 shows the path works |
| 5 | Application: `ClickContext` builder: `IUserAgentClassifier` (bot / mobile / tablet / desktop), referrer **host only** | No IP address and no full User-Agent leave the request (NFR-10) |
| 6 | Redirect: `RedirectController` | `GET` and `HEAD /{code}`; status and headers from ADR-0007; event only for `GET` from non-bots. Route must not shadow `/health/*`. |
| 7 | Docs | LLD §6.2 notes, README "Try it", CHANGELOG, log |

**Out of scope:** Kafka producer (TSK-23); circuit breaker on the primary fallback and the `503` when it is open, request timeout (TSK-11); rate limits per client (TSK-19); expiry (TSK-14: `Expired` → `410` arrives then); a cache (TSK-30, only if TSK-12 fails).

## 4. Constraints
- A malformed code returns `404` **without any database call**.
- `Location` is the stored long URL exactly as created (already validated: no whitespace or control characters, TSK-05).
- The Redirect role can only read `redirect_lookup` (TSK-06); nothing in this task needs more.
- Never log the long URL or the User-Agent; the code may be logged.
- No allocation-heavy work on the hot path beyond what's needed (the benchmark of TSK-12 measures it).

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Active link → `302`, `Location` = the long URL, `Cache-Control: no-store` | API test (Testcontainers) |
| AC2 | Unknown code → `404` + `no-store`; malformed code → `404` with **no lookup call** | API test + unit test with a fake lookup |
| AC3 | Deleted link → `410` + `Cache-Control: public, max-age=86400` | API test |
| AC4 | `HEAD` → same status and headers as `GET`, no body, **no click event** | API test with a recording publisher |
| AC5 | `GET` from a browser → exactly one `ClickEvent` (code, UUIDv7 `eventId`, `occurredAt`, referrer host, device); from a known bot → none | Unit + API tests |
| AC6 | **Create, then redirect at once** (Api then Redirect on the same database) → `302` | API test across both services |
| AC7 | Replica miss → primary lookup while the budget allows; once spent → `404` without a primary call | Unit test with a fake clock |
| AC8 | `/health/live` and `/health/ready` still answer (not captured as codes) | Existing tests stay green |
| AC9 | Gates green; coverage ≥ 80%; docs; log with prompt iterations and hours | CI and review |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Handler, budget, classifier, publisher, controller | ✅ (`implement.md`) | Reviews every line (high risk), especially headers and the fallback cap |
| Tests | ✅ (`write-tests.md`) | Checks AC4/AC5 really prove "no event for HEAD and bots" |
| Self-review, PR | ✅ (`review.md`) | Ownership statement; hours |

## 7. Risks
| Risk | Mitigation |
|---|---|
| A cached `302` or `404` breaks analytics or read-your-writes | Header assertions on every status (AC1–AC3) |
| Code scans hammer the primary | Token bucket, unit-tested (AC7) |
| Personal data in events or logs | Host-only referrer, device class only; review + tests |

## 8. Decisions for the engineer
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | Build `ClickEvent`s in stage 1 although they are discarded? | (a) yes, with a no-op publisher; (b) no events until TSK-23 | **(a).** The plan says the interfaces exist from stage 1; TSK-23 then only adds a Kafka publisher (a new implementation, not a change). |
| D2 | Replica miss when the fallback budget is spent | (a) `404` + `no-store`; (b) `503` + `Retry-After` | **(a).** It only happens under scan-like load; `no-store` means a genuine new link resolves on the next try. (`503` is for an open breaker, TSK-11.) |
| D3 | Bot detection | (a) a small built-in list of User-Agent tokens (`bot`, `crawler`, `spider`, `preview`, known link-preview agents); (b) a third-party library | **(a)** (A-34: "better detection" is a later improvement). |
