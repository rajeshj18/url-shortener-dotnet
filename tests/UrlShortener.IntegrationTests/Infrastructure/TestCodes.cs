using System;
using System.Security.Cryptography;
using UrlShortener.Domain;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>Unique codes per test, so tests sharing one database never collide.</summary>
    public static class TestCodes
    {
        private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        /// <summary>A random generated-shape code (7 Base62 characters).</summary>
        public static ShortCode NewGenerated()
        {
            char[] chars = new char[ShortCode.GeneratedLength];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = Base62[RandomNumberGenerator.GetInt32(Base62.Length)];
            }

            return ShortCode.FromGenerated(new string(chars));
        }

        /// <summary>A random alias (not the generated shape, because of the dash).</summary>
        public static CustomAlias NewAlias()
        {
            return CustomAlias.Create(string.Concat("it-", Guid.NewGuid().ToString("N").AsSpan(0, 12)));
        }
    }
}
