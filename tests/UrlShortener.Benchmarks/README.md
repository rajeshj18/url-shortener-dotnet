# Benchmarks

Micro-benchmarks with [BenchmarkDotNet](https://benchmarkdotnet.org/). They are not part of `dotnet test` or CI:
results depend on the machine, so they are run on demand and the numbers are recorded in the task's PR and the
traceability log.

## Run

Always in Release, from the repository root:

```bash
dotnet run -c Release --project tests/UrlShortener.Benchmarks -- --filter "*ShortCodeBenchmarks*"
```

Use `--filter "*"` for all benchmarks. Results are written to `BenchmarkDotNet.Artifacts/`, which is git-ignored.

## Results

### `ShortCodeBenchmarks` (TSK-07)

The cost of one generated code (LLD §6.4). Measured on 2026-10-23 with BenchmarkDotNet 0.15.8, .NET 8.0.30, on
Windows 11 Arm64 (Snapdragon X, 8 cores).

| Method | Mean | Allocated | What it measures |
|---|---:|---:|---|
| `Permute` | 2,015 ns | 0 B | The Feistel permutation: 4 HMAC-SHA256 rounds, ~1.25 passes with cycle-walking |
| `Encode` | 15 ns | 80 B | Base62 to 7 characters (the string) |
| `NextCode` | 2,110 ns | 184 B | The whole path: lock, take an ID, permute, encode, `ShortCode`. Leases come from memory. |

**Reading:** at the 3.5K writes/s peak, generation uses under 1% of one core. The HMAC rounds are nearly all of
the cost; a cheaper round function is not worth weakening the scrambling for.
