using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UrlShortener.Infrastructure.Web.Authentication
{
    /// <summary>
    /// Authentication middleware for the management API (FR-09, A-24, TSK-08 D1). The <c>X-Api-Key</c> header is
    /// hashed with SHA-256 and compared in constant time with the configured hashes; a match sets the owner ID.
    /// The key itself is never stored or logged.
    /// </summary>
    public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        /// <summary>Creates the handler.</summary>
        /// <param name="options">Scheme options (the configured clients).</param>
        /// <param name="logger">Logger factory.</param>
        /// <param name="encoder">URL encoder.</param>
        public ApiKeyAuthenticationHandler(IOptionsMonitor<ApiKeyAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out Microsoft.Extensions.Primitives.StringValues values)
                || values.Count != 1
                || string.IsNullOrEmpty(values[0]))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            string? ownerId = FindOwner(values[0]!, Options.Clients);
            if (ownerId == null)
            {
                return Task.FromResult(AuthenticateResult.Fail("Unknown API key."));
            }

            ClaimsIdentity identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, ownerId) },
                ApiKeyAuthenticationDefaults.Scheme);
            AuthenticationTicket ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationDefaults.Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        /// <summary>
        /// Answers <c>401</c> with <c>WWW-Authenticate: ApiKey header="X-Api-Key"</c>. RFC 9110 §15.5.2: a server
        /// generating a 401 MUST send this header, naming the scheme the client should use (TSK-15). The problem
        /// body is added by the status-code-pages middleware.
        /// </summary>
        /// <param name="properties">Authentication properties.</param>
        /// <returns>A task.</returns>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers.WWWAuthenticate = ApiKeyAuthenticationDefaults.Scheme + " header=\"" + ApiKeyAuthenticationDefaults.HeaderName + "\"";
            return base.HandleChallengeAsync(properties);
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
                .Where(client => CryptographicOperations.FixedTimeEquals(presentedHash, client.KeyHashBytes()))
                .Select(client => client.OwnerId)
                .ToList();
            return matches.Count > 0 ? matches[0] : null;
        }
    }
}
