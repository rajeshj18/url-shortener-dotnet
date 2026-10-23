using System;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// Converts between the domain's <see cref="ShortUrl"/> and its persistence model. Loading goes through the
    /// domain's validated factories, so a record that breaks a domain rule can't be turned into a domain object; it
    /// becomes a <see cref="StoredDataInvalidException"/> (a <c>500</c>), never a client error (TSK-QW #7).
    /// </summary>
    public static class ShortUrlRecordMapper
    {
        /// <summary>Domain → record.</summary>
        /// <param name="shortUrl">The domain link.</param>
        /// <returns>A new record.</returns>
        public static ShortUrlRecord ToRecord(ShortUrl shortUrl)
        {
            ArgumentNullException.ThrowIfNull(shortUrl);

            return new ShortUrlRecord
            {
                Code = shortUrl.Code.Value,
                Target = shortUrl.Target.Value,
                OwnerId = shortUrl.OwnerId,
                IsCustom = shortUrl.IsCustom,
                CreatedAt = shortUrl.CreatedAt,
                DeletedAt = shortUrl.DeletedAt,
                ExpiresAt = shortUrl.ExpiresAt,
            };
        }

        /// <summary>Record → domain.</summary>
        /// <param name="record">The stored record.</param>
        /// <returns>The domain link.</returns>
        public static ShortUrl ToDomain(ShortUrlRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            try
            {
                ShortCode code = record.IsCustom
                    ? ShortCode.FromAlias(CustomAlias.Create(record.Code))
                    : ShortCode.FromGenerated(record.Code);

                return ShortUrl.Rehydrate(code, LongUrl.Create(record.Target), record.OwnerId, record.CreatedAt, record.DeletedAt, record.ExpiresAt);
            }
            catch (DomainValidationException exception)
            {
                throw new StoredDataInvalidException($"Stored link '{record.Code}' breaks the domain rule '{exception.Code}'.", exception);
            }
        }
    }
}
