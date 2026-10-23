using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using UrlShortener.Infrastructure.Persistence.Links;
using Xunit;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>
    /// One real PostgreSQL 16.15 for the whole test collection. It runs the <b>same</b>
    /// <c>docker/postgres/init/01-roles.sh</c> as docker-compose (so the script is tested too), then applies the
    /// EF migrations as the <b>migrator</b> role, the way the migrate job does. Services never migrate themselves.
    /// </summary>
    public sealed class PostgresFixture : IAsyncLifetime
    {
        /// <summary>Database name used by the tests.</summary>
        public const string Database = "links";

        private const string BootstrapUser = "bootstrap";

        private readonly Dictionary<string, string> _passwords = new Dictionary<string, string>
        {
            ["urlshortener_migrator"] = NewPassword(),
            ["urlshortener_api"] = NewPassword(),
            ["urlshortener_redirect"] = NewPassword(),
        };

        private readonly PostgreSqlContainer _container;

        /// <summary>Creates the fixture; the container starts in <see cref="InitializeAsync"/>.</summary>
        public PostgresFixture()
        {
            _container = new PostgreSqlBuilder("postgres:16.15-alpine")
                .WithDatabase(Database)
                .WithUsername(BootstrapUser)
                .WithPassword(NewPassword())
                .WithEnvironment("LINKS_MIGRATOR_PASSWORD", _passwords["urlshortener_migrator"])
                .WithEnvironment("LINKS_API_PASSWORD", _passwords["urlshortener_api"])
                .WithEnvironment("LINKS_REDIRECT_PASSWORD", _passwords["urlshortener_redirect"])
                .WithResourceMapping(RolesScriptPath(), "/docker-entrypoint-initdb.d/")
                .Build();
        }

        /// <summary>Connection string for the migrator role (DDL).</summary>
        public string MigratorConnection => ConnectionFor("urlshortener_migrator");

        /// <summary>Connection string for the Api role (data read/write, no DDL).</summary>
        public string ApiConnection => ConnectionFor("urlshortener_api");

        /// <summary>Connection string for the Redirect role (SELECT on redirect_lookup only).</summary>
        public string RedirectConnection => ConnectionFor("urlshortener_redirect");

        /// <summary>Connection string for the bootstrap superuser. Used only for schema inspection in tests.</summary>
        public string BootstrapConnection => _container.GetConnectionString();

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            await _container.StartAsync();

            DbContextOptions<LinksDbContext> options = new DbContextOptionsBuilder<LinksDbContext>()
                .UseNpgsql(MigratorConnection)
                .Options;
            await using LinksDbContext context = new LinksDbContext(options);
            await context.Database.MigrateAsync();
        }

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            await _container.DisposeAsync();
        }

        private string ConnectionFor(string role)
        {
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
            {
                Username = role,
                Password = _passwords[role],
            };
            return builder.ConnectionString;
        }

        private static string NewPassword()
        {
            return "test-" + Guid.NewGuid().ToString("N");
        }

        private static string RolesScriptPath()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UrlShortener.sln")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new InvalidOperationException("Repository root (UrlShortener.sln) not found.");
            }

            return Path.Combine(directory.FullName, "docker", "postgres", "init", "01-roles.sh");
        }
    }

    /// <summary>All tests that need the database share one container.</summary>
    [CollectionDefinition(PostgresCollectionDefinition.Name)]
    public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
    {
        /// <summary>Collection name used in <c>[Collection(...)]</c>.</summary>
        public const string Name = "postgres";
    }
}
