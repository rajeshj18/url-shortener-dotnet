using System;

namespace UrlShortener.Core.Database
{
    /// <summary>
    /// <c>COMMIT</c> failed, so the transaction may or may not have committed (LLD §6.7). A create may be retried only
    /// with an <c>Idempotency-Key</c>: the retry then replays the committed key, or creates the link now.
    /// </summary>
    public sealed class CommitOutcomeUnknownException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public CommitOutcomeUnknownException()
            : base("The commit failed; whether it took effect is unknown.")
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">The message.</param>
        public CommitOutcomeUnknownException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and the driver's failure.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The failure.</param>
        public CommitOutcomeUnknownException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// A stored record breaks a domain rule. That is the server's fault, not the client's: a logged <c>500</c>, never the
    /// <c>400</c> a domain validation error would give (TSK-QW #7). The message names the record and the rule, never values.
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
