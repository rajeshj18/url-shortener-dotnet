using BenchmarkDotNet.Attributes;
using UrlShortener.Core.Codes;
using UrlShortener.Domain;

namespace UrlShortener.Benchmarks
{
    /// <summary>
    /// TSK-07 AC9: the CPU cost of one generated code. <c>Permute</c> (4 HMAC-SHA256 rounds, ~1.25 network passes),
    /// <c>Encode</c> (Base62), and <c>GenerateCode</c>, the full path from an ID to a <see cref="ShortCode"/>.
    /// TSK-SIMPLE: the code allocator now leases from the database itself, so the lease (taken from memory almost
    /// always) is no longer part of the measured path.
    /// </summary>
    [MemoryDiagnoser]
    public class ShortCodeBenchmarks
    {
        private readonly FeistelPermutation _permutation =
            FeistelPermutation.FromPassphrase("benchmark-feistel-passphrase-0123456789");

        private long _id;

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

        /// <summary>The whole CPU path for one code: permute, encode, wrap.</summary>
        /// <returns>The code.</returns>
        [Benchmark(Baseline = true)]
        public ShortCode GenerateCode()
        {
            return ShortCode.FromGenerated(Base62Encoder.Encode(_permutation.Permute(_id++ % Base62Encoder.CodeSpaceSize)));
        }
    }
}
