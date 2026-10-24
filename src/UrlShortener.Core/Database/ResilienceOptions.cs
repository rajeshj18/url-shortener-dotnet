using System;
using Microsoft.Extensions.Configuration;

namespace UrlShortener.Core.Database
{
    /// <summary>
    /// Configuration section <c>Resilience</c> (LLD §6.7, §8). Defaults are the LLD values. Database timeouts in a
    /// connection string win over these defaults.
    /// </summary>
    public sealed class ResilienceOptions
    {
        /// <summary>Configuration section name.</summary>
        public const string SectionName = "Resilience";

        /// <summary>Whole-request budget; on expiry the request gets <c>503</c>. Api 3 s; Redirect 500 ms.</summary>
        public TimeSpan? RequestTimeout { get; set; }

        /// <summary>Opening a database connection (seconds).</summary>
        public int ConnectTimeoutSeconds { get; set; } = 2;

        /// <summary>One database command, client side (seconds; Npgsql's minimum is 1).</summary>
        public int CommandTimeoutSeconds { get; set; } = 1;

        /// <summary>Server-side guard for the Api's statements (TSK-11 D1).</summary>
        public int StatementTimeoutMilliseconds { get; set; } = 2000;

        /// <summary>Pool cap of the Api's writer connection (TSK-11b): a full pool makes callers wait, never grows.</summary>
        public int ApiPrimaryMaxPoolSize { get; set; } = 30;

        /// <summary>Pool cap of Redirect's replica connection.</summary>
        public int RedirectReplicaMaxPoolSize { get; set; } = 30;

        /// <summary>Pool cap of Redirect's primary-fallback connection.</summary>
        public int RedirectPrimaryMaxPoolSize { get; set; } = 10;

        /// <summary>Pool cap of the analytics connection (worker and stats).</summary>
        public int AnalyticsMaxPoolSize { get; set; } = 10;

        /// <summary>Wait before the single server-side retry (plus up to the same again as jitter).</summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>The breaker opens when at least this share of calls fails…</summary>
        public double BreakerFailureRatio { get; set; } = 0.5;

        /// <summary>…within this window…</summary>
        public TimeSpan BreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>…with at least this many calls in the window (Polly's minimum is 2).</summary>
        public int BreakerMinimumThroughput { get; set; } = 20;

        /// <summary>How long it stays open, before one trial call…</summary>
        public TimeSpan BreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>…plus or minus a random part of this, so instances don't probe in lockstep.</summary>
        public TimeSpan BreakerBreakJitter { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>How often expired idempotency keys are deleted (LLD §6.6).</summary>
        public TimeSpan IdempotencyCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>Rows per delete batch.</summary>
        public int IdempotencyCleanupBatchSize { get; set; } = 50_000;

        /// <summary>Time limit per delete batch (seconds).</summary>
        public int IdempotencyCleanupBatchTimeoutSeconds { get; set; } = 10;

        /// <summary>Binds the <c>Resilience</c> section over the defaults.</summary>
        /// <param name="configuration">Configuration.</param>
        /// <returns>The options.</returns>
        public static ResilienceOptions Read(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ResilienceOptions options = new ResilienceOptions();
            configuration.GetSection(SectionName).Bind(options);
            return options;
        }
    }
}
