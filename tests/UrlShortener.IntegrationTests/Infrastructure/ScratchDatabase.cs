using System;
using System.Threading.Tasks;
using Npgsql;
using UrlShortener.Infrastructure.Persistence.Links;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>A migrated database of its own for one test, dropped on dispose (see <see cref="PostgresFixture.CreateScratchDatabaseAsync"/>).</summary>
    public sealed class ScratchDatabase : IAsyncDisposable
    {
        private readonly Func<string, Task> _drop;

        internal ScratchDatabase(string name, string migratorConnection, string apiConnection, string bootstrapConnection, Func<string, Task> drop)
        {
            Name = name;
            MigratorConnection = migratorConnection;
            ApiConnection = apiConnection;
            BootstrapConnection = bootstrapConnection;
            _drop = drop;
        }

        /// <summary>Database name.</summary>
        public string Name { get; }

        /// <summary>Connection string for the migrator role.</summary>
        public string MigratorConnection { get; }

        /// <summary>Connection string for the Api role.</summary>
        public string ApiConnection { get; }

        /// <summary>Connection string for the bootstrap superuser (test setup only).</summary>
        public string BootstrapConnection { get; }

        /// <summary>A primary data source as the Api role, the way the Api connects. The caller disposes it.</summary>
        /// <returns>The data source.</returns>
        public LinksPrimaryDataSource CreateApiDataSource()
        {
            return new LinksPrimaryDataSource(NpgsqlDataSource.Create(ApiConnection));
        }

        /// <summary>Runs setup SQL as the bootstrap user.</summary>
        /// <param name="sql">The SQL.</param>
        /// <returns>A task.</returns>
        public async Task ExecuteAsync(string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Runs a scalar query as the bootstrap user.</summary>
        /// <param name="sql">The SQL.</param>
        /// <returns>The first column of the first row.</returns>
        public async Task<object?> ScalarAsync(string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }

        /// <summary>Drops the database now, even while clients are connected. Safe to call more than once.</summary>
        /// <returns>A task.</returns>
        public async Task DropAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await _drop(Name);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await DropAsync();
        }
    }
}
