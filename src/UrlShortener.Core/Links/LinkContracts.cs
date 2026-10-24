using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UrlShortener.Domain;

namespace UrlShortener.Core.Links
{
    /// <summary>A create request after authentication (DTO).</summary>
    /// <param name="OwnerId">The authenticated caller.</param>
    /// <param name="Destination">The long URL exactly as sent, not yet validated.</param>
    /// <param name="CustomAlias">The requested alias, or <c>null</c> for a generated code.</param>
    /// <param name="ExpiresAt">The requested expiry, or <c>null</c> for a link that never expires.</param>
    /// <param name="IdempotencyKey">The client's <c>Idempotency-Key</c>, already format-checked, or <c>null</c>.</param>
    public sealed record CreateLinkCommand(string OwnerId, string? Destination, string? CustomAlias, DateTimeOffset? ExpiresAt, string? IdempotencyKey);

    /// <summary>The <c>201</c> response body (LLD §3). Stored as-is for idempotent replays.</summary>
    /// <param name="Code">The short code or alias.</param>
    /// <param name="ShortUrl">The full short link.</param>
    /// <param name="LongUrl">The destination, exactly as sent.</param>
    /// <param name="CreatedAt">Creation time (UTC).</param>
    /// <param name="ExpiresAt">Expiry (UTC), or <c>null</c>.</param>
    public sealed record CreatedShortUrl(string Code, Uri ShortUrl, Uri LongUrl, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

    /// <summary>The owner's view of a link (<c>GET /api/v1/urls/{code}</c>).</summary>
    /// <param name="Code">The short code or alias.</param>
    /// <param name="ShortUrl">The full short link.</param>
    /// <param name="LongUrl">The destination, exactly as created.</param>
    /// <param name="IsCustom"><c>true</c> for an alias.</param>
    /// <param name="CreatedAt">Creation time (UTC).</param>
    /// <param name="DeletedAt">Deletion time, or <c>null</c> while active.</param>
    /// <param name="ExpiresAt">Expiry, or <c>null</c>.</param>
    public sealed record ShortUrlDetails(string Code, Uri ShortUrl, Uri LongUrl, bool IsCustom, DateTimeOffset CreatedAt, DateTimeOffset? DeletedAt, DateTimeOffset? ExpiresAt);

    /// <summary>How a create ended. Expected outcomes are values, not exceptions.</summary>
    public enum CreateOutcome
    {
        /// <summary>A new link (<c>201</c>).</summary>
        Created,

        /// <summary>A retry of a completed request (<c>201</c> + <c>Idempotent-Replayed</c>).</summary>
        Replayed,

        /// <summary>The alias is taken (<c>409</c>).</summary>
        AliasTaken,

        /// <summary>The idempotency key belongs to a different request (<c>422</c>).</summary>
        KeyReused,
    }

    /// <summary>The result of a create: the outcome, and the body for <c>201</c>s.</summary>
    /// <param name="Outcome">What happened.</param>
    /// <param name="Body">The <c>201</c> body, or <c>null</c>.</param>
    public sealed record CreateResult(CreateOutcome Outcome, CreatedShortUrl? Body);

    /// <summary>
    /// Stable error codes added next to <see cref="DomainErrorCodes"/>. Clients may depend on them, so <b>never rename one</b>.
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>The long URL points at our own short domain (FR-10).</summary>
        public const string UrlSelfLink = "url.self-link";

        /// <summary>No longer emitted since TSK-14; reserved because codes are never reused.</summary>
        public const string ExpiryUnsupported = "expiry.unsupported";

        /// <summary>The <c>Idempotency-Key</c> header isn't 1–128 printable ASCII characters.</summary>
        public const string IdempotencyKeyFormat = "idempotency-key.format";

        /// <summary>The key was already used for a different request (<c>422</c>).</summary>
        public const string IdempotencyKeyReused = "idempotency-key.reused";

        /// <summary>No such link, or not the caller's (<c>404</c>, the same answer in both cases).</summary>
        public const string LinkNotFound = "link.not-found";

        /// <summary>The custom alias is already taken (<c>409</c>).</summary>
        public const string AliasTaken = "alias.taken";

        /// <summary>The stats range is invalid (from after to, or longer than 90 days).</summary>
        public const string StatsRange = "stats.range";
    }

    /// <summary>The idempotency rules of ADR-0010: the key format and the normalized request hash.</summary>
    public static class IdempotencyRules
    {
        /// <summary>Maximum key length, part of the API contract (the column is sized to match).</summary>
        public const int MaxKeyLength = 128;

        /// <summary>How long a key is honoured, from its creation (ADR-0010, A-23).</summary>
        public static readonly TimeSpan Window = TimeSpan.FromHours(24);

        /// <summary>Checks the key format: 1–128 printable ASCII characters.</summary>
        /// <param name="key">The header value.</param>
        /// <returns><c>true</c> if valid.</returns>
        public static bool IsValidKey(string? key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
            {
                return false;
            }

            foreach (char c in key)
            {
                if (c < ' ' || c > '~')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// SHA-256 of <c>longUrl=…\ncustomAlias=…\nexpiresAt=…</c> (UTC, to the second). JSON whitespace, field order and
        /// time-zone notation don't change it. Never change this format: stored keys would stop matching their retries.
        /// </summary>
        /// <param name="longUrl">The validated long URL; its text exactly as sent is hashed.</param>
        /// <param name="customAlias">The alias, or <c>null</c>.</param>
        /// <param name="expiresAt">The expiry, or <c>null</c>.</param>
        /// <returns>64 uppercase hex characters.</returns>
        public static string RequestHash(LongUrl longUrl, string? customAlias, DateTimeOffset? expiresAt)
        {
            ArgumentNullException.ThrowIfNull(longUrl);
            string expires = expiresAt.HasValue
                ? expiresAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : string.Empty;
            string normalized = "longUrl=" + longUrl.Value + "\ncustomAlias=" + (customAlias ?? string.Empty) + "\nexpiresAt=" + expires;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        }
    }
}
