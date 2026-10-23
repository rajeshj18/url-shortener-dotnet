using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// <see cref="IRedirectLookup"/> using plain Npgsql on the <c>redirect_lookup</c> view, as the read-only user
    /// <c>urlshortener_redirect</c>. Refinement R5: the hot path (~35K/s at peak) needs no change tracking.
    /// </summary>
    public sealed class NpgsqlRedirectLookup : IRedirectLookup
    {
        // The only statement the Redirect user can run. It reads the view, never the table (ADR-0009).
        private const string LookupSql = "SELECT long_url, deleted_at, expires_at FROM redirect_lookup WHERE code = $1";

        private readonly LinksReplicaDataSource _replicas;
        private readonly LinksPrimaryDataSource _primary;

        /// <summary>Creates the lookup.</summary>
        /// <param name="replicas">Data source for the normal path.</param>
        /// <param name="primary">Data source for the read-your-writes fallback.</param>
        public NpgsqlRedirectLookup(LinksReplicaDataSource replicas, LinksPrimaryDataSource primary)
        {
            _replicas = replicas ?? throw new ArgumentNullException(nameof(replicas));
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        }

        /// <inheritdoc />
        public Task<RedirectTarget?> FindAsync(ShortCode code, CancellationToken cancellationToken)
        {
            return QueryAsync(_replicas.DataSource, code, cancellationToken);
        }

        /// <inheritdoc />
        public Task<RedirectTarget?> FindOnPrimaryAsync(ShortCode code, CancellationToken cancellationToken)
        {
            return QueryAsync(_primary.DataSource, code, cancellationToken);
        }

        private static async Task<RedirectTarget?> QueryAsync(NpgsqlDataSource dataSource, ShortCode code, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(code);

            await using NpgsqlCommand command = dataSource.CreateCommand(LookupSql);
            command.Parameters.Add(new NpgsqlParameter { Value = code.Value });

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            LongUrl target = LongUrl.Create(reader.GetString(0));
            LinkState state = await reader.IsDBNullAsync(1, cancellationToken) ? LinkState.Active : LinkState.Deleted;
            DateTimeOffset? expiresAt = await reader.IsDBNullAsync(2, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken);
            return new RedirectTarget(code, target, state, expiresAt);
        }
    }
}
