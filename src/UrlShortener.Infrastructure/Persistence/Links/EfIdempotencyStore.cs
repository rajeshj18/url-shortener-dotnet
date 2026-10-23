using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Application.Ports;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// <see cref="IIdempotencyStore"/> on <c>idempotency_keys</c> (ADR-0010, LLD §6.1), inside the caller's
    /// transaction. The insert uses <c>ON CONFLICT DO NOTHING</c>: a concurrent request with the same key blocks on
    /// the primary key until the first transaction ends, then sees its committed row (or none, if it rolled back).
    /// </summary>
    public sealed class EfIdempotencyStore : IIdempotencyStore
    {
        /// <summary>How long a key is honoured, measured from its creation (ADR-0010, A-23).</summary>
        public static readonly TimeSpan Window = TimeSpan.FromHours(24);

        private readonly LinksDbContext _db;

        /// <summary>Creates the store.</summary>
        /// <param name="db">The scoped <c>links</c> context (same transaction as the repository).</param>
        public EfIdempotencyStore(LinksDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <inheritdoc />
        public async Task<IdempotencyBeginResult> TryBeginAsync(string ownerId, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ownerId);
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(requestHash);

            int inserted = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO idempotency_keys (owner_id, idem_key, request_hash, response_body, created_at) VALUES ({ownerId}, {key}, {requestHash}, NULL, {now}) ON CONFLICT DO NOTHING",
                cancellationToken);
            if (inserted == 1)
            {
                return IdempotencyBeginResult.New;
            }

            // The key exists and its transaction has ended. Lock the row for the rest of ours.
            // ToListAsync doesn't compose around the SQL, so FOR UPDATE stays at the top level.
            List<IdempotencyKeyRecord> rows = await _db.IdempotencyKeys
                .FromSql($"SELECT * FROM idempotency_keys WHERE owner_id = {ownerId} AND idem_key = {key} FOR UPDATE")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            IdempotencyKeyRecord stored = rows.Single();

            if (stored.CreatedAt < now - Window)
            {
                // Expired: treat as absent and take the key over for this request.
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE idempotency_keys SET request_hash = {requestHash}, response_body = NULL, created_at = {now} WHERE owner_id = {ownerId} AND idem_key = {key}",
                    cancellationToken);
                return IdempotencyBeginResult.New;
            }

            if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return IdempotencyBeginResult.Mismatch;
            }

            if (stored.ResponseBody == null)
            {
                // Can't happen: the body is written in the same transaction that inserted the row.
                throw new InvalidOperationException("A committed idempotency key has no stored response.");
            }

            return IdempotencyBeginResult.Replay(stored.ResponseBody);
        }

        /// <inheritdoc />
        public async Task CompleteAsync(string ownerId, string key, string responseBody, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ownerId);
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(responseBody);

            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE idempotency_keys SET response_body = CAST({responseBody} AS jsonb) WHERE owner_id = {ownerId} AND idem_key = {key}",
                cancellationToken);
        }
    }
}
