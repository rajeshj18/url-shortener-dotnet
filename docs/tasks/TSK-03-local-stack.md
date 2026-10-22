# TSK-03: Local stack (Docker Compose)

| Field | Value |
|---|---|
| **Stage / phase** | Stage 1 (core) · Publish, workflow and platform |
| **Risk class** | Medium (ADR-0012 §4) |
| **Depends on** | TSK-02 |
| **Estimate** | 1.5 h (`04-task-breakdown.md`) |
| **Branch** | `task/TSK-03-local-stack` |
| **Status** | Approved by the engineer on 2026-10-22, including the deviation in §8 |

## 1. Intent
Make the system **runnable end to end on a laptop with open-source images**, using Docker Compose. That is
the brief's "working prototype" requirement (requirements §6.4). It is also the base that the CI smoke
test (TSK-04, TSK-12) and every later integration step build on.

This task is also **re-estimate point 1** (`04` §1): afterwards, the stage-1 plan is re-estimated from actual hours.

## 2. Technical context
- **HLD §4.2 (local stack):** PostgreSQL and `grafana/otel-lgtm` in stage 1. Kafka and the worker are **not deployed until stage 2** (`04` §1).
- **ADR-0009:** one image per service; the services are stateless; health endpoints.
- **ADR-0011:** no secrets in the repo. Local values come from `.env`, created from `.env.example` with throwaway values. `.env` is git-ignored.
- **ADR-0013:** services export OTLP to `grafana/otel-lgtm`. Grafana, Tempo, Loki and Prometheus run in a single container.
- **TSK-02:** services listen on `/health/live` and `/health/ready`; observability is off when no endpoint is set.

**Pinned image versions (checked today):** `postgres:16.15-alpine`, `grafana/otel-lgtm:0.37.0`, `mcr.microsoft.com/dotnet/sdk:8.0-alpine` and `aspnet:8.0-alpine`. The .NET images use the `8.0` tag, so they pick up security patches; Trivy scanning arrives in stage 2.

## 3. Scope
**In scope:**

| # | File / component | Purpose |
|---|---|---|
| 1 | `src/UrlShortener.Api/Dockerfile`, `src/UrlShortener.Redirect/Dockerfile`, `src/UrlShortener.AnalyticsWorker/Dockerfile` | Multi-stage builds (SDK → ASP.NET runtime, Alpine). They run as the **non-root `app` user**, listen on port 8080, and include a `HEALTHCHECK` on `/health/live`. The worker image is built but not run in stage 1. |
| 2 | `.dockerignore` | Keeps `bin/`, `obj/`, `.git/`, `*.pdf`, `.env` and test results **out of the build context**, so the brief and secrets can never end up in an image |
| 3 | `docker-compose.yml` | Services `postgres`, `otel-lgtm`, `api`, `redirect`. Health checks, `depends_on` conditions, a named volume for the database, host ports bound to **127.0.0.1** only. |
| 4 | `.env.example` | Throwaway local values (database name, user, password), copied to `.env` |
| 5 | `scripts/compose-smoke.ps1` and `scripts/compose-smoke.sh` | Wait until every service is healthy, then check each health endpoint. Non-zero exit on failure, so CI can reuse it (TSK-04, TSK-12). |
| 6 | README "Run locally" section | Prerequisites, the two commands, URLs (Api, Redirect, Grafana), how to stop and reset |
| 7 | `CHANGELOG.md`, traceability log | Entries for TSK-03, including **re-estimate point 1** |

**Out of scope:**
- the `migrate` step and database users (**TSK-06**, because there are no migrations yet; this moves it from TSK-03, see §8),
- Kafka and the worker in compose (stage 2, TSK-23),
- CI (TSK-04),
- a read-replica profile (later; not needed for stage-1 tests).

## 4. Constraints
- **One command after copying `.env`:** `docker compose up -d --build`.
- **Nothing secret in the repository.** Compose reads values from `.env`. The values in `.env.example` are clearly dev-only.
- Containers run as non-root. Host ports are bound to `127.0.0.1`, so nothing is exposed on the network.
- Total memory stays roughly within the HLD budget (stage 1 without Kafka: about 1.5 GB).
- SOLID and middleware rules are unchanged. This task adds no application code.

## 5. Acceptance criteria
| # | Criterion | How it's verified |
|---|---|---|
| AC1 | `docker compose up -d --build` from a clean state brings **all four services to `healthy`** | `docker compose ps` output in the PR |
| AC2 | `/health/live` and `/health/ready` return `200` for Api (`localhost:5080`) and Redirect (`localhost:5081`) | Smoke script output |
| AC3 | **Traces from both services are visible in Grafana** (`localhost:3000`, Tempo), using the service names `urlshortener-api` and `urlshortener-redirect` | Tempo search result / screenshot in the PR |
| AC4 | The containers run as **non-root** | `docker compose exec api id` output |
| AC5 | Build context and images contain **no `.pdf`, `.env` or `bin/obj`** | Listing of the build context, or `docker run … ls` in the PR |
| AC6 | The smoke script exits `0` when healthy, and **non-zero when a service is down** (tested by stopping `redirect`) | Both outputs in the PR |
| AC7 | README run instructions work as written | The engineer runs them |
| AC8 | CHANGELOG entry; log entry with actual hours (before merge); re-estimate recorded | Review |

## 6. AI usage plan
| Step | AI | Engineer |
|---|---|---|
| Dockerfiles, compose, `.dockerignore`, `.env.example`, smoke scripts | ✅ | Reviews |
| Run the stack; collect evidence for AC1–AC6 | ✅ | Checks Grafana (AC3); runs the README steps (AC7) |
| Re-estimate of stage 1 from TSK-01 to TSK-03 actuals | ✅ drafts | Decides any scope change |
| Self-review and PR description | ✅ (`review.md`) | Gives actual hours before merging |

**Tool:** Claude Code (Claude Opus 5.5).

## 7. Risks
| Risk | Mitigation |
|---|---|
| Docker Desktop not running, or low on resources | Prerequisites in the README; memory budget kept small |
| The NuGet audit in the image build needs network access | Same as the local build; failures are visible in the build output |
| The `8.0-alpine` tag moves under us | Intended, for security patches. Trivy (stage 2) checks the result. A digest can be pinned later if needed. |
| A traces check that depends on timing | The smoke script retries; Grafana is checked after a few requests |

## 8. Deviation from the approved breakdown
The `migrate` step moves from TSK-03 to **TSK-06**, because there are no migrations to run until the
schema exists. This is recorded in `04-task-breakdown.md` History when this spec is approved.
