using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Infrastructure.Persistence.Links;
using UrlShortener.Infrastructure.ShortCodes;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.ShortCodes
{
    /// <summary>
    /// TSK-07 AC6: the startup check stops the Api when this region has no active block, or when blocks of another
    /// region exist, and passes (taking the first lease) with B-001.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class ShortCodeStartupCheckTests
    {
        private static readonly FeistelPermutation Permutation = FeistelPermutation.FromPassphrase(ApiFactory.TestFeistelPassphrase);

        private readonly PostgresFixture _db;

        public ShortCodeStartupCheckTests(PostgresFixture db)
        {
            _db = db;
        }

        [Fact]
        public async Task Passes_with_B001_and_takes_the_first_lease()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();

            await RunCheckAsync(scratch, region: 0);

            (await scratch.ScalarAsync("SELECT next_id FROM id_blocks WHERE block_id = 'B-001'")).Should().Be(10_000L);
        }

        [Fact]
        public async Task Fails_without_an_active_block_for_the_region()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync("UPDATE id_blocks SET status = 'exhausted' WHERE block_id = 'B-001'");

            Func<Task> act = () => RunCheckAsync(scratch, region: 0);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no active ID block for region 0*");
        }

        [Fact]
        public async Task Fails_when_blocks_of_another_region_exist()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync(
                "INSERT INTO id_blocks VALUES ('B-002', 1, 440201825776, 880403651552, 440201825776, 'active')");

            Func<Task> act = () => RunCheckAsync(scratch, region: 0);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*another region (B-002)*");
        }

        [Fact]
        public async Task Fails_when_the_region_index_is_misconfigured()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();

            Func<Task> act = () => RunCheckAsync(scratch, region: 1);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*another region (B-001)*");
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

        private static async Task RunCheckAsync(ScratchDatabase scratch, short region)
        {
            using LinksPrimaryDataSource source = scratch.CreateApiDataSource();
            using ShortCodeGenerator generator = new ShortCodeGenerator(new PostgresIdRangeLeaser(source, region), Permutation, 10_000);
            ShortCodeStartupCheck check = new ShortCodeStartupCheck(source, generator, region, NullLogger<ShortCodeStartupCheck>.Instance);

            await check.StartAsync(CancellationToken.None);
            await check.StopAsync(CancellationToken.None);
        }
    }
}
