using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using UrlShortener.Application.ShortUrls;
using UrlShortener.Domain;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Api
{
    /// <summary>
    /// TSK-14 AC1-AC3 and AC5 through HTTP on a real PostgreSQL. The passage of time is simulated by moving a
    /// link's stored created_at and expires_at into the past together (expiry stays after creation, as in real
    /// data), which is what the clock reaching the expiry looks like to the services (they use the real clock).
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class LinkExpiryApiTests : IDisposable
    {
        private readonly PostgresFixture _db;
        private readonly HttpClient _api;
        private readonly RedirectFactory _redirect;
        private readonly HttpClient _browser;

        public LinkExpiryApiTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _api = db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
            _redirect = new RedirectFactory(db.RedirectConnection);
            _browser = _redirect.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _browser.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0");
        }

        public void Dispose()
        {
            _api.Dispose();
            _browser.Dispose();
            _redirect.Dispose();
        }

        [Fact]
        public async Task Link_with_expiry_redirects_until_it_expires_then_is_gone()
        {
            DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(7);
            string alias = TestCodes.NewAlias().Value;

            using HttpResponseMessage created = await CreateAsync($"{{\"longUrl\":\"https://example.com/sale\",\"customAlias\":\"{alias}\",\"expiresAt\":\"{expiresAt:O}\"}}", null);
            using HttpResponseMessage before = await _browser.GetAsync(new Uri("/" + alias, UriKind.Relative));
            await ExecuteAsync($"UPDATE short_urls SET created_at = now() - interval '2 hours', expires_at = now() - interval '1 second' WHERE code = '{alias}'");
            using HttpResponseMessage after = await _browser.GetAsync(new Uri("/" + alias, UriKind.Relative));
            using HttpRequestMessage headRequest = new HttpRequestMessage(HttpMethod.Head, new Uri("/" + alias, UriKind.Relative));
            using HttpResponseMessage head = await _browser.SendAsync(headRequest);

            created.StatusCode.Should().Be(HttpStatusCode.Created);
            (await JsonAsync(created)).GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(expiresAt, TimeSpan.FromMilliseconds(1));
            before.StatusCode.Should().Be(HttpStatusCode.Found);
            after.StatusCode.Should().Be(HttpStatusCode.Gone);
            after.Headers.CacheControl!.Public.Should().BeTrue();
            after.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(1));
            head.StatusCode.Should().Be(HttpStatusCode.Gone);
            _redirect.Clicks.Events.Should().ContainSingle("only the redirect before expiry counts");
        }

        [Fact]
        public async Task Metadata_shows_the_expiry_and_an_expired_alias_stays_taken()
        {
            DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddHours(3);
            string alias = TestCodes.NewAlias().Value;
            using HttpResponseMessage created = await CreateAsync($"{{\"longUrl\":\"https://example.com/m\",\"customAlias\":\"{alias}\",\"expiresAt\":\"{expiresAt:O}\"}}", null);
            await ExecuteAsync($"UPDATE short_urls SET created_at = now() - interval '2 hours', expires_at = now() - interval '1 second' WHERE code = '{alias}'");

            using HttpResponseMessage metadata = await _api.GetAsync(new Uri("/api/v1/urls/" + alias, UriKind.Relative));
            using HttpResponseMessage again = await CreateAsync($"{{\"longUrl\":\"https://example.com/m2\",\"customAlias\":\"{alias}\"}}", null);

            metadata.StatusCode.Should().Be(HttpStatusCode.OK);
            (await JsonAsync(metadata)).GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.String);
            again.StatusCode.Should().Be(HttpStatusCode.Conflict, "codes are never reused, also after expiry (A-25)");
        }

        [Fact]
        public async Task Link_without_expiry_has_a_null_expiry()
        {
            using HttpResponseMessage created = await CreateAsync("{\"longUrl\":\"https://example.com/forever\"}", null);

            (await JsonAsync(created)).GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        }

        [Fact]
        public async Task Same_instant_in_another_offset_replays_and_a_different_expiry_is_422()
        {
            string key = Guid.NewGuid().ToString();
            DateTimeOffset utc = new DateTimeOffset(DateTime.UtcNow.AddDays(2).Date, TimeSpan.Zero);
            string plusTwo = utc.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

            using HttpResponseMessage first = await CreateAsync($"{{\"longUrl\":\"https://example.com/k\",\"expiresAt\":\"{utc:yyyy-MM-dd'T'HH:mm:ss'Z'}\"}}", key);
            using HttpResponseMessage sameInstant = await CreateAsync($"{{\"longUrl\":\"https://example.com/k\",\"expiresAt\":\"{plusTwo}\"}}", key);
            using HttpResponseMessage otherInstant = await CreateAsync($"{{\"longUrl\":\"https://example.com/k\",\"expiresAt\":\"{utc.AddDays(1):yyyy-MM-dd'T'HH:mm:ss'Z'}\"}}", key);

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            sameInstant.StatusCode.Should().Be(HttpStatusCode.Created);
            sameInstant.Headers.Contains(UrlShortener.Api.Controllers.ShortUrlsController.ReplayedHeader).Should().BeTrue("time-zone notation doesn't change the request hash");
            otherInstant.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(otherInstant)).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be(ApplicationErrorCodes.IdempotencyKeyReused);
        }

        [Theory]
        [InlineData("2020-01-01T00:00:00Z", DomainErrorCodes.ExpiryPast)]
        [InlineData("2999-01-01T00:00:00Z", DomainErrorCodes.ExpiryTooFar)]
        public async Task Invalid_expiry_is_400_with_its_code(string expiresAt, string expectedCode)
        {
            using HttpResponseMessage response = await CreateAsync($"{{\"longUrl\":\"https://example.com/\",\"expiresAt\":\"{expiresAt}\"}}", null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await JsonAsync(response)).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be(expectedCode);
        }

        private async Task<HttpResponseMessage> CreateAsync(string json, string? idempotencyKey)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/urls", UriKind.Relative))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (idempotencyKey != null)
            {
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            }

            return await _api.SendAsync(request);
        }

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        private async Task ExecuteAsync(string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
