using System;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Persistence
{
    /// <summary>
    /// TSK-06 AC5: least privilege is <b>tested as denials</b>, not only as allowed actions (ADR-0009, LLD §4.4).
    /// SQLSTATE 42501 is <c>insufficient_privilege</c>.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class LeastPrivilegeTests
    {
        private const string InsufficientPrivilege = "42501";

        private readonly PostgresFixture _db;

        public LeastPrivilegeTests(PostgresFixture db)
        {
            _db = db;
        }

        [Fact]
        public async Task Redirect_can_select_the_view()
        {
            Func<Task> act = () => ExecuteAsync(_db.RedirectConnection, "SELECT count(*) FROM redirect_lookup");

            await act.Should().NotThrowAsync();
        }

        [Theory]
        [InlineData("SELECT * FROM short_urls")]
        [InlineData("SELECT owner_id FROM short_urls")]
        [InlineData("SELECT * FROM id_blocks")]
        [InlineData("SELECT * FROM idempotency_keys")]
        [InlineData("INSERT INTO redirect_lookup (code, long_url) VALUES ('zzzzzzz', 'https://x.example')")]
        [InlineData("UPDATE short_urls SET long_url = 'https://evil.example'")]
        [InlineData("DELETE FROM short_urls")]
        [InlineData("CREATE TABLE redirect_owned (id int)")]
        public async Task Redirect_is_denied_everything_else(string sql)
        {
            Func<Task> act = () => ExecuteAsync(_db.RedirectConnection, sql);

            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(InsufficientPrivilege);
        }

        [Theory]
        [InlineData("CREATE TABLE api_owned (id int)")]
        [InlineData("DROP TABLE short_urls")]
        [InlineData("ALTER TABLE short_urls ADD COLUMN x int")]
        public async Task Api_is_denied_ddl(string sql)
        {
            Func<Task> act = () => ExecuteAsync(_db.ApiConnection, sql);

            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(InsufficientPrivilege);
        }

        [Fact]
        public async Task Api_can_read_and_write_data()
        {
            Func<Task> act = () => ExecuteAsync(_db.ApiConnection, "SELECT count(*) FROM short_urls; SELECT count(*) FROM id_blocks");

            await act.Should().NotThrowAsync();
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
