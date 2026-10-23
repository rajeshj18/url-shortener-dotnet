using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;
using UrlShortener.Infrastructure.ShortCodes;
using Xunit;

namespace UrlShortener.UnitTests.Infrastructure.ShortCodes
{
    /// <summary>TSK-07 AC4: unique codes under concurrency, pre-fetch at 50%, no stall between leases.</summary>
    public sealed class ShortCodeGeneratorTests
    {
        private static readonly FeistelPermutation Permutation =
            FeistelPermutation.FromPassphrase("unit-test-feistel-passphrase-0123456789");

        [Fact]
        public async Task Concurrent_callers_get_only_unique_codes()
        {
            InMemoryLeaser leaser = new InMemoryLeaser();
            using ShortCodeGenerator generator = new ShortCodeGenerator(leaser, Permutation, 1_000);
            ConcurrentBag<string> codes = new ConcurrentBag<string>();

            Task[] workers = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 2_000; i++)
                {
                    ShortCode code = await generator.NextAsync(CancellationToken.None);
                    codes.Add(code.Value);
                }
            })).ToArray();
            await Task.WhenAll(workers);

            codes.Should().HaveCount(100_000);
            codes.Distinct(StringComparer.Ordinal).Should().HaveCount(100_000);
            codes.Should().OnlyContain(code => ShortCode.FromGenerated(code).IsGenerated);
        }

        [Fact]
        public async Task Codes_are_the_permuted_leased_ids()
        {
            InMemoryLeaser leaser = new InMemoryLeaser(firstId: 5_000);
            using ShortCodeGenerator generator = new ShortCodeGenerator(leaser, Permutation, 100);

            ShortCode code = await generator.NextAsync(CancellationToken.None);

            Permutation.Invert(Base62Encoder.Decode(code.Value)).Should().Be(5_000);
        }

        [Fact]
        public async Task Next_lease_is_prefetched_at_half_use()
        {
            InMemoryLeaser leaser = new InMemoryLeaser();
            using ShortCodeGenerator generator = new ShortCodeGenerator(leaser, Permutation, 100);

            await TakeAsync(generator, 49);
            leaser.Calls.Should().Be(1, "less than half of the lease is used");

            await TakeAsync(generator, 1);
            await leaser.WaitForCallsAsync(2);
            leaser.Calls.Should().Be(2, "the pre-fetch starts when half is used");
        }

        [Fact]
        public async Task Crossing_into_the_prefetched_lease_needs_no_new_lease_call()
        {
            InMemoryLeaser leaser = new InMemoryLeaser();
            using ShortCodeGenerator generator = new ShortCodeGenerator(leaser, Permutation, 100);

            await TakeAsync(generator, 100);
            await leaser.WaitForCallsAsync(2);

            List<ShortCode> next = await TakeAsync(generator, 10);

            leaser.Calls.Should().Be(2, "the switch uses the pre-fetched range");
            Permutation.Invert(Base62Encoder.Decode(next[0].Value)).Should().Be(100, "IDs continue in the second lease");
        }

        [Fact]
        public async Task WarmUp_takes_the_first_lease_once()
        {
            InMemoryLeaser leaser = new InMemoryLeaser();
            using ShortCodeGenerator generator = new ShortCodeGenerator(leaser, Permutation, 100);

            await generator.WarmUpAsync(CancellationToken.None);
            await generator.WarmUpAsync(CancellationToken.None);
            await generator.NextAsync(CancellationToken.None);

            leaser.Calls.Should().Be(1);
        }

        [Fact]
        public async Task A_failed_lease_surfaces_and_the_next_call_retries()
        {
            InMemoryLeaser leaser = new InMemoryLeaser { FailNextCalls = 1 };
            using ShortCodeGenerator generator = new ShortCodeGenerator(leaser, Permutation, 100);

            Func<Task> first = () => generator.NextAsync(CancellationToken.None);
            await first.Should().ThrowAsync<IdSpaceExhaustedException>();

            ShortCode code = await generator.NextAsync(CancellationToken.None);
            code.IsGenerated.Should().BeTrue();
        }

        [Fact]
        public async Task A_failed_prefetch_is_not_shown_to_callers_and_is_retried()
        {
            InMemoryLeaser leaser = new InMemoryLeaser();
            using ShortCodeGenerator generator = new ShortCodeGenerator(leaser, Permutation, 100);
            await generator.WarmUpAsync(CancellationToken.None);

            leaser.FailNextCalls = 1;
            await TakeAsync(generator, 50);
            await leaser.WaitForCallsAsync(2); // the pre-fetch fails in the background

            List<ShortCode> codes = await TakeAsync(generator, 60);

            codes.Should().HaveCount(60);
            leaser.Calls.Should().Be(3, "the refill leases directly after the failed pre-fetch");
        }

        [Fact]
        public void Invalid_arguments_throw()
        {
            Action noLeaser = () => _ = new ShortCodeGenerator(null!, Permutation, 100);
            Action noPermutation = () => _ = new ShortCodeGenerator(new InMemoryLeaser(), null!, 100);
            Action tinyLease = () => _ = new ShortCodeGenerator(new InMemoryLeaser(), Permutation, 1);

            noLeaser.Should().Throw<ArgumentNullException>();
            noPermutation.Should().Throw<ArgumentNullException>();
            tinyLease.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static async Task<List<ShortCode>> TakeAsync(ShortCodeGenerator generator, int count)
        {
            List<ShortCode> codes = new List<ShortCode>();
            for (int i = 0; i < count; i++)
            {
                codes.Add(await generator.NextAsync(CancellationToken.None));
            }

            return codes;
        }

        /// <summary>Hands out consecutive, non-overlapping ranges, like the atomic lease statement.</summary>
        private sealed class InMemoryLeaser : IIdRangeLeaser
        {
            private readonly object _gate = new object();
            private long _next;
            private int _calls;

            public InMemoryLeaser(long firstId = 0)
            {
                _next = firstId;
            }

            public int Calls => Volatile.Read(ref _calls);

            public int FailNextCalls { get; set; }

            public Task<IdRange> LeaseAsync(int size, CancellationToken cancellationToken)
            {
                lock (_gate)
                {
                    _calls++;
                    if (FailNextCalls > 0)
                    {
                        FailNextCalls--;
                        return Task.FromException<IdRange>(new IdSpaceExhaustedException());
                    }

                    long start = _next;
                    _next += size;
                    return Task.FromResult(new IdRange(start, start + size));
                }
            }

            public async Task WaitForCallsAsync(int calls)
            {
                using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (Calls < calls)
                {
                    await Task.Delay(5, timeout.Token);
                }

                await Task.Delay(20); // let the background task's continuation finish
            }
        }
    }
}
