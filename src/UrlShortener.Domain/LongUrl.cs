using System;

namespace UrlShortener.Domain
{
    /// <summary>
    /// The destination of a short link (FR-10, A-20). It's always valid once created:
    /// an absolute <c>http</c>/<c>https</c> URL with a host, ≤ 2,048 characters, no user-info, and
    /// <b>no whitespace or control characters</b>. A CR/LF reaching the redirect's <c>Location</c> header
    /// would be a header-injection risk. The original string is kept exactly as sent (needed for the
    /// idempotency hash, LLD §6.1).
    /// </summary>
    public sealed class LongUrl : IEquatable<LongUrl>
    {
        /// <summary>Maximum length in characters.</summary>
        public const int MaxLength = 2048;

        private LongUrl(string value, Uri uri)
        {
            Value = value;
            Uri = uri;
        }

        /// <summary>The URL exactly as sent.</summary>
        public string Value { get; }

        /// <summary>The parsed absolute URI.</summary>
        public Uri Uri { get; }

        /// <summary>Creates a long URL, or throws <see cref="DomainValidationException"/> with the first rule broken.</summary>
        /// <param name="value">The URL to shorten.</param>
        /// <returns>The valid URL.</returns>
        public static LongUrl Create(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw Invalid(DomainErrorCodes.UrlRequired, "A URL is required.");
            }

            if (value.Length > MaxLength)
            {
                throw Invalid(DomainErrorCodes.UrlLength, "A URL may be at most 2048 characters long.");
            }

            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    throw Invalid(DomainErrorCodes.UrlCharacters, "A URL must not contain spaces or control characters.");
                }
            }

            // Checked on the raw text, before Uri parsing, so the result is the same on every OS
            // (on Linux, Uri treats "/path" as file:///path).
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(DomainErrorCodes.UrlScheme, "A URL must be absolute and start with http:// or https://.");
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Host))
            {
                throw Invalid(DomainErrorCodes.UrlFormat, "The URL is not a valid absolute address with a host.");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw Invalid(DomainErrorCodes.UrlCredentials, "A URL must not contain a user name or password.");
            }

            return new LongUrl(value, uri);
        }

        /// <inheritdoc />
        public bool Equals(LongUrl? other)
        {
            return other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as LongUrl);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Value;
        }

        private static DomainValidationException Invalid(string code, string message)
        {
            return new DomainValidationException(code, message);
        }
    }
}
