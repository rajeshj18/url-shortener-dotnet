namespace UrlShortener.Application.ShortUrls
{
    /// <summary>
    /// Stable error codes added by the Application layer, next to <see cref="UrlShortener.Domain.DomainErrorCodes"/>.
    /// Clients may depend on them, so <b>never rename an existing code</b>.
    /// </summary>
    public static class ApplicationErrorCodes
    {
        /// <summary>The long URL points at our own short domain (FR-10, LLD §7).</summary>
        public const string UrlSelfLink = "url.self-link";

        /// <summary><c>expiresAt</c> was sent, but expiry isn't available yet (TSK-14).</summary>
        public const string ExpiryUnsupported = "expiry.unsupported";

        /// <summary>The <c>Idempotency-Key</c> header isn't 1–128 printable ASCII characters.</summary>
        public const string IdempotencyKeyFormat = "idempotency-key.format";

        /// <summary>The idempotency key was already used for a different request (<c>422</c>).</summary>
        public const string IdempotencyKeyReused = "idempotency-key.reused";

        /// <summary>The custom alias is already taken (<c>409</c>).</summary>
        public const string AliasTaken = "alias.taken";
    }
}
