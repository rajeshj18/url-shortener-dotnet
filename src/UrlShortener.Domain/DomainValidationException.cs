using System;

namespace UrlShortener.Domain
{
    /// <summary>
    /// Thrown when input breaks a domain rule. <see cref="Code"/> is stable (see <see cref="DomainErrorCodes"/>).
    /// The message describes the rule, <b>never the rejected input</b>, which may be unsafe to echo or log.
    /// </summary>
    public sealed class DomainValidationException : Exception
    {
        private const string GenericCode = "domain.invalid";

        /// <summary>Creates an exception with the generic code.</summary>
        public DomainValidationException()
            : this(GenericCode, "A domain rule was violated.")
        {
        }

        /// <summary>Creates an exception with the generic code and a message.</summary>
        /// <param name="message">A description of the rule that was broken.</param>
        public DomainValidationException(string message)
            : this(GenericCode, message)
        {
        }

        /// <summary>Creates an exception with the generic code, a message and an inner exception.</summary>
        /// <param name="message">A description of the rule that was broken.</param>
        /// <param name="innerException">The underlying cause.</param>
        public DomainValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = GenericCode;
        }

        /// <summary>Creates an exception for a specific rule.</summary>
        /// <param name="code">A stable code from <see cref="DomainErrorCodes"/>.</param>
        /// <param name="message">A description of the rule that was broken.</param>
        public DomainValidationException(string code, string message)
            : base(message)
        {
            ArgumentException.ThrowIfNullOrEmpty(code);
            Code = code;
        }

        /// <summary>The stable error code, e.g. <c>alias.reserved</c>.</summary>
        public string Code { get; }
    }
}
