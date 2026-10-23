using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.CircuitBreaker;
using UrlShortener.Infrastructure.Observability;

namespace UrlShortener.Infrastructure.Resilience
{
    /// <summary>
    /// A circuit breaker for one database dependency (LLD §6.7), on Polly v8. Opens when ≥ 50% of calls fail
    /// (transient failures only) in a 10 s window with ≥ 20 calls; stays open 5 s ± 1 s; then one trial call.
    /// While open, calls fail fast with <see cref="BrokenCircuitException"/>. Per instance and in memory.
    /// </summary>
    public sealed class DatabaseCircuitBreaker
    {
        private readonly ResiliencePipeline _pipeline;

        /// <summary>Creates the breaker.</summary>
        /// <param name="dependency">Name used in metrics, e.g. <c>links-primary</c>.</param>
        /// <param name="options">Thresholds.</param>
        /// <param name="time">Clock (fake in tests).</param>
        /// <param name="onOpened">Called when it opens, e.g. to clear the connection pool so the trial call reconnects.</param>
        public DatabaseCircuitBreaker(string dependency, ResilienceOptions options, TimeProvider time, Action? onOpened)
        {
            ArgumentException.ThrowIfNullOrEmpty(dependency);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(time);

            Dependency = dependency;
            CircuitStateMetrics.Set(dependency, CircuitState.Closed);
            _pipeline = new ResiliencePipelineBuilder { TimeProvider = time }
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

        /// <summary>Runs one call through the breaker.</summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="call">The database work.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result. Throws <see cref="BrokenCircuitException"/> while open.</returns>
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(call);
            return await _pipeline.ExecuteAsync(async token => await call(token), cancellationToken);
        }

        private static TimeSpan WithJitter(TimeSpan duration, TimeSpan jitter)
        {
            long jitterMs = (long)jitter.TotalMilliseconds;
            long offset = jitterMs <= 0 ? 0 : RandomNumberGenerator.GetInt32(-(int)jitterMs, (int)jitterMs + 1);
            return duration + TimeSpan.FromMilliseconds(offset);
        }
    }

    /// <summary>
    /// The <c>circuit_state</c> gauge per dependency (ADR-0013): 0 closed, 1 open, 2 half-open, plus a
    /// <c>circuit_transitions</c> counter.
    /// </summary>
    public static class CircuitStateMetrics
    {
        private static readonly ConcurrentDictionary<string, CircuitState> States = new ConcurrentDictionary<string, CircuitState>(StringComparer.Ordinal);

        private static readonly Counter<long> Transitions = UrlShortenerMeters.Meter.CreateCounter<long>(
            "circuit_transitions", unit: "{transition}", description: "Circuit-breaker state changes.");

        private static readonly ObservableGauge<int> Gauge = UrlShortenerMeters.Meter.CreateObservableGauge(
            "circuit_state",
            () => States.Select(pair => new Measurement<int>(Code(pair.Value), new KeyValuePair<string, object?>("dependency", pair.Key))),
            unit: "{state}",
            description: "Circuit-breaker state per dependency: 0 closed, 1 open, 2 half-open.");

        /// <summary>The current state of a dependency's breaker (for tests and diagnostics).</summary>
        /// <param name="dependency">The dependency name.</param>
        /// <returns>The state, or closed if unknown.</returns>
        public static CircuitState Get(string dependency)
        {
            return States.TryGetValue(dependency, out CircuitState state) ? state : CircuitState.Closed;
        }

        internal static void Set(string dependency, CircuitState state)
        {
            _ = Gauge; // the gauge is created with the class and reads States when metrics are collected
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
