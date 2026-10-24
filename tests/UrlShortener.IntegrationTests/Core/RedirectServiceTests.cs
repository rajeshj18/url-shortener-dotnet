using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Database;
using UrlShortener.Core.Redirects;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Core
{
    /// <summary>
    /// The redirect use case against real PostgreSQL, as the read-only Redirect role (TSK-SIMPLE; replaces the lookup
    /// tests). The read goes through the <c>redirect_lookup</c> view, and a replica miss falls back to the primary.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class RedirectServiceTests
    {
        private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36";

        private readonly PostgresFixture _db;

        public RedirectServiceTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public async Task Active_link_redirects_and_publishes_one_privacy_safe_click()
        {
            string code = await InsertAsync("https://example.com/r?x=1", deleted: false, expiresAt: null);
            using Harness harness = new Harness(_db.RedirectConnection, _db.RedirectConnection);

            ResolveResult result = await harness.Service.ResolveAsync(code, false, Chrome, "https://news.example.org/story?id=7", CancellationToken.None);

            result.Outcome.Should().Be(ResolveOutcome.Redirect);
            result.Target!.OriginalString.Should().Be("https://example.com/r?x=1");
            ClickEvent click = harness.Clicks.Should().ContainSingle().Subject;
            click.Code.Should().Be(code);
            click.Device.Should().Be("desktop");
            click.ReferrerHost.Should().Be("news.example.org", "only the host is kept");
        }

        [Fact]
        public async Task Head_requests_and_bots_get_the_redirect_without_a_click()
        {
            string code = await InsertAsync("https://example.com/h", deleted: false, expiresAt: null);
            using Harness harness = new Harness(_db.RedirectConnection, _db.RedirectConnection);

            (await harness.Service.ResolveAsync(code, true, Chrome, null, CancellationToken.None)).Outcome.Should().Be(ResolveOutcome.Redirect);
            (await harness.Service.ResolveAsync(code, false, "Twitterbot/1.0", null, CancellationToken.None)).Outcome.Should().Be(ResolveOutcome.Redirect);

            harness.Clicks.Should().BeEmpty();
        }

        [Fact]
        public async Task Deleted_and_expired_links_are_gone_and_unknown_codes_not_found()
        {
            string deleted = await InsertAsync("https://example.com/d", deleted: true, expiresAt: null);
            string expired = await InsertAsync("https://example.com/e", deleted: false, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
            using Harness harness = new Harness(_db.RedirectConnection, _db.RedirectConnection);

            (await harness.Service.ResolveAsync(deleted, false, Chrome, null, CancellationToken.None)).Outcome.Should().Be(ResolveOutcome.Gone);
            (await harness.Service.ResolveAsync(expired, false, Chrome, null, CancellationToken.None)).Outcome.Should().Be(ResolveOutcome.Gone);
            (await harness.Service.ResolveAsync(TestCodes.NewGenerated().Value, false, Chrome, null, CancellationToken.None)).Outcome.Should().Be(ResolveOutcome.NotFound);
            harness.Clicks.Should().BeEmpty();
        }

        [Fact]
        public async Task A_replica_miss_is_answered_from_the_primary_while_the_budget_allows()
        {
            // The "replica" is an empty scratch database: it doesn't have the new link yet (replication lag).
            await using ScratchDatabase lagging = await _db.CreateScratchDatabaseAsync();
            string code = await InsertAsync("https://example.com/fresh", deleted: false, expiresAt: null);
            using Harness harness = new Harness(lagging.RedirectConnection, _db.RedirectConnection, fallbacksPerSecond: 1);

            ResolveResult first = await harness.Service.ResolveAsync(code, false, Chrome, null, CancellationToken.None);
            ResolveResult second = await harness.Service.ResolveAsync(code, false, Chrome, null, CancellationToken.None);

            first.Outcome.Should().Be(ResolveOutcome.Redirect, "read-your-writes: a new link redirects at once (NFR-07)");
            second.Outcome.Should().Be(ResolveOutcome.NotFound, "the budget of 1 per second is used up, so the primary is protected");
        }

        [Fact]
        public async Task The_redirect_role_reads_the_view_but_nothing_else()
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.RedirectConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand table = new NpgsqlCommand("SELECT count(*) FROM short_urls", connection);
            await using NpgsqlCommand analytics = new NpgsqlCommand("SELECT count(*) FROM analytics.click_rollups_hourly", connection);

            Func<Task> readTable = () => table.ExecuteScalarAsync();
            Func<Task> readAnalytics = () => analytics.ExecuteScalarAsync();

            (await readTable.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
            (await readAnalytics.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        }

        private async Task<string> InsertAsync(string url, bool deleted, DateTimeOffset? expiresAt)
        {
            string code = TestCodes.NewGenerated().Value;
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.ApiConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(
                "INSERT INTO short_urls (code, long_url, owner_id, is_custom, created_at, deleted_at, expires_at) " +
                "VALUES (@code, @url, 'client-42', false, now() - interval '1 hour', @deleted, @expires)",
                connection);
            command.Parameters.AddWithValue("code", code);
            command.Parameters.AddWithValue("url", url);
            command.Parameters.AddWithValue("deleted", deleted ? DateTimeOffset.UtcNow.AddMinutes(-5) : DBNull.Value);
            command.Parameters.AddWithValue("expires", expiresAt.HasValue ? expiresAt.Value : DBNull.Value);
            await command.ExecuteNonQueryAsync();
            return code;
        }

        /// <summary>A <see cref="RedirectService"/> wired like the Redirect service, recording accepted clicks.</summary>
        private sealed class Harness : IDisposable
        {
            private readonly ReplicaDatabase _replicas;
            private readonly PrimaryDatabase _primary;
            private readonly ClickPublisher _publisher = new ClickPublisher(new KafkaOptions());

            public Harness(string replicaConnection, string primaryConnection, int fallbacksPerSecond = 500)
            {
                _replicas = new ReplicaDatabase(NpgsqlDataSource.Create(replicaConnection));
                _primary = new PrimaryDatabase(NpgsqlDataSource.Create(primaryConnection));
                _publisher.Accepted += (_, e) => Clicks.Enqueue(e.Click);
                DatabaseGuard guard = new DatabaseGuard("it-" + Guid.NewGuid().ToString("N"), new ResilienceOptions(), TimeProvider.System, null, NullLogger.Instance);
                Service = new RedirectService(_replicas, _primary, guard, _publisher, fallbacksPerSecond, TimeProvider.System);
            }

            public RedirectService Service { get; }

            public ConcurrentQueue<ClickEvent> Clicks { get; } = new ConcurrentQueue<ClickEvent>();

            public void Dispose()
            {
                _publisher.Dispose();
                _replicas.Dispose();
                _primary.Dispose();
            }
        }
    }
}
