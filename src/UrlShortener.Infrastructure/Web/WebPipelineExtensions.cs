using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace UrlShortener.Infrastructure.Web
{
    /// <summary>
    /// Registers and wires the cross-cutting middleware shared by every web service, so that
    /// controllers only map HTTP to use cases. Later tasks add authentication (TSK-08),
    /// request timeouts (TSK-11) and rate limiting (TSK-19) here.
    /// </summary>
    public static class WebPipelineExtensions
    {
        /// <summary>Registers problem details and the exception-handling middleware.</summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddUrlShortenerWebDefaults(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddProblemDetails();
            services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
            return services;
        }

        /// <summary>
        /// Adds the shared middleware, in order: exception handling first (so it covers everything after
        /// it), then RFC 7807 bodies for empty 4xx/5xx responses.
        /// </summary>
        /// <param name="app">The application being configured.</param>
        /// <returns>The same application, for chaining.</returns>
        public static WebApplication UseUrlShortenerMiddleware(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.UseExceptionHandler();
            app.UseStatusCodePages();
            return app;
        }
    }
}
