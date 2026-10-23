using System;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// No block of this region has room for another lease. The Api maps it to <c>503</c> (TSK-08). Operationally it
    /// means the 80% alert was missed and the runbook (LLD §6.4) must assign a new block.
    /// </summary>
    public sealed class IdSpaceExhaustedException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public IdSpaceExhaustedException()
            : base("No ID block of this region has room for another lease. Assign a new block (LLD §6.4 runbook).")
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">The message.</param>
        public IdSpaceExhaustedException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and an inner exception.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The cause.</param>
        public IdSpaceExhaustedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
