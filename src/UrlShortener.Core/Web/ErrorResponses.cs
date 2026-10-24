using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using UrlShortener.Core.Codes;
using UrlShortener.Core.Database;
using UrlShortener.Domain;

namespace UrlShortener.Core.Web
{
    /// <summary>
    /// The one exception handler of every web service. It turns an exception into an RFC 7807
    /// <c>application/problem+json</c> response with a 32-hex <c>traceId</c>, never with the exception's message:
    /// <list type="bullet">
    /// <item>a domain rule broken by the input → <c>400</c> with its stable code (not logged as an error);</item>
    /// <item>ID space exhausted → <c>503</c> + <c>Retry-After: 5</c> (critical: an operator must assign a block);</item>
    /// <item>database unavailable (open breaker, or a transient failure left after the retry rule) → <c>503</c> +
    /// <c>Retry-After</c> (warning: an outage, not a bug);</item>
    /// <item>anything else → <c>500</c>, logged once with the trace ID.</item>
    /// </list>
    /// </summary>
    public sealed partial class ErrorResponses : IExceptionHandler
    {
        /// <summary>The media type of RFC 7807 problem responses.</summary>
        public const string ProblemJsonContentType = "application/problem+json";

        /// <summary><c>Retry-After</c> when the ID space is exhausted.</summary>
        public const int IdSpaceRetryAfterSeconds = 5;

        private readonly int _unavailableRetryAfterSeconds;
        private readonly ILogger<ErrorResponses> _logger;

        /// <summary>Creates the handler.</summary>
        /// <param name="unavailableRetryAfterSeconds"><c>Retry-After</c> when the database is unavailable: Api 5, Redirect 1.</param>
        /// <param name="logger">Logger.</param>
        public ErrorResponses(int unavailableRetryAfterSeconds, ILogger<ErrorResponses> logger)
        {
            if (unavailableRetryAfterSeconds < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(unavailableRetryAfterSeconds), "Retry-After must be at least 1 second.");
            }

            _unavailableRetryAfterSeconds = unavailableRetryAfterSeconds;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>The trace ID of the request: the W3C trace ID if tracing is on, otherwise ASP.NET Core's.</summary>
        /// <param name="httpContext">The request.</param>
        /// <returns>The trace ID.</returns>
        public static string TraceId(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            Activity? activity = Activity.Current;
            return activity != null ? activity.TraceId.ToString() : httpContext.TraceIdentifier;
        }

        /// <summary>Creates a problem with a trace ID and, optionally, one stable error code.</summary>
        /// <param name="httpContext">The request.</param>
        /// <param name="status">HTTP status.</param>
        /// <param name="title">Short, human-readable summary.</param>
        /// <param name="code">A stable error code, or <c>null</c>.</param>
        /// <returns>The problem.</returns>
        public static ProblemDetails Create(HttpContext httpContext, int status, string title, string? code)
        {
            ProblemDetails problem = new ProblemDetails { Status = status, Title = title };
            problem.Extensions["traceId"] = TraceId(httpContext);
            if (code != null)
            {
                problem.Extensions["errors"] = new[] { new Dictionary<string, string> { ["code"] = code, ["message"] = title } };
            }

            return problem;
        }

        /// <summary>Writes a problem response; its status becomes the response status.</summary>
        /// <param name="httpContext">The request.</param>
        /// <param name="problem">The problem.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task.</returns>
        public static Task WriteAsync(HttpContext httpContext, ProblemDetails problem, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(problem);
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            return httpContext.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, ProblemJsonContentType, cancellationToken);
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

            string traceId = TraceId(httpContext);
            ProblemDetails problem;
            if (exception is DomainValidationException validation)
            {
                problem = Create(httpContext, StatusCodes.Status400BadRequest, validation.Message, validation.Code);
            }
            else if (exception is IdSpaceExhaustedException)
            {
                LogIdSpaceExhausted(_logger, traceId);
                httpContext.Response.Headers.RetryAfter = IdSpaceRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                problem = Create(httpContext, StatusCodes.Status503ServiceUnavailable, "Short codes are temporarily unavailable. Retry later.", null);
            }
            else if (exception is BrokenCircuitException || TransientDatabaseErrors.IsTransient(exception))
            {
                LogDatabaseUnavailable(_logger, exception is BrokenCircuitException, traceId, exception.GetType().Name);
                httpContext.Response.Headers.RetryAfter = _unavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                problem = Create(httpContext, StatusCodes.Status503ServiceUnavailable, "The database is temporarily unavailable. Retry later.", null);
            }
            else
            {
                LogUnhandled(_logger, traceId, exception);
                problem = Create(httpContext, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null);
                problem.Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1";
            }

            await WriteAsync(httpContext, problem, cancellationToken);
            return true;
        }

        [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Unhandled exception. TraceId: {TraceId}")]
        private static partial void LogUnhandled(ILogger logger, string traceId, Exception exception);

        [LoggerMessage(EventId = 1001, Level = LogLevel.Critical, Message = "ID space exhausted for this region; creates fail until a block is assigned (LLD §6.4 runbook). TraceId: {TraceId}")]
        private static partial void LogIdSpaceExhausted(ILogger logger, string traceId);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Database unavailable (breaker open: {BreakerOpen}, {ExceptionType}); answered 503. TraceId: {TraceId}")]
        private static partial void LogDatabaseUnavailable(ILogger logger, bool breakerOpen, string traceId, string exceptionType);
    }
}
