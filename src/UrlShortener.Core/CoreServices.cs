using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Codes;
using UrlShortener.Core.Database;
using UrlShortener.Core.Links;
using UrlShortener.Core.Redirects;
using UrlShortener.Core.Web;

namespace UrlShortener.Core
{
    /// <summary>
    /// Wiring for each service, in one place: what the Api, Redirect and the AnalyticsWorker need, and nothing more.
    /// Invalid configuration fails at startup, with messages that never contain secrets.
    /// </summary>
    public static class CoreServices
    {
        /// <summary>Configuration key of the public short-link base URL (LLD §8).</summary>
        public const string PublicBaseUrlKey = "PublicBaseUrl";

        /// <summary>Configuration key of Redirect's primary-fallback budget (LLD §8).</summary>
        public const string FallbackPerSecondKey = "Redirect:PrimaryFallbackPerSecond";

        /// <summary>
        /// Api: the links primary (EF Core and Npgsql), code allocation, the link and stats services, API-key
        /// authentication, the idempotency cleanup job and the web defaults (3 s budget, <c>Retry-After: 5</c>).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddApiCore(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            ResilienceOptions resilience = ResilienceOptions.Read(configuration);
            Uri publicBaseUrl = ReadPublicBaseUrl(configuration);
            ShortCodeSettings codes = ShortCodeSettings.Read(configuration);
            KafkaOptions kafka = KafkaOptions.Read(configuration);
            string primary = ConnectionStrings.Read(configuration, ConnectionStrings.LinksPrimary, resilience, resilience.ApiPrimaryMaxPoolSize, withStatementTimeout: true);

            // Stats read the analytics schema as the Api user; locally it lives in the links database.
            string analytics = string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStrings.Analytics))
                ? primary
                : ConnectionStrings.Read(configuration, ConnectionStrings.Analytics, resilience, resilience.AnalyticsMaxPoolSize, withStatementTimeout: true);

            services.AddWebDefaults(resilience.RequestTimeout ?? TimeSpan.FromSeconds(3), unavailableRetryAfterSeconds: 5);
            ApiKeyAuthentication.Add(services, configuration);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(resilience);
            services.AddSingleton(kafka);

            // Registered by factory: the container disposes only what it creates, and a data source owns a pool (CA2000).
            services.AddSingleton(_ => new PrimaryDatabase(NpgsqlDataSource.Create(primary)));
            services.AddSingleton(_ => new AnalyticsDatabase(NpgsqlDataSource.Create(analytics)));
            services.AddDbContextFactory<LinksDbContext>((provider, options) => options.UseNpgsql(provider.GetRequiredService<PrimaryDatabase>().DataSource));
            AddReadyCheck(services, "links-primary", provider => provider.GetRequiredService<PrimaryDatabase>().DataSource);

            services.AddSingleton(provider => new CodeAllocator(
                provider.GetRequiredService<PrimaryDatabase>(),
                codes.Permutation,
                codes.RegionIndex,
                codes.LeaseSize,
                provider.GetRequiredService<ILogger<CodeAllocator>>()));
            services.AddHostedService(provider => provider.GetRequiredService<CodeAllocator>()); // startup check, first lease

            // One breaker per instance; when it opens, the pool is cleared so the trial call reconnects (LLD §6.7).
            services.AddSingleton(provider => new LinkService(
                provider.GetRequiredService<IDbContextFactory<LinksDbContext>>(),
                provider.GetRequiredService<CodeAllocator>(),
                new DatabaseGuard("links-primary", resilience, provider.GetRequiredService<TimeProvider>(), () => provider.GetRequiredService<PrimaryDatabase>().ClearPool(), provider.GetRequiredService<ILogger<DatabaseGuard>>()),
                publicBaseUrl,
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<LinkService>>()));
            services.AddSingleton<StatsService>();
            services.AddHostedService<IdempotencyCleanupJob>();
            return services;
        }

        /// <summary>
        /// Redirect: the replicas and the primary (read-only user), the redirect service with its fallback budget and
        /// breaker, the click publisher, and the web defaults (500 ms budget, <c>Retry-After: 1</c>).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddRedirectCore(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            ResilienceOptions resilience = ResilienceOptions.Read(configuration);
            int fallbacksPerSecond = configuration.GetValue(FallbackPerSecondKey, 500);
            if (fallbacksPerSecond < 1)
            {
                throw new InvalidOperationException(FallbackPerSecondKey + " must be at least 1.");
            }

            string primary = ConnectionStrings.Read(configuration, ConnectionStrings.LinksPrimary, resilience, resilience.RedirectPrimaryMaxPoolSize, withStatementTimeout: false);
            string replicas = ConnectionStrings.Read(configuration, ConnectionStrings.LinksReplicas, resilience, resilience.RedirectReplicaMaxPoolSize, withStatementTimeout: false);
            KafkaOptions kafka = KafkaOptions.Read(configuration);

            services.AddWebDefaults(resilience.RequestTimeout ?? TimeSpan.FromMilliseconds(500), unavailableRetryAfterSeconds: 1);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(_ => new PrimaryDatabase(NpgsqlDataSource.Create(primary)));
            services.AddSingleton(_ => new ReplicaDatabase(NpgsqlDataSource.Create(replicas)));
            AddReadyCheck(services, "links-replicas", provider => provider.GetRequiredService<ReplicaDatabase>().DataSource);
            services.TryAddSingleton(_ => new ClickPublisher(kafka));
            services.AddSingleton(provider => new RedirectService(
                provider.GetRequiredService<ReplicaDatabase>(),
                provider.GetRequiredService<PrimaryDatabase>(),
                new DatabaseGuard("redirect-primary-fallback", resilience, provider.GetRequiredService<TimeProvider>(), () => provider.GetRequiredService<PrimaryDatabase>().ClearPool(), provider.GetRequiredService<ILogger<DatabaseGuard>>()),
                provider.GetRequiredService<ClickPublisher>(),
                fallbacksPerSecond,
                provider.GetRequiredService<TimeProvider>()));
            return services;
        }

        /// <summary>AnalyticsWorker: the analytics store (worker user) and the Kafka consumer.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddWorkerCore(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            ResilienceOptions resilience = ResilienceOptions.Read(configuration);
            KafkaOptions kafka = KafkaOptions.Read(configuration);

            // Batch transactions may take longer than a request (LLD §6.7: 30 s per batch).
            resilience.CommandTimeoutSeconds = Math.Max(resilience.CommandTimeoutSeconds, 30);
            string analytics = ConnectionStrings.Read(configuration, ConnectionStrings.Analytics, resilience, resilience.AnalyticsMaxPoolSize, withStatementTimeout: false);

            services.AddHealthChecks();
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(kafka);
            services.AddSingleton(_ => new AnalyticsDatabase(NpgsqlDataSource.Create(analytics)));
            AddReadyCheck(services, "analytics", provider => provider.GetRequiredService<AnalyticsDatabase>().DataSource);
            services.AddSingleton<ClickConsumer>();
            services.AddHostedService(provider => provider.GetRequiredService<ClickConsumer>());
            return services;
        }

        private static void AddReadyCheck(IServiceCollection services, string name, Func<IServiceProvider, NpgsqlDataSource> dataSource)
        {
            services.AddHealthChecks().Add(new HealthCheckRegistration(
                name,
                provider => new DatabaseHealthCheck(dataSource(provider)),
                HealthStatus.Unhealthy,
                new[] { WebDefaults.ReadyTag }));
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

        /// <summary>The validated <c>ShortCode</c> section (LLD §8). The Feistel key is a secret and never appears in a message.</summary>
        private sealed class ShortCodeSettings
        {
            public short RegionIndex { get; private set; }

            public int LeaseSize { get; private set; }

            public FeistelPermutation Permutation { get; private set; } = null!;

            public static ShortCodeSettings Read(IConfiguration configuration)
            {
                string? key = configuration["ShortCode:FeistelKey"];
                short region = configuration.GetValue<short>("ShortCode:DeploymentRegionIndex");
                int leaseSize = configuration.GetValue("ShortCode:LeaseSize", 10_000);
                if (string.IsNullOrEmpty(key) || key.Length < FeistelPermutation.MinPassphraseLength)
                {
                    throw new InvalidOperationException(
                        "ShortCode:FeistelKey is missing or shorter than 32 characters (set ShortCode__FeistelKey). It must never change once codes exist.");
                }

                if (leaseSize < 100 || leaseSize > 1_000_000)
                {
                    throw new InvalidOperationException("ShortCode:LeaseSize must be between 100 and 1,000,000.");
                }

                if (region < 0)
                {
                    throw new InvalidOperationException("ShortCode:DeploymentRegionIndex must be 0 or greater.");
                }

                return new ShortCodeSettings { RegionIndex = region, LeaseSize = leaseSize, Permutation = FeistelPermutation.FromPassphrase(key) };
            }
        }
    }
}
