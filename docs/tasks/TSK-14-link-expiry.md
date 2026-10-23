# TSK-14: Link expiry (brownfield enhancement)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 2, brownfield |
| **Risk class** | **High** (schema change on the hot path; the `redirect_lookup` contract; the idempotency hash) |
| **Depends on** | TSK-13 (scenario 1 merged) |
| **Estimate** | 1 h |
| **Branch** | `task/TSK-14-link-expiry` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-14 to TSK-18) |

## 1. Intent
Clients can set an optional expiry on a link (FR-04, A-22). After it, the link answers `410 Gone` like a deleted
one. This is the first change to **existing, merged code**, so it starts with an impact analysis and ships as an
expand-only migration that old instances tolerate.

## 2. Technical context and impact analysis
- **Requirements:** FR-04; A-22 (no expiry by default), A-25 (codes never reused, also after expiry). **ADR-0007** (`410` cacheable for a day), **ADR-0010** (the request hash already reserves `expiresAt`), **ADR-0009** (expand, then contract).

| Area | Today | Change |
|---|---|---|
| Schema `short_urls` | no expiry column (TSK-06 R1) | **add** `expires_at timestamptz NULL` (expand-only) |
| View `redirect_lookup` | `code, long_url, deleted_at` | `CREATE OR REPLACE VIEW` **appending** `expires_at`; grants survive a replace; never `security_invoker` |
| Domain `ShortUrl` | `State` from `DeletedAt` | `ExpiresAt`; `GetState(now)` → `Expired` when `now ≥ ExpiresAt` (the LLD §5 note) |
| Domain `LinkState` | `Active`, `Deleted` | add `Expired` |
| Create (`CreateShortUrlHandler`) | `expiresAt` → `400 expiry.unsupported` | accept a future `expiresAt`; normalized into the request hash (already in its format) and the stored `201` body |
| Redirect (`NpgsqlRedirectLookup`, `ResolveShortUrlHandler`) | `Deleted` → `410` | `Expired` → `410` with the same caching |
| Metadata (`GET /api/v1/urls/{code}`) | no expiry | adds `expiresAt` |
| OpenAPI, LLD §3/§4/§6, README | | updated; drift test regenerates the document |
| Unchanged | ID generation, auth, resilience, cleanup | — |

**Deployment order** (expand-only): migrate first; old Api/Redirect instances keep working because they never
read the new column; then roll out the new instances.

## 3. Scope
| # | Component | Purpose |
|---|---|---|
| 1 | Migration `AddLinkExpiry` | Column + view replacement; a Testcontainers test that the Redirect role still reads only the view, and that the view has no `security_invoker` |
| 2 | Domain | `ShortUrl.ExpiresAt`, `GetState(now)`, `LinkState.Expired`; creation validates `expiresAt` (new code `expiry.past`; see D1 for the upper bound) |
| 3 | Create | Accept `expiresAt`; the hash uses it as designed; the `expiry.unsupported` code is no longer emitted but stays documented (codes are never reused) |
| 4 | Redirect | `410` for expired links; `HEAD` the same; no click event |
| 5 | Metadata | `expiresAt` in the response |
| 6 | Docs | Impact analysis (in this spec and the PR), LLD, OpenAPI, CHANGELOG, log |

**Out of scope:** archiving or deleting expired rows (a housekeeping job, LLD §6.6 "later"); changing an expiry after creation.

## 4. Constraints
- Expand-only: no column rename or drop, no change to existing view columns' order or meaning.
- An expired link is permanent (A-25): its alias can't be re-created.
- Time comes from `TimeProvider`; expiry is compared in UTC.
- Existing tests keep passing unchanged, except those that assert `expiry.unsupported` (they change with this task, and the PR says so).

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Create with a future `expiresAt` → `201` with it in the body; a past or present one → `400 expiry.past`; beyond the upper bound → `400` (D1) | Unit + API tests |
| AC2 | Before expiry → `302`; after it → `410` + `Cache-Control: public, max-age=86400`, for `GET` and `HEAD`, no click event | API tests with a controllable clock |
| AC3 | Idempotency: the same key with a different `expiresAt` → `422`; with the same instant in another time-zone notation → replay | API tests |
| AC4 | Migration is expand-only and the Redirect role still can't read `short_urls` | Testcontainers tests |
| AC5 | Metadata shows `expiresAt`; OpenAPI drift test passes with the regenerated document | API + drift tests |
| AC6 | Gates green; coverage ≥ 80%; docs; log with prompt iterations and hours | CI and review |

## 6. AI usage plan
AI implements and tests (`implement.md`, `write-tests.md`) and writes the impact analysis; the engineer reviews every line of the migration and the state rule (high risk), and gives hours when opening the PR.

## 7. Risks
| Risk | Mitigation |
|---|---|
| Redirect starts failing during a rolling deploy | Expand-only migration; old code never selects `expires_at` |
| Clock differences between instances flip a link early or late | Tolerated: up to clock skew (seconds); documented |
| Replay returns a body with a different `expiresAt` format | The stored body is replayed through the same serializer (TSK-08) |

## 8. Decisions for the engineer
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | Upper bound for `expiresAt` | (a) none; (b) at most 10 years ahead (`400 expiry.too-far`) | **(b)**: catches unit mistakes (milliseconds as seconds) and keeps "never" meaning "no expiry" |
