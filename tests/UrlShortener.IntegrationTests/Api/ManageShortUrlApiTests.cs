using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using UrlShortener.Application.ShortUrls;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Api
{
    /// <summary>TSK-10 AC1–AC5 through HTTP on a real PostgreSQL, across the Api and the Redirect service.</summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class ManageShortUrlApiTests : IDisposable
    {
        private readonly PostgresFixture _db;
        private readonly HttpClient _one;
        private readonly HttpClient _two;

        public ManageShortUrlApiTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _one = db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
            _two = db.SharedApi.CreateClientFor(ApiFactory.CallerTwoHeader);
        }

        public void Dispose()
        {
            _one.Dispose();
            _two.Dispose();
        }

        [Fact]
        public async Task Owner_reads_the_documented_fields()
        {
            string alias = TestCodes.NewAlias().Value;
            await CreateAsync(alias);

            using HttpResponseMessage response = await _one.GetAsync(Url(alias));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = body.RootElement;
            root.GetProperty("code").GetString().Should().Be(alias);
            root.GetProperty("shortUrl").GetString().Should().Be(ApiFactory.PublicBaseUrl + "/" + alias);
            root.GetProperty("longUrl").GetString().Should().Be("https://example.com/managed");
            root.GetProperty("isCustom").GetBoolean().Should().BeTrue();
            root.GetProperty("createdAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
            root.GetProperty("deletedAt").ValueKind.Should().Be(JsonValueKind.Null);
        }

        [Fact]
        public async Task Another_client_and_an_unknown_code_get_the_same_404()
        {
            string alias = TestCodes.NewAlias().Value;
            await CreateAsync(alias);

            using HttpResponseMessage others = await _two.GetAsync(Url(alias));
            using HttpResponseMessage unknown = await _one.GetAsync(Url(TestCodes.NewAlias().Value));

            others.StatusCode.Should().Be(HttpStatusCode.NotFound);
            unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await WithoutTraceIdAsync(others)).Should().Be(await WithoutTraceIdAsync(unknown), "nothing reveals that the link exists");
        }

        [Fact]
        public async Task Delete_is_204_idempotent_and_keeps_the_first_time()
        {
            string alias = TestCodes.NewAlias().Value;
            await CreateAsync(alias);

            using HttpResponseMessage first = await _one.DeleteAsync(Url(alias));
            object? firstTime = await ScalarAsync($"SELECT deleted_at FROM short_urls WHERE code = '{alias}'");
            await Task.Delay(20);
            using HttpResponseMessage second = await _one.DeleteAsync(Url(alias));
            object? secondTime = await ScalarAsync($"SELECT deleted_at FROM short_urls WHERE code = '{alias}'");

            first.StatusCode.Should().Be(HttpStatusCode.NoContent);
            second.StatusCode.Should().Be(HttpStatusCode.NoContent);
            firstTime.Should().NotBeNull().And.Be(secondTime);
        }

        [Fact]
        public async Task Another_client_cannot_delete()
        {
            string alias = TestCodes.NewAlias().Value;
            await CreateAsync(alias);

            using HttpResponseMessage response = await _two.DeleteAsync(Url(alias));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ScalarAsync($"SELECT deleted_at FROM short_urls WHERE code = '{alias}'")).Should().Be(DBNull.Value);
        }

        [Fact]
        public async Task After_delete_redirect_is_410_metadata_shows_it_and_the_alias_stays_taken()
        {
            string alias = TestCodes.NewAlias().Value;
            await CreateAsync(alias);
            using RedirectFactory redirect = new RedirectFactory(_db.RedirectConnection);
            using HttpClient browser = redirect.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            using HttpResponseMessage before = await browser.GetAsync(new Uri("/" + alias, UriKind.Relative));
            using HttpResponseMessage delete = await _one.DeleteAsync(Url(alias));
            using HttpResponseMessage after = await browser.GetAsync(new Uri("/" + alias, UriKind.Relative));
            using HttpResponseMessage metadata = await _one.GetAsync(Url(alias));
            using HttpResponseMessage recreate = await PostAsync(_one, alias);

            before.StatusCode.Should().Be(HttpStatusCode.Found);
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
            after.StatusCode.Should().Be(HttpStatusCode.Gone);
            using (JsonDocument body = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync()))
            {
                body.RootElement.GetProperty("deletedAt").ValueKind.Should().Be(JsonValueKind.String);
            }

            recreate.StatusCode.Should().Be(HttpStatusCode.Conflict, "codes are never reused (A-25)");
        }

        [Fact]
        public async Task Missing_api_key_is_401()
        {
            using HttpClient anonymous = _db.SharedApi.CreateClient();

            using HttpResponseMessage get = await anonymous.GetAsync(Url("anything-here"));
            using HttpResponseMessage delete = await anonymous.DeleteAsync(Url("anything-here"));

            get.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            delete.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Not_found_problem_carries_its_code()
        {
            using HttpResponseMessage response = await _one.GetAsync(Url(TestCodes.NewAlias().Value));

            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be(ApplicationErrorCodes.LinkNotFound);
        }

        private static Uri Url(string code)
        {
            return new Uri("/api/v1/urls/" + code, UriKind.Relative);
        }

        private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
        {
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            System.Collections.Generic.Dictionary<string, JsonElement> fields = new System.Collections.Generic.Dictionary<string, JsonElement>();
            foreach (JsonProperty property in body.RootElement.EnumerateObject())
            {
                if (property.Name != "traceId")
                {
                    fields[property.Name] = property.Value.Clone();
                }
            }

            return JsonSerializer.Serialize(fields);
        }

        private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string alias)
        {
            using StringContent content = new StringContent(
                JsonSerializer.Serialize(new { longUrl = "https://example.com/managed", customAlias = alias }), Encoding.UTF8, "application/json");
            return await client.PostAsync(new Uri("/api/v1/urls", UriKind.Relative), content);
        }

        private async Task CreateAsync(string alias)
        {
            using HttpResponseMessage response = await PostAsync(_one, alias);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        private async Task<object?> ScalarAsync(string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }
    }
}
