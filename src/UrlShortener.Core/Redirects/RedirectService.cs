using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Database;
using UrlShortener.Core.Web;
using UrlShortener.Domain;

namespace UrlShortener.Core.Redirects
{
    /// <summary>How a redirect request ends.</summary>
    public enum ResolveOutcome
    {
        /// <summary><c>302</c> to the long URL.</summary>
        Redirect,

        /// <summary><c>404</c>: unknown or malformed code.</summary>
        NotFound,

        /// <summary><c>410</c>: deleted or expired.</summary>
        Gone,
    }

    /// <summary>The result of resolving a code.</summary>
    /// <param name="Outcome">What happened.</param>
    /// <param name="Target">The long URL exactly as created; set only for <see cref="ResolveOutcome.Redirect"/>.</param>
    public sealed record ResolveResult(ResolveOutcome Outcome, Uri? Target);

    /// <summary>
    /// The redirect use case (FR-03, LLD §6.2, ADR-0007). A malformed code is answered without any database call. The
    /// lookup reads the <c>redirect_lookup</c> view (the read-only Redirect user can't see anything else) on the
    /// <b>replicas</b>; a miss falls back to the <b>primary</b> while a token bucket allows (read-your-writes, NFR-07),
    /// through its own breaker. A click is published only for <c>GET</c> requests from non-bots, and publishing never
    /// blocks or fails the redirect.
    /// </summary>
    public sealed class RedirectService
    {
        /// <summary>Counter of fallback decisions, tagged <c>outcome=granted|denied</c>.</summary>
        public const string FallbackCounterName = "redirect_primary_fallback";

        // The only statement the Redirect user can run (ADR-0009).
        private const string LookupSql = "SELECT long_url, deleted_at, expires_at FROM redirect_lookup WHERE code = $1";

        private static readonly Counter<long> FallbackDecisions = AppTelemetry.Meter.CreateCounter<long>(
            FallbackCounterName, unit: "{decision}", description: "Replica misses that were (granted) or weren't (denied) allowed to query the primary.");

        private readonly ReplicaDatabase _replicas;
        private readonly PrimaryDatabase _primary;
        private readonly DatabaseGuard _primaryGuard;
        private readonly ClickPublisher _clicks;
        private readonly TimeProvider _time;
        private readonly object _bucketGate = new object();
        private readonly double _fallbacksPerSecond;
        private double _tokens;
        private long _lastRefill;

        /// <summary>Creates the service.</summary>
        /// <param name="replicas">The normal read path.</param>
        /// <param name="primary">The read-your-writes fallback.</param>
        /// <param name="primaryGuard">The fallback's breaker.</param>
        /// <param name="clicks">Click events.</param>
        /// <param name="fallbacksPerSecond">Primary fallbacks allowed per second and instance, with a burst of one second's worth.</param>
        /// <param name="time">Clock.</param>
        public RedirectService(ReplicaDatabase replicas, PrimaryDatabase primary, DatabaseGuard primaryGuard, ClickPublisher clicks, int fallbacksPerSecond, TimeProvider time)
        {
            _replicas = replicas ?? throw new ArgumentNullException(nameof(replicas));
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _primaryGuard = primaryGuard ?? throw new ArgumentNullException(nameof(primaryGuard));
            _clicks = clicks ?? throw new ArgumentNullException(nameof(clicks));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            if (fallbacksPerSecond < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fallbacksPerSecond), "The fallback budget must allow at least 1 per second.");
            }

            _fallbacksPerSecond = fallbacksPerSecond;
            _tokens = fallbacksPerSecond;
            _lastRefill = time.GetTimestamp();
        }

        /// <summary>Resolves a code.</summary>
        /// <param name="code">The path segment, unvalidated.</param>
        /// <param name="isHead"><c>true</c> for <c>HEAD</c>: the same answer, but no click.</param>
        /// <param name="userAgent">The <c>User-Agent</c> header, used only to classify the client.</param>
        /// <param name="referrer">The <c>Referer</c> header, of which only the host is kept.</param>
        /// <param name="cancellationToken">The request's token.</param>
        /// <returns>The outcome.</returns>
        public async Task<ResolveResult> ResolveAsync(string? code, bool isHead, string? userAgent, string? referrer, CancellationToken cancellationToken)
        {
            if (!ShortCode.TryParse(code, out ShortCode? parsed))
            {
                return new ResolveResult(ResolveOutcome.NotFound, null); // no database call for malformed codes
            }

            Row? row = await LookupAsync(_replicas.DataSource, parsed, cancellationToken);
            if (row == null && TryTakeFallback())
            {
                // Possibly created a moment ago and not yet on the replica.
                row = await _primaryGuard.ExecuteAsync(token => LookupAsync(_primary.DataSource, parsed, token), cancellationToken);
            }

            if (row == null)
            {
                return new ResolveResult(ResolveOutcome.NotFound, null);
            }

            DateTimeOffset now = _time.GetUtcNow();
            if (ShortUrl.ComputeState(row.DeletedAt, row.ExpiresAt, now) != LinkState.Active)
            {
                return new ResolveResult(ResolveOutcome.Gone, null);
            }

            string? device = isHead ? null : ClickClassifier.Device(userAgent);
            if (device != null)
            {
                _clicks.TryPublish(new ClickEvent(1, UuidV7.Create(now), parsed.Value, now, ClickClassifier.ReferrerHost(referrer), device));
            }

            return new ResolveResult(ResolveOutcome.Redirect, row.Target.Uri);
        }

        /// <summary>Takes one primary fallback from the token bucket, if any is left. Thread-safe.</summary>
        /// <returns><c>true</c> if the primary may be queried now.</returns>
        public bool TryTakeFallback()
        {
            lock (_bucketGate)
            {
                long now = _time.GetTimestamp();
                _tokens = Math.Min(_fallbacksPerSecond, _tokens + (_time.GetElapsedTime(_lastRefill, now).TotalSeconds * _fallbacksPerSecond));
                _lastRefill = now;
                if (_tokens < 1)
                {
                    FallbackDecisions.Add(1, new KeyValuePair<string, object?>("outcome", "denied"));
                    return false;
                }

                _tokens -= 1;
                FallbackDecisions.Add(1, new KeyValuePair<string, object?>("outcome", "granted"));
                return true;
            }
        }

        private static async Task<Row?> LookupAsync(NpgsqlDataSource dataSource, ShortCode code, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand command = dataSource.CreateCommand(LookupSql);
            command.Parameters.Add(new NpgsqlParameter { Value = code.Value });
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            LongUrl target;
            try
            {
                target = LongUrl.Create(reader.GetString(0));
            }
            catch (DomainValidationException exception)
            {
                // A stored URL that breaks the rules is a server-side data problem, not a bad request (TSK-QW #7).
                throw new StoredDataInvalidException($"Stored link '{code.Value}' breaks the domain rule '{exception.Code}'.", exception);
            }

            DateTimeOffset? deletedAt = await reader.IsDBNullAsync(1, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken);
            DateTimeOffset? expiresAt = await reader.IsDBNullAsync(2, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken);
            return new Row(target, deletedAt, expiresAt);
        }

        private sealed record Row(LongUrl Target, DateTimeOffset? DeletedAt, DateTimeOffset? ExpiresAt);
    }
}
