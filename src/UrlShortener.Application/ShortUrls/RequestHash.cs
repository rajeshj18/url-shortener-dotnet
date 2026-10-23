using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UrlShortener.Domain;

namespace UrlShortener.Application.ShortUrls
{
    /// <summary>
    /// SHA-256 of the <b>normalized</b> create request (ADR-0010, LLD §6.1): the text
    /// <c>longUrl=&lt;as sent&gt;\ncustomAlias=&lt;or empty&gt;\nexpiresAt=&lt;UTC ISO-8601 to the second, or empty&gt;</c>.
    /// JSON whitespace, field order and time-zone notation therefore don't change it. Never change this format:
    /// stored keys would stop matching their retries.
    /// </summary>
    public static class RequestHash
    {
        /// <summary>Computes the hash.</summary>
        /// <param name="longUrl">The validated long URL; its text exactly as sent is hashed.</param>
        /// <param name="customAlias">The alias, or <c>null</c>.</param>
        /// <param name="expiresAt">The expiry, or <c>null</c> (always <c>null</c> until TSK-14).</param>
        /// <returns>64 uppercase hex characters.</returns>
        public static string Compute(LongUrl longUrl, string? customAlias, DateTimeOffset? expiresAt)
        {
            ArgumentNullException.ThrowIfNull(longUrl);

            string expires = expiresAt.HasValue
                ? expiresAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : string.Empty;
            string normalized = "longUrl=" + longUrl.Value + "\ncustomAlias=" + (customAlias ?? string.Empty) + "\nexpiresAt=" + expires;

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hash);
        }
    }
}
