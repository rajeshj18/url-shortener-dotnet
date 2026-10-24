using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Core.Codes;
using UrlShortener.Core.Database;
using UrlShortener.Domain;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.ShortCodes
{
    /// <summary>
    /// TSK-07 AC5/AC6 (kept) on real PostgreSQL, as the Api role: concurrent leases never overlap, a full block switches
    /// to the next reserved one (also under a race), a region with no room throws, the startup check refuses wrong
    /// regions, and codes are unique under concurrency. Each test has its own scratch database.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class CodeAllocatorTests
    {
        private const long B001End = 440_201_825_776L;
        private const int Lease = 1_000;

        private static readonly FeistelPermutation Permutation = FeistelPermutation.FromPassphrase(ApiFactory.TestFeistelPassphrase);

        private readonly PostgresFixture _db;

        public CodeAllocatorTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public async Task Concurrent_instances_never_receive_overlapping_ranges()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();

            List<long> starts = await LeaseConcurrentlyAsync(scratch, instances: 8, leasesEach: 25);

            starts.Should().HaveCount(200);
            AssertDisjoint(starts);
            starts.Min().Should().Be(0);
            (starts.Max() + Lease).Should().Be(200_000, "no IDs are skipped while the block has room");
            (await scratch.ScalarAsync("SELECT next_id FROM id_blocks WHERE block_id = 'B-001'")).Should().Be(200_000L);
        }

        [Fact]
        public async Task Full_block_switches_to_the_lowest_reserved_block()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync($@"
UPDATE id_blocks SET next_id = {B001End - 500} WHERE block_id = 'B-001';
INSERT INTO id_blocks VALUES ('B-003', 0, {B001End + 200_000}, {B001End + 300_000}, {B001End + 200_000}, 'reserved');
INSERT INTO id_blocks VALUES ('B-002', 0, {B001End}, {B001End + 100_000}, {B001End}, 'reserved');");
            using PrimaryDatabase source = scratch.CreateApiDataSource();
            using CodeAllocator allocator = NewAllocator(source, 0);

            long start = await allocator.LeaseAsync(CancellationToken.None);

            start.Should().Be(B001End, "the first lease of B-002; the last 500 IDs of B-001 are skipped");
            (await StatusAsync(scratch, "B-001")).Should().Be("exhausted");
            (await StatusAsync(scratch, "B-002")).Should().Be("active");
            (await StatusAsync(scratch, "B-003")).Should().Be("reserved");
        }

        [Fact]
        public async Task Racing_instances_switch_blocks_once_and_stay_disjoint()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync($@"
UPDATE id_blocks SET next_id = {B001End - 2_500} WHERE block_id = 'B-001';
INSERT INTO id_blocks VALUES ('B-002', 0, {B001End}, {B001End + 1_000_000}, {B001End}, 'reserved');
INSERT INTO id_blocks VALUES ('B-003', 0, {B001End + 1_000_000}, {B001End + 2_000_000}, {B001End + 1_000_000}, 'reserved');");

            List<long> starts = await LeaseConcurrentlyAsync(scratch, instances: 6, leasesEach: 10);

            AssertDisjoint(starts);
            starts.Count(s => s < B001End).Should().Be(2, "B-001 had room for exactly 2 more leases");
            starts.Should().OnlyContain(s => s + Lease <= B001End || (s >= B001End && s + Lease <= B001End + 1_000_000));
            (await StatusAsync(scratch, "B-003")).Should().Be("reserved", "only one switch happened");
        }

        [Fact]
        public async Task Region_without_room_throws_and_leaves_the_block_unchanged()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync($"UPDATE id_blocks SET next_id = {B001End - 10} WHERE block_id = 'B-001'");
            using PrimaryDatabase source = scratch.CreateApiDataSource();
            using CodeAllocator allocator = NewAllocator(source, 0);

            Func<Task> act = () => allocator.NextAsync(CancellationToken.None);

            await act.Should().ThrowAsync<IdSpaceExhaustedException>();
            (await StatusAsync(scratch, "B-001")).Should().Be("active", "the switch is rolled back when there is nothing to switch to");
        }

        [Fact]
        public async Task Concurrent_callers_get_unique_codes_that_are_the_permuted_ids()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            using PrimaryDatabase source = scratch.CreateApiDataSource();
            using CodeAllocator allocator = NewAllocator(source, 0);

            ShortCode[] codes = await Task.WhenAll(Enumerable.Range(0, 2_500).Select(_ => Task.Run(() => allocator.NextAsync(CancellationToken.None))));

            codes.Select(c => c.Value).Distinct().Should().HaveCount(2_500, "IDs are unique and the permutation is a bijection");
            codes.Select(c => Permutation.Invert(Base62Encoder.Decode(c.Value))).Should().BeEquivalentTo(Enumerable.Range(0, 2_500).Select(i => (long)i));
            ((long)(await scratch.ScalarAsync("SELECT next_id FROM id_blocks WHERE block_id = 'B-001'"))!).Should().BeLessThanOrEqualTo(4 * Lease, "leases come from memory; at most the next one is prefetched");
        }

        [Fact]
        public async Task Startup_check_passes_with_B001_and_takes_the_first_lease()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            using PrimaryDatabase source = scratch.CreateApiDataSource();
            using CodeAllocator allocator = NewAllocator(source, 0);

            await allocator.StartAsync(CancellationToken.None);
            await allocator.StopAsync(CancellationToken.None);

            (await scratch.ScalarAsync("SELECT next_id FROM id_blocks WHERE block_id = 'B-001'")).Should().Be((long)Lease);
        }

        [Theory]
        [InlineData("UPDATE id_blocks SET status = 'exhausted' WHERE block_id = 'B-001'", 0, "*no active ID block for region 0*")]
        [InlineData("INSERT INTO id_blocks VALUES ('B-002', 1, 440201825776, 880403651552, 440201825776, 'active')", 0, "*another region (B-002)*")]
        [InlineData("SELECT 1", 1, "*another region (B-001)*")]
        public async Task Startup_check_refuses_a_wrong_or_missing_block(string setup, short region, string message)
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync(setup);
            using PrimaryDatabase source = scratch.CreateApiDataSource();
            using CodeAllocator allocator = NewAllocator(source, region);

            Func<Task> act = () => allocator.StartAsync(CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        }

        [Fact]
        public async Task The_Api_refuses_to_start_when_the_check_fails()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync("UPDATE id_blocks SET status = 'exhausted' WHERE block_id = 'B-001'");
            using ApiFactory factory = new ApiFactory(scratch.ApiConnection);

            Action act = () =>
            {
                using HttpClient client = factory.CreateClient();
            };

            act.Should().Throw<InvalidOperationException>().WithMessage("*no active ID block*");
        }

        private static CodeAllocator NewAllocator(PrimaryDatabase source, short region)
        {
            return new CodeAllocator(source, Permutation, region, Lease, NullLogger<CodeAllocator>.Instance);
        }

        private static async Task<List<long>> LeaseConcurrentlyAsync(ScratchDatabase scratch, int instances, int leasesEach)
        {
            List<PrimaryDatabase> sources = Enumerable.Range(0, instances).Select(_ => scratch.CreateApiDataSource()).ToList();
            List<CodeAllocator> allocators = sources.Select(source => NewAllocator(source, 0)).ToList();
            try
            {
                using Barrier start = new Barrier(instances);
                List<long>[] results = await Task.WhenAll(allocators.Select(allocator => Task.Run(async () =>
                {
                    List<long> leased = new List<long>();
                    start.SignalAndWait();
                    for (int i = 0; i < leasesEach; i++)
                    {
                        leased.Add(await allocator.LeaseAsync(CancellationToken.None));
                    }

                    return leased;
                })));
                return results.SelectMany(r => r).ToList();
            }
            finally
            {
                allocators.ForEach(a => a.Dispose());
                sources.ForEach(s => s.Dispose());
            }
        }

        private static void AssertDisjoint(List<long> starts)
        {
            List<long> sorted = starts.OrderBy(s => s).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                sorted[i].Should().BeGreaterThanOrEqualTo(sorted[i - 1] + Lease, "ranges must never overlap");
            }
        }

        private static async Task<string?> StatusAsync(ScratchDatabase scratch, string blockId)
        {
            return (string?)await scratch.ScalarAsync($"SELECT status FROM id_blocks WHERE block_id = '{blockId}'");
        }
    }
}
