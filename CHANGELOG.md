# Changelog

All notable changes to this project are recorded here, following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/).
Each entry names the task (`TSK-xx`) that made the change.

## [Unreleased]

### Added
- Local stack: `docker-compose.yml` with PostgreSQL 16.15, `grafana/otel-lgtm` 0.37.0, Api and Redirect.
  - Multi-stage Alpine Dockerfiles for all 3 services, running as the non-root `app` user, with health checks.
  - `.dockerignore` keeping the brief, secrets and build output out of images.
  - `.env.example`.
  - Smoke scripts (`scripts/compose-smoke.sh` / `.ps1`).
  - README "Run locally".

  (TSK-03)
- Solution skeleton: 10 projects (3 libraries, 3 services, 4 test/benchmark projects).
  - Strict shared build settings: .NET 8, C# 11, warnings as errors, `AnalysisMode=All`, NuGet audit; central package versions; style rules enforced as build errors.
  - Exception-handling middleware returning RFC 7807 responses with a `traceId`.
  - `/health/live` and `/health/ready` in every service, and OpenTelemetry wiring (off when no endpoint is configured).
  - Architecture tests for every ADR-0009 rule.

  (TSK-02)
- Engineering workflow: `CLAUDE.md` (AI project context), task-spec template, versioned prompt library, PR template with the ADR-0012 checklist, `CODEOWNERS`, `.gitattributes`, MIT `LICENSE`, and this changelog. (TSK-01)
- Design documentation: requirements with a traceability matrix, HLD, LLD, 13 accepted ADRs, assumptions register, task breakdown. (Design phase, before TSK-00)
