using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using UrlShortener.Application.Ports;
using UrlShortener.Infrastructure.Persistence.Links;
using UrlShortener.Infrastructure.Resilience;
using UrlShortener.Infrastructure.Web;

namespace UrlShortener.Infrastructure.Persistence
{
    /// <summary>
    /// Wiring for the <c>links</c> store. Each service registers only the side it needs: the Api gets the write
    /// store, Redirect gets the read store (ADR-0009, refinement R2).
    /// </summary>
    public static class LinksPersistenceServiceCollectionExtensions
    {
        /// <summary>Connection-string name of the writer endpoint (LLD §8).</summary>
        public const string PrimaryConnectionName = "LinksPrimary";

        /// <summary>Connection-string name of the reader endpoint (LLD §8).</summary>
        public const string ReplicasConnectionName = "LinksReplicas";

        /// <summary>
        /// Api service: EF Core on the primary, <see cref="IShortUrlRepository"/>, and a readiness check.
        /// Never runs migrations (ADR-0009).
        /// </summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <param name="configuration">Configuration that holds <c>ConnectionStrings:LinksPrimary</c>.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddLinksWriteStore(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            ResilienceOptions resilience = ResilienceConfiguration.Read(configuration);
            string primary = ResilienceConfiguration.ApplyConnectionDefaults(
                RequiredConnectionString(configuration, PrimaryConnectionName), resilience, resilience.ApiPrimaryMaxPoolSize, withStatementTimeout: true);

            // Registered by factory, not by instance: the container disposes only objects it creates itself,
            // and a data source owns a connection pool (found by CA2000).
            services.AddSingleton(_ => new LinksPrimaryDataSource(NpgsqlDataSource.Create(primary)));
            services.AddDbContext<LinksDbContext>((provider, options) =>
                options.UseNpgsql(provider.GetRequiredService<LinksPrimaryDataSource>().DataSource));
            services.AddScoped<IShortUrlRepository, EfShortUrlRepository>();

            services.AddHealthChecks().Add(new HealthCheckRegistration(
                "links-primary",
                provider => new DataSourceHealthCheck(provider.GetRequiredService<LinksPrimaryDataSource>().DataSource),
                HealthStatus.Unhealthy,
                new[] { HealthEndpointExtensions.ReadyTag }));
            return services;
        }

        /// <summary>
        /// Redirect service: the replicas and the primary (for the read-your-writes fallback),
        /// <see cref="IRedirectLookup"/>, and a readiness check on the replicas.
        /// </summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <param name="configuration">Configuration that holds both connection strings (read-only user).</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddLinksReadStore(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            ResilienceOptions resilience = ResilienceConfiguration.Read(configuration);
            string primary = ResilienceConfiguration.ApplyConnectionDefaults(
                RequiredConnectionString(configuration, PrimaryConnectionName), resilience, resilience.RedirectPrimaryMaxPoolSize, withStatementTimeout: false);
            string replicas = ResilienceConfiguration.ApplyConnectionDefaults(
                RequiredConnectionString(configuration, ReplicasConnectionName), resilience, resilience.RedirectReplicaMaxPoolSize, withStatementTimeout: false);
            services.AddSingleton(_ => new LinksPrimaryDataSource(NpgsqlDataSource.Create(primary)));
            services.AddSingleton(_ => new LinksReplicaDataSource(NpgsqlDataSource.Create(replicas)));
            services.AddSingleton<IRedirectLookup, NpgsqlRedirectLookup>();

            services.AddHealthChecks().Add(new HealthCheckRegistration(
                "links-replicas",
                provider => new DataSourceHealthCheck(provider.GetRequiredService<LinksReplicaDataSource>().DataSource),
                HealthStatus.Unhealthy,
                new[] { HealthEndpointExtensions.ReadyTag }));
            return services;
        }

        private static string RequiredConnectionString(IConfiguration configuration, string name)
        {
            string? value = configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                // Fail fast at startup with a clear message, rather than on the first request.
                throw new InvalidOperationException($"Connection string '{name}' is not configured (ConnectionStrings__{name}).");
            }

            return value;
        }
    }
}
