using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;

namespace UrlShortener.Infrastructure.Web.Authentication
{
    /// <summary>Names used by API-key authentication.</summary>
    public static class ApiKeyAuthenticationDefaults
    {
        /// <summary>The authentication scheme name.</summary>
        public const string Scheme = "ApiKey";

        /// <summary>The request header that carries the key (LLD §3).</summary>
        public const string HeaderName = "X-Api-Key";

        /// <summary>Configuration section with the clients (<c>ApiKeys:Clients:n:OwnerId</c> / <c>KeyHash</c>).</summary>
        public const string ConfigurationSection = "ApiKeys";
    }

    /// <summary>Options of the API-key scheme: the known clients.</summary>
    public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        /// <summary>The configured clients.</summary>
        public IReadOnlyList<ApiKeyClient> Clients { get; set; } = Array.Empty<ApiKeyClient>();
    }

    /// <summary>One client: an owner ID and the SHA-256 (hex) of its API key. The key itself is never configured.</summary>
    public sealed class ApiKeyClient
    {
        /// <summary>The owner ID stored on the client's links (≤ 64 characters).</summary>
        public string OwnerId { get; set; } = string.Empty;

        /// <summary>SHA-256 of the API key, 64 hex characters.</summary>
        public string KeyHash { get; set; } = string.Empty;

        /// <summary>The hash as bytes.</summary>
        /// <returns>32 bytes.</returns>
        public byte[] KeyHashBytes()
        {
            return Convert.FromHexString(KeyHash);
        }
    }
}
