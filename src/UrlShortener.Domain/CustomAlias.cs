using System;
using System.Diagnostics.CodeAnalysis;

namespace UrlShortener.Domain
{
    /// <summary>
    /// A caller-chosen short code (FR-02, A-21). It's always valid once created:
    /// 4–30 characters <c>[A-Za-z0-9_-]</c>, not a reserved path, and <b>not</b> exactly 7 Base62 characters,
    /// because that shape is reserved for generated codes (ADR-0005).
    /// </summary>
    public sealed class CustomAlias : IEquatable<CustomAlias>
    {
        /// <summary>Minimum alias length.</summary>
        public const int MinLength = 4;

        /// <summary>Maximum alias length.</summary>
        public const int MaxLength = 30;

        private static readonly string[] ReservedPaths = { "api", "health", "swagger", "favicon.ico", "robots.txt" };

        private CustomAlias(string value)
        {
            Value = value;
        }

        /// <summary>The alias exactly as given (case is preserved; codes are case-sensitive).</summary>
        public string Value { get; }

        /// <summary>Creates an alias, or throws <see cref="DomainValidationException"/> with the first rule broken.</summary>
        /// <param name="value">The requested alias.</param>
        /// <returns>The valid alias.</returns>
        public static CustomAlias Create(string? value)
        {
            string? errorCode = Validate(value);
            if (errorCode != null)
            {
                throw new DomainValidationException(errorCode, DescribeRule(errorCode));
            }

            return new CustomAlias(value!);
        }

        /// <summary>Creates an alias without throwing.</summary>
        /// <param name="value">The requested alias.</param>
        /// <param name="alias">The valid alias, or <c>null</c>.</param>
        /// <returns><c>true</c> if <paramref name="value"/> is a valid alias.</returns>
        public static bool TryCreate(string? value, [NotNullWhen(true)] out CustomAlias? alias)
        {
            alias = Validate(value) == null ? new CustomAlias(value!) : null;
            return alias != null;
        }

        /// <inheritdoc />
        public bool Equals(CustomAlias? other)
        {
            return other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as CustomAlias);
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

        /// <summary>The single place where alias rules are checked. Returns the error code, or <c>null</c> if valid.</summary>
        internal static string? Validate(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return DomainErrorCodes.AliasRequired;
            }

            if (value.Length < MinLength || value.Length > MaxLength)
            {
                return DomainErrorCodes.AliasLength;
            }

            foreach (char c in value)
            {
                if (!CharacterRules.IsAliasCharacter(c))
                {
                    return DomainErrorCodes.AliasCharacters;
                }
            }

            if (ShortCode.IsGeneratedShape(value))
            {
                return DomainErrorCodes.AliasGeneratedShape;
            }

            foreach (string reserved in ReservedPaths)
            {
                if (string.Equals(value, reserved, StringComparison.OrdinalIgnoreCase))
                {
                    return DomainErrorCodes.AliasReserved;
                }
            }

            return null;
        }

        private static string DescribeRule(string errorCode)
        {
            switch (errorCode)
            {
                case DomainErrorCodes.AliasRequired:
                    return "An alias is required.";
                case DomainErrorCodes.AliasLength:
                    return "An alias must be between 4 and 30 characters long.";
                case DomainErrorCodes.AliasCharacters:
                    return "An alias may contain only letters, digits, '_' and '-'.";
                case DomainErrorCodes.AliasGeneratedShape:
                    return "An alias cannot be exactly 7 letters or digits; that format is reserved for generated codes.";
                default:
                    return "This alias is reserved.";
            }
        }
    }
}
