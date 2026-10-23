using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// EF Core implementation of <see cref="IShortUrlRepository"/> on the <b>primary</b> (connection
    /// <c>LinksPrimary</c>, user <c>urlshortener_api</c>).
    /// </summary>
    public sealed class EfShortUrlRepository : IShortUrlRepository
    {
        /// <summary>PostgreSQL SQLSTATE for a unique-constraint violation.</summary>
        private const string UniqueViolation = "23505";

        private readonly LinksDbContext _db;

        /// <summary>Creates the repository.</summary>
        /// <param name="db">The scoped <c>links</c> context.</param>
        public EfShortUrlRepository(LinksDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <inheritdoc />
        public async Task<bool> TryInsertAsync(ShortUrl shortUrl, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(shortUrl);

            ShortUrlRecord record = ShortUrlRecordMapper.ToRecord(shortUrl);
            _db.ShortUrls.Add(record);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // The code is taken. Detach so the context can be reused in this scope.
                _db.Entry(record).State = EntityState.Detached;
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<ShortUrl?> FindByCodeAsync(ShortCode code, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(code);

            ShortUrlRecord? record = await _db.ShortUrls
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Code == code.Value, cancellationToken);
            return record == null ? null : ShortUrlRecordMapper.ToDomain(record);
        }

        /// <inheritdoc />
        public async Task SaveDeletionAsync(ShortUrl shortUrl, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(shortUrl);
            if (!shortUrl.DeletedAt.HasValue)
            {
                throw new InvalidOperationException("Call MarkDeleted before saving a deletion.");
            }

            string code = shortUrl.Code.Value;
            DateTimeOffset deletedAt = shortUrl.DeletedAt.Value;

            // "AND deleted_at IS NULL" keeps the first deletion time, so a repeated delete changes nothing (A-25).
            await _db.ShortUrls
                .Where(x => x.Code == code && x.DeletedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DeletedAt, deletedAt), cancellationToken);
        }

        private static bool IsUniqueViolation(DbUpdateException exception)
        {
            PostgresException? postgres = exception.InnerException as PostgresException;
            return postgres != null && postgres.SqlState == UniqueViolation;
        }
    }
}
