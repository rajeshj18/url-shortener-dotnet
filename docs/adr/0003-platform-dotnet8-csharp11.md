# ADR-0003: .NET 8, C# 11, classic object-oriented style

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0009

## Context
- The engineer chose .NET 8 (installed) and a clean, classic object-oriented style.
- Compatibility only goes one way: C# 4/5-style code compiles under C# 11, but ASP.NET Core 8 can't be built with `LangVersion` 4 or 5.

## Decision
- `net8.0` with `<LangVersion>11</LangVersion>`, `Nullable` enabled, `TreatWarningsAsErrors`, `AnalysisMode=All`.
- **Style:**
  - controllers
  - an explicit `Program.Main`
  - block-scoped namespaces
  - interfaces and constructor injection
  - encapsulated domain classes
  - `async`/`await`
  - `record` only for DTOs and messages
- **Avoid:**
  - top-level statements
  - file-scoped namespaces
  - primary constructors
  - collection expressions
  - long pattern-matching chains
- Style rules are enforced through `.editorconfig` and the AI project context file.

## Alternatives considered
| Option | Why not chosen |
|---|---|
| `LangVersion` 4/5 | Can't be built with ASP.NET Core 8 |
| .NET 10 (current LTS) | Engineer's choice to stay on the installed .NET 8 |

## Consequences
- **.NET 8 support ends on 10 Nov 2026.** This is a known limitation. Upgrading means changing the target framework.
## History
- 2026-10-21 · Accepted · Engineer chose .NET 8 and C# 11 in a classic OOP style.
- 2026-10-21 · Engineer confirmed `record` types (DTOs/messages) and nullable annotations are kept.
