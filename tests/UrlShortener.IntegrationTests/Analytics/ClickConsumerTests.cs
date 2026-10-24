using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Database;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Analytics
{
    /// <summary>
    /// NFR-08 on real PostgreSQL, as the worker role: a batch becomes hourly, referrer and device rollups in one
    /// transaction with the partition offsets, so replaying records (after a crash or a rebalance) never counts a click
    /// twice. Each test uses its own consumer group, so offsets never collide.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class ClickConsumerTests
    {
        private static readonly DateTimeOffset Hour = new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

        private readonly PostgresFixture _db;

        public ClickConsumerTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public async Task A_batch_becomes_hourly_referrer_and_device_rollups()
        {
            using Worker worker = new Worker(_db.WorkerConnection);
            string code = TestCodes.NewGenerated().Value;
            List<ClickRecord> batch = new List<ClickRecord>
            {
                Record(0, 0, code, Hour.AddMinutes(1), "twitter.com", "mobile"),
                Record(0, 1, code, Hour.AddMinutes(59), "twitter.com", "desktop"),
                Record(1, 0, code, Hour.AddMinutes(61), null, "mobile"),
            };

            int counted = await worker.Consumer.ApplyBatchAsync(batch, CancellationToken.None);

            counted.Should().Be(3);
            (await CountAsync($"SELECT clicks FROM analytics.click_rollups_hourly WHERE code = '{code}' AND bucket_hour = '{Hour:O}'")).Should().Be(2);
            (await CountAsync($"SELECT clicks FROM analytics.click_rollups_hourly WHERE code = '{code}' AND bucket_hour = '{Hour.AddHours(1):O}'")).Should().Be(1);
            (await CountAsync($"SELECT sum(clicks) FROM analytics.click_rollups_referrer WHERE code = '{code}' AND referrer_host = 'twitter.com'")).Should().Be(2);
            (await CountAsync($"SELECT sum(clicks) FROM analytics.click_rollups_device WHERE code = '{code}' AND device = 'mobile'")).Should().Be(2);
            (await CountAsync($"SELECT next_offset FROM analytics.consumer_offsets WHERE consumer_group = '{worker.Group}' AND partition_id = 0")).Should().Be(2);
            (await CountAsync($"SELECT next_offset FROM analytics.consumer_offsets WHERE consumer_group = '{worker.Group}' AND partition_id = 1")).Should().Be(1);
        }

        [Fact]
        public async Task Replayed_records_are_never_counted_twice()
        {
            using Worker worker = new Worker(_db.WorkerConnection);
            string code = TestCodes.NewGenerated().Value;
            List<ClickRecord> first = Enumerable.Range(0, 5).Select(i => Record(2, i, code, Hour, null, "desktop")).ToList();
            List<ClickRecord> overlapping = Enumerable.Range(3, 4).Select(i => Record(2, i, code, Hour, null, "desktop")).ToList(); // offsets 3..6

            int firstCount = await worker.Consumer.ApplyBatchAsync(first, CancellationToken.None);
            int replayCount = await worker.Consumer.ApplyBatchAsync(first, CancellationToken.None);
            int overlapCount = await worker.Consumer.ApplyBatchAsync(overlapping, CancellationToken.None);

            firstCount.Should().Be(5);
            replayCount.Should().Be(0, "every offset is below the stored next offset");
            overlapCount.Should().Be(2, "only offsets 5 and 6 are new");
            (await CountAsync($"SELECT clicks FROM analytics.click_rollups_hourly WHERE code = '{code}'")).Should().Be(7);
        }

        [Fact]
        public async Task A_failed_batch_counts_nothing_and_leaves_the_offset_for_a_retry()
        {
            using Worker worker = new Worker(_db.WorkerConnection);
            string code = TestCodes.NewGenerated().Value;
            string tooLongHost = new string('h', 300) + ".example"; // breaks varchar(255): the whole transaction rolls back
            List<ClickRecord> batch = new List<ClickRecord>
            {
                Record(0, 0, code, Hour, null, "desktop"),
                Record(0, 1, code, Hour, tooLongHost, "desktop"),
            };

            Func<Task> act = () => worker.Consumer.ApplyBatchAsync(batch, CancellationToken.None);

            await act.Should().ThrowAsync<PostgresException>();
            (await CountAsync($"SELECT count(*) FROM analytics.click_rollups_hourly WHERE code = '{code}'")).Should().Be(0);
            (await CountAsync($"SELECT count(*) FROM analytics.consumer_offsets WHERE consumer_group = '{worker.Group}'")).Should().Be(0);
        }

        [Fact]
        public async Task Malformed_records_are_skipped_but_their_offsets_advance()
        {
            using Worker worker = new Worker(_db.WorkerConnection);
            string code = TestCodes.NewGenerated().Value;
            List<ClickRecord> batch = new List<ClickRecord>
            {
                new ClickRecord(0, 0, "not json"),
                Record(0, 1, code, Hour, null, "tablet"),
                new ClickRecord(0, 2, null),
            };

            int counted = await worker.Consumer.ApplyBatchAsync(batch, CancellationToken.None);

            counted.Should().Be(1);
            (await CountAsync($"SELECT next_offset FROM analytics.consumer_offsets WHERE consumer_group = '{worker.Group}' AND partition_id = 0")).Should().Be(3, "a bad record must not block its partition for ever");
        }

        [Fact]
        public async Task The_worker_role_writes_analytics_and_cannot_read_links()
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.WorkerConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand links = new NpgsqlCommand("SELECT count(*) FROM short_urls", connection);
            await using NpgsqlCommand dropRollups = new NpgsqlCommand("DROP TABLE analytics.click_rollups_device", connection);

            Func<Task> readLinks = () => links.ExecuteScalarAsync();
            Func<Task> ddl = () => dropRollups.ExecuteNonQueryAsync();

            (await readLinks.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
            (await ddl.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", "only the migrator owns the schema");
        }

        [Fact]
        public async Task The_api_role_reads_analytics_but_cannot_write_it()
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.ApiConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand read = new NpgsqlCommand("SELECT count(*) FROM analytics.click_rollups_hourly", connection);
            await using NpgsqlCommand write = new NpgsqlCommand("INSERT INTO analytics.click_rollups_hourly VALUES ('x', now(), 1)", connection);

            await read.ExecuteScalarAsync();
            Func<Task> act = () => write.ExecuteNonQueryAsync();

            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        }

        internal static ClickRecord Record(int partition, long offset, string code, DateTimeOffset at, string? referrer, string device)
        {
            return new ClickRecord(partition, offset, ClickPublisher.Serialize(new ClickEvent(1, Guid.NewGuid(), code, at, referrer, device)));
        }

        private async Task<long> CountAsync(string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            object? value = await command.ExecuteScalarAsync();
            return value is DBNull or null ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A consumer as the worker role, with a consumer group of its own.</summary>
        internal sealed class Worker : IDisposable
        {
            private readonly AnalyticsDatabase _database;

            public Worker(string workerConnection, string? group = null)
            {
                Group = group ?? "it-" + Guid.NewGuid().ToString("N");
                _database = new AnalyticsDatabase(NpgsqlDataSource.Create(workerConnection));
                Consumer = new ClickConsumer(_database, new KafkaOptions { ConsumerGroup = Group }, TimeProvider.System, NullLogger<ClickConsumer>.Instance);
            }

            public string Group { get; }

            public ClickConsumer Consumer { get; }

            public void Dispose()
            {
                Consumer.Dispose();
                _database.Dispose();
            }
        }
    }
}
