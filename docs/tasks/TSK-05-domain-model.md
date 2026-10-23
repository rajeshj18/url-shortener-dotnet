# TSK-05: Domain model

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 1, greenfield |
| **Risk class** | Medium (new domain logic behind no public contract yet, ADR-0012 §4) |
| **Depends on** | TSK-02 |
| **Estimate** | 1.5 h (re-estimated: ~0.5 h, `04` §5.1) |
| **Branch** | `task/TSK-05-domain-model` |
| **Status** | Approved by the engineer on 2026-10-23 |

## 1. Intent
Model the URL shortener's **core business rules in plain C#**, with no framework dependencies, so every later
task (persistence, short-code generation, APIs) builds on rules that are **defined once, encapsulated and fully
unit-tested**. This is the first code that the **coverage gate** (≥ 80% for Domain) measures.

## 2. Technical context
- **LLD §5 (core types):** `ShortUrl` entity; value objects `ShortCode`, `LongUrl`, `CustomAlias`; `LinkState`.
- **LLD §7 / A-20 / A-21 (validation rules):**
  - URLs: absolute, `http`/`https` only, ≤ 2,048 characters, no embedded credentials.
  - Aliases: 4–30 characters `[A-Za-z0-9_-]`; not a reserved path; **not exactly 7 Base62 characters** (that shape is reserved for generated codes, ADR-0005).
- **ADR-0002 / A-25:** soft delete; codes are never reused; deleting again is idempotent.
- **ADR-0003:** classic OOP; `record` only for DTOs, so value objects are sealed classes implementing `IEquatable<T>`.
- **ADR-0009:** Domain references nothing (enforced by the architecture tests).
- Engineer requirements (TSK-02): **SOLID**; each rule lives in exactly one place.

**Deliberately excluded:**
- **Expiry (FR-04)** is the brownfield enhancement **TSK-14**. `LinkState` is therefore `Active` / `Deleted` only, and `ShortUrl` has no `ExpiresAt` yet. TSK-14 adds them to this existing code.
- **Rules that depend on configuration or context** live in **Application** (TSK-08), not Domain:
  - **self-loop** (needs our own host),
  - **reserved-path list from config**,
  - **threat/denylist checks** (TSK-19).

  Domain holds only the rules that are always true.

## 3. Scope
**In scope:**

| # | Type | Rules |
|---|---|---|
| 1 | `ShortCode` (value object) | The identifier in a short URL. Two kinds: **generated** (exactly 7 chars `[0-9A-Za-z]`) and **alias** (a valid `CustomAlias`). `IsGenerated` tells them apart *by shape* (ADR-0005). `TryParse(string, out ShortCode)` is for the redirect path's cheap format check (LLD §6.2). Case-sensitive equality. |
| 2 | `CustomAlias` (value object) | 4–30 chars `[A-Za-z0-9_-]`; **not** exactly 7 Base62 chars; **not** a reserved path (`api`, `health`, `swagger`, `favicon.ico`, `robots.txt`; case-insensitive) |
| 3 | `LongUrl` (value object) | Absolute URI; scheme `http` or `https` (case-insensitive); has a host; ≤ 2,048 characters; **no user-info** (`user:pass@`); **no whitespace or control characters** (they could be used to inject response headers via `Location`). The original string is kept exactly as sent (needed for the idempotency hash, LLD §6.1). |
| 4 | `ShortUrl` (entity) | `Code`, `Target`, `OwnerId` (1–64 chars), `IsCustom`, `CreatedAt`, `DeletedAt?`. Factories: `CreateGenerated` and `CreateWithAlias`. `GetState()` → `Active` / `Deleted`. `MarkDeleted(at)` is **idempotent**: it keeps the first deletion time, so a second delete doesn't change the record (A-25). |
| 5 | `LinkState` (enum) | `Active`, `Deleted` (`Expired` added by TSK-14) |
| 6 | `DomainValidationException` | Thrown by `Create` factories. It has a **stable error code** (e.g. `alias.reserved`, `url.scheme`) so the Api (TSK-08) can map it to RFC 7807 `400` responses, plus a human-readable message. The invalid input itself is **not** included in the message, because it could be unsafe to echo or log. |
| 7 | Unit tests | Every rule, boundaries and error codes (see §5) |
| 8 | `CHANGELOG.md`, log | Entries |

**Out of scope:**
- expiry (TSK-14),
- configuration/context rules and threat checks (TSK-08, TSK-19),
- ID generation and Feistel (TSK-07),
- persistence mapping (TSK-06).

## 4. Constraints
- **Domain stays plain C#**: no NuGet packages, no `Microsoft.*` (enforced by the architecture tests).
- **Value objects are immutable and always valid.** A `ShortCode`, `CustomAlias` or `LongUrl` instance can't exist in an invalid state. The constructors are private; the factories validate.
- **Times are passed in** (`DateTimeOffset`, UTC), not read from a clock inside Domain, so tests are deterministic.
- **SOLID:** one responsibility per type; each validation rule exists **once**. `ShortCode` reuses `CustomAlias`'s rules rather than duplicating them.
- No regular expressions with catastrophic backtracking: character checks are simple loops, or anchored, linear patterns.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | `CustomAlias` accepts 4 and 30 chars; rejects 3 and 31; rejects any character outside `[A-Za-z0-9_-]`; **rejects `abc1234`** (7 Base62) but **accepts `abc-123`** (7 chars with a dash); rejects every reserved path in any letter case | Unit tests, one per rule and boundary |
| AC2 | `ShortCode`: a generated code requires exactly 7 Base62 chars; `TryParse` accepts generated and alias shapes and rejects everything else (empty, too long, bad characters) without exceptions; `IsGenerated` is correct for both kinds; equality is case-sensitive | Unit tests |
| AC3 | `LongUrl` accepts `http://` and `https://` (any letter case); rejects relative URLs, `javascript:`, `data:`, `ftp:`, `file:`, `mailto:`; rejects 2,049 chars and accepts exactly 2,048; rejects user-info; rejects whitespace and control characters (including CR/LF); keeps the original string unchanged | Unit tests |
| AC4 | `ShortUrl`: the factories set every field; `OwnerId` rules hold; `GetState` returns `Active` and then `Deleted`; **`MarkDeleted` twice keeps the first time**; `MarkDeleted` before `CreatedAt` is rejected | Unit tests |
| AC5 | Every `DomainValidationException` carries a **stable code**, and its message **never contains the rejected input** | Unit tests |
| AC6 | **Domain line coverage ≥ 80%** (target ~100%) in the **CI gate**. The first real measurement, replacing "n/a". | The gate annotation and summary on the PR |
| AC7 | Architecture tests still pass (Domain has no dependencies); build, format and all CI checks green | CI |
| AC8 | CHANGELOG; log entry with actual hours **given when the PR is opened** | Review |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Domain types | ✅ (`implement.md`) | Reviews the rules against LLD §7, A-20, A-21 and ADR-0005 |
| Tests | ✅ (`write-tests.md`), mapping tests to ACs | Checks boundary cases |
| Self-review and PR description | ✅ (`review.md`) | **Gives actual hours when opening the PR**; merges |

**Tool:** Claude Code (Claude Opus 5.5).

## 7. Risks
| Risk | Mitigation |
|---|---|
| Unclear where rules belong (Domain vs Application) | §2: Domain holds only the rules that are always true; context rules go to Application |
| `System.Uri` accepts surprising inputs (e.g. `http:/x`, IDN hosts, Windows file paths) | Explicit tests for these inputs; scheme, host and absolute checks applied on top of `Uri.TryCreate` |
| Building expiry too early would leave TSK-14 nothing real to change | Excluded on purpose (§2) |
