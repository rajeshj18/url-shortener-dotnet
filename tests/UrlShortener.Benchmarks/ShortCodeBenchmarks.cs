using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;
using UrlShortener.Infrastructure.ShortCodes;

namespace UrlShortener.Benchmarks
{
    /// <summary>
    /// TSK-07 AC9: the CPU cost of one generated code. <c>Permute</c> (4 HMAC-SHA256 rounds, ~1.25 network passes),
    /// <c>Encode</c> (Base62), and <c>NextCode</c>, the full path from a leased ID to a <see cref="ShortCode"/>
    /// (the lock included; leases come from memory, so the database is not measured).
    /// </summary>
    [MemoryDiagnoser]
    public class ShortCodeBenchmarks : IDisposable
    {
        private readonly FeistelPermutation _permutation =
            FeistelPermutation.FromPassphrase("benchmark-feistel-passphrase-0123456789");

        private ShortCodeGenerator? _generator;
        private long _id;

        /// <summary>Creates a generator with an in-memory leaser.</summary>
        [GlobalSetup]
        public void Setup()
        {
            _generator = new ShortCodeGenerator(new InMemoryLeaser(), _permutation, 1_000_000);
        }

        /// <summary>Disposes the generator.</summary>
        [GlobalCleanup]
        public void Cleanup()
        {
            Dispose();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases the generator. BenchmarkDotNet needs a non-sealed class, hence the full pattern.</summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _generator?.Dispose();
                _generator = null;
            }
        }

        /// <summary>The keyed permutation alone.</summary>
        /// <returns>The permuted value.</returns>
        [Benchmark]
        public long Permute()
        {
            return _permutation.Permute(_id++ % Base62Encoder.CodeSpaceSize);
        }

        /// <summary>Base62 encoding alone.</summary>
        /// <returns>The code text.</returns>
        [Benchmark]
        public string Encode()
        {
            return Base62Encoder.Encode(_id++ % Base62Encoder.CodeSpaceSize);
        }

        /// <summary>The whole generator path for one code.</summary>
        /// <returns>The code.</returns>
        [Benchmark(Baseline = true)]
        public Task<ShortCode> NextCode()
        {
            return _generator!.NextAsync(CancellationToken.None);
        }

        private sealed class InMemoryLeaser : IIdRangeLeaser
        {
            private long _next;

            public Task<IdRange> LeaseAsync(int size, CancellationToken cancellationToken)
            {
                long start = Interlocked.Add(ref _next, size) - size;
                return Task.FromResult(new IdRange(start, start + size));
            }
        }
    }
}
