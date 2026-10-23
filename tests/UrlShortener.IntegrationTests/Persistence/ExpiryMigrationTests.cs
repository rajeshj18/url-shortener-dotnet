using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using UrlShortener.Infrastructure.Persistence.Links;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Persistence
{
    /// <summary>
    /// TSK-14 AC4: AddLinkExpiry is expand-only and reversible. On a scratch database: roll it back (the view loses
    /// the column but keeps its grant, so the Redirect role still reads it), then apply it again.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class ExpiryMigrationTests
    {
        private readonly PostgresFixture _db;

        public ExpiryMigrationTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public async Task Migration_rolls_back_and_forward_and_the_redirect_role_keeps_its_view()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            DbContextOptions<LinksDbContext> options = new DbContextOptionsBuilder<LinksDbContext>().UseNpgsql(scratch.MigratorConnection).Options;
            await using LinksDbContext context = new LinksDbContext(options);
            IMigrator migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync("20261023045509_SeedBlockB001");
            long viewColumnsAfterDown = await CountViewColumnsAsync(scratch);
            await RedirectRoleCanReadViewAsync(scratch, "SELECT code, long_url, deleted_at FROM redirect_lookup LIMIT 1");

            await migrator.MigrateAsync();
            long viewColumnsAfterUp = await CountViewColumnsAsync(scratch);
            await RedirectRoleCanReadViewAsync(scratch, "SELECT code, long_url, deleted_at, expires_at FROM redirect_lookup LIMIT 1");

            viewColumnsAfterDown.Should().Be(3);
            viewColumnsAfterUp.Should().Be(4);
        }

        private static async Task<long> CountViewColumnsAsync(ScratchDatabase scratch)
        {
            return (long)(await scratch.ScalarAsync("SELECT count(*) FROM information_schema.columns WHERE table_name = 'redirect_lookup'"))!;
        }

        private async Task RedirectRoleCanReadViewAsync(ScratchDatabase scratch, string sql)
        {
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(_db.RedirectConnection) { Database = scratch.Name };
            await using NpgsqlConnection connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            Func<Task> read = () => command.ExecuteScalarAsync();
            await read.Should().NotThrowAsync("the Redirect role keeps SELECT on the view across the migration");
        }
    }
}
