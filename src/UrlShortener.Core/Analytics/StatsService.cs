using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UrlShortener.Core.Database;
using UrlShortener.Core.Links;
using UrlShortener.Domain;

namespace UrlShortener.Core.Analytics
{
    /// <summary>One bucket of the time series.</summary>
    /// <param name="BucketStart">Start of the hour or day (UTC).</param>
    /// <param name="Clicks">Clicks in the bucket.</param>
    public sealed record StatsBucket(DateTimeOffset BucketStart, long Clicks);

    /// <summary>Clicks from one referring host.</summary>
    /// <param name="Host">The referrer's host.</param>
    /// <param name="Clicks">Clicks in the range.</param>
    public sealed record ReferrerClicks(string Host, long Clicks);

    /// <summary>The stats response (LLD §3).</summary>
    /// <param name="Code">The short code.</param>
    /// <param name="From">Start of the range (inclusive, UTC).</param>
    /// <param name="To">End of the range (exclusive, UTC).</param>
    /// <param name="Granularity"><c>hour</c> or <c>day</c>.</param>
    /// <param name="TotalClicks">Clicks in the range.</param>
    /// <param name="DataFreshAsOf">When the worker last caught up, or <c>null</c> if it never ran.</param>
    /// <param name="Series">Buckets that had clicks, oldest first.</param>
    /// <param name="TopReferrers">Up to 10 referring hosts, most clicks first.</param>
    /// <param name="Devices">Clicks per device class.</param>
    public sealed record LinkStats(
        string Code,
        DateTimeOffset From,
        DateTimeOffset To,
        string Granularity,
        long TotalClicks,
        DateTimeOffset? DataFreshAsOf,
        IReadOnlyList<StatsBucket> Series,
        IReadOnlyList<ReferrerClicks> TopReferrers,
        IReadOnlyDictionary<string, long> Devices);

    /// <summary>
    /// Click statistics for a link's owner (FR-07, FR-09). Ownership is checked on the primary, the counts come from the
    /// analytics rollups. "Not found" and "someone else's" are the same answer.
    /// </summary>
    public sealed class StatsService
    {
        /// <summary>Longest range one request may ask for (LLD §3).</summary>
        public static readonly TimeSpan MaxRange = TimeSpan.FromDays(90);

        /// <summary>Range used when the caller sends no <c>from</c>.</summary>
        public static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);

        private const string OwnerSql = "SELECT owner_id FROM short_urls WHERE code = @code";

        private const string SeriesSql =
            "SELECT date_trunc(@unit, bucket_hour AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS bucket, sum(clicks)::bigint " +
            "FROM analytics.click_rollups_hourly WHERE code = @code AND bucket_hour >= @from AND bucket_hour < @to GROUP BY bucket ORDER BY bucket";

        private const string ReferrersSql =
            "SELECT referrer_host, sum(clicks)::bigint AS total FROM analytics.click_rollups_referrer " +
            "WHERE code = @code AND bucket_hour >= @from AND bucket_hour < @to GROUP BY referrer_host ORDER BY total DESC, referrer_host LIMIT 10";

        private const string DevicesSql =
            "SELECT device, sum(clicks)::bigint FROM analytics.click_rollups_device " +
            "WHERE code = @code AND bucket_hour >= @from AND bucket_hour < @to GROUP BY device ORDER BY device";

        private const string FreshnessSql =
            "SELECT min(updated_at) FROM analytics.consumer_offsets WHERE consumer_group = @group";

        private readonly PrimaryDatabase _primary;
        private readonly AnalyticsDatabase _analytics;
        private readonly string _consumerGroup;
        private readonly TimeProvider _time;

        /// <summary>Creates the service.</summary>
        /// <param name="primary">The links primary (ownership).</param>
        /// <param name="analytics">The analytics store (counts).</param>
        /// <param name="kafka">Kafka settings (the worker's consumer group).</param>
        /// <param name="time">Clock.</param>
        public StatsService(PrimaryDatabase primary, AnalyticsDatabase analytics, KafkaOptions kafka, TimeProvider time)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            ArgumentNullException.ThrowIfNull(kafka);
            _consumerGroup = kafka.ConsumerGroup;
            _time = time ?? throw new ArgumentNullException(nameof(time));
        }

        /// <summary>The caller's stats, or <c>null</c> if the link doesn't exist or isn't the caller's.</summary>
        /// <param name="ownerId">The authenticated caller.</param>
        /// <param name="code">The path segment, unvalidated.</param>
        /// <param name="from">Range start, or <c>null</c> for 7 days before <paramref name="to"/>.</param>
        /// <param name="to">Range end, or <c>null</c> for now.</param>
        /// <param name="granularity"><c>hour</c> or <c>day</c>; <c>null</c> means <c>day</c>.</param>
        /// <param name="cancellationToken">The request's token.</param>
        /// <returns>The stats, or <c>null</c>. A bad range or granularity throws <see cref="DomainValidationException"/>.</returns>
        public async Task<LinkStats?> GetAsync(string ownerId, string? code, DateTimeOffset? from, DateTimeOffset? to, string? granularity, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ownerId);
            string unit = granularity ?? "day";
            if (unit != "hour" && unit != "day")
            {
                throw new DomainValidationException(ErrorCodes.StatsRange, "granularity must be 'hour' or 'day'.");
            }

            DateTimeOffset end = (to ?? _time.GetUtcNow()).ToUniversalTime();
            DateTimeOffset start = (from ?? end - DefaultRange).ToUniversalTime();
            if (start >= end || end - start > MaxRange)
            {
                throw new DomainValidationException(ErrorCodes.StatsRange, "from must be before to, and the range at most 90 days.");
            }

            if (!ShortCode.TryParse(code, out ShortCode? parsed) || !await IsOwnerAsync(ownerId, parsed.Value, cancellationToken))
            {
                return null;
            }

            await using NpgsqlConnection connection = await _analytics.DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand seriesCommand = new NpgsqlCommand(SeriesSql, connection);
            List<StatsBucket> series = await ReadAsync(
                seriesCommand, parsed.Value, start, end, unit, r => new StatsBucket(new DateTimeOffset(r.GetDateTime(0), TimeSpan.Zero), r.GetInt64(1)), cancellationToken);
            await using NpgsqlCommand referrersCommand = new NpgsqlCommand(ReferrersSql, connection);
            List<ReferrerClicks> referrers = await ReadAsync(
                referrersCommand, parsed.Value, start, end, unit, r => new ReferrerClicks(r.GetString(0), r.GetInt64(1)), cancellationToken);
            await using NpgsqlCommand devicesCommand = new NpgsqlCommand(DevicesSql, connection);
            List<KeyValuePair<string, long>> devices = await ReadAsync(
                devicesCommand, parsed.Value, start, end, unit, r => new KeyValuePair<string, long>(r.GetString(0), r.GetInt64(1)), cancellationToken);

            await using NpgsqlCommand freshness = new NpgsqlCommand(FreshnessSql, connection);
            freshness.Parameters.AddWithValue("group", _consumerGroup);
            object? freshAsOf = await freshness.ExecuteScalarAsync(cancellationToken);

            return new LinkStats(
                parsed.Value,
                start,
                end,
                unit,
                series.Sum(bucket => bucket.Clicks),
                freshAsOf is DateTime asOf ? new DateTimeOffset(asOf, TimeSpan.Zero) : null,
                series,
                referrers,
                new Dictionary<string, long>(devices, StringComparer.Ordinal));
        }

        private static async Task<List<T>> ReadAsync<T>(NpgsqlCommand command, string code, DateTimeOffset from, DateTimeOffset to, string unit, Func<NpgsqlDataReader, T> map, CancellationToken cancellationToken)
        {
            command.Parameters.AddWithValue("code", code);
            command.Parameters.AddWithValue("from", from);
            command.Parameters.AddWithValue("to", to);
            command.Parameters.AddWithValue("unit", unit);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            List<T> rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
        }

        private async Task<bool> IsOwnerAsync(string ownerId, string code, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand command = _primary.DataSource.CreateCommand(OwnerSql);
            command.Parameters.AddWithValue("code", code);
            object? owner = await command.ExecuteScalarAsync(cancellationToken);
            return owner is string stored && string.Equals(stored, ownerId, StringComparison.Ordinal);
        }
    }
}
