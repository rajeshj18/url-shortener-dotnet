using System.Threading;
using System.Threading.Tasks;
using UrlShortener.Domain;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// Write side of the <c>links</c> store, plus the owner's reads. Always uses the <b>primary</b> (ADR-0002, LLD §4.3).
    /// Used by the Api service, which owns the schema (ADR-0009).
    /// </summary>
    public interface IShortUrlRepository
    {
        /// <summary>
        /// Inserts a new link. Returns <c>false</c>, without throwing, if the code already exists. That is how
        /// alias clashes surface (FR-02 → 409), and codes are never reused (A-25).
        /// </summary>
        /// <param name="shortUrl">The new link.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><c>true</c> if inserted; <c>false</c> if the code is taken.</returns>
        Task<bool> TryInsertAsync(ShortUrl shortUrl, CancellationToken cancellationToken);

        /// <summary>Loads a link with all its fields (owner, timestamps) from the primary.</summary>
        /// <param name="code">The short code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The link, or <c>null</c> if no link has this code.</returns>
        Task<ShortUrl?> FindByCodeAsync(ShortCode code, CancellationToken cancellationToken);

        /// <summary>
        /// Persists a soft delete. Idempotent: a link that's already deleted keeps its first deletion time.
        /// </summary>
        /// <param name="shortUrl">A link on which <see cref="ShortUrl.MarkDeleted"/> was called.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the change is stored.</returns>
        Task SaveDeletionAsync(ShortUrl shortUrl, CancellationToken cancellationToken);
    }
}
