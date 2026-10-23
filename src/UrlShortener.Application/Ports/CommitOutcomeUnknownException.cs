using System;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// <see cref="IUnitOfWorkTransaction.CommitAsync"/> failed, so the transaction may or may not have committed
    /// (LLD §6.7). A create may be retried only if it carries an <c>Idempotency-Key</c>: the retry then either finds
    /// the committed key and replays it, or creates the link now. Without a key, a retry could create a duplicate.
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

        /// <summary>Creates the exception with a message and the commit failure.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The failure from the database driver.</param>
        public CommitOutcomeUnknownException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
