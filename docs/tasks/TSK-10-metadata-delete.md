# TSK-10: Metadata and delete (`GET` / `DELETE /api/v1/urls/{code}`)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | Medium |
| **Depends on** | TSK-08 (authentication, controller, error mapping) |
| **Estimate** | 1.5 h |
| **Branch** | `task/TSK-10-metadata-delete` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-09 to TSK-13) |

## 1. Intent
Owners can read their link's details and delete it. A deleted link then answers `410` on redirect. Other
clients can't tell whether a link exists.

## 2. Technical context
- **LLD §3** (contract: `200` / `204`, `404` also for non-owners), **§6.3** (delete flow: ownership on the primary → `MarkDeleted` → `SaveDeletionAsync`, idempotent).
- FR-05 (delete), FR-06 (metadata), FR-09 (ownership), A-24, A-25.
- **Existing code:** `IShortUrlRepository.FindByCodeAsync` / `SaveDeletionAsync` (TSK-06), `ShortUrl.MarkDeleted` (idempotent, TSK-05), API-key authentication and problem mapping (TSK-08).

## 3. Scope
| # | Component | Purpose |
|---|---|---|
| 1 | Application: `IGetShortUrlHandler`, `IDeleteShortUrlHandler` (+ implementations) | Owner check on the primary; outcomes `Found(details)` / `NotFound`, `Deleted` / `NotFound` |
| 2 | Api: `ShortUrlsController.Get` / `Delete` | `200` with `{ code, shortUrl, longUrl, isCustom, createdAt, deletedAt }`; `204`; `404` problem |
| 3 | `Location` from TSK-08 now resolves | — |
| 4 | OpenAPI document regenerated | The drift test from TSK-08 enforces it |
| 5 | Docs | LLD §3 response shape, README, CHANGELOG, log |

**Out of scope:** stats (TSK-23); `503` during failover and the delete retry (TSK-11); cache purge on delete (only if TSK-30 happens).

## 4. Constraints
- Non-owner and unknown code give the **same** `404` response (no existence leak).
- Delete is idempotent: deleting again returns `204` and keeps the first `deletedAt` (A-25).
- Codes are never freed: creating the same alias after a delete still returns `409`.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Owner `GET` → `200` with the documented fields | API test |
| AC2 | Non-owner or unknown → identical `404` bodies (apart from `traceId`) | API test |
| AC3 | Owner `DELETE` → `204`; again → `204`, `deletedAt` unchanged; non-owner → `404` and the link is untouched | API tests |
| AC4 | After delete: Redirect answers `410`; `GET` metadata shows `deletedAt`; the alias can't be re-created (`409`) | API test across Api and Redirect |
| AC5 | No or unknown API key → `401` | API test |
| AC6 | Gates green; coverage ≥ 80%; OpenAPI updated; docs; log and hours | CI and review |

## 6. AI usage plan
Implementation and tests by AI (`implement.md`, `write-tests.md`); the engineer reviews the owner checks and the `404` equivalence; hours given when the PR is opened.

## 7. Risks
| Risk | Mitigation |
|---|---|
| Existence leak through different `404`s or timing | Same code path and body for both cases (AC2) |
| A repeated delete moves `deletedAt` | Existing `AND deleted_at IS NULL` guard; test (AC3) |

## 8. Decisions for the engineer
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | Metadata of a **deleted** link for its owner | (a) `200` with `deletedAt` set; (b) `404` | **(a).** The owner can confirm the delete; it's their data, and redirect already answers `410` to everyone else. |
