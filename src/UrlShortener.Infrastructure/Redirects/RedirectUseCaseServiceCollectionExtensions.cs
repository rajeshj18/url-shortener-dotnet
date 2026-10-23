using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UrlShortener.Application.Ports;
using UrlShortener.Application.Redirects;
using UrlShortener.Infrastructure.Persistence.Links;
using UrlShortener.Infrastructure.Resilience;

namespace UrlShortener.Infrastructure.Redirects
{
    /// <summary>Wiring for the Redirect service's use case. Requires <c>AddLinksReadStore</c>.</summary>
    public static class RedirectUseCaseServiceCollectionExtensions
    {
        /// <summary>Configuration key of the primary-fallback budget (LLD §8).</summary>
        public const string FallbackPerSecondKey = "Redirect:PrimaryFallbackPerSecond";

        /// <summary>Default fallback budget per instance and second.</summary>
        public const int DefaultFallbackPerSecond = 500;

        /// <summary>Registers the resolve handler, the fallback budget, the classifier and the click publisher.</summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <param name="configuration">Configuration.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddRedirectUseCases(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            int perSecond = configuration.GetValue(FallbackPerSecondKey, DefaultFallbackPerSecond);
            if (perSecond < 1)
            {
                throw new InvalidOperationException(FallbackPerSecondKey + " must be at least 1.");
            }

            ResilienceOptions resilience = ResilienceConfiguration.Read(configuration);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(provider => new DatabaseCircuitBreaker(
                "redirect-primary-fallback",
                resilience,
                provider.GetRequiredService<TimeProvider>(),
                () => provider.GetRequiredService<LinksPrimaryDataSource>().ClearPool()));
            services.AddSingleton<IPrimaryFallbackBudget>(provider => new TokenBucketFallbackBudget(perSecond, provider.GetRequiredService<TimeProvider>()));
            services.TryAddSingleton<IUserAgentClassifier, TokenUserAgentClassifier>();
            services.TryAddSingleton<IClickEventPublisher, NoOpClickEventPublisher>();
            services.AddSingleton<IResolveShortUrlHandler>(provider => new ResolveShortUrlHandler(
                new BreakerGuardedRedirectLookup(provider.GetRequiredService<IRedirectLookup>(), provider.GetRequiredService<DatabaseCircuitBreaker>()),
                provider.GetRequiredService<IPrimaryFallbackBudget>(),
                provider.GetRequiredService<IClickEventPublisher>(),
                provider.GetRequiredService<IUserAgentClassifier>(),
                provider.GetRequiredService<TimeProvider>()));
            return services;
        }
    }
}
