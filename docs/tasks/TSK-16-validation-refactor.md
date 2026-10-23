# TSK-16: Refactor URL validation into rule classes (brownfield)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 2, brownfield |
| **Risk class** | Low (no behaviour change, proven by characterization tests) |
| **Depends on** | TSK-13 (independent of TSK-14 and TSK-15) |
| **Estimate** | 1 h |
| **Branch** | `task/TSK-16-validation-refactor` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-14 to TSK-18) |

## 1. Intent
Today, URL validation is one method (`LongUrl.Create`, six rules in sequence) plus one Application check
(`SelfLinkCheck`). Scenario 3 (TSK-19) needs to add rules such as a threat check without editing working code.
This refactor turns each rule into its own class behind `IUrlValidationRule` (LLD §7), with **behaviour pinned by
characterization tests written first**.

## 2. Technical context and impact analysis
| Area | Today | After |
|---|---|---|
| `LongUrl.Create` (Domain) | six checks inline: required, length, characters, scheme, format, credentials | composes six Domain rule classes in the same order; still the only way to build a `LongUrl` (invariants stay in the Domain) |
| `SelfLinkCheck` (Application) | a single class called by the create handler | `SelfLinkRule : IUrlValidationRule`, one of the Application's **policy** rules, resolved from DI as `IEnumerable<IUrlValidationRule>` |
| `CreateShortUrlHandler` | calls `SelfLinkCheck.Ensure` | runs every registered policy rule, in registration order |
| Error codes and messages | | **unchanged** (stable, clients depend on them) |
| Untouched | redirect, persistence, API contract | — |

## 3. Scope
| # | Step | Purpose |
|---|---|---|
| 1 | Commit 1: characterization tests | A table of ~30 URLs (valid ones, each rule's failure, boundaries such as 2,048 vs 2,049 characters, mixed-case schemes, IDN hosts, self-links) → the **current** outcome and error code; also through the API. They pass on the current code. |
| 2 | Commit 2: the refactor | Domain rule classes + `IUrlValidationRule` + `SelfLinkRule`; DI registration; the characterization tests pass **unchanged** |
| 3 | Unit tests per rule | Each class tested on its own |
| 4 | Docs | LLD §5/§7, CHANGELOG (Changed: internal), log |

**Out of scope:** new rules (threat checks, denylist: TSK-19).

## 4. Constraints
- Characterization tests are committed before any production change, and not edited by the refactor commit.
- Rule order is preserved, so the **first** broken rule's code is still the one reported.
- Classic OOP style (ADR-0003); SOLID: one rule per class, new rules by adding classes (open/closed).

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Characterization tests committed first and green on the old code | CI run of commit 1 |
| AC2 | The refactor commit doesn't touch them, and they stay green | Diff + CI |
| AC3 | Adding a policy rule needs only a new class and one DI line | A test registers a fake rule and sees it enforced |
| AC4 | Gates green; docs; log and hours | CI and review |

## 6. AI usage plan
AI writes the characterization corpus first, then the refactor; the engineer checks the corpus covers every rule and boundary before the refactor lands.

## 7. Risks
| Risk | Mitigation |
|---|---|
| A subtle order change alters which error code is reported | Corpus includes URLs that break two rules at once |
| Performance on the create path | Rule objects are singletons; no allocation per rule beyond today's |

## 8. Decisions for the engineer
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | Where the intrinsic URL rules live | (a) Domain rule classes composed by `LongUrl.Create` (invariants stay in the Domain), policy rules in Application behind `IUrlValidationRule`; (b) every rule in Application, `LongUrl` without checks | **(a)**: a `LongUrl` can never exist in an invalid state, and the header-injection guard can't be skipped |
