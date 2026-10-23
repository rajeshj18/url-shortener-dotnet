using System;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace UrlShortener.Infrastructure.Resilience
{
    /// <summary>Reads <see cref="ResilienceOptions"/> and applies the database timeouts of LLD §6.7 to connection strings.</summary>
    public static class ResilienceConfiguration
    {
        /// <summary>Binds the <c>Resilience</c> section over the LLD defaults.</summary>
        /// <param name="configuration">Configuration.</param>
        /// <returns>The options.</returns>
        public static ResilienceOptions Read(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ResilienceOptions options = new ResilienceOptions();
            configuration.GetSection(ResilienceOptions.SectionName).Bind(options);
            return options;
        }

        /// <summary>
        /// Adds connect and command timeouts, a pool cap (TSK-11b), and optionally the server-side
        /// <c>statement_timeout</c> (TSK-11 D1) to a connection string. Values already in the connection string are
        /// kept: it is the more specific setting.
        /// </summary>
        /// <param name="connectionString">The configured connection string.</param>
        /// <param name="options">The defaults.</param>
        /// <param name="maxPoolSize">Pool cap for this data source.</param>
        /// <param name="withStatementTimeout"><c>true</c> for the Api's writer connection.</param>
        /// <returns>The connection string with the defaults applied.</returns>
        public static string ApplyConnectionDefaults(string connectionString, ResilienceOptions options, int maxPoolSize, bool withStatementTimeout)
        {
            if (maxPoolSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPoolSize), "A connection pool needs at least 1 connection.");
            }

            ArgumentNullException.ThrowIfNull(connectionString);
            ArgumentNullException.ThrowIfNull(options);

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
}
