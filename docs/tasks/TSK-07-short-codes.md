# TSK-07: Short-code generation

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | **High** (ID generation and the ID block registry, ADR-0012 §4). Requires the ownership statement and logged prompt iterations. |
| **Depends on** | TSK-06 |
| **Estimate** | 3 h (re-estimated: ~1.35 h, `04` §5.1) |
| **Branch** | `task/TSK-07-short-codes` |
| **Status** | Approved by the engineer on 2026-10-23, with D1–D3 |

## 1. Intent
Generate **7-character codes that are unique for ever (NFR-05), can't be enumerated in order (NFR-09), and need almost
no coordination**:
- each Api instance leases batches of 10,000 IDs from its region's block;
- a keyed **Feistel permutation** maps each ID one-to-one onto the code space;
- **Base62** turns the result into 7 characters.

This task also seeds the first region block, **B-001**, and adds the **block registry** with its CI overlap check (ADR-0005).

## 2. Technical context
- **ADR-0005** (accepted):
  - region blocks assigned explicitly, with block IDs `B-001`…;
  - one shared Feistel key that **must never change**;
  - leasing with one atomic `UPDATE … RETURNING`, the next lease fetched at 50% use;
  - **startup check**: refuse to start without an active block for this region, or with blocks of another region;
  - an 80% usage alert;
  - aliases never take the generated shape.
- **LLD §6.4:**
  - the three levels (ID space → region block → lease);
  - the lease SQL;
  - **switching blocks** under `pg_advisory_xact_lock`;
  - the runbook;
  - Feistel: 42 bits split into 21/21, 4 rounds, HMAC-SHA256 round function, **cycle-walking** while the result ≥ 62⁷.
- **TSK-06:** the `id_blocks` table, its CHECK constraints, and the one-active-block-per-region index.
- **ADR-0011:** the Feistel key is a secret. It comes from configuration or the environment only.

## 3. Scope
**In scope:**

| # | Component | Purpose |
|---|---|---|
| 1 | Application ports | `IShortCodeGenerator.NextAsync`, `IIdRangeLeaser.LeaseAsync(size)`, `IdRange`, `IdSpaceExhaustedException` (mapped to `503` in TSK-08) |
| 2 | `Base62Encoder` | Encodes a number as exactly 7 characters `0-9A-Za-z` (left-padded), and decodes back. Rejects values outside `[0, 62⁷)`. |
| 3 | `FeistelPermutation` | A keyed, 4-round Feistel network over 42 bits, with **cycle-walking** into `[0, 62⁷)` and an **inverse**. The round function is `HMAC-SHA256(key, round ‖ R)` truncated to 21 bits, using `HMACSHA256.HashData` on stack buffers (no allocations per call). |
| 4 | `ShortCodeGenerator` | ID → Feistel → Base62 → `ShortCode.FromGenerated`. It keeps the current lease and a **pre-fetched** next one, hands out IDs under a lock, and starts a background refill at 50% use. |
| 5 | `PostgresIdRangeLeaser` | The atomic lease `UPDATE id_blocks … RETURNING`. On exhaustion it **switches blocks**: in one transaction, take a per-region advisory lock, re-check, mark the old block `exhausted`, activate the lowest `reserved` one, and retry. No reserved block → `IdSpaceExhaustedException`. Runs as the **Api role**. |
| 6 | `ShortCodeStartupCheck` (hosted service) | Before the Api accepts traffic: an active block must exist for `DeploymentRegionIndex`, there must be **no rows for another region**, and the first range is leased. Otherwise startup fails (ADR-0005). |
| 7 | Seed migration `SeedBlockB001` | Inserts `B-001`: region 0, `[0, 440,201,825,776)`, `active` |
| 8 | `config/id-blocks.json` + `scripts/ci/check-id-blocks.py` + CI step | The **block registry**, the single source of truth. CI fails if two blocks overlap, a range falls outside `[0, 62⁷)`, block IDs repeat or are malformed, or the format is wrong. |
| 9 | Configuration | `ShortCode:DeploymentRegionIndex` (default 0), `ShortCode:LeaseSize` (10,000), `ShortCode:FeistelKey` (**required**, at least 32 characters; turned into a 32-byte key via SHA-256). Compose passes a **dev-only** key from `.env`. |
| 10 | Benchmark | `ShortCodeBenchmarks` (BenchmarkDotNet): Feistel + Base62 per code, run once locally and the result recorded |
| 11 | Docs | LLD §6.4/§8 (key format, implementation notes), README, CHANGELOG, log with **prompt iterations** |

**Out of scope:**
- the create endpoint that uses the generator (TSK-08),
- the 80% usage alert and its metric (`id_block_used_ratio`, TSK-22 dashboards; the metric hook can wait until then),
- routing requests by decoding a code to its region (multi-region release).

## 4. Constraints
- **No collision is possible by construction.** Tests must still prove it, both empirically and by round-trip.
- **The Feistel key must never change** (ADR-0005). It is required at startup with no default, never logged, and kept out of the repository except as a clearly marked dev-only value in `.env.example`.
- No dependency on clocks or on coordination between instances. Thread-safe under concurrent requests.
- Leasing happens only in the Api. Redirect never generates codes (ADR-0009).
- SOLID: encoder, permutation, leaser and generator each have one responsibility; the generator depends on the `IIdRangeLeaser` abstraction.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Base62: 0 → `0000000`; 62⁷−1 → `zzzzzzz`; encode/decode round-trips; out-of-range values rejected | Unit tests |
| AC2 | **The Feistel permutation is a one-to-one map on `[0, 62⁷)`:** `Inverse(Permute(x)) == x` for 100,000 random and boundary IDs, and **no duplicate codes across 1,000,000 consecutive IDs** | Unit (property-style) tests |
| AC3 | Codes **don't look sequential**: consecutive IDs give codes with no shared prefix pattern (measured by distinct first characters across a sample); a different key gives different codes | Unit tests |
| AC4 | `ShortCodeGenerator` under **concurrency** (e.g. 50 tasks × 2,000 codes) yields only unique codes; it pre-fetches at 50%; and it doesn't stall when crossing from one lease to the next | Unit tests with an in-memory leaser |
| AC5 | `PostgresIdRangeLeaser`: **concurrent leases never overlap** (parallel leasers on a real PostgreSQL); a block switch activates the reserved block; a fully exhausted region throws `IdSpaceExhaustedException` | Testcontainers tests on a dedicated test region, cleaned up afterwards |
| AC6 | The startup check stops the Api when there is no active block, or when rows of another region exist, and passes with B-001 | Integration tests |
| AC7 | `check-id-blocks.py` passes for the real registry and **fails** for overlapping, out-of-range, duplicate-ID or malformed registries; it runs in CI | Unit tests of the script plus the CI step; proof of failure recorded in the PR |
| AC8 | The seeded B-001 row matches the registry exactly (no drift) | Integration test |
| AC9 | The benchmark has been run, with ns per code recorded in the PR and log | BenchmarkDotNet output |
| AC10 | All gates green; Domain/Application coverage ≥ 80%; docs updated; log with prompt iterations and actual hours given when the PR is opened | CI and review |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Encoder, permutation, generator, leaser, startup check, seed migration | ✅ (`implement.md`) | **Reviews every line** (high risk), especially the Feistel round function, cycle-walking, and the block-switch SQL |
| Property and concurrency tests | ✅ (`write-tests.md`) | Checks that AC2 and AC5 really prove "no collision" and "no overlap" |
| Registry and CI check | ✅ | Reviews the registry, which is the source of truth |
| Benchmark | ✅ runs it | — |
| Self-review and PR description | ✅ (`review.md`) | Ownership statement; gives hours when opening the PR |

**Tool:** Claude Code (Claude Opus 5.5). Prompt iterations are recorded in the log entry.

## 7. Risks
| Risk | Mitigation |
|---|---|
| A subtle Feistel mistake (e.g. a wrong bit mask) quietly breaks the one-to-one mapping | Round-trip property test over 100k IDs, plus a 1M-ID no-duplicate test; the domain is checked at the boundaries |
| Cycle-walking loops for ever | Impossible for a permutation (every cycle returns into the range), and the expected number of steps is ~1.25. A hard cap with an exception guards against implementation bugs. |
| Test regions pollute the shared test database (the startup check rejects foreign rows) | Block-switch tests use their own region, and remove its rows in `finally` |
| The key leaks through logs or exceptions | The key is never logged, and options validation error messages contain no values |
| Thread-safety bugs in the lease handover | Concurrency test (AC4), with lock-based handover rather than lock-free cleverness |

## 8. Decisions for the engineer
| # | Proposal | Why |
|---|---|---|
| D1 | **No property-testing library** (e.g. FsCheck). Seeded random loops and exhaustive 1M ranges in xUnit instead. | One fewer dependency. The properties are simple enough, and failures are still reproducible from the fixed seed. |
| D2 | The **Feistel key is a passphrase** (≥ 32 characters) **hashed with SHA-256** into the 32-byte key | Easier to handle as a secret than raw base64. It also keeps `.env.example` free of high-entropy strings that gitleaks would flag. |
| D3 | The usage metric and the 80% alert are **deferred to TSK-22** (dashboards) | At 100M/day, B-001 lasts ~12 years, so there's no urgency. It is listed as a known gap until then. |
