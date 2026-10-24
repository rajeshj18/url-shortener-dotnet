using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using UrlShortener.Core.Database;
using UrlShortener.Core.Web;

namespace UrlShortener.Core.Analytics
{
    /// <summary>One consumed Kafka record: where it came from, and its value.</summary>
    /// <param name="Partition">The partition.</param>
    /// <param name="Offset">The record's offset in the partition.</param>
    /// <param name="Value">The JSON value.</param>
    public sealed record ClickRecord(int Partition, long Offset, string? Value);

    /// <summary>
    /// The AnalyticsWorker loop (LLD §6.5, NFR-08). It consumes click events in batches and adds them to hourly rollups.
    /// <b>Exactly once:</b> the next offset of every partition is stored in the database, in the <b>same transaction</b>
    /// as the counts. Records below it are skipped, so a replay after a crash or a rebalance never counts a click twice.
    /// Committing offsets back to Kafka is informational only.
    /// </summary>
    public sealed partial class ClickConsumer : BackgroundService
    {
        private const string EnsureOffsetSql =
            "INSERT INTO analytics.consumer_offsets (consumer_group, topic, partition_id, next_offset, updated_at) VALUES (@group, @topic, @partition, 0, @now) " +
            "ON CONFLICT DO NOTHING";

        private const string LockOffsetSql =
            "SELECT next_offset FROM analytics.consumer_offsets WHERE consumer_group = @group AND topic = @topic AND partition_id = @partition FOR UPDATE";

        private const string SaveOffsetSql =
            "UPDATE analytics.consumer_offsets SET next_offset = @next, updated_at = @now WHERE consumer_group = @group AND topic = @topic AND partition_id = @partition";

        private const string TouchOffsetsSql =
            "UPDATE analytics.consumer_offsets SET updated_at = @now WHERE consumer_group = @group AND topic = @topic";

        private const string ReadOffsetSql =
            "SELECT next_offset FROM analytics.consumer_offsets WHERE consumer_group = @group AND topic = @topic AND partition_id = @partition";

        private const string UpsertHourlySql =
            "INSERT INTO analytics.click_rollups_hourly (code, bucket_hour, clicks) VALUES (@code, @hour, @clicks) " +
            "ON CONFLICT (code, bucket_hour) DO UPDATE SET clicks = analytics.click_rollups_hourly.clicks + EXCLUDED.clicks";

        private const string UpsertReferrerSql =
            "INSERT INTO analytics.click_rollups_referrer (code, bucket_hour, referrer_host, clicks) VALUES (@code, @hour, @key, @clicks) " +
            "ON CONFLICT (code, bucket_hour, referrer_host) DO UPDATE SET clicks = analytics.click_rollups_referrer.clicks + EXCLUDED.clicks";

        private const string UpsertDeviceSql =
            "INSERT INTO analytics.click_rollups_device (code, bucket_hour, device, clicks) VALUES (@code, @hour, @key, @clicks) " +
            "ON CONFLICT (code, bucket_hour, device) DO UPDATE SET clicks = analytics.click_rollups_device.clicks + EXCLUDED.clicks";

        private static readonly Counter<long> Counted = AppTelemetry.Meter.CreateCounter<long>(
            "click_events_counted", unit: "{event}", description: "Click events added to the rollups (each exactly once).");

        private static readonly Counter<long> Skipped = AppTelemetry.Meter.CreateCounter<long>(
            "click_events_skipped", unit: "{event}", description: "Records skipped: already counted (replay) or not a valid v1 event.");

        private static readonly Histogram<double> Lag = AppTelemetry.Meter.CreateHistogram<double>(
            "click_event_lag", unit: "s", description: "Time from the redirect to the click being counted (freshness, NFR-08).");

        private readonly AnalyticsDatabase _database;
        private readonly KafkaOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<ClickConsumer> _logger;

        /// <summary>Creates the consumer.</summary>
        /// <param name="database">The analytics store (worker role).</param>
        /// <param name="options">Kafka and batch settings.</param>
        /// <param name="time">Clock.</param>
        /// <param name="logger">Logger.</param>
        public ClickConsumer(AnalyticsDatabase database, KafkaOptions options, TimeProvider time, ILogger<ClickConsumer> logger)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Adds one batch to the rollups, in one transaction with the partitions' next offsets. Records whose offset is
        /// below the stored next offset were counted before and are skipped.
        /// </summary>
        /// <param name="batch">Records, in offset order per partition.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of clicks counted.</returns>
        public async Task<int> ApplyBatchAsync(IReadOnlyCollection<ClickRecord> batch, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(batch);
            DateTimeOffset now = _time.GetUtcNow();
            await using NpgsqlConnection connection = await _database.DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            List<ClickEvent> fresh = new List<ClickEvent>();
            int skipped = 0;
            foreach (IGrouping<int, ClickRecord> partition in batch.GroupBy(record => record.Partition))
            {
                long next = await LockOffsetAsync(connection, transaction, partition.Key, now, cancellationToken);
                long last = next - 1;
                foreach (ClickRecord record in partition.Where(record => record.Offset >= next))
                {
                    ClickEvent? click = ClickPublisher.Parse(record.Value);
                    if (click == null)
                    {
                        skipped++; // a malformed record is passed over, never retried for ever
                    }
                    else
                    {
                        fresh.Add(click);
                    }

                    last = Math.Max(last, record.Offset);
                }

                skipped += partition.Count(record => record.Offset < next);
                if (last >= next)
                {
                    await using (NpgsqlCommand command = new NpgsqlCommand(SaveOffsetSql, connection, transaction))
                    {
                        await ExecuteAsync(command, cancellationToken, ("next", last + 1), ("now", now), ("partition", partition.Key));
                    }
                }
            }

            // Aggregated in memory first: one upsert per code and hour per batch, even for a viral link.
            foreach (IGrouping<(string Code, DateTimeOffset Hour), ClickEvent> group in fresh.GroupBy(click => (click.Code, Hour(click.OccurredAt))))
            {
                await using (NpgsqlCommand command = new NpgsqlCommand(UpsertHourlySql, connection, transaction))
                {
                    await UpsertAsync(command, group.Key.Code, group.Key.Hour, null, group.Count(), cancellationToken);
                }
                foreach (IGrouping<string, ClickEvent> byReferrer in group.Where(click => click.ReferrerHost != null).GroupBy(click => click.ReferrerHost!))
                {
                    await using (NpgsqlCommand command = new NpgsqlCommand(UpsertReferrerSql, connection, transaction))
                    {
                        await UpsertAsync(command, group.Key.Code, group.Key.Hour, byReferrer.Key, byReferrer.Count(), cancellationToken);
                    }
                }

                foreach (IGrouping<string, ClickEvent> byDevice in group.GroupBy(click => click.Device))
                {
                    await using (NpgsqlCommand command = new NpgsqlCommand(UpsertDeviceSql, connection, transaction))
                    {
                        await UpsertAsync(command, group.Key.Code, group.Key.Hour, byDevice.Key, byDevice.Count(), cancellationToken);
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken);

            Counted.Add(fresh.Count);
            Skipped.Add(skipped);
            foreach (ClickEvent click in fresh)
            {
                Lag.Record(Math.Max(0, (now - click.OccurredAt).TotalSeconds));
            }

            return fresh.Count;
        }

        /// <summary>Marks the data as fresh when a poll found nothing new (feeds the stats' <c>dataFreshAsOf</c>).</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task TouchAsync(CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _database.DataSource.OpenConnectionAsync(cancellationToken);
            await using (NpgsqlCommand command = new NpgsqlCommand(TouchOffsetsSql, connection, null))
            {
                await ExecuteAsync(command, cancellationToken, ("now", _time.GetUtcNow()));
            }
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                LogDisabled(_logger);
                return;
            }

            await Task.Yield(); // Consume blocks; don't hold up host start-up
            using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                GroupId = _options.ConsumerGroup,
                EnableAutoCommit = false,
                EnableAutoOffsetStore = false,
                AutoOffsetReset = AutoOffsetReset.Earliest,
            })
                .SetPartitionsAssignedHandler((_, partitions) => partitions.Select(SeekToStoredOffset))
                .Build();
            consumer.Subscribe(_options.Topic);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    List<ConsumeResult<string, string>> batch = Collect(consumer, stoppingToken);
                    if (batch.Count == 0)
                    {
                        await TouchAsync(stoppingToken);
                        continue;
                    }

                    // One span per batch, linked to the redirects that produced its events (ADR-0013: traces across Kafka).
                    using (AppTelemetry.Source.StartActivity("click-batch", ActivityKind.Consumer, default(ActivityContext), links: TraceLinks(batch)))
                    {
                        await ApplyBatchAsync(batch.Select(r => new ClickRecord(r.Partition.Value, r.Offset.Value, r.Message.Value)).ToList(), stoppingToken);
                    }

                    CommitToKafka(consumer, batch);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (TransientDatabaseErrors.IsTransient(exception) || exception is KafkaException)
                {
                    // The transaction rolled back, so nothing was counted. Re-read from the stored offsets and try again.
                    LogBatchFailed(_logger, exception.GetType().Name);
                    foreach (TopicPartition partition in consumer.Assignment)
                    {
                        consumer.Seek(SeekToStoredOffset(partition));
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }

            consumer.Close();
        }

        private static DateTimeOffset Hour(DateTimeOffset occurredAt)
        {
            DateTimeOffset utc = occurredAt.ToUniversalTime();
            return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
        }

        private static List<ActivityLink> TraceLinks(List<ConsumeResult<string, string>> batch)
        {
            List<ActivityLink> links = new List<ActivityLink>();
            foreach (ConsumeResult<string, string> result in batch.Take(32))
            {
                if (result.Message.Headers != null
                    && result.Message.Headers.TryGetLastBytes("traceparent", out byte[] header)
                    && ActivityContext.TryParse(System.Text.Encoding.ASCII.GetString(header), null, out ActivityContext context))
                {
                    links.Add(new ActivityLink(context));
                }
            }

            return links;
        }

        private List<ConsumeResult<string, string>> Collect(IConsumer<string, string> consumer, CancellationToken stoppingToken)
        {
            List<ConsumeResult<string, string>> batch = new List<ConsumeResult<string, string>>();
            Stopwatch watch = Stopwatch.StartNew();
            while (batch.Count < _options.BatchMaxRecords && watch.Elapsed < _options.BatchMaxDuration && !stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = consumer.Consume(_options.BatchMaxDuration - watch.Elapsed);
                if (result == null)
                {
                    break;
                }

                if (!result.IsPartitionEOF)
                {
                    batch.Add(result);
                }
            }

            return batch;
        }

        private void CommitToKafka(IConsumer<string, string> consumer, List<ConsumeResult<string, string>> batch)
        {
            try
            {
                consumer.Commit(batch
                    .GroupBy(r => r.TopicPartition)
                    .Select(g => new TopicPartitionOffset(g.Key, new Offset(g.Max(r => r.Offset.Value) + 1))));
            }
            catch (KafkaException exception)
            {
                LogKafkaCommitFailed(_logger, exception.Error.Reason); // informational only; the database holds the truth
            }
        }

        // Runs on Kafka's rebalance callback, which is synchronous.
        private TopicPartitionOffset SeekToStoredOffset(TopicPartition partition)
        {
            using NpgsqlConnection connection = _database.DataSource.OpenConnection();
            using NpgsqlCommand command = new NpgsqlCommand(ReadOffsetSql, connection);
            command.Parameters.AddWithValue("group", _options.ConsumerGroup);
            command.Parameters.AddWithValue("topic", _options.Topic);
            command.Parameters.AddWithValue("partition", partition.Partition.Value);
            object? next = command.ExecuteScalar();
            return new TopicPartitionOffset(partition, next == null ? Offset.Beginning : new Offset((long)next));
        }

        private async Task<long> LockOffsetAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int partition, DateTimeOffset now, CancellationToken cancellationToken)
        {
            await using (NpgsqlCommand ensure = new NpgsqlCommand(EnsureOffsetSql, connection, transaction))
            {
                await ExecuteAsync(ensure, cancellationToken, ("partition", partition), ("now", now));
            }

            await using NpgsqlCommand select = new NpgsqlCommand(LockOffsetSql, connection, transaction);
            AddParameters(select, ("partition", partition));
            return (long)(await select.ExecuteScalarAsync(cancellationToken))!;
        }

        // The caller builds the command from a constant (no SQL is ever composed from input) and disposes it.
        private async Task ExecuteAsync(NpgsqlCommand command, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
        {
            AddParameters(command, parameters);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Like ExecuteAsync: the caller owns the command.
        private static async Task UpsertAsync(NpgsqlCommand command, string code, DateTimeOffset hour, string? key, long clicks, CancellationToken cancellationToken)
        {
            command.Parameters.AddWithValue("code", code);
            command.Parameters.AddWithValue("hour", hour);
            command.Parameters.AddWithValue("clicks", clicks);
            if (key != null)
            {
                command.Parameters.AddWithValue("key", key);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private void AddParameters(NpgsqlCommand command, params (string Name, object Value)[] parameters)
        {
            command.Parameters.AddWithValue("group", _options.ConsumerGroup);
            command.Parameters.AddWithValue("topic", _options.Topic);
            foreach ((string name, object value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }
        }

        [LoggerMessage(EventId = 7000, Level = LogLevel.Warning, Message = "Kafka:BootstrapServers is not set; the AnalyticsWorker consumes nothing.")]
        private static partial void LogDisabled(ILogger logger);

        [LoggerMessage(EventId = 7001, Level = LogLevel.Warning, Message = "Click batch failed ({ExceptionType}); nothing was counted, retrying from the stored offsets.")]
        private static partial void LogBatchFailed(ILogger logger, string exceptionType);

        [LoggerMessage(EventId = 7002, Level = LogLevel.Information, Message = "Committing offsets to Kafka failed ({Reason}); harmless, the database holds the offsets.")]
        private static partial void LogKafkaCommitFailed(ILogger logger, string reason);
    }
}
