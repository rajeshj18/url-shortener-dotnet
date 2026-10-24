using System;

namespace UrlShortener.Core.Links
{
    /// <summary>
    /// Row of <c>short_urls</c>. The domain's <c>ShortUrl</c> has private constructors and validated value objects, so
    /// EF maps this plain row; <see cref="LinkService"/> converts between the two.
    /// </summary>
    public sealed class ShortUrlRecord
    {
        /// <summary>Primary key: a generated code (7 chars) or a custom alias (up to 30).</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>The destination URL exactly as sent (column <c>long_url</c>).</summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>The owning API key's owner.</summary>
        public string OwnerId { get; set; } = string.Empty;

        /// <summary><c>true</c> for a custom alias.</summary>
        public bool IsCustom { get; set; }

        /// <summary>Creation time (UTC).</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>Soft-delete time (UTC), or <c>null</c>.</summary>
        public DateTimeOffset? DeletedAt { get; set; }

        /// <summary>Expiry, or <c>null</c> (TSK-14).</summary>
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    /// <summary>Row of <c>id_blocks</c>: a region's block of IDs (ADR-0005, LLD §6.4).</summary>
    public sealed class IdBlockRecord
    {
        /// <summary>Block ID from the registry, e.g. <c>B-001</c>.</summary>
        public string BlockId { get; set; } = string.Empty;

        /// <summary>The deployment region that owns the block.</summary>
        public short RegionIndex { get; set; }

        /// <summary>First ID of the block (inclusive).</summary>
        public long RangeStart { get; set; }

        /// <summary>End of the block (exclusive).</summary>
        public long RangeEnd { get; set; }

        /// <summary>The next ID not yet leased.</summary>
        public long NextId { get; set; }

        /// <summary><c>active</c>, <c>reserved</c> or <c>exhausted</c>.</summary>
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>Row of <c>idempotency_keys</c> (ADR-0010).</summary>
    public sealed class IdempotencyKeyRecord
    {
        /// <summary>Owner of the API key; keys are scoped per owner.</summary>
        public string OwnerId { get; set; } = string.Empty;

        /// <summary>The <c>Idempotency-Key</c> header value.</summary>
        public string IdemKey { get; set; } = string.Empty;

        /// <summary>SHA-256 (hex) of the normalized request.</summary>
        public string RequestHash { get; set; } = string.Empty;

        /// <summary>The stored 201 response body (JSON); set in the same transaction, so never null once committed.</summary>
        public string? ResponseBody { get; set; }

        /// <summary>Start of the 24-hour window.</summary>
        public DateTimeOffset CreatedAt { get; set; }
    }
}
