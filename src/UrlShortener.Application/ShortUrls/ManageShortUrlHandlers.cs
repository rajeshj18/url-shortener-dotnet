using System;
using System.Threading;
using System.Threading.Tasks;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;

namespace UrlShortener.Application.ShortUrls
{
    /// <summary>
    /// Finds a link on the primary and checks ownership. "Not found" and "someone else's" are deliberately the same
    /// result, so callers can't probe which codes exist (TSK-10).
    /// </summary>
    public abstract class OwnedShortUrlHandler
    {
        /// <summary>Creates the base.</summary>
        /// <param name="repository">The links store (primary).</param>
        protected OwnedShortUrlHandler(IShortUrlRepository repository)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>The links store.</summary>
        protected IShortUrlRepository Repository { get; }

        /// <summary>Loads the caller's link.</summary>
        /// <param name="ownerId">The authenticated caller.</param>
        /// <param name="code">The unvalidated code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The link, or <c>null</c>.</returns>
        protected async Task<ShortUrl?> FindOwnedAsync(string ownerId, string? code, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ownerId);
            if (!ShortCode.TryParse(code, out ShortCode? parsed))
            {
                return null;
            }

            ShortUrl? shortUrl = await Repository.FindByCodeAsync(parsed, cancellationToken);
            return shortUrl != null && string.Equals(shortUrl.OwnerId, ownerId, StringComparison.Ordinal) ? shortUrl : null;
        }
    }

    /// <summary>The metadata use case (FR-06).</summary>
    public sealed class GetShortUrlHandler : OwnedShortUrlHandler, IGetShortUrlHandler
    {
        private readonly Uri _publicBaseUrl;

        /// <summary>Creates the handler.</summary>
        /// <param name="repository">The links store.</param>
        /// <param name="publicBaseUrl">Base of the short links (ends with <c>/</c>).</param>
        public GetShortUrlHandler(IShortUrlRepository repository, Uri publicBaseUrl)
            : base(repository)
        {
            _publicBaseUrl = publicBaseUrl ?? throw new ArgumentNullException(nameof(publicBaseUrl));
        }

        /// <inheritdoc />
        public async Task<ShortUrlDetails?> GetAsync(string ownerId, string? code, CancellationToken cancellationToken)
        {
            ShortUrl? shortUrl = await FindOwnedAsync(ownerId, code, cancellationToken);
            if (shortUrl == null)
            {
                return null;
            }

            return new ShortUrlDetails(
                shortUrl.Code.Value,
                new Uri(_publicBaseUrl, shortUrl.Code.Value),
                shortUrl.Target.Uri,
                shortUrl.IsCustom,
                shortUrl.CreatedAt,
                shortUrl.DeletedAt,
                shortUrl.ExpiresAt);
        }
    }

    /// <summary>The delete use case (FR-05, LLD §6.3): soft delete; the code is never reused (A-25).</summary>
    public sealed class DeleteShortUrlHandler : OwnedShortUrlHandler, IDeleteShortUrlHandler
    {
        private readonly TimeProvider _time;

        /// <summary>Creates the handler.</summary>
        /// <param name="repository">The links store.</param>
        /// <param name="time">Clock.</param>
        public DeleteShortUrlHandler(IShortUrlRepository repository, TimeProvider time)
            : base(repository)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string ownerId, string? code, CancellationToken cancellationToken)
        {
            ShortUrl? shortUrl = await FindOwnedAsync(ownerId, code, cancellationToken);
            if (shortUrl == null)
            {
                return false;
            }

            // Already deleted: nothing to store; MarkDeleted keeps the first time anyway.
            if (shortUrl.DeletedAt.HasValue)
            {
                return true;
            }

            shortUrl.MarkDeleted(_time.GetUtcNow());
            await Repository.SaveDeletionAsync(shortUrl, cancellationToken);
            return true;
        }
    }
}
