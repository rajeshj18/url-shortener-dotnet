# TSK-17: Test and documentation improvements (brownfield)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Scenario 2, brownfield |
| **Risk class** | Low |
| **Depends on** | TSK-14, TSK-15, TSK-16 |
| **Estimate** | 1 h |
| **Branch** | `task/TSK-17-tests-docs` |
| **Status** | Draft spec, awaiting engineer approval (batch review TSK-14 to TSK-18) |

## 1. Intent
Close the gaps a reviewer would find: untested code, incomplete API documentation, and one place where the
code doesn't match an accepted ADR (console log format, found on 2026-10-23 while answering a question about
logging).

## 2. Technical context
- The CI gate covers only Domain and Application (≥ 80%). Infrastructure and the services are measured but not gated.
- The OpenAPI document (TSK-08, committed with a drift test) lists the error statuses but not which error codes each can carry, or examples.
- **ADR-0013** says logs go out through OpenTelemetry "plus JSON console output". The OpenTelemetry part exists; the console uses .NET's default **plain-text** formatter. (`docker compose logs` shows text; Loki is unaffected.)

## 3. Scope
| # | Component | Purpose |
|---|---|---|
| 1 | Merged coverage report | Locally with ReportGenerator over all test projects; list uncovered Infrastructure/service code by class in the PR |
| 2 | Tests for real gaps | Add tests where uncovered code has behaviour (error branches, guards, configuration validation); skip trivial or generated code, and say so |
| 3 | OpenAPI completeness | Per status, the possible error codes and an example body; descriptions for every field and header; regenerate the committed document |
| 4 | **JSON console logging (ADR-0013)** | Configure the console formatter as JSON in all three services (`Logging:Console:FormatterName = json`, with UTC timestamps), plus a test that each host resolves the JSON formatter |
| 5 | README and docs pass | Fix stale statements found while doing 1–4 |
| 6 | Docs | CHANGELOG, log |

**Out of scope:** raising the gate to Infrastructure (D1); mutation testing (TSK-28, stage 3).

## 4. Constraints
- No production behaviour change other than the console log format.
- Never log long URLs, IP addresses or API keys; the JSON format changes nothing about what is logged.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | Coverage report attached to the PR; each remaining uncovered class explained | PR |
| AC2 | New tests for the listed behavioural gaps | CI |
| AC3 | OpenAPI shows error codes and examples per status; drift test passes | Drift test + review |
| AC4 | `docker compose logs api` shows one JSON object per line with `Timestamp`, `LogLevel`, `Category`, `Message` | Compose check + test |
| AC5 | Gates green; docs; log and hours | CI and review |

## 6. AI usage plan
AI produces the report, the tests and the documentation; the engineer judges which gaps are worth tests, and gives hours when opening the PR.

## 7. Risks
| Risk | Mitigation |
|---|---|
| Chasing coverage numbers with low-value tests | Tests only where code has behaviour; exclusions explained |
| JSON logs harder to read locally | `docker compose logs` piped through `jq` documented in the README |

## 8. Decisions for the engineer
| # | Question | Options | Recommendation |
|---|---|---|---|
| D1 | Gate Infrastructure coverage too? | (a) report only; (b) gate at a threshold (e.g. 70%) | **(a)** for now: much of Infrastructure is exercised by integration tests the gate already requires, and a threshold would invite low-value tests |
