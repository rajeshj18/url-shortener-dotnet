# v1 load test (TSK-12)

Measured on 2026-10-23 against the local compose stack, to decide whether v1 needs a cache (ADR-0006) and to
check the latency SLOs (NFR-02). The pass/fail thresholds were written into the k6 scripts **before** any run
(`tests/load/`) and are exactly the SLOs. Raw k6 summaries: [`2026-10-23/`](2026-10-23/).

## Setup

| | |
|---|---|
| Machine | Windows 11 laptop, Snapdragon X (Arm64); Docker Desktop VM with **8 CPUs, 8 GB** |
| Stack | One Api, one Redirect, one PostgreSQL 16.15, `grafana/otel-lgtm` (100% tracing), all in that VM |
| Load generator | `grafana/k6:2.3.0` in a container **on the same VM**, on the compose network |
| Build | TSK-11 + TSK-11b (connection-pool caps) + TSK-12; Release images |
| Data | 2,000 seeded links per redirect run; `short_urls` held ~110K rows from earlier runs during the redirect runs, and 192K after the create run |
| Method | Open model (`constant-arrival-rate`): the rate is held whatever the latency. 15 s warm-up at the same rate, then **2 minutes measured**; thresholds apply to the measured scenario only. CPU sampled mid-run (`docker stats`). |

**Read the numbers as per-instance results on a laptop**, not production capacity: the services, PostgreSQL,
the observability stack and k6 itself (0.3–0.6 cores) share one VM.

## Results

| Scenario | Rate (req/s) | p50 | p95 | **p99** | Max | Errors | App CPU | PostgreSQL CPU | SLO |
|---|---|---|---|---|---|---|---|---|---|
| Redirect (random seeded links) | 1,477 | 0.53 ms | 1.09 ms | **3.77 ms** | 82 ms | 0% | Redirect 1.49 cores | 0.32 cores | p99 ≤ 50 ms ✅ |
| Hot key (30% on one link) | 1,501 | 0.51 ms | 0.93 ms | **2.87 ms** | 68 ms | 0% | Redirect 1.35 | 0.26 | p99 ≤ 50 ms ✅ |
| Scan (random unknown codes) | 1,500 | 0.57 ms | 1.13 ms | **3.44 ms** | 56 ms | 0% | Redirect 1.58 | 0.37 | p99 ≤ 50 ms ✅ |
| Create (generated codes) | 600 | 1.82 ms | 3.12 ms | **7.14 ms** | 70 ms | 0% | Api 1.68 | 0.33 | p99 ≤ 200 ms ✅ |

**Scan and the fallback budget:** over the ~135 s of the scan, `redirect_primary_fallback` counted **67,992
granted** and **134,460 denied** decisions: ~500 primary lookups per second, the configured per-instance
budget (plus its one-second burst). The rest were answered `404` at once, without the primary.

**Where it saturates** (45 s probes): redirects passed at 1,000 and 1,500/s and failed at 2,000/s, with the
Redirect process CPU-bound (~1.8 cores) and PostgreSQL at ~0.3 cores. *That probe ran before the pool-cap fix
and wasn't repeated*, so 1,500/s is the highest rate confirmed. Switching OpenTelemetry off didn't move the knee,
so tracing is not the bottleneck. Creates passed at 300 and 600/s; higher rates weren't probed.

## Findings the load test produced

1. **Connection exhaustion (fixed in TSK-11b, PR #18).** The first 2-minute runs at 1,500/s failed (redirect
   p99 827 ms, 12% errors; scan p99 641 ms, 11% errors) although 45 s probes at the same rate passed.
   PostgreSQL logged 11,163 "too many clients": the pools weren't capped, as LLD §6.7 requires. With caps, the
   same runs give the results above.
2. **Cold start.** Right after a deploy, a 30 s smoke run had redirect p99 65 ms; on warm services, 6 ms. The
   first requests pay for JIT compilation and new connections. Every script now warms up for 15 s before
   measuring; the thresholds didn't change. (This matters for CI, where the stack always starts cold.)
3. **The application tier is the bottleneck, not the database.** ~1 ms of CPU per redirect in the Redirect
   process versus ~0.2 ms in PostgreSQL. A cache would take load off the database, which isn't the constraint.

## ADR-0006 criteria

| Criterion (fixed before the test) | Result | Verdict |
|---|---|---|
| Redirect p99 ≤ 50 ms at target load per replica | 3.8 ms at 1,500/s. The production target per replica (35K/s ÷ 4–8 replicas ≈ 4.4–8.8K/s) can't be generated locally; PostgreSQL used 0.32 cores at 1,500/s, ≈ 1.9 cores at 8.8K/s if it scales linearly, with no sign of contention. | ✅ at the tested rate; **extrapolated** beyond it |
| Replica CPU < 70% at peak with the planned replica count | 0.32 cores per 1,500 lookups/s → ≈ 7.5 cores for 35K/s in total → ≈ 1.9 cores per replica with 4 replicas: **≈ 47% of a 4-vCPU replica**, ≈ 24% with 8. | ✅ (extrapolated) |
| Hot key (30% on one code): p99 ≤ 50 ms | 2.9 ms; the hot row stays in PostgreSQL's buffer cache | ✅ |
| Scan of random codes: primary fallback within its budget | ~500 fallbacks/s granted, the rest refused without the primary; p99 3.4 ms | ✅ |

**Decision: no cache for v1** (TSK-30 is not triggered). Every criterion holds where it can be measured, the
extrapolations rest on per-request database cost, and the measured bottleneck is application CPU, which is
scaled by adding stateless Redirect instances.

## Sizing (extrapolated, to confirm on production hardware)

| Tier | Measured here | At peak (35K redirects/s, 3.5K creates/s) |
|---|---|---|
| Redirect | 1,500/s per instance at ~1.5 cores | ~24 instances of this size (fewer on faster cores); add instances, not a cache |
| Api | 600/s per instance at ~1.7 cores | ~6 instances |
| PostgreSQL replicas | 0.32 cores per 1,500 lookups/s | 4 replicas of 4 vCPU at ~47% CPU (the LLD planned 4–8) |
| PostgreSQL primary | 0.33 cores per 600 creates/s | ≈ 1.9 cores at 3.5K creates/s |
| Connections | Redirect used its 30-connection cap at 1,500/s | instances × pool caps must stay under `max_connections` (LLD §6.7): e.g. 24 Redirect × 40 = 960, so production needs a connection pooler (PgBouncer) or a higher `max_connections` |

**Before launch:** repeat these runs on production-sized nodes, with the load generator on separate machines,
and record the results here and in ADR-0006.
