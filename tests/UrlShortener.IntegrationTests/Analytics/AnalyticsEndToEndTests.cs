using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Analytics
{
    /// <summary>
    /// FR-07 and NFR-08 end to end, through a real broker: create a link, follow it, and the stats endpoint shows the
    /// clicks within the freshness target. Restarting the worker re-reads the topic from its stored offsets and counts
    /// nothing twice.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class AnalyticsEndToEndTests : IClassFixture<RedpandaFixture>
    {
        private const string Iphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148";
        private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36";

        private readonly PostgresFixture _db;
        private readonly RedpandaFixture _kafka;

        public AnalyticsEndToEndTests(PostgresFixture db, RedpandaFixture kafka)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _kafka = kafka ?? throw new ArgumentNullException(nameof(kafka));
        }

        [Fact]
        public async Task Redirects_become_stats_within_the_freshness_target_and_a_restart_counts_nothing_twice()
        {
            // A consumer group of its own, shared by the worker (offsets) and the Api (dataFreshAsOf).
            Dictionary<string, string> kafka = new Dictionary<string, string>
            {
                ["Kafka:BootstrapServers"] = _kafka.BootstrapServers,
                ["Kafka:ConsumerGroup"] = "e2e-" + Guid.NewGuid().ToString("N"),
                ["Kafka:BatchMaxDuration"] = "00:00:01",
            };
            using ApiFactory api = new ApiFactory(_db.ApiConnection, settings: kafka);
            using HttpClient owner = api.CreateClientFor(ApiFactory.CallerOneHeader);
            using RedirectFactory redirect = new RedirectFactory(_db.RedirectConnection, settings: kafka);
            using HttpClient browser = redirect.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            string code = await CreateAsync(owner);

            await FollowAsync(browser, code, Iphone, "https://twitter.com/post/1");
            await FollowAsync(browser, code, Iphone, "https://twitter.com/post/2");
            await FollowAsync(browser, code, Chrome, null);
            using (HttpRequestMessage head = new HttpRequestMessage(HttpMethod.Head, new Uri("/" + code, UriKind.Relative)))
            {
                (await browser.SendAsync(head)).Dispose(); // HEAD: no click
            }

            Stopwatch watch = Stopwatch.StartNew();
            using (WorkerFactory worker = new WorkerFactory(_db.WorkerConnection, kafka))
            {
                using HttpClient started = worker.CreateClient(); // starts the consumer
                JsonElement stats = await WaitForClicksAsync(owner, code, expected: 3);

                watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), "stats lag p99 ≤ 60 s (NFR-08)");
                stats.GetProperty("devices").GetProperty("mobile").GetInt64().Should().Be(2);
                stats.GetProperty("devices").GetProperty("desktop").GetInt64().Should().Be(1);
                stats.GetProperty("topReferrers")[0].GetProperty("host").GetString().Should().Be("twitter.com");
            }

            // A new worker instance resumes from the offsets stored with the counts: nothing is counted twice.
            await FollowAsync(browser, code, Chrome, null);
            using (WorkerFactory restarted = new WorkerFactory(_db.WorkerConnection, kafka))
            {
                using HttpClient started = restarted.CreateClient();
                JsonElement stats = await WaitForClicksAsync(owner, code, expected: 4);
                await Task.Delay(TimeSpan.FromSeconds(3)); // a few more polls: the total must stay at 4
                (await StatsAsync(owner, code)).GetProperty("totalClicks").GetInt64().Should().Be(4);
                stats.GetProperty("dataFreshAsOf").ValueKind.Should().Be(JsonValueKind.String);
            }
        }

        private static async Task FollowAsync(HttpClient browser, string code, string userAgent, string? referrer)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri("/" + code, UriKind.Relative));
            request.Headers.UserAgent.ParseAdd(userAgent);
            if (referrer != null)
            {
                request.Headers.Referrer = new Uri(referrer);
            }

            using HttpResponseMessage response = await browser.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Found);
        }

        private static async Task<JsonElement> WaitForClicksAsync(HttpClient owner, string code, long expected)
        {
            Stopwatch watch = Stopwatch.StartNew();
            JsonElement stats = await StatsAsync(owner, code);
            while (stats.GetProperty("totalClicks").GetInt64() < expected && watch.Elapsed < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(500);
                stats = await StatsAsync(owner, code);
            }

            stats.GetProperty("totalClicks").GetInt64().Should().Be(expected);
            return stats;
        }

        private static async Task<JsonElement> StatsAsync(HttpClient owner, string code)
        {
            using HttpResponseMessage response = await owner.GetAsync(new Uri($"/api/v1/urls/{code}/stats", UriKind.Relative));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.Clone();
        }

        private static async Task<string> CreateAsync(HttpClient client)
        {
            using StringContent content = new StringContent(JsonSerializer.Serialize(new { longUrl = "https://example.com/e2e" }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(new Uri("/api/v1/urls", UriKind.Relative), content);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("code").GetString()!;
        }
    }
}
