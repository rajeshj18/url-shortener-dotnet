using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Polly.CircuitBreaker;
using UrlShortener.Core.Database;
using UrlShortener.Core.Links;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Resilience
{
    /// <summary>
    /// TSK-11 AC3–AC6 on real PostgreSQL: fault injection, the Redirect breaker, cleanup, request timeouts. TSK-SIMPLE:
    /// there are no use-case interfaces to replace with hanging fakes, so the timeouts are provoked for real: a create
    /// waits on another transaction's idempotency-key lock, and a redirect waits on a database that never answers.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class ResilienceIntegrationTests
    {
        // A closed port: connections are refused, like a primary that is down.
        private const string UnreachablePrimary = "Host=127.0.0.1;Port=1;Database=links;Username=nobody;Password=x;Timeout=1";

        private readonly PostgresFixture _db;

        public ResilienceIntegrationTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public async Task Frozen_database_gives_a_fast_503_and_creates_recover_without_a_restart()
        {
            // A dedicated container, so pausing it can't affect any other test.
            PostgresFixture dedicated = new PostgresFixture();
            await dedicated.InitializeAsync();
            bool paused = false;
            try
            {
                using HttpClient client = dedicated.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
                (await CreateAsync(client)).StatusCode.Should().Be(HttpStatusCode.Created, "healthy before the fault");

                await dedicated.PauseAsync();
                paused = true;
                Stopwatch watch = Stopwatch.StartNew();
                using HttpResponseMessage during = await CreateAsync(client);
                watch.Stop();

                during.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
                during.Headers.RetryAfter.Should().NotBeNull();
                watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the 3 s request budget and the 1–2 s database timeouts bound it");

                await dedicated.UnpauseAsync();
                paused = false;
                HttpStatusCode after = HttpStatusCode.ServiceUnavailable;
                for (int attempt = 0; attempt < 20 && after != HttpStatusCode.Created; attempt++)
                {
                    using HttpResponseMessage retry = await CreateAsync(client);
                    after = retry.StatusCode;
                    if (after != HttpStatusCode.Created)
                    {
                        await Task.Delay(500);
                    }
                }

                after.Should().Be(HttpStatusCode.Created, "the same instance recovers once the database is back");
            }
            finally
            {
                if (paused)
                {
                    await dedicated.UnpauseAsync(); // an assertion failed while frozen
                }

                await dedicated.DisposeAsync();
            }
        }

        [Fact]
        public async Task Redirect_keeps_serving_from_the_replica_while_the_primary_breaker_is_open()
        {
            using HttpClient api = _db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
            using HttpResponseMessage created = await CreateAsync(api);
            string code = await CodeOf(created);
            Dictionary<string, string> settings = new Dictionary<string, string>
            {
                ["Resilience:BreakerMinimumThroughput"] = "2",
                ["Resilience:RequestTimeout"] = "00:00:10", // connection refusal is slow on some hosts; let the breaker see it
            };
            using RedirectFactory redirect = new RedirectFactory(_db.RedirectConnection, UnreachablePrimary, settings);
            using HttpClient browser = redirect.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // Replica misses fall back to the dead primary: 503 (not 404), and the breaker counts the failures.
            for (int i = 0; i < 2; i++)
            {
                using HttpResponseMessage miss = await browser.GetAsync(new Uri("/missing-" + i, UriKind.Relative));
                miss.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            }

            CircuitStateMetrics.Get("redirect-primary-fallback").Should().Be(CircuitState.Open);
            Stopwatch watch = Stopwatch.StartNew();
            using HttpResponseMessage fastMiss = await browser.GetAsync(new Uri("/missing-x", UriKind.Relative));
            watch.Stop();
            using HttpResponseMessage hit = await browser.GetAsync(new Uri("/" + code, UriKind.Relative));

            fastMiss.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            fastMiss.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500), "an open breaker rejects without trying the primary");
            hit.StatusCode.Should().Be(HttpStatusCode.Found, "existing links keep redirecting from the replica");
        }

        [Fact]
        public async Task Cleanup_deletes_expired_keys_in_batches_and_only_one_instance_runs()
        {
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await scratch.ExecuteAsync(@"
INSERT INTO idempotency_keys (owner_id, idem_key, request_hash, response_body, created_at)
SELECT 'o', 'old-' || g, repeat('A', 64), '{}'::jsonb, now() - interval '25 hours' FROM generate_series(1, 5) g;
INSERT INTO idempotency_keys (owner_id, idem_key, request_hash, response_body, created_at)
SELECT 'o', 'new-' || g, repeat('A', 64), '{}'::jsonb, now() - interval '23 hours' FROM generate_series(1, 2) g;");
            using PrimaryDatabase source = scratch.CreateApiDataSource();
            using IdempotencyCleanupJob cleanup = new IdempotencyCleanupJob(
                source, new ResilienceOptions { IdempotencyCleanupBatchSize = 2 }, TimeProvider.System, NullLogger<IdempotencyCleanupJob>.Instance);

            // Another instance holds the job's lock: this one skips the run. Unpooled, so closing it really ends the
            // session and releases the session-level lock (a pooled connection would keep it until reused).
            await using (NpgsqlConnection other = new NpgsqlConnection(scratch.ApiConnection + ";Pooling=false"))
            {
                await other.OpenAsync();
                await using NpgsqlCommand hold = new NpgsqlCommand("SELECT pg_advisory_lock(7006, 1)", other);
                await hold.ExecuteScalarAsync();

                (await cleanup.RunOnceAsync(CancellationToken.None)).Should().BeNull("only the lock holder works");
            }

            long? deleted = await cleanup.RunOnceAsync(CancellationToken.None);

            deleted.Should().Be(5, "three batches of at most 2");
            (await scratch.ScalarAsync("SELECT count(*) FROM idempotency_keys WHERE idem_key LIKE 'new-%'")).Should().Be(2L);
            (await scratch.ScalarAsync("SELECT count(*) FROM idempotency_keys WHERE idem_key LIKE 'old-%'")).Should().Be(0L);
        }

        [Fact]
        public async Task Api_request_over_its_budget_is_503()
        {
            // Another transaction holds this owner's idempotency key, so the create waits on the row lock.
            string key = Guid.NewGuid().ToString();
            await using NpgsqlConnection holder = new NpgsqlConnection(_db.ApiConnection);
            await holder.OpenAsync();
            await using NpgsqlTransaction transaction = await holder.BeginTransactionAsync();
            await using (NpgsqlCommand claim = new NpgsqlCommand(
                "INSERT INTO idempotency_keys (owner_id, idem_key, request_hash, created_at) VALUES (@owner, @key, repeat('A', 64), now())", holder, transaction))
            {
                claim.Parameters.AddWithValue("owner", ApiFactory.CallerOneOwner);
                claim.Parameters.AddWithValue("key", key);
                await claim.ExecuteNonQueryAsync();
            }

            using ApiFactory factory = new ApiFactory(_db.ApiConnection, settings: new Dictionary<string, string> { ["Resilience:RequestTimeout"] = "00:00:00.300" });
            using HttpClient client = factory.CreateClientFor(ApiFactory.CallerOneHeader);
            client.DefaultRequestHeaders.Add("Idempotency-Key", key);

            Stopwatch watch = Stopwatch.StartNew();
            using HttpResponseMessage response = await CreateAsync(client);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(5));
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "the 300 ms budget ends it, before the 2 s statement timeout");
            await transaction.RollbackAsync();
        }

        [Fact]
        public async Task Redirect_over_its_budget_is_503()
        {
            // A lock blocks the lookup's SELECT on the view, like a replica stuck behind a long operation. Npgsql cancels
            // a running query when the 200 ms budget expires.
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            await using NpgsqlConnection locker = new NpgsqlConnection(scratch.BootstrapConnection);
            await locker.OpenAsync();
            await using NpgsqlTransaction transaction = await locker.BeginTransactionAsync();
            await using (NpgsqlCommand lockTable = new NpgsqlCommand("LOCK TABLE short_urls IN ACCESS EXCLUSIVE MODE", locker, transaction))
            {
                await lockTable.ExecuteNonQueryAsync();
            }

            using RedirectFactory redirect = new RedirectFactory(
                scratch.RedirectConnection,
                settings: new Dictionary<string, string> { ["Resilience:RequestTimeout"] = "00:00:00.200" });
            using HttpClient browser = redirect.CreateClient();

            Stopwatch watch = Stopwatch.StartNew();
            using HttpResponseMessage response = await browser.GetAsync(new Uri("/aZ3k9Qx", UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "the running query is cancelled when the budget expires");
            await transaction.RollbackAsync();
        }

        [Fact]
        public async Task A_database_that_hangs_during_connect_is_bounded_by_the_connect_timeout()
        {
            // Finding (TSK-SIMPLE): Npgsql doesn't abandon a connection attempt whose server never answers the startup
            // message when the request is cancelled, so here the bound is the 2 s connect timeout (LLD §6.7), not the
            // 500 ms request budget. The answer is still a 503, never a hung request.
            using BlackHole replicas = new BlackHole();
            using RedirectFactory redirect = new RedirectFactory(
                replicas.ConnectionString.Replace("Timeout=10;Command Timeout=10", "Timeout=2", StringComparison.Ordinal));
            using HttpClient browser = redirect.CreateClient();

            Stopwatch watch = Stopwatch.StartNew();
            using HttpResponseMessage response = await browser.GetAsync(new Uri("/aZ3k9Qx", UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
        }

        private static async Task<HttpResponseMessage> CreateAsync(HttpClient client)
        {
            using StringContent content = new StringContent(
                JsonSerializer.Serialize(new { longUrl = "https://example.com/resilience" }), Encoding.UTF8, "application/json");
            return await client.PostAsync(new Uri("/api/v1/urls", UriKind.Relative), content);
        }

        private static async Task<string> CodeOf(HttpResponseMessage response)
        {
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("code").GetString()!;
        }
    }
}
