using System;
using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UrlShortener.Core.Web
{
    /// <summary>
    /// The cross-cutting web pipeline shared by every service: problem details, the exception handler, the request
    /// timeout and the health endpoints. Controllers only map HTTP to the Core services.
    /// </summary>
    public static class WebDefaults
    {
        /// <summary>Tag for health checks that must pass before the service accepts traffic.</summary>
        public const string ReadyTag = "ready";

        /// <summary>Path of the liveness probe (no checks: answers while the process is up).</summary>
        public const string LivePath = "/health/live";

        /// <summary>Path of the readiness probe (runs the checks tagged <see cref="ReadyTag"/>).</summary>
        public const string ReadyPath = "/health/ready";

        /// <summary>Registers problem details, the exception handler, health checks and the request timeout (LLD §6.7).</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="requestTimeout">Whole-request budget: Api 3 s, Redirect 500 ms. On expiry: <c>503</c>.</param>
        /// <param name="unavailableRetryAfterSeconds"><c>Retry-After</c> when the database is unavailable: Api 5, Redirect 1.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddWebDefaults(this IServiceCollection services, TimeSpan requestTimeout, int unavailableRetryAfterSeconds)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (requestTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(requestTimeout), "The request timeout must be positive.");
            }

            // One traceId format everywhere (TSK-15): framework problems (401, status-code pages) get the same 32-hex ID.
            services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = ErrorResponses.TraceId(context.HttpContext));
            services.AddSingleton<IExceptionHandler>(provider => new ErrorResponses(
                unavailableRetryAfterSeconds, provider.GetRequiredService<ILogger<ErrorResponses>>()));
            services.AddHealthChecks();

            string retryAfter = unavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = requestTimeout,
                TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
                WriteTimeoutResponse = context =>
                {
                    // Not RequestAborted: the timeout has just cancelled it, and writing with it would turn the 503 into a 500.
                    context.Response.Headers.RetryAfter = retryAfter;
                    return ErrorResponses.WriteAsync(
                        context,
                        ErrorResponses.Create(context, StatusCodes.Status503ServiceUnavailable, "The request took too long. Retry later.", null),
                        CancellationToken.None);
                },
            });
            return services;
        }

        /// <summary>
        /// Adds the middleware in order (exception handling first, so it covers everything after it; then problem bodies for
        /// empty 4xx/5xx responses; then the request timeout, which cancels <c>RequestAborted</c> and so every database
        /// call) and maps the health endpoints.
        /// </summary>
        /// <param name="app">The application.</param>
        /// <returns>The same application.</returns>
        public static WebApplication UseWebDefaults(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.UseExceptionHandler();
            app.UseStatusCodePages();
            app.UseRequestTimeouts();
            app.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false });
            app.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(ReadyTag) });
            return app;
        }
    }
}
