using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Persistence
{
    /// <summary>TSK-06 AC1: the migration creates exactly the LLD §4.1 schema (without expiry, refinement R1).</summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class SchemaTests
    {
        private static readonly string[] ExpectedRelations = { "short_urls", "id_blocks", "idempotency_keys", "redirect_lookup" };

        private readonly PostgresFixture _db;

        public SchemaTests(PostgresFixture db)
        {
            _db = db;
        }

        [Fact]
        public async Task Migration_creates_tables_and_view()
        {
            List<string> relations = await QueryStrings(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name");

            relations.Should().Contain(ExpectedRelations);
        }

        [Fact]
        public async Task Short_urls_gained_only_the_expiry_column()
        {
            // TSK-14 (expand-only): the TSK-06 columns are unchanged, expires_at is appended.
            List<string> columns = await QueryStrings(
                "SELECT column_name FROM information_schema.columns WHERE table_name = 'short_urls' ORDER BY ordinal_position");

            columns.Should().Equal("code", "long_url", "owner_id", "is_custom", "created_at", "deleted_at", "expires_at");
        }

        [Fact]
        public async Task Redirect_lookup_exposes_only_four_columns_and_runs_as_owner()
        {
            List<string> columns = await QueryStrings(
                "SELECT column_name FROM information_schema.columns WHERE table_name = 'redirect_lookup' ORDER BY ordinal_position");
            List<string> options = await QueryStrings(
                "SELECT coalesce(array_to_string(reloptions, ','), '') FROM pg_class WHERE relname = 'redirect_lookup'");

            columns.Should().Equal("code", "long_url", "deleted_at", "expires_at"); // TSK-14 appended expires_at
            options.Should().ContainSingle().Which.Should().NotContain("security_invoker");
        }

        [Fact]
        public async Task Id_blocks_has_one_active_block_index_and_check_constraints()
        {
            List<string> indexes = await QueryStrings("SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_id_blocks_one_active'");
            List<string> checks = await QueryStrings(
                "SELECT conname FROM pg_constraint WHERE conrelid = 'id_blocks'::regclass AND contype = 'c' ORDER BY conname");

            indexes.Should().ContainSingle().Which.Should().Contain("UNIQUE").And.Contain("WHERE").And.Contain("active");
            checks.Should().Equal("ck_id_blocks_next_within_range", "ck_id_blocks_status");
        }

        private async Task<List<string>> QueryStrings(string sql)
        {
            List<string> values = new List<string>();
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }
    }
}
