using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UrlShortener.Application.Ports;
using UrlShortener.Infrastructure.Persistence.Links;
using UrlShortener.Infrastructure.ShortCodes;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.ShortCodes
{
    /// <summary>
    /// TSK-07 AC5 on a real PostgreSQL, as the Api role: concurrent leases never overlap, a full block switches to
    /// the next reserved one (also under a race), and a region with no room left throws. Each test has its own
    /// scratch database, so the shared one keeps exactly B-001.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class PostgresIdRangeLeaserTests
    {
        private const long B001End = 440_201_825_776L;

        private readonly PostgresFixture _db;

        public PostgresIdRangeLeaserTests(PostgresFixture db)
        {
            _db = db;
        }

        [Fact]
        public async Task Concurrent_instances_never_receive_overlapping_ranges()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();

            // 8 "instances", each with its own data source (connection pool), 25 leases each, all at once.
            List<IdRange> ranges = await LeaseConcurrentlyAsync(scratch, instances: 8, leasesEach: 25, size: 1_000);

            ranges.Should().HaveCount(200);
            AssertDisjoint(ranges);
            ranges.Min(r => r.Start).Should().Be(0);
            ranges.Max(r => r.EndExclusive).Should().Be(200_000, "no IDs are skipped while the block has room");
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
            using LinksPrimaryDataSource source = scratch.CreateApiDataSource();
            PostgresIdRangeLeaser leaser = new PostgresIdRangeLeaser(source, 0);

            IdRange range = await leaser.LeaseAsync(1_000, CancellationToken.None);

            range.Start.Should().Be(B001End, "the first lease of B-002; the last 500 IDs of B-001 are skipped");
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

            List<IdRange> ranges = await LeaseConcurrentlyAsync(scratch, instances: 6, leasesEach: 10, size: 1_000);

            AssertDisjoint(ranges);
            ranges.Count(r => r.Start < B001End).Should().Be(2, "B-001 had room for exactly 2 more leases");
            ranges.Should().OnlyContain(r => r.EndExclusive <= B001End || (r.Start >= B001End && r.EndExclusive <= B001End + 1_000_000));
            (await StatusAsync(scratch, "B-002")).Should().Be("active");
            (await StatusAsync(scratch, "B-003")).Should().Be("reserved", "only one switch happened");
        }

        [Fact]
        public async Task Region_without_room_throws_and_leaves_the_block_unchanged()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync($"UPDATE id_blocks SET next_id = {B001End - 10} WHERE block_id = 'B-001'");
            using LinksPrimaryDataSource source = scratch.CreateApiDataSource();
            PostgresIdRangeLeaser leaser = new PostgresIdRangeLeaser(source, 0);

            Func<Task> act = () => leaser.LeaseAsync(1_000, CancellationToken.None);

            await act.Should().ThrowAsync<IdSpaceExhaustedException>();
            (await StatusAsync(scratch, "B-001")).Should().Be("active", "the switch is rolled back when there is nothing to switch to");
        }

        [Fact]
        public async Task A_region_with_no_blocks_throws()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            using LinksPrimaryDataSource source = scratch.CreateApiDataSource();
            PostgresIdRangeLeaser leaser = new PostgresIdRangeLeaser(source, 7);

            Func<Task> act = () => leaser.LeaseAsync(1_000, CancellationToken.None);

            await act.Should().ThrowAsync<IdSpaceExhaustedException>();
        }

        [Fact]
        public async Task Invalid_arguments_throw()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            using LinksPrimaryDataSource source = scratch.CreateApiDataSource();
            PostgresIdRangeLeaser leaser = new PostgresIdRangeLeaser(source, 0);

            Func<Task> zero = () => leaser.LeaseAsync(0, CancellationToken.None);
            Action noSource = () => _ = new PostgresIdRangeLeaser(null!, 0);

            await zero.Should().ThrowAsync<ArgumentOutOfRangeException>();
            noSource.Should().Throw<ArgumentNullException>();
        }

        private static async Task<List<IdRange>> LeaseConcurrentlyAsync(ScratchDatabase scratch, int instances, int leasesEach, int size)
        {
            List<LinksPrimaryDataSource> sources = Enumerable.Range(0, instances).Select(_ => scratch.CreateApiDataSource()).ToList();
            try
            {
                using Barrier start = new Barrier(instances);
                IEnumerable<Task<List<IdRange>>> workers = sources.Select(source => Task.Run(async () =>
                {
                    PostgresIdRangeLeaser leaser = new PostgresIdRangeLeaser(source, 0);
                    List<IdRange> leased = new List<IdRange>();
                    start.SignalAndWait();
                    for (int i = 0; i < leasesEach; i++)
                    {
                        leased.Add(await leaser.LeaseAsync(size, CancellationToken.None));
                    }

                    return leased;
                }));

                List<IdRange>[] results = await Task.WhenAll(workers);
                return results.SelectMany(r => r).ToList();
            }
            finally
            {
                sources.ForEach(s => s.Dispose());
            }
        }

        private static void AssertDisjoint(List<IdRange> ranges)
        {
            List<IdRange> sorted = ranges.OrderBy(r => r.Start).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                sorted[i].Start.Should().BeGreaterThanOrEqualTo(sorted[i - 1].EndExclusive, "ranges must never overlap");
            }
        }

        private static async Task<string?> StatusAsync(ScratchDatabase scratch, string blockId)
        {
            return (string?)await scratch.ScalarAsync($"SELECT status FROM id_blocks WHERE block_id = '{blockId}'");
        }
    }
}
