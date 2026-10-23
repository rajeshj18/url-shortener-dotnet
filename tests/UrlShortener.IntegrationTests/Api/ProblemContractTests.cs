using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Api
{
    /// <summary>
    /// TSK-15: the error-response contract across every code path that produces a problem. Written to reproduce two
    /// defects found in the merged code, and committed before the fixes:
    /// (1) a <c>401</c> must carry <c>WWW-Authenticate</c> (RFC 9110 §15.5.2: "MUST");
    /// (2) every problem's <c>traceId</c> has one format, the 32-hex W3C trace ID that logs and traces use
    /// (TSK-15 D2), whether our handlers or the framework produced the response.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed partial class ProblemContractTests : IDisposable
    {
        private readonly PostgresFixture _db;
        private readonly HttpClient _anonymous;
        private readonly HttpClient _caller;

        public ProblemContractTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _anonymous = db.SharedApi.CreateClient();
            _caller = db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
        }

        public void Dispose()
        {
            _anonymous.Dispose();
            _caller.Dispose();
        }

        [Fact]
        public async Task Missing_api_key_401_names_the_scheme_in_WWW_Authenticate()
        {
            using HttpResponseMessage response = await PostAsync(_anonymous);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Should().ContainSingle("RFC 9110 §15.5.2: a 401 MUST carry WWW-Authenticate")
                .Which.Scheme.Should().Be("ApiKey");
            response.Headers.WwwAuthenticate.Single().Parameter.Should().Contain("X-Api-Key", "it tells clients which header to send");
        }

        [Fact]
        public async Task Unknown_api_key_401_carries_WWW_Authenticate_too()
        {
            using HttpClient wrong = _db.SharedApi.CreateClientFor("not-a-known-caller");

            using HttpResponseMessage response = await PostAsync(wrong);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("ApiKey");
        }

        [Fact]
        public async Task Every_problem_uses_the_32_hex_trace_id()
        {
            using RedirectFactory redirect = new RedirectFactory(_db.RedirectConnection);
            using HttpClient browser = redirect.CreateClient();
            Dictionary<string, Func<Task<HttpResponseMessage>>> requests = new Dictionary<string, Func<Task<HttpResponseMessage>>>
            {
                ["400 from our validation handler"] = () => PostAsync(_caller, "{\"longUrl\":\"ftp://example.com/\"}"),
                ["401 from the authentication challenge"] = () => PostAsync(_anonymous),
                ["404 from our controller"] = () => _caller.GetAsync(new Uri("/api/v1/urls/" + TestCodes.NewAlias().Value, UriKind.Relative)),
                ["404 from Redirect's status-code pages"] = () => browser.GetAsync(new Uri("/zzzzzz9", UriKind.Relative)),
            };

            foreach (KeyValuePair<string, Func<Task<HttpResponseMessage>>> request in requests)
            {
                using HttpResponseMessage response = await request.Value();
                using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                string? traceId = body.RootElement.GetProperty("traceId").GetString();
                traceId.Should().MatchRegex(TraceIdPattern().ToString(), "{0} must use the same traceId format as the logs", request.Key);
            }
        }

        private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string json = "{\"longUrl\":\"https://example.com/\"}")
        {
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            return await client.PostAsync(new Uri("/api/v1/urls", UriKind.Relative), content);
        }

        [GeneratedRegex("^[0-9a-f]{32}$")]
        private static partial Regex TraceIdPattern();
    }
}
