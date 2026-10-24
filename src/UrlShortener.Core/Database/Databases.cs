using System;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace UrlShortener.Core.Database
{
    /// <summary>
    /// The <c>LinksPrimary</c> data source: the writer endpoint (LLD §6.7). A named type, so the container can't mix it
    /// up with the replicas. Owns its pool.
    /// </summary>
    public sealed class PrimaryDatabase : IDisposable
    {
        /// <summary>Takes ownership of the data source.</summary>
        /// <param name="dataSource">The Npgsql data source.</param>
        public PrimaryDatabase(NpgsqlDataSource dataSource)
        {
            DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        /// <summary>The data source.</summary>
        public NpgsqlDataSource DataSource { get; }

        /// <summary>Drops idle connections, so the next call reconnects through the writer endpoint (after a failover).</summary>
        public void ClearPool()
        {
            using NpgsqlConnection connection = DataSource.CreateConnection();
            NpgsqlConnection.ClearPool(connection);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DataSource.Dispose();
        }
    }

    /// <summary>The <c>LinksReplicas</c> data source: the reader endpoint or a host list (LLD §6.7).</summary>
    public sealed class ReplicaDatabase : IDisposable
    {
        /// <summary>Takes ownership of the data source.</summary>
        /// <param name="dataSource">The Npgsql data source.</param>
        public ReplicaDatabase(NpgsqlDataSource dataSource)
        {
            DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        /// <summary>The data source.</summary>
        public NpgsqlDataSource DataSource { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            DataSource.Dispose();
        }
    }

    /// <summary>The <c>Analytics</c> data source: rollups and Kafka offsets (LLD §6.5).</summary>
    public sealed class AnalyticsDatabase : IDisposable
    {
        /// <summary>Takes ownership of the data source.</summary>
        /// <param name="dataSource">The Npgsql data source.</param>
        public AnalyticsDatabase(NpgsqlDataSource dataSource)
        {
            DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        /// <summary>The data source.</summary>
        public NpgsqlDataSource DataSource { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            DataSource.Dispose();
        }
    }

    /// <summary>Connection strings: names, required values and the LLD §6.7 timeouts and pool caps.</summary>
    public static class ConnectionStrings
    {
        /// <summary>Writer endpoint of the <c>links</c> database.</summary>
        public const string LinksPrimary = "LinksPrimary";

        /// <summary>Reader endpoint of the <c>links</c> database.</summary>
        public const string LinksReplicas = "LinksReplicas";

        /// <summary>The analytics store (rollups and offsets).</summary>
        public const string Analytics = "Analytics";

        /// <summary>Reads a connection string and applies the defaults; fails at startup if it's missing.</summary>
        /// <param name="configuration">Configuration.</param>
        /// <param name="name">Connection-string name.</param>
        /// <param name="options">Timeout defaults.</param>
        /// <param name="maxPoolSize">Pool cap for this data source (TSK-11b).</param>
        /// <param name="withStatementTimeout"><c>true</c> for the Api's writer connection (server-side guard).</param>
        /// <returns>The connection string with defaults.</returns>
        public static string Read(IConfiguration configuration, string name, ResilienceOptions options, int maxPoolSize, bool withStatementTimeout)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            string? value = configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Connection string '{name}' is not configured (ConnectionStrings__{name}).");
            }

            return ApplyDefaults(value, options, maxPoolSize, withStatementTimeout);
        }

        /// <summary>
        /// Adds connect and command timeouts, a pool cap and optionally <c>statement_timeout</c>. Values already in the
        /// connection string win: they are the more specific setting.
        /// </summary>
        /// <param name="connectionString">The configured connection string.</param>
        /// <param name="options">The defaults.</param>
        /// <param name="maxPoolSize">Pool cap.</param>
        /// <param name="withStatementTimeout">Whether to add the server-side statement timeout.</param>
        /// <returns>The connection string with defaults.</returns>
        public static string ApplyDefaults(string connectionString, ResilienceOptions options, int maxPoolSize, bool withStatementTimeout)
        {
            ArgumentNullException.ThrowIfNull(connectionString);
            ArgumentNullException.ThrowIfNull(options);
            if (maxPoolSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPoolSize), "A connection pool needs at least 1 connection.");
            }

            // A plain builder holds only the keys that were actually written, unlike NpgsqlConnectionStringBuilder.
            DbConnectionStringBuilder written = new DbConnectionStringBuilder { ConnectionString = connectionString };
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!HasAny(written, "Timeout", "Connect Timeout", "Connection Timeout"))
            {
                builder.Timeout = options.ConnectTimeoutSeconds;
            }

            if (!HasAny(written, "Command Timeout", "CommandTimeout"))
            {
                builder.CommandTimeout = options.CommandTimeoutSeconds;
            }

            if (!HasAny(written, "Max Pool Size", "MaxPoolSize", "Maximum Pool Size"))
            {
                builder.MaxPoolSize = maxPoolSize;
            }

            if (withStatementTimeout && (builder.Options == null || !builder.Options.Contains("statement_timeout", StringComparison.OrdinalIgnoreCase)))
            {
                string statementTimeout = string.Create(CultureInfo.InvariantCulture, $"-c statement_timeout={options.StatementTimeoutMilliseconds}");
                builder.Options = string.IsNullOrWhiteSpace(builder.Options) ? statementTimeout : builder.Options + " " + statementTimeout;
            }

            return builder.ConnectionString;
        }

        private static bool HasAny(DbConnectionStringBuilder written, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (written.ContainsKey(key))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Readiness check: <c>SELECT 1</c> needs no table rights, so it works for every database user.</summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>Creates the check.</summary>
        /// <param name="dataSource">The data source to probe.</param>
        public DatabaseHealthCheck(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        /// <inheritdoc />
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await using NpgsqlCommand command = _dataSource.CreateCommand("SELECT 1");
                await command.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (NpgsqlException ex)
            {
                return HealthCheckResult.Unhealthy("Database unreachable.", ex);
            }
        }
    }
}
