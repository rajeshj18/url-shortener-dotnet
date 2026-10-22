# TSK-02: Solution skeleton

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Publish, workflow and platform |
| **Risk class** | Medium (ADR-0012 §4) |
| **Depends on** | TSK-01 |
| **Estimate** | 2 h (`04-task-breakdown.md`) |
| **Branch** | `task/TSK-02-solution-skeleton` |
| **Status** | Approved by the engineer on 2026-10-22, with additions (SOLID; middleware for cross-cutting concerns) |

## 1. Intent
Create the empty but **buildable and tested** .NET 8 solution that every later task builds on. The
architecture rules (ADR-0009) and the C# style (ADR-0003) are **enforced by the build and by tests from
the first commit**, not left to review discipline.

## 2. Technical context
- **ADR-0003:** `net8.0`, C# 11, nullable reference types, warnings as errors, `AnalysisMode=All`; classic OOP style (controllers, explicit `Program.Main`, block-scoped namespaces, no top-level statements or primary constructors).
- **ADR-0009:** three services plus three libraries; the dependency rules; health endpoints in all three services; services never reference each other.
- **ADR-0013:** OpenTelemetry for traces, metrics and logs, exported over OTLP. The collector (`grafana/otel-lgtm`) arrives in TSK-03.
- **LLD §5:** the project layout.
- **Installed SDK:** 8.0.424.

## 3. Scope
**In scope:**

| # | File / component | Purpose |
|---|---|---|
| 1 | `UrlShortener.sln` | Solution file containing all 10 projects |
| 2 | `global.json` | Pins the SDK to `8.0.424` (`rollForward: latestFeature`) |
| 3 | `Directory.Build.props` | `net8.0`, `LangVersion 11`, `Nullable enable`, `TreatWarningsAsErrors`, `AnalysisMode All`, `NuGetAudit` on, deterministic builds |
| 4 | `Directory.Packages.props` | **Central package management**: one place for every NuGet version |
| 5 | `.editorconfig` | ADR-0003 style enforced as build errors: block-scoped namespaces, no top-level statements, no primary constructors, naming rules |
| 6 | `src/UrlShortener.Domain`, `.Application`, `.Infrastructure` | Empty class libraries with the correct project references only |
| 7 | `src/UrlShortener.Api`, `src/UrlShortener.Redirect` | ASP.NET Core with controllers and an explicit `Program.Main`; `/health/live` and `/health/ready` |
| 8 | `src/UrlShortener.AnalyticsWorker` | Worker with a placeholder `BackgroundService`; health endpoints. **Not deployed until stage 2** (no Kafka yet). |
| 9 | Observability wiring | A single `AddUrlShortenerObservability()` extension in Infrastructure, adding OpenTelemetry traces, metrics and logs with OTLP export. It's off when no endpoint is configured, so the services start without a collector. |
| 10 | `tests/UrlShortener.ArchitectureTests` | One NetArchTest test **per ADR-0009 rule** (see AC4) |
| 11 | `tests/UrlShortener.IntegrationTests` | Health-endpoint tests for each service (`WebApplicationFactory`, no Docker needed) |
| 12 | `tests/UrlShortener.UnitTests`, `tests/UrlShortener.Benchmarks` | Empty projects, wired up, ready for TSK-05 and TSK-07 |
| 13 | `CLAUDE.md` | The process fix from TSK-01: get the **actual hours and any edits or rejections before the engineer merges**. **Engineer addition:** coding principles: **SOLID**, and **cross-cutting concerns in middleware**, not in controllers |
| 13a | Exception-handling middleware (engineer addition) | A shared `IExceptionHandler` in both web services: unhandled exceptions become RFC 7807 `application/problem+json` with `traceId`, logged once, and never leak exception details |
| 14 | `CHANGELOG.md`, traceability log | Entries for TSK-02 |

**Out of scope:**
- Docker and compose (TSK-03),
- CI (TSK-04),
- domain logic (TSK-05),
- database and EF Core (TSK-06),
- OpenAPI/Swagger (TSK-08).

## 4. Constraints
- **SOLID** (engineer addition):
  - single responsibility per class,
  - extend through new implementations rather than editing existing ones,
  - implementations honour their interface contracts,
  - small, role-specific interfaces,
  - depend on abstractions, wired up in each service's composition root.
- **Middleware for cross-cutting concerns** (engineer addition): exception handling, correlation, auth (TSK-08), request timeouts (TSK-11) and rate limiting (TSK-19) go in the ASP.NET Core pipeline, **not in controllers**. Controllers only map HTTP to Application use cases.
- **No warning suppressions without a written reason.** If `AnalysisMode=All` flags the template code, fix the code. A suppression is allowed only as a targeted `.editorconfig` entry with a justification comment.
- Project references follow ADR-0009 **exactly**. No `InternalsVisibleTo` between libraries.
- No secrets, and no hard-coded URLs or ports beyond the `launchSettings` defaults.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | `dotnet build` succeeds with **0 warnings and 0 errors** | Build output in the PR |
| AC2 | `dotnet test` passes with **failed = 0, skipped = 0** | Test output in the PR |
| AC3 | `Directory.Build.props` and `global.json` match §3 (items 2–3) | Review |
| AC4 | Architecture tests cover each rule: Domain references nothing; Application references only Domain; Infrastructure doesn't reference services; services don't reference each other; controllers don't depend on Infrastructure types | Test names map to the rules |
| AC5 | **The architecture tests can actually fail:** a deliberate violation (e.g. Domain referencing Infrastructure) makes a test fail. Shown temporarily and **not committed**. | Output pasted in the PR |
| AC6 | **The style rules can actually fail:** a file-scoped namespace or top-level statement breaks the build. Shown temporarily and not committed. | Output pasted in the PR |
| AC7 | Each service starts with `dotnet run`, and `/health/live` returns `200` | Integration tests, plus a manual run |
| AC8 | The services start with no OTLP endpoint configured (observability off, no errors) | Integration tests |
| AC9a | An unhandled exception returns `500` as `application/problem+json` with a `traceId` and **no stack trace or exception message** | Integration test |
| AC9 | CHANGELOG entry; `CLAUDE.md` process fix; log entry with actual hours, recorded **before** merging | Review |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Create projects, props and editorconfig | ✅ | Reviews the project references and rules |
| Fix analyzer findings in template code | ✅ | Approves any suppression and its reason |
| Architecture and health tests | ✅ (`write-tests.md`) | Checks that each rule has a test |
| AC5/AC6 negative checks | ✅ runs them | Sees the output in the PR |
| Self-review and PR description | ✅ (`review.md`) | Completes the checklist; gives actual hours before merging |

**Tool:** Claude Code (Claude Opus 5.5).

## 7. Risks
| Risk | Mitigation |
|---|---|
| `AnalysisMode=All` with warnings as errors is very strict, so the template code may produce many findings | Fix them in the code. Only targeted, justified suppressions, listed in the PR. |
| NuGet package versions drift or have vulnerabilities | Central package management, plus `NuGetAudit` (which fails the build on known vulnerabilities) |
| The worker project exists but isn't used until stage 2 | It's an explicit placeholder with health endpoints only; the HLD says it's deployed from stage 2 |
