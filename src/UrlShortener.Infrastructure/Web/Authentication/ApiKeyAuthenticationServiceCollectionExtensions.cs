using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UrlShortener.Infrastructure.Web.Authentication
{
    /// <summary>Wiring for API-key authentication (Api only). Invalid configuration fails at startup.</summary>
    public static class ApiKeyAuthenticationServiceCollectionExtensions
    {
        /// <summary>Registers the scheme as the default, and authorization.</summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <param name="configuration">Configuration that holds <c>ApiKeys:Clients</c>.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            List<ApiKeyClient> clients = configuration
                .GetSection(ApiKeyAuthenticationDefaults.ConfigurationSection + ":Clients")
                .Get<List<ApiKeyClient>>() ?? new List<ApiKeyClient>();
            Validate(clients);

            services
                .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
                .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                    ApiKeyAuthenticationDefaults.Scheme,
                    options => options.Clients = clients);
            services.AddAuthorization();
            return services;
        }

        /// <summary>Adds authentication and authorization to the pipeline, after exception handling.</summary>
        /// <param name="app">The application.</param>
        /// <returns>The same application, for chaining.</returns>
        public static WebApplication UseApiKeyAuthentication(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.UseAuthentication();
            app.UseAuthorization();
            return app;
        }

        private static void Validate(List<ApiKeyClient> clients)
        {
            if (clients.Count == 0)
            {
                throw new InvalidOperationException("ApiKeys:Clients is empty; configure at least one client (OwnerId + KeyHash).");
            }

            for (int i = 0; i < clients.Count; i++)
            {
                ApiKeyClient client = clients[i];
                string where = string.Create(CultureInfo.InvariantCulture, $"ApiKeys:Clients:{i}");
                if (string.IsNullOrWhiteSpace(client.OwnerId) || client.OwnerId.Length > 64)
                {
                    throw new InvalidOperationException(where + ":OwnerId must be 1-64 characters.");
                }

                if (client.KeyHash.Length != 64 || !client.KeyHash.All(Uri.IsHexDigit))
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
