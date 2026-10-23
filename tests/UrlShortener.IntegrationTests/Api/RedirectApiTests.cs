using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using UrlShortener.Application.Redirects;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Api
{
    /// <summary>
    /// TSK-09 AC1–AC6 through HTTP, on a real PostgreSQL: links are created through the Api, then resolved by the
    /// Redirect service as its least-privileged role (the view only).
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class RedirectApiTests : IDisposable
    {
        private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36";

        private readonly PostgresFixture _db;
        private readonly RedirectFactory _redirect;
        private readonly HttpClient _client;

        public RedirectApiTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _redirect = new RedirectFactory(db.RedirectConnection);
            _client = _redirect.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);
        }

        public void Dispose()
        {
            _client.Dispose();
            _redirect.Dispose();
        }

        [Fact]
        public async Task Created_link_redirects_at_once_with_no_store()
        {
            string longUrl = "https://example.com/target?q=" + Guid.NewGuid().ToString("N");
            string code = await CreateAsync(longUrl, null);

            using HttpResponseMessage response = await _client.GetAsync(new Uri("/" + code, UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location!.OriginalString.Should().Be(longUrl);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }

        [Fact]
        public async Task Alias_redirects_too()
        {
            string alias = TestCodes.NewAlias().Value;
            await CreateAsync("https://example.com/alias-target", alias);

            using HttpResponseMessage response = await _client.GetAsync(new Uri("/" + alias, UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location!.OriginalString.Should().Be("https://example.com/alias-target");
        }

        [Theory]
        [InlineData("/zzzzzz9")]          // generated shape, never created
        [InlineData("/no-such-alias")]
        [InlineData("/abc")]              // malformed: too short
        [InlineData("/health")]           // reserved path, not a code
        public async Task Unknown_or_malformed_code_is_404_with_no_store(string path)
        {
            using HttpResponseMessage response = await _client.GetAsync(new Uri(path, UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }

        [Fact]
        public async Task Deleted_link_is_410_and_cacheable_for_a_day()
        {
            string code = await CreateAsync("https://example.com/deleted", null);
            await ExecuteAsync($"UPDATE short_urls SET deleted_at = now() WHERE code = '{code}'");

            using HttpResponseMessage response = await _client.GetAsync(new Uri("/" + code, UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.Gone);
            response.Headers.CacheControl!.Public.Should().BeTrue();
            response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(1));
            _redirect.Clicks.Events.Should().BeEmpty();
        }

        [Fact]
        public async Task Head_answers_like_get_without_a_body_or_a_click()
        {
            string code = await CreateAsync("https://example.com/head", null);

            using HttpRequestMessage head = new HttpRequestMessage(HttpMethod.Head, new Uri("/" + code, UriKind.Relative));
            using HttpResponseMessage response = await _client.SendAsync(head);

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            response.Headers.Location!.OriginalString.Should().Be("https://example.com/head");
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
            _redirect.Clicks.Events.Should().BeEmpty();
        }

        [Fact]
        public async Task Get_from_a_browser_publishes_one_click_and_a_bot_none()
        {
            string code = await CreateAsync("https://example.com/clicks", null);

            using (HttpRequestMessage browser = new HttpRequestMessage(HttpMethod.Get, new Uri("/" + code, UriKind.Relative)))
            {
                browser.Headers.Referrer = new Uri("https://news.example.org/story/42?utm=x");
                using HttpResponseMessage response = await _client.SendAsync(browser);
                response.StatusCode.Should().Be(HttpStatusCode.Found);
            }

            using (HttpRequestMessage bot = new HttpRequestMessage(HttpMethod.Get, new Uri("/" + code, UriKind.Relative)))
            {
                bot.Headers.UserAgent.Clear();
                bot.Headers.UserAgent.ParseAdd("Slackbot-LinkExpanding/1.0");
                using HttpResponseMessage response = await _client.SendAsync(bot);
                response.StatusCode.Should().Be(HttpStatusCode.Found);
            }

            ClickEvent click = _redirect.Clicks.Events.Should().ContainSingle().Subject;
            click.Code.Should().Be(code);
            click.ReferrerHost.Should().Be("news.example.org");
            click.Device.Should().Be("desktop");
        }

        [Fact]
        public async Task Health_endpoints_are_not_treated_as_codes()
        {
            using HttpResponseMessage live = await _client.GetAsync(new Uri("/health/live", UriKind.Relative));

            live.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        private async Task<string> CreateAsync(string longUrl, string? alias)
        {
            using HttpClient api = _db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
            string json = alias == null
                ? JsonSerializer.Serialize(new { longUrl })
                : JsonSerializer.Serialize(new { longUrl, customAlias = alias });
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await api.PostAsync(new Uri("/api/v1/urls", UriKind.Relative), content);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("code").GetString()!;
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
