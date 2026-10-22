# ADR-0007: HTTP 302 redirects

**Status:** Proposed · **Date:** 2026-10-21 · **Related:** ADR-0002, ADR-0008

## Context
Browsers and proxies cache a `301` indefinitely. Clicks would then never reach us, which undercounts analytics,
and deleted links would keep redirecting, which breaks the 60 s staleness SLA.

## Decision
- `302 Found` with `Cache-Control: private, max-age=0`.
- Unknown code → `404`.
- Expired or deleted code → `410 Gone`.

## Alternatives considered
| Option | Why not chosen |
|---|---|
| 301 | Cached by clients; breaks analytics and deletes |
| 307 | Redirects are always GET, so it adds nothing over 302 |

## Consequences
Every click reaches our servers, as the read-load estimate assumes.

## History
- 2026-10-21 · Proposed.
