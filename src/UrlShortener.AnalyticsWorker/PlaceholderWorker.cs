using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UrlShortener.AnalyticsWorker
{
    /// <summary>
    /// Placeholder background service. Click-event consumption arrives in stage 2 (TSK-23, ADR-0008).
    /// Until then the worker only reports that it started and answers its health endpoints.
    /// </summary>
    public sealed partial class PlaceholderWorker : BackgroundService
    {
        private readonly ILogger<PlaceholderWorker> _logger;

        /// <summary>Creates the placeholder worker.</summary>
        /// <param name="logger">Logger for start-up information.</param>
        public PlaceholderWorker(ILogger<PlaceholderWorker> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LogPlaceholderStarted(_logger);
            return Task.CompletedTask;
        }

        [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "AnalyticsWorker placeholder started; click processing arrives in stage 2 (ADR-0008).")]
        private static partial void LogPlaceholderStarted(ILogger logger);
    }
}
