using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using UrlShortener.Core.Web;

namespace UrlShortener.Core.Database
{
    /// <summary>
    /// Protects one database dependency (LLD §6.7): a Polly circuit breaker, plus the retry rule for callers that ask
    /// for it. The breaker opens when ≥ 50% of calls fail (transient failures only) in a 10 s window with ≥ 20 calls,
    /// stays open 5 s ± 1 s, then lets one trial call through. While open, calls fail fast with
    /// <see cref="BrokenCircuitException"/>. Per instance, in memory.
    /// </summary>
    public sealed partial class DatabaseGuard
    {
        private readonly ResiliencePipeline _breaker;
        private readonly TimeSpan _retryDelay;
        private readonly ILogger _logger;

        /// <summary>Creates the guard.</summary>
        /// <param name="dependency">Name used in metrics, e.g. <c>links-primary</c>.</param>
        /// <param name="options">Thresholds and retry delay.</param>
        /// <param name="time">Clock (fake in tests).</param>
        /// <param name="onOpened">Called when the breaker opens, e.g. to clear the pool so the trial call reconnects.</param>
        /// <param name="logger">Logger for retries.</param>
        public DatabaseGuard(string dependency, ResilienceOptions options, TimeProvider time, Action? onOpened, ILogger logger)
        {
            ArgumentException.ThrowIfNullOrEmpty(dependency);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(time);

            Dependency = dependency;
            _retryDelay = options.RetryDelay;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            CircuitStateMetrics.Set(dependency, CircuitState.Closed);
            _breaker = new ResiliencePipelineBuilder { TimeProvider = time }
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = options.BreakerFailureRatio,
                    SamplingDuration = options.BreakerSamplingDuration,
                    MinimumThroughput = options.BreakerMinimumThroughput,
                    BreakDurationGenerator = _ => new ValueTask<TimeSpan>(WithJitter(options.BreakerBreakDuration, options.BreakerBreakJitter)),
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(TransientDatabaseErrors.IsTransient),
                    OnOpened = _ =>
                    {
                        onOpened?.Invoke();
                        CircuitStateMetrics.Set(dependency, CircuitState.Open);
                        return default;
                    },
                    OnHalfOpened = _ =>
                    {
                        CircuitStateMetrics.Set(dependency, CircuitState.HalfOpen);
                        return default;
                    },
                    OnClosed = _ =>
                    {
                        CircuitStateMetrics.Set(dependency, CircuitState.Closed);
                        return default;
                    },
                })
                .Build();
        }

        /// <summary>The dependency name.</summary>
        public string Dependency { get; }

        /// <summary>One call through the breaker, no retry (Redirect's primary fallback: the request budget is 500 ms).</summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="call">The database work.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result. Throws <see cref="BrokenCircuitException"/> while open.</returns>
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(call);
            return await _breaker.ExecuteAsync(async token => await call(token), cancellationToken);
        }

        /// <summary>
        /// The breaker plus at most <b>one</b> retry (LLD §6.7): only for a transient failure, never while the breaker is
        /// open, never after cancellation, and after an unknown commit outcome only if the caller says a repeat can't
        /// change the result. Each attempt must use a fresh <c>DbContext</c>: a failed transaction leaves it unusable.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="operation">Name for logs.</param>
        /// <param name="attempt">One attempt.</param>
        /// <param name="retryAfterUnknownCommit">Whether a <see cref="CommitOutcomeUnknownException"/> may be retried.</param>
        /// <param name="cancellationToken">The request's token.</param>
        /// <returns>The result.</returns>
        public async Task<T> ExecuteWithRetryAsync<T>(string operation, Func<CancellationToken, Task<T>> attempt, bool retryAfterUnknownCommit, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(attempt);
            try
            {
                return await ExecuteAsync(attempt, cancellationToken);
            }
            catch (Exception exception) when (MayRetry(exception, retryAfterUnknownCommit, cancellationToken))
            {
                LogRetry(_logger, operation, exception.GetType().Name);
                await Task.Delay(Jittered(_retryDelay), cancellationToken);
                return await ExecuteAsync(attempt, cancellationToken);
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

        private static TimeSpan WithJitter(TimeSpan duration, TimeSpan jitter)
        {
            long jitterMs = (long)jitter.TotalMilliseconds;
            long offset = jitterMs <= 0 ? 0 : RandomNumberGenerator.GetInt32(-(int)jitterMs, (int)jitterMs + 1);
            return duration + TimeSpan.FromMilliseconds(offset);
        }

        private static TimeSpan Jittered(TimeSpan delay)
        {
            int ms = (int)delay.TotalMilliseconds;
            return ms <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ms + RandomNumberGenerator.GetInt32(ms + 1));
        }

        [LoggerMessage(EventId = 5000, Level = LogLevel.Warning, Message = "Transient database failure in {Operation} ({ExceptionType}); retrying once.")]
        private static partial void LogRetry(ILogger logger, string operation, string exceptionType);
    }

    /// <summary>
    /// The <c>circuit_state</c> gauge per dependency (ADR-0013): 0 closed, 1 open, 2 half-open, plus a
    /// <c>circuit_transitions</c> counter.
    /// </summary>
    public static class CircuitStateMetrics
    {
        private static readonly ConcurrentDictionary<string, CircuitState> States = new ConcurrentDictionary<string, CircuitState>(StringComparer.Ordinal);

        private static readonly Counter<long> Transitions = AppTelemetry.Meter.CreateCounter<long>(
            "circuit_transitions", unit: "{transition}", description: "Circuit-breaker state changes.");

        private static readonly ObservableGauge<int> Gauge = AppTelemetry.Meter.CreateObservableGauge(
            "circuit_state",
            () => States.Select(pair => new Measurement<int>(Code(pair.Value), new KeyValuePair<string, object?>("dependency", pair.Key))),
            unit: "{state}",
            description: "Circuit-breaker state per dependency: 0 closed, 1 open, 2 half-open.");

        /// <summary>The current state of a dependency's breaker (tests and diagnostics).</summary>
        /// <param name="dependency">The dependency name.</param>
        /// <returns>The state, or closed if unknown.</returns>
        public static CircuitState Get(string dependency)
        {
            return States.TryGetValue(dependency, out CircuitState state) ? state : CircuitState.Closed;
        }

        internal static void Set(string dependency, CircuitState state)
        {
            _ = Gauge; // created with the class; reads States when metrics are collected
            States[dependency] = state;
            Transitions.Add(1, new KeyValuePair<string, object?>("dependency", dependency), new KeyValuePair<string, object?>("state", state.ToString()));
        }

        private static int Code(CircuitState state)
        {
            switch (state)
            {
                case CircuitState.Open:
                case CircuitState.Isolated:
                    return 1;
                case CircuitState.HalfOpen:
                    return 2;
                default:
                    return 0;
            }
        }
    }
}
