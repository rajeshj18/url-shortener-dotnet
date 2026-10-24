using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace UrlShortener.Core.Web
{
    /// <summary>One client: an owner ID and the SHA-256 (hex) of its API key. The key itself is never configured.</summary>
    public sealed class ApiKeyClient
    {
        /// <summary>The owner ID stored on the client's links (≤ 64 characters).</summary>
        public string OwnerId { get; set; } = string.Empty;

        /// <summary>SHA-256 of the API key, 64 hex characters.</summary>
        public string KeyHash { get; set; } = string.Empty;
    }

    /// <summary>Options of the API-key scheme: the known clients.</summary>
    public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        /// <summary>The configured clients.</summary>
        public IReadOnlyList<ApiKeyClient> Clients { get; set; } = Array.Empty<ApiKeyClient>();
    }

    /// <summary>
    /// Authentication for the management API (FR-09, A-24). The <c>X-Api-Key</c> header is hashed with SHA-256 and
    /// compared in constant time with every configured hash; a match sets the owner ID. The key is never stored or logged.
    /// </summary>
    public sealed class ApiKeyAuthentication : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        /// <summary>The scheme name.</summary>
        public const string SchemeName = "ApiKey";

        /// <summary>The header that carries the key.</summary>
        public const string HeaderName = "X-Api-Key";

        /// <summary>Creates the handler.</summary>
        /// <param name="options">Scheme options.</param>
        /// <param name="logger">Logger factory.</param>
        /// <param name="encoder">URL encoder.</param>
        public ApiKeyAuthentication(IOptionsMonitor<ApiKeyAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <summary>Registers the scheme as the default, and authorization. Invalid configuration fails at startup.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration that holds <c>ApiKeys:Clients</c>.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection Add(IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            List<ApiKeyClient> clients = configuration.GetSection("ApiKeys:Clients").Get<List<ApiKeyClient>>() ?? new List<ApiKeyClient>();
            Validate(clients);
            services.AddAuthentication(SchemeName).AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthentication>(SchemeName, options => options.Clients = clients);
            services.AddAuthorization();
            return services;
        }

        /// <summary>Finds the owner whose key hash matches. Every client is compared, so timing reveals nothing.</summary>
        /// <param name="presentedKey">The header value.</param>
        /// <param name="clients">The configured clients.</param>
        /// <returns>The owner ID, or <c>null</c>.</returns>
        public static string? FindOwner(string presentedKey, IReadOnlyList<ApiKeyClient> clients)
        {
            ArgumentNullException.ThrowIfNull(presentedKey);
            ArgumentNullException.ThrowIfNull(clients);

            byte[] presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
            // ToList evaluates every client (no early exit), so the time taken doesn't depend on which one matches.
            List<string> matches = clients
                .Where(client => CryptographicOperations.FixedTimeEquals(presentedHash, Convert.FromHexString(client.KeyHash)))
                .Select(client => client.OwnerId)
                .ToList();
            return matches.Count > 0 ? matches[0] : null;
        }

        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out StringValues values) || values.Count != 1 || string.IsNullOrEmpty(values[0]))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            string? ownerId = FindOwner(values[0]!, Options.Clients);
            if (ownerId == null)
            {
                return Task.FromResult(AuthenticateResult.Fail("Unknown API key."));
            }

            ClaimsIdentity identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, ownerId) }, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }

        /// <summary>
        /// <c>401</c> with <c>WWW-Authenticate: ApiKey header="X-Api-Key"</c>: RFC 9110 §15.5.2 requires the header (TSK-15).
        /// The problem body comes from the status-code-pages middleware.
        /// </summary>
        /// <param name="properties">Authentication properties.</param>
        /// <returns>A task.</returns>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers.WWWAuthenticate = SchemeName + " header=\"" + HeaderName + "\"";
            return base.HandleChallengeAsync(properties);
        }

        private static void Validate(List<ApiKeyClient> clients)
        {
            if (clients.Count == 0)
            {
                throw new InvalidOperationException("ApiKeys:Clients is empty; configure at least one client (OwnerId + KeyHash).");
            }

            for (int i = 0; i < clients.Count; i++)
            {
                string where = string.Create(CultureInfo.InvariantCulture, $"ApiKeys:Clients:{i}");
                if (string.IsNullOrWhiteSpace(clients[i].OwnerId) || clients[i].OwnerId.Length > 64)
                {
                    throw new InvalidOperationException(where + ":OwnerId must be 1-64 characters.");
                }

                if (clients[i].KeyHash.Length != 64 || !clients[i].KeyHash.All(Uri.IsHexDigit))
                {
                    throw new InvalidOperationException(where + ":KeyHash must be the SHA-256 of the key as 64 hex characters.");
                }
            }

            if (clients.Select(c => c.KeyHash.ToUpperInvariant()).Distinct().Count() != clients.Count)
            {
                throw new InvalidOperationException("ApiKeys:Clients contains the same key hash twice.");
            }
        }
    }
}
