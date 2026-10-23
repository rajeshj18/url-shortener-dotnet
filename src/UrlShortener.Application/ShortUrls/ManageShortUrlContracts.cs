using System;
using System.Threading;
using System.Threading.Tasks;

namespace UrlShortener.Application.ShortUrls
{
    /// <summary>The owner's view of a link (<c>GET /api/v1/urls/{code}</c>, LLD §3). DTO.</summary>
    /// <param name="Code">The short code or alias.</param>
    /// <param name="ShortUrl">The full short link.</param>
    /// <param name="LongUrl">The destination, exactly as created.</param>
    /// <param name="IsCustom"><c>true</c> for an alias.</param>
    /// <param name="CreatedAt">Creation time (UTC).</param>
    /// <param name="DeletedAt">Deletion time, or <c>null</c> while active (TSK-10 D1: shown to the owner).</param>
    public sealed record ShortUrlDetails(string Code, Uri ShortUrl, Uri LongUrl, bool IsCustom, DateTimeOffset CreatedAt, DateTimeOffset? DeletedAt);

    /// <summary>Reads a link for its owner (FR-06, FR-09).</summary>
    public interface IGetShortUrlHandler
    {
        /// <summary>Finds the caller's link.</summary>
        /// <param name="ownerId">The authenticated caller.</param>
        /// <param name="code">The path segment, unvalidated.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The details, or <c>null</c> if the link doesn't exist <b>or belongs to someone else</b> (same answer, no existence leak).</returns>
        Task<ShortUrlDetails?> GetAsync(string ownerId, string? code, CancellationToken cancellationToken);
    }

    /// <summary>Soft-deletes a link for its owner (FR-05, FR-09, LLD §6.3).</summary>
    public interface IDeleteShortUrlHandler
    {
        /// <summary>Deletes the caller's link. Idempotent: deleting again succeeds and keeps the first deletion time.</summary>
        /// <param name="ownerId">The authenticated caller.</param>
        /// <param name="code">The path segment, unvalidated.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns><c>true</c> if the link is (now) deleted; <c>false</c> if it doesn't exist or isn't the caller's.</returns>
        Task<bool> DeleteAsync(string ownerId, string? code, CancellationToken cancellationToken);
    }
}
