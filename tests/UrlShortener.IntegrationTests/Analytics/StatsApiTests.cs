using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Links;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Analytics
{
    /// <summary>
    /// FR-07 and FR-09: <c>GET /api/v1/urls/{code}/stats</c> returns the owner's totals, time series, top referrers and
    /// devices from the rollups, and the same 404 for unknown codes and other clients' links.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class StatsApiTests
    {
        private readonly PostgresFixture _db;

        public StatsApiTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public async Task Owner_gets_totals_series_referrers_and_devices()
        {
            using HttpClient client = _db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
            string code = await CreateAsync(client);
            DateTimeOffset hour = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0, TimeSpan.Zero).AddHours(-3);
            using (ClickConsumerTests.Worker worker = new ClickConsumerTests.Worker(_db.WorkerConnection, "analytics-worker"))
            {
                // Offsets far above anything another test stores for this group, so they never collide.
                long offset = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
                await worker.Consumer.ApplyBatchAsync(
                    new List<ClickEvent>
                    {
                        Click(code, hour.AddMinutes(5), "twitter.com", "mobile"),
                        Click(code, hour.AddMinutes(10), "twitter.com", "desktop"),
                        Click(code, hour.AddMinutes(70), "news.example.org", "mobile"),
                        Click(code, hour.AddMinutes(80), null, "tablet"),
                    }.ConvertAll(c => new ClickRecord(2, offset++, ClickPublisher.Serialize(c))),
                    CancellationToken.None);
            }

            using HttpResponseMessage response = await client.GetAsync(new Uri($"/api/v1/urls/{code}/stats?granularity=hour", UriKind.Relative));
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = body.RootElement;

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            root.GetProperty("code").GetString().Should().Be(code);
            root.GetProperty("granularity").GetString().Should().Be("hour");
            root.GetProperty("totalClicks").GetInt64().Should().Be(4);
            root.GetProperty("series").GetArrayLength().Should().Be(2);
            root.GetProperty("series")[0].GetProperty("clicks").GetInt64().Should().Be(2);
            root.GetProperty("topReferrers")[0].GetProperty("host").GetString().Should().Be("twitter.com");
            root.GetProperty("topReferrers")[0].GetProperty("clicks").GetInt64().Should().Be(2);
            root.GetProperty("devices").GetProperty("mobile").GetInt64().Should().Be(2);
            root.GetProperty("devices").GetProperty("tablet").GetInt64().Should().Be(1);
            root.GetProperty("dataFreshAsOf").ValueKind.Should().Be(JsonValueKind.String);
        }

        [Fact]
        public async Task Daily_granularity_is_the_default_and_a_link_without_clicks_has_zero()
        {
            using HttpClient client = _db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
            string code = await CreateAsync(client);

            using HttpResponseMessage response = await client.GetAsync(new Uri($"/api/v1/urls/{code}/stats", UriKind.Relative));
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.RootElement.GetProperty("granularity").GetString().Should().Be("day");
            body.RootElement.GetProperty("totalClicks").GetInt64().Should().Be(0);
            body.RootElement.GetProperty("series").GetArrayLength().Should().Be(0);
        }

        [Fact]
        public async Task Other_clients_links_and_unknown_codes_are_the_same_404()
        {
            using HttpClient owner = _db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
            using HttpClient other = _db.SharedApi.CreateClientFor(ApiFactory.CallerTwoHeader);
            string code = await CreateAsync(owner);

            using HttpResponseMessage foreign = await other.GetAsync(new Uri($"/api/v1/urls/{code}/stats", UriKind.Relative));
            using HttpResponseMessage unknown = await owner.GetAsync(new Uri($"/api/v1/urls/{TestCodes.NewGenerated().Value}/stats", UriKind.Relative));

            foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
            unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await foreign.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.LinkNotFound);
        }

        [Theory]
        [InlineData("granularity=minute")]
        [InlineData("from=2026-01-01T00:00:00Z&to=2026-06-01T00:00:00Z")]
        [InlineData("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
        public async Task Invalid_ranges_and_granularities_are_400(string query)
        {
            using HttpClient client = _db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
            string code = await CreateAsync(client);

            using HttpResponseMessage response = await client.GetAsync(new Uri($"/api/v1/urls/{code}/stats?{query}", UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.StatsRange);
        }

        [Fact]
        public async Task Stats_need_an_api_key()
        {
            using HttpClient anonymous = _db.SharedApi.CreateClient();

            using HttpResponseMessage response = await anonymous.GetAsync(new Uri("/api/v1/urls/aZ3k9Qx/stats", UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        private static ClickEvent Click(string code, DateTimeOffset at, string? referrer, string device)
        {
            return new ClickEvent(1, Guid.NewGuid(), code, at, referrer, device);
        }

        private static async Task<string> CreateAsync(HttpClient client)
        {
            using StringContent content = new StringContent(JsonSerializer.Serialize(new { longUrl = "https://example.com/stats" }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(new Uri("/api/v1/urls", UriKind.Relative), content);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("code").GetString()!;
        }
    }
}
