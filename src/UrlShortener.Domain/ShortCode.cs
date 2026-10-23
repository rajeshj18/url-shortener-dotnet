using System;
using System.Diagnostics.CodeAnalysis;

namespace UrlShortener.Domain
{
    /// <summary>
    /// The identifier in a short URL (<c>/{code}</c>). It is one of two kinds, told apart by shape alone (ADR-0005):
    /// <b>generated</b> (exactly 7 Base62 characters) or an <b>alias</b> (a valid <see cref="CustomAlias"/>, which can
    /// never have the generated shape). Equality is case-sensitive.
    /// </summary>
    public sealed class ShortCode : IEquatable<ShortCode>
    {
        /// <summary>Length of a generated code.</summary>
        public const int GeneratedLength = 7;

        private ShortCode(string value, bool isGenerated)
        {
            Value = value;
            IsGenerated = isGenerated;
        }

        /// <summary>The code as it appears in the URL.</summary>
        public string Value { get; }

        /// <summary><c>true</c> for a generated code; <c>false</c> for a custom alias.</summary>
        public bool IsGenerated { get; }

        /// <summary>Wraps a code produced by the short-code generator (TSK-07).</summary>
        /// <param name="value">Exactly 7 Base62 characters.</param>
        /// <returns>The generated short code.</returns>
        public static ShortCode FromGenerated(string? value)
        {
            if (!IsGeneratedShape(value))
            {
                throw new DomainValidationException(DomainErrorCodes.CodeFormat, "A generated code must be exactly 7 letters or digits.");
            }

            return new ShortCode(value!, isGenerated: true);
        }

        /// <summary>Wraps a validated custom alias.</summary>
        /// <param name="alias">The alias.</param>
        /// <returns>The alias as a short code.</returns>
        public static ShortCode FromAlias(CustomAlias alias)
        {
            ArgumentNullException.ThrowIfNull(alias);
            return new ShortCode(alias.Value, isGenerated: false);
        }

        /// <summary>
        /// Parses a code from an incoming URL without throwing. This is the redirect path's cheap format check
        /// (LLD §6.2): malformed input is rejected before any database read.
        /// </summary>
        /// <param name="value">The path segment.</param>
        /// <param name="code">The parsed code, or <c>null</c>.</param>
        /// <returns><c>true</c> if <paramref name="value"/> is a generated code or a valid alias.</returns>
        public static bool TryParse(string? value, [NotNullWhen(true)] out ShortCode? code)
        {
            if (IsGeneratedShape(value))
            {
                code = new ShortCode(value!, isGenerated: true);
                return true;
            }

            if (CustomAlias.TryCreate(value, out CustomAlias? alias))
            {
                code = FromAlias(alias);
                return true;
            }

            code = null;
            return false;
        }

        /// <inheritdoc />
        public bool Equals(ShortCode? other)
        {
            return other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as ShortCode);
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

        /// <summary>The single definition of the generated-code shape: exactly 7 Base62 characters.</summary>
        internal static bool IsGeneratedShape(string? value)
        {
            return value != null && value.Length == GeneratedLength && CharacterRules.AllBase62(value);
        }
    }
}
