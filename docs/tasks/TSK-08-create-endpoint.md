# TSK-08: Create endpoint (`POST /api/v1/urls`)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | **High** (authentication, idempotency, user input reaching the database, ADR-0012 §4). Requires the ownership statement and logged prompt iterations. |
| **Depends on** | TSK-07 |
| **Estimate** | 3 h (`04-task-breakdown.md`) |
| **Branch** | `task/TSK-08-create-endpoint` |
| **Status** | Draft spec, awaiting engineer approval |

## 1. Intent
Clients can create short links: with a generated code or a custom alias, authenticated by API key, and safe to
retry with an `Idempotency-Key`. This is the first real endpoint. It also adds the OpenAPI document and the
Swagger UI, so the API can be explored locally.

## 2. Technical context
- **LLD §3** (contract: `201` + `Location`, replay header, `400`/`401`/`409`/`422`/`503`, RFC 7807 with `traceId`), **§6.1** (create flow and the end-to-end idempotency flow), **§7** (validation), **§8** (`PublicBaseUrl`).
- **ADR-0010** (idempotency: normalized request hash, one transaction, only `201` stored, 24 h window), **ADR-0005** (generated codes; aliases never have the generated shape), **ADR-0009** (Api owns `links`; controllers use Application interfaces only), **ADR-0011/0013** (API keys and full long URLs are never logged).
- **Requirements:** FR-01, FR-02, FR-08, FR-09, FR-10. Assumptions A-20, A-21, A-23, A-24.
- **Existing code:** Domain factories `ShortUrl.CreateGenerated/CreateWithAlias`, `LongUrl.Create`, `CustomAlias.Create` with stable error codes (TSK-05); `IShortUrlRepository.TryInsertAsync`, the `idempotency_keys` table (TSK-06); `IShortCodeGenerator` (TSK-07); `ProblemDetailsExceptionHandler` (TSK-02).

## 3. Scope
**In scope:**

| # | Component | Purpose |
|---|---|---|
| 1 | Application: `CreateShortUrlCommand` / `CreateShortUrlResult` (records), `ICreateShortUrlHandler` + `CreateShortUrlHandler` | The use case of LLD §6.1. The outcome is a result value (`Created`, `Replayed`, `AliasTaken`, `KeyReused`), not exceptions for expected cases. Time comes from `TimeProvider`. |
| 2 | Application: `RequestHash` | SHA-256 of `longUrl=<as sent>\ncustomAlias=<or empty>\nexpiresAt=<empty>` (ADR-0010). `expiresAt` is always empty until TSK-14, so hashes of today's requests stay valid when expiry arrives. |
| 3 | Application: ports `IIdempotencyStore` (`TryBeginAsync` → `New` / `Replay(body)` / `Mismatch`, `CompleteAsync`) and `IUnitOfWork` | As in the LLD class diagram |
| 4 | Application: `SelfLinkCheck` | Rejects long URLs whose host is our own short domain (from `PublicBaseUrl`), error code `url.self-link`. See D3. |
| 5 | Infrastructure: `EfIdempotencyStore`, `EfUnitOfWork` | `INSERT … ON CONFLICT DO NOTHING`, then read the row: older than 24 h → overwrite; same hash → replay; different → mismatch. One transaction on the primary for key + link + response. |
| 6a | Alias insert | `TryInsertAsync` directly, **no prior read**: `false` → `409`. The primary key is the guard, so this is correct under concurrency (D2). |
| 6 | Generated-code conflict (defect path, LLD §6.1 step 5) | A savepoint around the insert; on a conflict, log an error and retry with the next code (at most 3 attempts), because an aborted PostgreSQL transaction can't otherwise continue |
| 7 | Infrastructure.Web: `ApiKeyAuthenticationHandler` | ASP.NET Core authentication (middleware, not the controller). `X-Api-Key` → SHA-256 → constant-time match against configured hashes → `ownerId` claim. Missing/unknown → `401` problem. See D1. |
| 8 | Infrastructure.Web: exception handlers | New `IExceptionHandler`s (extension, not edits to the existing one): `DomainValidationException` → `400` with `errors: [{ code, message }]`; `IdSpaceExhaustedException` → `503` + `Retry-After: 5`. |
| 9 | Api: `ShortUrlsController.Create` | Maps HTTP ↔ command: body, `Idempotency-Key` format (1–128 printable ASCII → else `400`), `201` + `Location: /api/v1/urls/{code}` (served from TSK-10) + body, `Idempotent-Replayed: true`, `409`, `422` as problems |
| 10 | `expiresAt` in the request | Rejected with `400` (`expiry.unsupported`) until TSK-14, rather than silently ignored. See D4. |
| 11 | OpenAPI | Swashbuckle. Document at `/swagger/v1/swagger.json`, **Swagger UI at http://localhost:5080/swagger**, enabled by `OpenApi:EnableUi` (true in compose, false by default). The document is committed as `docs/api/openapi-v1.json`, and a test fails if the code drifts from it. See D5. |
| 12 | Configuration | `PublicBaseUrl`; `ApiKeys:Clients:<n>:OwnerId` / `KeyHash` (SHA-256 hex, never the key itself). Compose and `.env.example` get one dev client; the dev key is documented as throwaway. |
| 13 | Docs | LLD §3/§6.1/§8 (auth, error codes, config), README ("Try the API": curl + Swagger), CHANGELOG, log with prompt iterations |

**Out of scope:**
- `GET`/`DELETE /api/v1/urls/{code}` (TSK-10). The `Location` header already points there.
- Transient-failure handling: server-side retry, circuit breaker, request timeout, `503` on failover, idempotency-key cleanup (TSK-11). In TSK-08 a database outage surfaces as `500`.
- Rate limits / `429` (TSK-19); expiry (TSK-14); splitting validation into `IUrlValidationRule` classes (TSK-16).

## 4. Constraints
- Controllers only map HTTP to the Application use case; authentication and error mapping live in the pipeline (CLAUDE.md, ADR-0009).
- **Never log** API keys, full long URLs or request bodies (ADR-0013). Idempotency keys may be logged.
- API keys are stored only as SHA-256 hashes and compared in constant time. No key in the repository except the clearly marked dev key in `.env.example`.
- Error `code` values are stable and documented (existing `DomainErrorCodes` plus new ones); never renamed.
- A replay returns the **stored** body byte for byte, not a re-rendered one.
- No new behaviour in Redirect or the AnalyticsWorker.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Valid request without alias → `201`, a 7-character generated code, `shortUrl` = `PublicBaseUrl/<code>`, `Location`, link stored with the caller as owner | API test (Testcontainers) |
| AC2 | Alias → `201` with that code; same alias again → `409` problem; **two simultaneous requests for the same alias → exactly one `201` and one `409`, never `500`**; alias of the generated shape, reserved or malformed → `400` with its error code | API tests, incl. a concurrency test |
| AC3 | Invalid `longUrl` (each §7 rule incl. self-link) → `400` with the rule's code; `expiresAt` present → `400 expiry.unsupported`; malformed `Idempotency-Key` → `400` | Unit + API tests |
| AC4 | No or unknown `X-Api-Key` → `401` problem; the key never appears in logs | API tests (log capture) |
| AC5 | **Idempotency:** same key + same request → identical body, `Idempotent-Replayed: true`, one link; same key + different request → `422`; same key, other owner → independent; key older than 24 h → new link; a `409` stores nothing, so a retry after freeing the cause runs again | API tests |
| AC6 | **Two identical requests at the same moment** with one key → one link, both `201` with the same code (one replayed) | API concurrency test |
| AC7 | Request hash: field order, whitespace and JSON formatting don't change it; any field value does | Unit tests |
| AC8 | Generated-code conflict (simulated) → logged, retried with the next code, `201` | Unit test with fakes |
| AC9 | `IdSpaceExhaustedException` → `503` + `Retry-After: 5` | Unit/API test |
| AC10 | Swagger UI served at `/swagger` when enabled, absent by default; committed `docs/api/openapi-v1.json` matches the code | API tests (drift test) |
| AC11 | All gates green; Domain/Application coverage ≥ 80%; docs updated; log with prompt iterations and actual hours given when the PR is opened | CI and review |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Handler, ports, stores, auth handler, exception handlers, controller, OpenAPI | ✅ (`implement.md`) | **Reviews every line** (high risk), especially the transaction and idempotency branches, and the key comparison |
| Unit, API and concurrency tests | ✅ (`write-tests.md`) | Checks that AC5/AC6 really prove "no duplicate, never the wrong link" |
| Self-review and PR description | ✅ (`review.md`) | Ownership statement; gives hours when opening the PR |

**Tool:** Claude Code (Claude Opus 5.5). Prompt iterations are recorded in the log entry.

## 7. Risks
| Risk | Mitigation |
|---|---|
| A replay returns the wrong link, or a retry creates a duplicate | Store and compare the normalized hash; tests for every branch of the LLD §6.1 table, plus the concurrency test |
| The idempotency row survives a failed create, blocking retries | One transaction for key, link and response; tests that a `409` leaves no key row |
| A timing side channel on API-key comparison | Hash, then `CryptographicOperations.FixedTimeEquals` |
| Secrets or URLs in logs | Log capture test; no request-body logging |
| Swagger exposes the API publicly | UI off by default; enabled only by explicit configuration (compose) |

## 8. Decisions for the engineer (to settle at approval)
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | Where do API keys live in v1? | (a) configuration: per client an owner ID and the **SHA-256 hash** of its key, from env/secret store; (b) a database table with an admin endpoint | **(a).** No key-management feature exists yet (A-24); (b) adds an admin API outside the brief. |
| D2 | The plan says "**read-then-insert in v1**" so that TSK-15 can fix an alias race (500 instead of 409). But `TryInsertAsync` (TSK-06) already turns a duplicate into `false`, so a read-then-insert would answer `409` correctly even under a race; getting a `500` would mean deliberately writing broken code. | (a) implement it correctly now (insert and detect the conflict, no read) and pick a **real** bug for TSK-15 when we get there; (b) follow the plan literally and plant the race | **Decided: (a)** (engineer, 2026-10-23). A planted bug is not an honest brownfield exercise. TSK-15 will use a real defect found in the merged code; noted in `04-task-breakdown.md`. |
| D3 | Self-link check: a single class now, with TSK-16 later refactoring validation into `IUrlValidationRule` classes (as planned), or the rule interface now? | (a) single `SelfLinkCheck` now, refactor in TSK-16; (b) `IUrlValidationRule` now, and TSK-16 gets a different refactor | **(a)**, as planned. `LongUrl.Create` plus one check is small enough today. |
| D4 | `expiresAt` before expiry exists (TSK-14) | (a) reject with `400 expiry.unsupported`; (b) ignore it | **(a).** Ignoring it would let a client believe the link expires. Accepting it later is a backward-compatible change. |
| D5 | Swagger UI exposure | (a) behind `OpenApi:EnableUi` (on in compose, off by default); (b) `Development` environment only | **(a).** Explicit, and doesn't switch on other Development behaviour in the container. |
