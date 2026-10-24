using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using UrlShortener.Core.Links;
using Xunit;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>
    /// One real PostgreSQL 16.15 for the whole test collection. It runs the <b>same</b>
    /// <c>docker/postgres/init/01-roles.sh</c> as docker-compose (so the script is tested too), then applies the
    /// EF migrations as the <b>migrator</b> role, the way the migrate job does. Services never migrate themselves.
    /// </summary>
    public sealed class PostgresFixture : IAsyncLifetime, IDisposable
    {
        /// <summary>Database name used by the tests.</summary>
        public const string Database = "links";

        private const string BootstrapUser = "bootstrap";

        private readonly Dictionary<string, string> _passwords = new Dictionary<string, string>
        {
            ["urlshortener_migrator"] = NewPassword(),
            ["urlshortener_api"] = NewPassword(),
            ["urlshortener_redirect"] = NewPassword(),
            ["urlshortener_worker"] = NewPassword(),
        };

        private readonly PostgreSqlContainer _container;
        private ApiFactory? _sharedApi;

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
                .WithEnvironment("ANALYTICS_WORKER_PASSWORD", _passwords["urlshortener_worker"])
                .WithResourceMapping(RolesScriptPath(), "/docker-entrypoint-initdb.d/")
                .Build();
        }

        /// <summary>Connection string for the migrator role (DDL).</summary>
        public string MigratorConnection => ConnectionFor("urlshortener_migrator");

        /// <summary>Connection string for the Api role (data read/write, no DDL).</summary>
        public string ApiConnection => ConnectionFor("urlshortener_api");

        /// <summary>Connection string for the Redirect role (SELECT on redirect_lookup only).</summary>
        public string RedirectConnection => ConnectionFor("urlshortener_redirect");

        /// <summary>Connection string for the worker role (read/write on the analytics schema only).</summary>
        public string WorkerConnection => ConnectionFor("urlshortener_worker");

        /// <summary>Connection string for the bootstrap superuser. Used only for schema inspection in tests.</summary>
        public string BootstrapConnection => _container.GetConnectionString();

        /// <summary>
        /// One in-memory Api on the shared database, reused by the API tests. Each host owns a data source, and EF Core
        /// builds an internal service provider per data source: more than 20 hosts in one test process trip EF's
        /// <c>ManyServiceProvidersCreatedWarning</c> (production runs one host per process).
        /// </summary>
        public ApiFactory SharedApi => _sharedApi ?? throw new InvalidOperationException("The fixture is not initialized.");

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            await _container.StartAsync();
            await MigrateAsync(MigratorConnection);
            _sharedApi = new ApiFactory(ApiConnection);
        }

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            if (_sharedApi != null)
            {
                await _sharedApi.DisposeAsync();
            }

            await _container.DisposeAsync();
        }

        /// <summary>Freezes the database container (fault injection, TSK-11): connections hang, like a failed node.</summary>
        /// <returns>A task.</returns>
        public Task PauseAsync()
        {
            return _container.PauseAsync();
        }

        /// <summary>Resumes the database container.</summary>
        /// <returns>A task.</returns>
        public Task UnpauseAsync()
        {
            return _container.UnpauseAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _sharedApi?.Dispose();
        }

        /// <summary>
        /// Creates a fresh, fully migrated database in the same container, with the same roles and grants as the main
        /// one. For tests that change <c>id_blocks</c> (block switches, startup-check failures) or remove the database,
        /// so the shared database is never affected. Dispose it to drop it.
        /// </summary>
        /// <returns>The scratch database.</returns>
        public async Task<ScratchDatabase> CreateScratchDatabaseAsync()
        {
            // The name comes from a GUID, so it is a safe identifier; DDL can't take parameters.
            string name = "scratch_" + Guid.NewGuid().ToString("N");
            await ExecuteAsync(BootstrapConnection, $"CREATE DATABASE {name} OWNER urlshortener_migrator");
            await ExecuteAsync(
                ConnectionFor(BootstrapUser, name),
                $@"REVOKE ALL ON DATABASE {name} FROM PUBLIC;
GRANT CONNECT ON DATABASE {name} TO urlshortener_migrator, urlshortener_api, urlshortener_redirect, urlshortener_worker;
GRANT USAGE ON SCHEMA public TO urlshortener_api, urlshortener_redirect;");

            ScratchDatabase scratch = new ScratchDatabase(
                name,
                ConnectionFor("urlshortener_migrator", name),
                ConnectionFor("urlshortener_api", name),
                ConnectionFor("urlshortener_redirect", name),
                ConnectionFor("urlshortener_worker", name),
                ConnectionFor(BootstrapUser, name),
                DropScratchDatabaseAsync);
            await MigrateAsync(scratch.MigratorConnection);
            return scratch;
        }

        private Task DropScratchDatabaseAsync(string name)
        {
            return ExecuteAsync(BootstrapConnection, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
        }

        private static async Task MigrateAsync(string migratorConnection)
        {
            DbContextOptions<LinksDbContext> options = new DbContextOptionsBuilder<LinksDbContext>()
                .UseNpgsql(migratorConnection)
                .Options;
            await using LinksDbContext context = new LinksDbContext(options);
            await context.Database.MigrateAsync();
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private string ConnectionFor(string role, string database = Database)
        {
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
            {
                Database = database,
            };
            if (role != BootstrapUser)
            {
                builder.Username = role;
                builder.Password = _passwords[role];
            }

            return builder.ConnectionString;
        }

        private static string NewPassword()
        {
            return "test-" + Guid.NewGuid().ToString("N");
        }

        private static string RolesScriptPath()
        {
            return Path.Join(RepositoryPaths.Root(), "docker", "postgres", "init", "01-roles.sh");
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
