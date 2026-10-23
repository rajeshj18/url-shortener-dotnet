using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using UrlShortener.Infrastructure.Resilience;

namespace UrlShortener.Infrastructure.Web
{
    /// <summary>
    /// Maps database unavailability to <c>503</c> + <c>Retry-After</c> (LLD §6.7): a transient failure left after the
    /// retry rule, an unknown commit outcome without an idempotency key, or an open breaker (fast rejection). Logged as
    /// a warning: an outage, not a bug.
    /// </summary>
    public sealed partial class DatabaseUnavailableExceptionHandler : IExceptionHandler
    {
        private readonly int _retryAfterSeconds;
        private readonly ILogger<DatabaseUnavailableExceptionHandler> _logger;

        /// <summary>Creates the handler.</summary>
        /// <param name="retryAfterSeconds">The <c>Retry-After</c> value: Api 5, Redirect 1.</param>
        /// <param name="logger">Logger.</param>
        public DatabaseUnavailableExceptionHandler(int retryAfterSeconds, ILogger<DatabaseUnavailableExceptionHandler> logger)
        {
            if (retryAfterSeconds < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(retryAfterSeconds), "Retry-After must be at least 1 second.");
            }

            _retryAfterSeconds = retryAfterSeconds;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(exception);
            bool open = exception is BrokenCircuitException;
            if ((!open && !TransientDatabaseErrors.IsTransient(exception)) || httpContext.Response.HasStarted)
            {
                return false;
            }

            LogUnavailable(_logger, open, ProblemResponses.TraceId(httpContext), exception.GetType().Name);
            httpContext.Response.Headers.RetryAfter = _retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            await ProblemResponses.WriteAsync(
                httpContext,
                ProblemResponses.Create(httpContext, StatusCodes.Status503ServiceUnavailable, "The database is temporarily unavailable. Retry later.", null),
                cancellationToken);
            return true;
        }

        [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Database unavailable (breaker open: {BreakerOpen}, {ExceptionType}); answered 503. TraceId: {TraceId}")]
        private static partial void LogUnavailable(ILogger logger, bool breakerOpen, string traceId, string exceptionType);
    }
}
