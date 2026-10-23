using System;
using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UrlShortener.Infrastructure.Web
{
    /// <summary>
    /// Registers and wires the cross-cutting middleware shared by every web service, so that
    /// controllers only map HTTP to use cases. Authentication is wired by
    /// <c>AddApiKeyAuthentication</c> (TSK-08, Api only). Rate limiting (TSK-19) will be added here.
    /// </summary>
    public static class WebPipelineExtensions
    {
        /// <summary>
        /// Registers problem details, the exception handlers and the request timeout (LLD §6.7).
        /// </summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <param name="requestTimeout">Whole-request budget: Api 3 s, Redirect 500 ms. On expiry: <c>503</c>.</param>
        /// <param name="unavailableRetryAfterSeconds"><c>Retry-After</c> when the database is unavailable: Api 5, Redirect 1.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddUrlShortenerWebDefaults(this IServiceCollection services, TimeSpan requestTimeout, int unavailableRetryAfterSeconds)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (requestTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(requestTimeout), "The request timeout must be positive.");
            }

            // One traceId format everywhere (TSK-15): problems the framework generates (401 challenges,
            // status-code pages, ProblemDetailsFactory) get the same 32-hex trace ID as our handlers and the logs,
            // instead of the full traceparent the framework uses by default.
            services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = ProblemResponses.TraceId(context.HttpContext));

            // Handlers run in registration order: specific mappings first, the catch-all 500 last.
            services.AddExceptionHandler<DomainValidationExceptionHandler>();
            services.AddExceptionHandler<IdSpaceExhaustedExceptionHandler>();
            services.AddSingleton<IExceptionHandler>(provider => new DatabaseUnavailableExceptionHandler(
                unavailableRetryAfterSeconds, provider.GetRequiredService<ILogger<DatabaseUnavailableExceptionHandler>>()));
            services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

            string retryAfter = unavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = requestTimeout,
                TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
                WriteTimeoutResponse = context =>
                {
                    // Not RequestAborted: the timeout has just cancelled that token, so writing with it would throw
                    // and turn the 503 into a 500.
                    context.Response.Headers.RetryAfter = retryAfter;
                    return ProblemResponses.WriteAsync(
                        context,
                        ProblemResponses.Create(context, StatusCodes.Status503ServiceUnavailable, "The request took too long. Retry later.", null),
                        CancellationToken.None);
                },
            });
            return services;
        }

        /// <summary>
        /// Adds the shared middleware, in order: exception handling first (so it covers everything after
        /// it), then RFC 7807 bodies for empty 4xx/5xx responses, then the request timeout. The timeout cancels
        /// <c>HttpContext.RequestAborted</c>, which every database call receives, so the work stops and rolls back.
        /// </summary>
        /// <param name="app">The application being configured.</param>
        /// <returns>The same application, for chaining.</returns>
        public static WebApplication UseUrlShortenerMiddleware(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.UseExceptionHandler();
            app.UseStatusCodePages();
            app.UseRequestTimeouts();
            return app;
        }
    }
}
