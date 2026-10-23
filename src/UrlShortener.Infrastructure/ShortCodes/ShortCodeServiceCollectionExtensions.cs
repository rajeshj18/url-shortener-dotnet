using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UrlShortener.Application.Ports;
using UrlShortener.Infrastructure.Persistence.Links;

namespace UrlShortener.Infrastructure.ShortCodes
{
    /// <summary>
    /// Wiring for short-code generation (Api only, ADR-0009). Requires <c>AddLinksWriteStore</c>, which provides the
    /// primary data source. Invalid configuration fails at startup, and the error messages never contain the key.
    /// </summary>
    public static class ShortCodeServiceCollectionExtensions
    {
        /// <summary>Registers the generator, the leaser and the startup check.</summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <param name="configuration">Configuration that holds the <c>ShortCode</c> section.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddShortCodeGeneration(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            ShortCodeOptions options = new ShortCodeOptions();
            configuration.GetSection(ShortCodeOptions.SectionName).Bind(options);
            Validate(options);

            FeistelPermutation permutation = FeistelPermutation.FromPassphrase(options.FeistelKey);
            short region = options.DeploymentRegionIndex;
            int leaseSize = options.LeaseSize;

            services.AddSingleton<IIdRangeLeaser>(provider =>
                new PostgresIdRangeLeaser(provider.GetRequiredService<LinksPrimaryDataSource>(), region));
            services.AddSingleton(provider =>
                new ShortCodeGenerator(provider.GetRequiredService<IIdRangeLeaser>(), permutation, leaseSize));
            services.AddSingleton<IShortCodeGenerator>(provider => provider.GetRequiredService<ShortCodeGenerator>());
            services.AddHostedService(provider => new ShortCodeStartupCheck(
                provider.GetRequiredService<LinksPrimaryDataSource>(),
                provider.GetRequiredService<ShortCodeGenerator>(),
                region,
                provider.GetRequiredService<ILogger<ShortCodeStartupCheck>>()));
            return services;
        }

        private static void Validate(ShortCodeOptions options)
        {
            if (string.IsNullOrEmpty(options.FeistelKey) || options.FeistelKey.Length < FeistelPermutation.MinPassphraseLength)
            {
                throw new InvalidOperationException(
                    "ShortCode:FeistelKey is missing or shorter than 32 characters (set ShortCode__FeistelKey). It must never change once codes exist.");
            }

            if (options.LeaseSize < 100 || options.LeaseSize > 1_000_000)
            {
                throw new InvalidOperationException("ShortCode:LeaseSize must be between 100 and 1,000,000.");
            }

            if (options.DeploymentRegionIndex < 0)
            {
                throw new InvalidOperationException("ShortCode:DeploymentRegionIndex must be 0 or greater.");
            }
        }
    }
}
