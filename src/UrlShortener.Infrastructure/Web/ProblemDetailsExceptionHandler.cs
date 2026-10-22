using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace UrlShortener.Infrastructure.Web
{
    /// <summary>
    /// Exception-handling middleware component: turns any unhandled exception into an RFC 7807
    /// <c>application/problem+json</c> response with a trace ID. The exception is logged once, and its
    /// message or stack trace is never sent to the client.
    /// </summary>
    public sealed partial class ProblemDetailsExceptionHandler : IExceptionHandler
    {
        /// <summary>The media type of RFC 7807 problem responses.</summary>
        public const string ProblemJsonContentType = "application/problem+json";

        private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

        /// <summary>Creates the handler.</summary>
        /// <param name="logger">Logger used to record the exception once, with its trace ID.</param>
        public ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(exception);

            if (httpContext.Response.HasStarted)
            {
                return false;
            }

            string traceId = ResolveTraceId(httpContext);
            LogUnhandledException(_logger, traceId, exception);

            ProblemDetails problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            };
            problem.Extensions["traceId"] = traceId;

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, ProblemJsonContentType, cancellationToken);
            return true;
        }

        private static string ResolveTraceId(HttpContext httpContext)
        {
            Activity? activity = Activity.Current;
            return activity != null ? activity.TraceId.ToString() : httpContext.TraceIdentifier;
        }

        [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Unhandled exception. TraceId: {TraceId}")]
        private static partial void LogUnhandledException(ILogger logger, string traceId, Exception exception);
    }
}
