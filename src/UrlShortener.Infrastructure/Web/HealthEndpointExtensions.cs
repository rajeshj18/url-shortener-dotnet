using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace UrlShortener.Infrastructure.Web
{
    /// <summary>
    /// Health endpoints for every service (ADR-0009).
    /// <c>/health/live</c> runs no checks: it answers while the process is up.
    /// <c>/health/ready</c> runs only the checks tagged <see cref="ReadyTag"/>, e.g. the database from TSK-06.
    /// </summary>
    public static class HealthEndpointExtensions
    {
        /// <summary>Tag for health checks that must pass before the service accepts traffic.</summary>
        public const string ReadyTag = "ready";

        /// <summary>Path of the liveness probe.</summary>
        public const string LivePath = "/health/live";

        /// <summary>Path of the readiness probe.</summary>
        public const string ReadyPath = "/health/ready";

        /// <summary>Registers the health-check services.</summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <returns>A builder for adding health checks, tagged <see cref="ReadyTag"/> where relevant.</returns>
        public static IHealthChecksBuilder AddUrlShortenerHealthChecks(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return services.AddHealthChecks();
        }

        /// <summary>Maps the liveness and readiness endpoints.</summary>
        /// <param name="endpoints">The endpoint route builder.</param>
        /// <returns>The same builder, for chaining.</returns>
        public static IEndpointRouteBuilder MapUrlShortenerHealthEndpoints(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false });
            endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(ReadyTag) });
            return endpoints;
        }
    }
}
