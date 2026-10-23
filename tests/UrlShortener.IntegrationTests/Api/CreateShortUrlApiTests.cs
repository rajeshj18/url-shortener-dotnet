using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using UrlShortener.Application.ShortUrls;
using UrlShortener.Domain;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Api
{
    /// <summary>
    /// TSK-08 AC1–AC7 through HTTP, on a real PostgreSQL: the in-memory Api with authentication, the create use
    /// case, the idempotency table and the problem mapping.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class CreateShortUrlApiTests : IDisposable
    {
        private const string Path = "/api/v1/urls";

        private readonly PostgresFixture _db;
        private readonly ApiFactory _factory;
        private readonly HttpClient _one;
        private readonly HttpClient _two;

        public CreateShortUrlApiTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _factory = db.SharedApi;
            _one = _factory.CreateClientFor(ApiFactory.CallerOneHeader);
            _two = _factory.CreateClientFor(ApiFactory.CallerTwoHeader);
        }

        public void Dispose()
        {
            _one.Dispose();
            _two.Dispose();
        }

        [Fact]
        public async Task Generated_code_returns_201_with_location_and_body()
        {
            string longUrl = "https://example.com/" + Guid.NewGuid().ToString("N") + "?q=1";

            using HttpResponseMessage response = await PostAsync(_one, new { longUrl });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            JsonElement body = await JsonAsync(response);
            string code = body.GetProperty("code").GetString()!;
            ShortCode.TryParse(code, out ShortCode? parsed).Should().BeTrue();
            parsed!.IsGenerated.Should().BeTrue();
            body.GetProperty("shortUrl").GetString().Should().Be(ApiFactory.PublicBaseUrl + "/" + code);
            body.GetProperty("longUrl").GetString().Should().Be(longUrl);
            body.GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.Null);
            response.Headers.Location!.OriginalString.Should().Be("/api/v1/urls/" + code);
            (await ScalarAsync($"SELECT owner_id FROM short_urls WHERE code = '{code}'")).Should().Be(ApiFactory.CallerOneOwner);
        }

        [Fact]
        public async Task Alias_is_created_and_a_second_request_for_it_is_409()
        {
            string alias = TestCodes.NewAlias().Value;

            using HttpResponseMessage first = await PostAsync(_one, new { longUrl = "https://example.com/a", customAlias = alias });
            using HttpResponseMessage second = await PostAsync(_two, new { longUrl = "https://example.com/b", customAlias = alias });

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            (await JsonAsync(first)).GetProperty("code").GetString().Should().Be(alias);
            await AssertProblemAsync(second, HttpStatusCode.Conflict, ApplicationErrorCodes.AliasTaken);
        }

        [Fact]
        public async Task Concurrent_requests_for_one_alias_give_exactly_one_201_and_never_500()
        {
            string alias = TestCodes.NewAlias().Value;

            HttpStatusCode[] statuses = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
            {
                using HttpResponseMessage response = await PostAsync(i % 2 == 0 ? _one : _two, new { longUrl = "https://example.com/" + i, customAlias = alias });
                return response.StatusCode;
            }));

            statuses.Count(s => s == HttpStatusCode.Created).Should().Be(1);
            statuses.Count(s => s == HttpStatusCode.Conflict).Should().Be(11);
        }

        [Theory]
        [InlineData("{\"longUrl\":\"ftp://example.com/x\"}", DomainErrorCodes.UrlScheme)]
        [InlineData("{\"longUrl\":\"https://user:pw@example.com/\"}", DomainErrorCodes.UrlCredentials)]
        [InlineData("{\"longUrl\":\"https://sho.rt/loop\"}", ApplicationErrorCodes.UrlSelfLink)]
        [InlineData("{}", DomainErrorCodes.UrlRequired)]
        [InlineData("{\"longUrl\":\"https://example.com/\",\"customAlias\":\"spring1\"}", DomainErrorCodes.AliasGeneratedShape)]
        [InlineData("{\"longUrl\":\"https://example.com/\",\"customAlias\":\"health\"}", DomainErrorCodes.AliasReserved)]
        [InlineData("{\"longUrl\":\"https://example.com/\",\"expiresAt\":\"2030-01-01T00:00:00Z\"}", ApplicationErrorCodes.ExpiryUnsupported)]
        public async Task Invalid_request_is_400_with_its_error_code(string json, string expectedCode)
        {
            using HttpResponseMessage response = await PostRawAsync(_one, json, null);

            await AssertProblemAsync(response, HttpStatusCode.BadRequest, expectedCode);
        }

        [Theory]
        [InlineData("tab\tkey")]
        [InlineData("café")]
        public async Task Malformed_idempotency_key_is_400(string key)
        {
            using HttpRequestMessage request = Request("{\"longUrl\":\"https://example.com/\"}");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            using HttpResponseMessage response = await _one.SendAsync(request);

            await AssertProblemAsync(response, HttpStatusCode.BadRequest, ApplicationErrorCodes.IdempotencyKeyFormat);
        }

        [Fact]
        public async Task Too_long_idempotency_key_is_400()
        {
            using HttpResponseMessage response = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/\"}", new string('k', 129));

            await AssertProblemAsync(response, HttpStatusCode.BadRequest, ApplicationErrorCodes.IdempotencyKeyFormat);
        }

        [Fact]
        public async Task Missing_or_unknown_api_key_is_401_and_the_key_is_never_logged()
        {
            const string WrongHeader = "wrong-header-value-7f3a9c";
            using HttpClient anonymous = _factory.CreateClient();
            using HttpClient wrong = _factory.CreateClientFor(WrongHeader);

            using HttpResponseMessage none = await PostAsync(anonymous, new { longUrl = "https://example.com/" });
            using HttpResponseMessage unknown = await PostAsync(wrong, new { longUrl = "https://example.com/" });
            using HttpResponseMessage valid = await PostAsync(_one, new { longUrl = "https://example.com/secret-path-91" });

            none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            unknown.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            valid.StatusCode.Should().Be(HttpStatusCode.Created);
            _factory.Logs.Lines.Should().NotBeEmpty("otherwise this test proves nothing");
            string logs = string.Join("\n", _factory.Logs.Lines);
            logs.Should().NotContain(WrongHeader).And.NotContain(ApiFactory.CallerOneHeader)
                .And.NotContain("secret-path-91", "full long URLs are never logged");
        }

        [Fact]
        public async Task Same_key_and_request_replays_the_identical_body()
        {
            string key = Guid.NewGuid().ToString();

            using HttpResponseMessage first = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/r\",\"customAlias\":null}", key);
            // Different field order, whitespace and an explicit null: the same normalized request (AC7).
            using HttpResponseMessage second = await PostRawAsync(_one, "{ \"customAlias\": null,\n  \"longUrl\" : \"https://example.com/r\" }", key);

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            second.StatusCode.Should().Be(HttpStatusCode.Created);
            first.Headers.Contains(ShortUrlsControllerReplayedHeader).Should().BeFalse();
            second.Headers.GetValues(ShortUrlsControllerReplayedHeader).Should().Equal("true");
            (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
            second.Headers.Location.Should().Be(first.Headers.Location);
            (await LinkCountForAsync("https://example.com/r", ApiFactory.CallerOneOwner, key)).Should().Be(1);
        }

        [Fact]
        public async Task Same_key_for_a_different_request_is_422()
        {
            string key = Guid.NewGuid().ToString();
            using HttpResponseMessage first = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/x1\"}", key);

            using HttpResponseMessage second = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/x2\"}", key);

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            await AssertProblemAsync(second, HttpStatusCode.UnprocessableEntity, ApplicationErrorCodes.IdempotencyKeyReused);
        }

        [Fact]
        public async Task Same_key_from_another_client_is_independent()
        {
            string key = Guid.NewGuid().ToString();

            using HttpResponseMessage one = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/shared\"}", key);
            using HttpResponseMessage two = await PostRawAsync(_two, "{\"longUrl\":\"https://example.com/shared\"}", key);

            one.StatusCode.Should().Be(HttpStatusCode.Created);
            two.StatusCode.Should().Be(HttpStatusCode.Created);
            two.Headers.Contains(ShortUrlsControllerReplayedHeader).Should().BeFalse();
            (await CodeOf(two)).Should().NotBe(await CodeOf(one));
        }

        [Fact]
        public async Task Key_older_than_24_hours_creates_a_new_link()
        {
            string key = Guid.NewGuid().ToString();
            using HttpResponseMessage first = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/old\"}", key);
            await ExecuteAsync($"UPDATE idempotency_keys SET created_at = created_at - interval '25 hours' WHERE idem_key = '{key}'");

            using HttpResponseMessage second = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/old\"}", key);

            second.StatusCode.Should().Be(HttpStatusCode.Created);
            second.Headers.Contains(ShortUrlsControllerReplayedHeader).Should().BeFalse();
            (await CodeOf(second)).Should().NotBe(await CodeOf(first));
        }

        [Fact]
        public async Task A_409_stores_no_key_so_the_key_can_be_used_again()
        {
            string alias = TestCodes.NewAlias().Value;
            string key = Guid.NewGuid().ToString();
            using HttpResponseMessage taken = await PostAsync(_two, new { longUrl = "https://example.com/", customAlias = alias });

            using HttpResponseMessage conflict = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/\",\"customAlias\":\"" + alias + "\"}", key);
            object? rows = await ScalarAsync($"SELECT count(*) FROM idempotency_keys WHERE idem_key = '{key}'");
            using HttpResponseMessage retry = await PostRawAsync(_one, "{\"longUrl\":\"https://example.com/\"}", key);

            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
            rows.Should().Be(0L);
            retry.StatusCode.Should().Be(HttpStatusCode.Created, "a different request with the same key is new, not 422");
        }

        [Fact]
        public async Task Identical_requests_at_the_same_moment_create_one_link()
        {
            string key = Guid.NewGuid().ToString();
            string longUrl = "https://example.com/race-" + Guid.NewGuid().ToString("N");

            HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 10)
                .Select(_ => PostRawAsync(_one, "{\"longUrl\":\"" + longUrl + "\"}", key)));
            try
            {
                responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
                List<string> codes = new List<string>();
                foreach (HttpResponseMessage response in responses)
                {
                    codes.Add(await CodeOf(response));
                }

                codes.Distinct().Should().ContainSingle("every caller gets the same link");
                responses.Count(r => r.Headers.Contains(ShortUrlsControllerReplayedHeader)).Should().Be(9);
                (await ScalarAsync($"SELECT count(*) FROM short_urls WHERE long_url = '{longUrl}'")).Should().Be(1L);
            }
            finally
            {
                foreach (HttpResponseMessage response in responses)
                {
                    response.Dispose();
                }
            }
        }

        private const string ShortUrlsControllerReplayedHeader = UrlShortener.Api.Controllers.ShortUrlsController.ReplayedHeader;

        private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body)
        {
            return PostRawAsync(client, JsonSerializer.Serialize(body), null);
        }

        private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json, string? idempotencyKey)
        {
            using HttpRequestMessage request = Request(json);
            if (idempotencyKey != null)
            {
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            }

            return await client.SendAsync(request);
        }

        private static HttpRequestMessage Request(string json)
        {
            return new HttpRequestMessage(HttpMethod.Post, new Uri(Path, UriKind.Relative))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        private static async Task<string> CodeOf(HttpResponseMessage response)
        {
            return (await JsonAsync(response)).GetProperty("code").GetString()!;
        }

        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            response.StatusCode.Should().Be(status);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            JsonElement body = await JsonAsync(response);
            body.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be(code);
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        }

        private async Task<long> LinkCountForAsync(string longUrl, string owner, string key)
        {
            object? count = await ScalarAsync($"SELECT count(*) FROM short_urls WHERE long_url = '{longUrl}' AND owner_id = '{owner}' AND created_at >= (SELECT created_at FROM idempotency_keys WHERE idem_key = '{key}')");
            return (long)count!;
        }

        private async Task<object?> ScalarAsync(string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
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
