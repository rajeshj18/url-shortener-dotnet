using System;
using System.Threading;
using System.Threading.Tasks;

namespace UrlShortener.Application.ShortUrls
{
    /// <summary>A create request after authentication (DTO).</summary>
    /// <param name="OwnerId">The authenticated caller.</param>
    /// <param name="Destination">The long URL exactly as sent, not yet validated (the handler validates it).</param>
    /// <param name="CustomAlias">The requested alias, or <c>null</c> for a generated code.</param>
    /// <param name="ExpiresAt">The requested expiry, or <c>null</c> for a link that never expires (TSK-14).</param>
    /// <param name="IdempotencyKey">The client's <c>Idempotency-Key</c>, already format-checked, or <c>null</c>.</param>
    public sealed record CreateShortUrlCommand(string OwnerId, string? Destination, string? CustomAlias, DateTimeOffset? ExpiresAt, string? IdempotencyKey);

    /// <summary>The <c>201</c> response body (LLD §3). Stored as-is for idempotent replays.</summary>
    /// <param name="Code">The short code or alias.</param>
    /// <param name="ShortUrl">The full short link.</param>
    /// <param name="LongUrl">The destination, exactly as sent.</param>
    /// <param name="CreatedAt">Creation time (UTC).</param>
    /// <param name="ExpiresAt">Expiry (UTC), or <c>null</c> if the link never expires.</param>
    public sealed record CreatedShortUrl(string Code, Uri ShortUrl, Uri LongUrl, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

    /// <summary>How a create ended.</summary>
    public enum CreateShortUrlOutcome
    {
        /// <summary>A new link was created (<c>201</c>).</summary>
        Created,

        /// <summary>A retry of a completed request: the stored response (<c>201</c> + <c>Idempotent-Replayed</c>).</summary>
        Replayed,

        /// <summary>The alias is taken (<c>409</c>).</summary>
        AliasTaken,

        /// <summary>The idempotency key belongs to a different request (<c>422</c>).</summary>
        KeyReused,
    }

    /// <summary>The result of a create. Expected outcomes are values, not exceptions.</summary>
    public sealed class CreateShortUrlResult
    {
        private CreateShortUrlResult(CreateShortUrlOutcome outcome, CreatedShortUrl? body)
        {
            Outcome = outcome;
            Body = body;
        }

        /// <summary>The alias is taken.</summary>
        public static CreateShortUrlResult AliasTaken { get; } = new CreateShortUrlResult(CreateShortUrlOutcome.AliasTaken, null);

        /// <summary>The key was reused for another request.</summary>
        public static CreateShortUrlResult KeyReused { get; } = new CreateShortUrlResult(CreateShortUrlOutcome.KeyReused, null);

        /// <summary>What happened.</summary>
        public CreateShortUrlOutcome Outcome { get; }

        /// <summary>The <c>201</c> body for <see cref="CreateShortUrlOutcome.Created"/> and <see cref="CreateShortUrlOutcome.Replayed"/>.</summary>
        public CreatedShortUrl? Body { get; }

        /// <summary>A new link.</summary>
        /// <param name="body">The response body.</param>
        /// <returns>The result.</returns>
        public static CreateShortUrlResult Created(CreatedShortUrl body)
        {
            ArgumentNullException.ThrowIfNull(body);
            return new CreateShortUrlResult(CreateShortUrlOutcome.Created, body);
        }

        /// <summary>A replayed response.</summary>
        /// <param name="body">The stored response body.</param>
        /// <returns>The result.</returns>
        public static CreateShortUrlResult Replayed(CreatedShortUrl body)
        {
            ArgumentNullException.ThrowIfNull(body);
            return new CreateShortUrlResult(CreateShortUrlOutcome.Replayed, body);
        }
    }

    /// <summary>The create use case (LLD §6.1).</summary>
    public interface ICreateShortUrlHandler
    {
        /// <summary>Creates a short link, or replays a completed request.</summary>
        /// <param name="command">The request.</param>
        /// <param name="cancellationToken">Cancellation token (the request's).</param>
        /// <returns>The outcome. Invalid input throws <see cref="UrlShortener.Domain.DomainValidationException"/>.</returns>
        Task<CreateShortUrlResult> HandleAsync(CreateShortUrlCommand command, CancellationToken cancellationToken);
    }
}
