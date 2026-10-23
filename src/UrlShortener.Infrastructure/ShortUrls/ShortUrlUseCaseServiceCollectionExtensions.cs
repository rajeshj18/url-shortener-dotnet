using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using UrlShortener.Application.Ports;
using UrlShortener.Application.ShortUrls;
using UrlShortener.Infrastructure.Persistence.Links;

namespace UrlShortener.Infrastructure.ShortUrls
{
    /// <summary>
    /// Wiring for the Api's link use cases. Requires <c>AddLinksWriteStore</c> and <c>AddShortCodeGeneration</c>.
    /// </summary>
    public static class ShortUrlUseCaseServiceCollectionExtensions
    {
        /// <summary>Configuration key of the public short-link base URL (LLD §8).</summary>
        public const string PublicBaseUrlKey = "PublicBaseUrl";

        /// <summary>Registers the create use case, the unit of work and the idempotency store.</summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <param name="configuration">Configuration that holds <c>PublicBaseUrl</c>.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddShortUrlUseCases(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            Uri publicBaseUrl = ReadPublicBaseUrl(configuration);
            SelfLinkCheck selfLinkCheck = new SelfLinkCheck(publicBaseUrl);

            services.TryAddSingleton(TimeProvider.System);
            services.AddScoped<IUnitOfWork, EfUnitOfWork>();
            services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
            services.AddScoped<ICreateShortUrlHandler>(provider => new CreateShortUrlHandler(
                provider.GetRequiredService<IUnitOfWork>(),
                provider.GetRequiredService<IShortUrlRepository>(),
                provider.GetRequiredService<IShortCodeGenerator>(),
                provider.GetRequiredService<IIdempotencyStore>(),
                selfLinkCheck,
                publicBaseUrl,
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<CreateShortUrlHandler>>()));
            return services;
        }

        private static Uri ReadPublicBaseUrl(IConfiguration configuration)
        {
            string? value = configuration[PublicBaseUrlKey];
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("PublicBaseUrl must be an absolute http(s) URL, e.g. https://sho.rt (LLD §8).");
            }

            // A trailing slash makes new Uri(base, code) append the code instead of replacing the last segment.
            return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
        }
    }
}
