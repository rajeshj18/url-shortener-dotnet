namespace UrlShortener.Application.ShortUrls
{
    /// <summary>The <c>Idempotency-Key</c> format of ADR-0010: 1–128 printable ASCII characters (a UUID is recommended).</summary>
    public static class IdempotencyKeyFormat
    {
        /// <summary>Maximum length (the <c>idem_key</c> column is <c>varchar(128)</c>).</summary>
        public const int MaxLength = 128;

        /// <summary>Checks the format.</summary>
        /// <param name="key">The header value.</param>
        /// <returns><c>true</c> if valid.</returns>
        public static bool IsValid(string? key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxLength)
            {
                return false;
            }

            foreach (char c in key)
            {
                if (c < ' ' || c > '~')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
