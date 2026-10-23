using System;

namespace UrlShortener.Infrastructure.Persistence
{
    /// <summary>
    /// A stored record breaks a domain rule, so it can't become a domain object. That is the server's fault (data
    /// corruption, a write that bypassed the domain, a missed migration), not the client's: it surfaces as a logged
    /// <c>500</c>, not the <c>400</c> a <see cref="UrlShortener.Domain.DomainValidationException"/> would give
    /// (TSK-QW #7). The message names the record and the rule, never the stored values.
    /// </summary>
    public sealed class StoredDataInvalidException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public StoredDataInvalidException()
            : base("A stored record breaks a domain rule.")
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">The message (no stored values).</param>
        public StoredDataInvalidException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and the domain rule that failed.</summary>
        /// <param name="message">The message (no stored values).</param>
        /// <param name="innerException">The domain validation failure.</param>
        public StoredDataInvalidException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
