using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UrlShortener.Application.Ports;

namespace UrlShortener.Infrastructure.Web
{
    /// <summary>
    /// Maps <see cref="IdSpaceExhaustedException"/> to <c>503</c> + <c>Retry-After</c> (LLD §6.4). Logged as
    /// critical: the 80% alert was missed and an operator must assign a block (runbook).
    /// </summary>
    public sealed partial class IdSpaceExhaustedExceptionHandler : IExceptionHandler
    {
        /// <summary>Seconds clients are asked to wait.</summary>
        public const int RetryAfterSeconds = 5;

        private readonly ILogger<IdSpaceExhaustedExceptionHandler> _logger;

        /// <summary>Creates the handler.</summary>
        /// <param name="logger">Logger.</param>
        public IdSpaceExhaustedExceptionHandler(ILogger<IdSpaceExhaustedExceptionHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            if (exception is not IdSpaceExhaustedException || httpContext.Response.HasStarted)
            {
                return false;
            }

            LogExhausted(_logger, ProblemResponses.TraceId(httpContext));
            httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            await ProblemResponses.WriteAsync(
                httpContext,
                ProblemResponses.Create(httpContext, StatusCodes.Status503ServiceUnavailable, "Short codes are temporarily unavailable. Retry later.", null),
                cancellationToken);
            return true;
        }

        [LoggerMessage(EventId = 1001, Level = LogLevel.Critical, Message = "ID space exhausted for this region; creates fail until a block is assigned (LLD §6.4 runbook). TraceId: {TraceId}")]
        private static partial void LogExhausted(ILogger logger, string traceId);
    }
}
