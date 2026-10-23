using System;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// EF Core persistence model for <c>short_urls</c>. It's kept separate from the domain's <c>ShortUrl</c>, which
    /// has private constructors and no EF dependencies (ADR-0009); <see cref="ShortUrlRecordMapper"/> converts between them.
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
}
