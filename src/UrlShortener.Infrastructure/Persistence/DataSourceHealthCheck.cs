using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace UrlShortener.Infrastructure.Persistence
{
    /// <summary>
    /// Readiness check: runs <c>SELECT 1</c> through a data source. It needs no table privileges, so it works
    /// for every database user, including the read-only Redirect user.
    /// </summary>
    public sealed class DataSourceHealthCheck : IHealthCheck
    {
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>Creates the check.</summary>
        /// <param name="dataSource">The data source to probe.</param>
        public DataSourceHealthCheck(NpgsqlDataSource dataSource)
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
                // The exception goes to the health-check log; the probe response only says "Unhealthy".
                return HealthCheckResult.Unhealthy("Database unreachable.", ex);
            }
        }
    }
}
