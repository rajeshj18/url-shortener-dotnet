using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Api
{
    /// <summary>
    /// TSK-QW #7 (found during TSK-14): a stored row that breaks a domain rule is the server's fault, not the client's.
    /// Reading it must answer <c>500</c> (and be logged), not <c>400</c> as if the request were invalid. The row is
    /// written directly, as corruption or a missed migration would leave it.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class StoredDataErrorTests : IDisposable
    {
        // Breaks the LongUrl rules (scheme), which every write path enforces, so only direct writes can create it.
        private const string InvalidStoredUrl = "javascript:alert-stored-corruption";

        private readonly PostgresFixture _db;
        private readonly HttpClient _owner;

        public StoredDataErrorTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _owner = db.SharedApi.CreateClientFor(ApiFactory.CallerOneHeader);
        }

        public void Dispose()
        {
            _owner.Dispose();
        }

        [Fact]
        public async Task Metadata_of_a_corrupt_row_is_500_not_400()
        {
            string alias = await InsertCorruptRowAsync();

            using HttpResponseMessage response = await _owner.GetAsync(new Uri("/api/v1/urls/" + alias, UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, "the request is valid; the stored data is not");
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        }

        [Fact]
        public async Task Redirect_of_a_corrupt_row_is_500_not_400()
        {
            string alias = await InsertCorruptRowAsync();
            using RedirectFactory redirect = new RedirectFactory(_db.RedirectConnection);
            using HttpClient browser = redirect.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            using HttpResponseMessage response = await browser.GetAsync(new Uri("/" + alias, UriKind.Relative));

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().NotContain(InvalidStoredUrl, "stored data never leaks into responses");
        }

        [Fact]
        public async Task The_error_is_logged_without_the_stored_url()
        {
            string alias = await InsertCorruptRowAsync();

            using HttpResponseMessage response = await _owner.GetAsync(new Uri("/api/v1/urls/" + alias, UriKind.Relative));
            string logs = string.Join("\n", _db.SharedApi.Logs.Lines);

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            logs.Should().Contain(alias, "the log names the broken record so an operator can find it");
            logs.Should().NotContain(InvalidStoredUrl, "long URLs are never logged (ADR-0013)");
        }

        private async Task<string> InsertCorruptRowAsync()
        {
            string alias = TestCodes.NewAlias().Value;
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand insert = new NpgsqlCommand(
                "INSERT INTO short_urls (code, long_url, owner_id, is_custom, created_at) VALUES (@code, @url, @owner, true, now())", connection);
            insert.Parameters.AddWithValue("code", alias);
            insert.Parameters.AddWithValue("url", InvalidStoredUrl);
            insert.Parameters.AddWithValue("owner", ApiFactory.CallerOneOwner);
            await insert.ExecuteNonQueryAsync();
            return alias;
        }
    }
}
