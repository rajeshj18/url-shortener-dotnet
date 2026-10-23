using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using UrlShortener.Application.Ports;
using UrlShortener.Infrastructure.Observability;

namespace UrlShortener.Infrastructure.Redirects
{
    /// <summary>
    /// A token bucket per instance: up to <c>perSecond</c> primary fallbacks per second, refilled continuously, with a
    /// burst of one second's worth (LLD §6.2). Driven by <see cref="TimeProvider"/>, so it's testable without waiting.
    /// Thread-safe.
    /// </summary>
    public sealed class TokenBucketFallbackBudget : IPrimaryFallbackBudget
    {
        /// <summary>Counter of fallback decisions, tagged <c>outcome=granted|denied</c> (ADR-0006 scan criterion, TSK-12).</summary>
        public const string DecisionsCounterName = "redirect_primary_fallback";

        private static readonly Counter<long> Decisions = UrlShortenerMeters.Meter.CreateCounter<long>(
            DecisionsCounterName, unit: "{decision}", description: "Replica misses that were (granted) or weren't (denied) allowed to query the primary.");

        private static readonly KeyValuePair<string, object?> Granted = new KeyValuePair<string, object?>("outcome", "granted");
        private static readonly KeyValuePair<string, object?> Denied = new KeyValuePair<string, object?>("outcome", "denied");

        private readonly object _gate = new object();
        private readonly double _perSecond;
        private readonly TimeProvider _time;
        private double _tokens;
        private long _lastTimestamp;

        /// <summary>Creates a full bucket.</summary>
        /// <param name="perSecond">Fallbacks allowed per second (≥ 1).</param>
        /// <param name="time">Clock.</param>
        public TokenBucketFallbackBudget(int perSecond, TimeProvider time)
        {
            if (perSecond < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(perSecond), "The fallback budget must allow at least 1 per second.");
            }

            _time = time ?? throw new ArgumentNullException(nameof(time));
            _perSecond = perSecond;
            _tokens = perSecond;
            _lastTimestamp = time.GetTimestamp();
        }

        /// <inheritdoc />
        public bool TryAcquire()
        {
            lock (_gate)
            {
                long now = _time.GetTimestamp();
                double elapsedSeconds = _time.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
                _lastTimestamp = now;
                _tokens = Math.Min(_perSecond, _tokens + (elapsedSeconds * _perSecond));

                if (_tokens < 1)
                {
                    Decisions.Add(1, Denied);
                    return false;
                }

                _tokens -= 1;
                Decisions.Add(1, Granted);
                return true;
            }
        }
    }
}
