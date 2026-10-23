using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.ShortCodes
{
    /// <summary>
    /// TSK-07 AC8: the blocks seeded by the migrations match <c>config/id-blocks.json</c> exactly. The registry is
    /// the source of truth, so a seed that drifts from it fails here.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class BlockRegistryTests
    {
        private readonly PostgresFixture _db;

        public BlockRegistryTests(PostgresFixture db)
        {
            _db = db;
        }

        [Fact]
        public async Task Seeded_blocks_match_the_registry()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();

            List<string> seeded = await SeededBlocksAsync(scratch);

            seeded.Should().Equal(RegistryBlocks());
        }

        [Fact]
        public async Task Fresh_blocks_start_at_their_first_id_and_only_one_is_active()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();

            (await scratch.ScalarAsync("SELECT count(*) FROM id_blocks WHERE next_id <> range_start")).Should().Be(0L);
            (await scratch.ScalarAsync("SELECT block_id FROM id_blocks WHERE status = 'active'")).Should().Be("B-001");
        }

        private static List<string> RegistryBlocks()
        {
            using JsonDocument registry = JsonDocument.Parse(File.ReadAllText(Path.Join(RepositoryPaths.Root(), "config", "id-blocks.json")));
            return registry.RootElement.GetProperty("blocks").EnumerateArray()
                .Select(b => Describe(
                    b.GetProperty("blockId").GetString(),
                    b.GetProperty("regionIndex").GetInt16(),
                    b.GetProperty("rangeStart").GetInt64(),
                    b.GetProperty("rangeEnd").GetInt64()))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
        }

        private static async Task<List<string>> SeededBlocksAsync(ScratchDatabase scratch)
        {
            List<string> blocks = new List<string>();
            await using NpgsqlConnection connection = new NpgsqlConnection(scratch.BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(
                "SELECT block_id, region_index, range_start, range_end FROM id_blocks ORDER BY block_id", connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                blocks.Add(Describe(reader.GetString(0), reader.GetInt16(1), reader.GetInt64(2), reader.GetInt64(3)));
            }

            return blocks;
        }

        private static string Describe(string? blockId, short region, long start, long end)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{blockId} region={region} [{start}, {end})");
        }
    }
}
