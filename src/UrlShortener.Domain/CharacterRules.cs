namespace UrlShortener.Domain
{
    /// <summary>
    /// Character classes used by the domain rules. Plain loops instead of regular expressions: linear time,
    /// no backtracking risk, and no culture-dependent behaviour.
    /// </summary>
    internal static class CharacterRules
    {
        /// <summary>The Base62 alphabet of generated codes: <c>0-9A-Za-z</c> (ASCII only).</summary>
        public static bool IsBase62(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        /// <summary>The alias alphabet: Base62 plus <c>_</c> and <c>-</c>.</summary>
        public static bool IsAliasCharacter(char c)
        {
            return IsBase62(c) || c == '_' || c == '-';
        }

        /// <summary>True if every character of <paramref name="value"/> is Base62.</summary>
        public static bool AllBase62(string value)
        {
            foreach (char c in value)
            {
                if (!IsBase62(c))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
