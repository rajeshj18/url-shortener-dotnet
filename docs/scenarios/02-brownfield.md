# Scenario 2: Brownfield (TSK-14, TSK-15, TSK-QW)

Changing **merged, working code**: an enhancement, bug fixes, and a test-and-documentation improvement, each with
an impact analysis before the change and evidence after it. The planned refactor (TSK-16) was **not done**: the
build stopped after TSK-15 by the engineer's decision (see §6).

Every claim links to its evidence: a spec, a PR, a row of the [traceability log](../ai-usage/traceability-log.md),
a test, or a CI run.

## 1. The ask
Make changes to an existing system without breaking it: add a feature to live code, fix defects found in it, and
improve its tests and documentation.

## 2. Normalization
| Ask | Became | Where |
|---|---|---|
| "Links should be able to expire" | FR-04: optional `expiresAt`, `410` afterwards, at most 10 years ahead (D1), never reusing a code (A-25) | [TSK-14 spec](../tasks/TSK-14-link-expiry.md) |
| "Fix a bug" | A **real** defect from the merged code, not a planted one (TSK-08 D2), reproduced by a failing test committed first | [TSK-15 spec](../tasks/TSK-15-bug-fix.md) |
| "Improve tests and docs" | Close the deliverable gaps; fix the place where the code contradicted an ADR | [TSK-QW spec](../tasks/TSK-QW-quick-wins.md) |

## 3. Decomposition and impact analysis

### Enhancement: link expiry (TSK-14, [PR #24](https://github.com/rajeshj18/url-shortener-dotnet/pull/24))
| Area | Changed? | How |
|---|---|---|
| `short_urls` table | ✅ | **Expand-only**: a nullable `expires_at` column appended (metadata-only in PostgreSQL) |
| `redirect_lookup` view | ✅ | `CREATE OR REPLACE` appending `expires_at`; grants survive; `Down` recreates and re-grants |
| Domain (`ShortUrl`, `LinkState`) | ✅ | `ExpiresAt`, `ValidateExpiry`, `StateAt(now)`, `Expired`; **existing signatures kept** so callers didn't change |
| Create, redirect, metadata | ✅ | Accept, enforce (`410`), expose; `expiresAt` in the idempotency hash, as ADR-0010 had reserved |
| ID generation, auth, resilience | ❌ untouched | — |

**Deployment order:** migrate first; old instances keep working because they never select the new column.

### Bug fixes (TSK-15, [PR #25](https://github.com/rajeshj18/url-shortener-dotnet/pull/25); TSK-QW #7)
| Defect (found how) | Impacted code | Untouched |
|---|---|---|
| `401` without `WWW-Authenticate` (probing the running stack) | `ApiKeyAuthenticationHandler` | Domain, database, ID path |
| Two `traceId` formats in error bodies (same probe) | Problem-details customization | Our own handlers' format |
| A corrupt stored row answered `400` (found while writing TSK-14's tests) | `ShortUrlRecordMapper`, `NpgsqlRedirectLookup` | The domain rules themselves |

## 4. AI-assisted execution
- **Existing tests as a safety net.** Before writing TSK-14's tests, the AI ran the **unchanged** suite against the change: exactly 5 tests broke, all asserting "no expiry yet" (2 for `expiry.unsupported`, 2 schema tests, the OpenAPI snapshot), and only those were changed, as the spec allowed.
- **Compatibility over neatness.** CA1721 rejected the spec's `GetState(now)` next to the existing `State` property. Rather than change `State`'s meaning for existing callers, the AI kept it and named the new method `StateAt(now)` (logged deviation).
- **Failing test first.** TSK-15's tests were pushed alone; the PR's first CI run was red, "3 fail, 427 pass" ([run](https://github.com/rajeshj18/url-shortener-dotnet/actions/runs/39967332639/job/120460519399)); the fix turned it green. TSK-QW #7 was shown red locally (engineer's decision D1).
- **AI mistakes caught:** a test that moved `expires_at` before `created_at` (no real row can); CodeQL's note on disposing responses in `finally`; a changelog clean-up that landed in a merge commit, leaving an empty commit behind (recorded, not rewritten).

## 5. Validation
| Change | Evidence |
|---|---|
| Expiry | 15 unit + 8 integration tests; migration rolled back and forward on a scratch database; applied at once to the existing ~190K-row table; mutation check 3/3 (expiry boundary, redirect ignoring expiry, hash ignoring `expiresAt`) |
| `WWW-Authenticate`, `traceId` | Red-then-green CI on PR #25; contract tests across both services and every problem source |
| Corrupt rows | Red-then-green locally; `500` on metadata and redirect, logged without the stored URL |
| JSON console logging | Tests for all three hosts; a clean-clone run shows every console line is valid JSON with no secrets |

## 6. Outcome
| Brownfield kind | Status |
|---|---|
| Enhancement | ✅ Link expiry (FR-04 met) |
| Bug fixes | ✅ Three real defects fixed, each reproduced first |
| Test and documentation improvement | ✅ TSK-QW: testing strategy, risks and trade-offs, this write-up, the final summary, JSON console logging, honest status |
| **Refactor** | ❌ **Not done.** TSK-16 (URL validation into `IUrlValidationRule` classes, characterization tests first) has an approved spec but was descoped when the build stopped |

| Task | Estimate | Actual (engineer) |
|---|---|---|
| TSK-14 Link expiry | 1 h | 0.33 h |
| TSK-15 Bug fix | 1.5 h | 0.17 h |
| TSK-QW Quick wins | ~3–3.5 h | given when the PR is opened |
