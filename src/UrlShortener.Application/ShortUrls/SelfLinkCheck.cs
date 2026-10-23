using System;
using UrlShortener.Domain;

namespace UrlShortener.Application.ShortUrls
{
    /// <summary>
    /// Rejects long URLs that point back at our own short domain (FR-10, LLD §7), which would create redirect
    /// loops. A single check for now; TSK-16 moves URL validation into separate rule classes (TSK-08 D3).
    /// </summary>
    public sealed class SelfLinkCheck
    {
        private readonly string _shortHost;

        /// <summary>Creates the check.</summary>
        /// <param name="publicBaseUrl">Our public short-link base URL (<c>PublicBaseUrl</c>).</param>
        public SelfLinkCheck(Uri publicBaseUrl)
        {
            ArgumentNullException.ThrowIfNull(publicBaseUrl);
            _shortHost = publicBaseUrl.IdnHost;
        }

        /// <summary>Throws <see cref="DomainValidationException"/> if the URL's host is our short domain.</summary>
        /// <param name="longUrl">The validated long URL.</param>
        public void Ensure(LongUrl longUrl)
        {
            ArgumentNullException.ThrowIfNull(longUrl);
            if (string.Equals(longUrl.Uri.IdnHost, _shortHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainValidationException(ApplicationErrorCodes.UrlSelfLink, "A URL must not point to this short-link service.");
            }
        }
    }
}
