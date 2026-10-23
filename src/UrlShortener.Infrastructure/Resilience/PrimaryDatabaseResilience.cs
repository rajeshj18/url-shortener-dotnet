using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using UrlShortener.Application.Ports;

namespace UrlShortener.Infrastructure.Resilience
{
    /// <summary>
    /// The retry rule of LLD §6.7 around the <c>links-primary</c> breaker: at most <b>one</b> server-side retry, only
    /// for a transient failure, never while the breaker is open, never after cancellation, and for an unknown commit
    /// outcome only when the caller says a repeat can't change the result (an idempotency key).
    /// Order: retry → breaker → database, so every attempt counts towards the breaker.
    /// </summary>
    public sealed partial class PrimaryDatabaseResilience
    {
        private readonly DatabaseCircuitBreaker _breaker;
        private readonly TimeSpan _retryDelay;
        private readonly ILogger<PrimaryDatabaseResilience> _logger;

        /// <summary>Creates the executor.</summary>
        /// <param name="breaker">The primary's breaker.</param>
        /// <param name="options">Retry delay.</param>
        /// <param name="logger">Logger.</param>
        public PrimaryDatabaseResilience(DatabaseCircuitBreaker breaker, ResilienceOptions options, ILogger<PrimaryDatabaseResilience> logger)
        {
            _breaker = breaker ?? throw new ArgumentNullException(nameof(breaker));
            ArgumentNullException.ThrowIfNull(options);
            _retryDelay = options.RetryDelay;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Runs an operation with the breaker and at most one retry.</summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="operation">Name for logs.</param>
        /// <param name="attempt">One attempt, in a fresh scope (a failed transaction leaves its context unusable).</param>
        /// <param name="retryAfterUnknownCommit">Whether a <see cref="CommitOutcomeUnknownException"/> may be retried.</param>
        /// <param name="cancellationToken">The request's token.</param>
        /// <returns>The result.</returns>
        public async Task<T> ExecuteAsync<T>(string operation, Func<CancellationToken, Task<T>> attempt, bool retryAfterUnknownCommit, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(attempt);
            try
            {
                return await _breaker.ExecuteAsync(attempt, cancellationToken);
            }
            catch (Exception exception) when (MayRetry(exception, retryAfterUnknownCommit, cancellationToken))
            {
                LogRetry(_logger, operation, exception.GetType().Name);
                await Task.Delay(Jittered(_retryDelay), cancellationToken);
                return await _breaker.ExecuteAsync(attempt, cancellationToken);
            }
        }

        /// <summary>The retry rule, separately testable.</summary>
        /// <param name="exception">The failure of the first attempt.</param>
        /// <param name="retryAfterUnknownCommit">Whether an unknown commit outcome may be retried.</param>
        /// <param name="cancellationToken">The request's token.</param>
        /// <returns><c>true</c> if one retry is allowed.</returns>
        public static bool MayRetry(Exception exception, bool retryAfterUnknownCommit, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (cancellationToken.IsCancellationRequested || exception is OperationCanceledException || exception is BrokenCircuitException)
            {
                return false;
            }

            if (exception is CommitOutcomeUnknownException)
            {
                return retryAfterUnknownCommit;
            }

            return TransientDatabaseErrors.IsTransient(exception);
        }

        private static TimeSpan Jittered(TimeSpan delay)
        {
            int ms = (int)delay.TotalMilliseconds;
            return ms <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ms + RandomNumberGenerator.GetInt32(ms + 1));
        }

        [LoggerMessage(EventId = 5000, Level = LogLevel.Warning, Message = "Transient database failure in {Operation} ({ExceptionType}); retrying once.")]
        private static partial void LogRetry(ILogger logger, string operation, string exceptionType);
    }
}
