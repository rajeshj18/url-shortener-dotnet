namespace UrlShortener.Domain
{
    /// <summary>
    /// Stable error codes carried by <see cref="DomainValidationException"/>. The Api maps them to RFC 7807
    /// responses (TSK-08), so <b>never rename an existing code</b>, because clients may depend on it.
    /// </summary>
    public static class DomainErrorCodes
    {
        /// <summary>The alias is missing.</summary>
        public const string AliasRequired = "alias.required";

        /// <summary>The alias is shorter than 4 or longer than 30 characters.</summary>
        public const string AliasLength = "alias.length";

        /// <summary>The alias contains a character outside <c>[A-Za-z0-9_-]</c>.</summary>
        public const string AliasCharacters = "alias.characters";

        /// <summary>The alias has the shape reserved for generated codes (exactly 7 Base62 characters, ADR-0005).</summary>
        public const string AliasGeneratedShape = "alias.generated-shape";

        /// <summary>The alias is a reserved path such as <c>api</c> or <c>health</c>.</summary>
        public const string AliasReserved = "alias.reserved";

        /// <summary>A generated short code doesn't have exactly 7 Base62 characters.</summary>
        public const string CodeFormat = "code.format";

        /// <summary>The operation needs a generated code but received an alias, or the other way round.</summary>
        public const string CodeKind = "code.kind";

        /// <summary>The URL is missing.</summary>
        public const string UrlRequired = "url.required";

        /// <summary>The URL is longer than 2,048 characters.</summary>
        public const string UrlLength = "url.length";

        /// <summary>The URL contains whitespace or control characters (header-injection risk).</summary>
        public const string UrlCharacters = "url.characters";

        /// <summary>The URL doesn't start with <c>http://</c> or <c>https://</c>.</summary>
        public const string UrlScheme = "url.scheme";

        /// <summary>The URL can't be parsed as an absolute URI with a host.</summary>
        public const string UrlFormat = "url.format";

        /// <summary>The URL contains user-info (<c>user:pass@</c>).</summary>
        public const string UrlCredentials = "url.credentials";

        /// <summary>The owner ID is missing or blank.</summary>
        public const string OwnerRequired = "owner.required";

        /// <summary>The owner ID is longer than 64 characters.</summary>
        public const string OwnerLength = "owner.length";

        /// <summary>A timestamp is earlier than one it must follow (e.g. deleted before created).</summary>
        public const string TimeOrder = "time.order";

        /// <summary>The expiry is not after the creation time (TSK-14).</summary>
        public const string ExpiryPast = "expiry.past";

        /// <summary>The expiry is more than 10 years after creation (TSK-14 D1): most likely a unit mistake.</summary>
        public const string ExpiryTooFar = "expiry.too-far";
    }
}
