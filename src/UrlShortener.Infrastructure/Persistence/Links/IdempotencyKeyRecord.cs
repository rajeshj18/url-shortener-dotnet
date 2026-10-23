using System;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// An idempotency key (ADR-0010, LLD §6.1). The table is created here so the schema is complete (TSK-06);
    /// the store that uses it is implemented with the create endpoint (TSK-08).
    /// </summary>
    public sealed class IdempotencyKeyRecord
    {
        /// <summary>Owner of the API key; keys are scoped per owner.</summary>
        public string OwnerId { get; set; } = string.Empty;

        /// <summary>The <c>Idempotency-Key</c> header value.</summary>
        public string IdemKey { get; set; } = string.Empty;

        /// <summary>SHA-256 (hex) of the normalized request.</summary>
        public string RequestHash { get; set; } = string.Empty;

        /// <summary>The stored 201 response body (JSON). Set in the same transaction, so never null once committed.</summary>
        public string? ResponseBody { get; set; }

        /// <summary>Start of the 24-hour window.</summary>
        public DateTimeOffset CreatedAt { get; set; }
    }
}
