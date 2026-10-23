using System;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.ShortCodes
{
    /// <summary>
    /// Fixed-width Base62 for generated codes: every value in <c>[0, 62⁷)</c> becomes exactly 7 characters of
    /// <c>0-9A-Za-z</c> (left-padded with <c>0</c>), and back. The alphabet order matches the domain's Base62 rule.
    /// </summary>
    public static class Base62Encoder
    {
        /// <summary>Size of the 7-character code space: 62⁷ = 3,521,614,606,208.</summary>
        public const long CodeSpaceSize = 3_521_614_606_208L;

        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        /// <summary>Encodes a value as exactly 7 Base62 characters.</summary>
        /// <param name="value">A value in <c>[0, 62⁷)</c>.</param>
        /// <returns>The 7-character code.</returns>
        public static string Encode(long value)
        {
            if (value < 0 || value >= CodeSpaceSize)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The value is outside the 7-character code space.");
            }

            char[] chars = new char[ShortCode.GeneratedLength];
            for (int i = chars.Length - 1; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(value % 62)];
                value /= 62;
            }

            return new string(chars);
        }

        /// <summary>Decodes exactly 7 Base62 characters.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The value in <c>[0, 62⁷)</c>.</returns>
        public static long Decode(string code)
        {
            ArgumentNullException.ThrowIfNull(code);
            if (code.Length != ShortCode.GeneratedLength)
            {
                throw new ArgumentException("A generated code has exactly 7 characters.", nameof(code));
            }

            long value = 0;
            foreach (char c in code)
            {
                int digit = Alphabet.IndexOf(c, StringComparison.Ordinal);
                if (digit < 0)
                {
                    throw new ArgumentException("A generated code contains only 0-9, A-Z and a-z.", nameof(code));
                }

                value = (value * 62) + digit;
            }

            return value;
        }
    }
}
