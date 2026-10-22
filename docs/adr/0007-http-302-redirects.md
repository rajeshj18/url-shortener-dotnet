# ADR-0007: HTTP 302 redirects

**Status:** Accepted · **Date:** 2026-10-21 · **Related:** ADR-0002, ADR-0006, ADR-0008, FR-03, NFR-07, NFR-08

## Context
Browsers and proxies cache a `301` indefinitely. Clicks would then never reach us, which undercounts
analytics (NFR-08). Deleted links would also keep redirecting, which breaks the delete-propagation targets
(SLO 5 s, SLA 60 s). Separately, HTTP lets caches store `404` and `410` responses by default, so their
caching must be set deliberately.

## Decision
| Response | When | Headers | Click event |
|---|---|---|---|
| `302 Found` | Active link | `Location: <long URL>`, **`Cache-Control: no-store`** | Yes, for `GET` from a non-bot client |
| `404 Not Found` | Unknown or malformed code | **`Cache-Control: no-store`** | No |
| `410 Gone` | Deleted or expired link | **`Cache-Control: public, max-age=86400`** | No |

- **Why `no-store` on 302:** it forbids storing the redirect in any cache, including the browser, so every click reaches us. `private, max-age=0` was considered first, but it still lets browsers store and sometimes reuse the redirect.
- **Why `no-store` on 404:** a code that doesn't exist yet may exist a moment later (create, then click immediately). A cached 404 would break read-your-writes (NFR-07).
- **Why 410 may be cached:** codes are never reused (A-25), and deleted or expired links never come back, so a 410 is permanent. Caching it spares us repeated requests for dead links.
- **`HEAD /{code}`** returns the same status and headers as `GET`, but **sends no click event**. Link-preview fetchers (chat apps, social networks) often use `HEAD`. Those that use `GET` are excluded by the bot filter (A-34).

## Alternatives considered
| Option | Why not chosen |
|---|---|
| 301 | Cached by clients; breaks analytics and deletes |
| 307 | Redirects are always GET, so it adds nothing over 302 |
| 302 with `private, max-age=0` | Browsers may still store and reuse the redirect, so repeat clicks could go uncounted |

## Consequences
- Every click reaches our servers, as the read-load estimate assumes.
- Repeated hits on dead links are partly absorbed by browser and proxy caches through the cacheable 410.

## History
- 2026-10-21 · Proposed.
- 2026-10-22 · Proposed · Revised after a consistency review: `no-store` on 302 and 404; 410 cacheable for 1 day; `HEAD` and link-preview handling; wording aligned with the SLO/SLA terms.
- 2026-10-22 · **Accepted** · Engineer signed off.
