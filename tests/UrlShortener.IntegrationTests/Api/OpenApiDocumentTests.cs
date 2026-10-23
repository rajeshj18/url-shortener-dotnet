using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Api
{
    /// <summary>
    /// TSK-08 AC10: Swagger is off by default and served when enabled, and the committed
    /// <c>docs/api/openapi-v1.json</c> matches what the code generates. To accept an intended API change, run the
    /// tests once with <c>UPDATE_OPENAPI_SNAPSHOT=1</c> and commit the regenerated file.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class OpenApiDocumentTests
    {
        private const string DocumentPath = "/swagger/v1/swagger.json";

        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };

        private readonly PostgresFixture _db;

        public OpenApiDocumentTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public async Task Swagger_is_not_served_by_default()
        {
            using HttpClient client = _db.SharedApi.CreateClient();

            using HttpResponseMessage document = await client.GetAsync(new Uri(DocumentPath, UriKind.Relative));
            using HttpResponseMessage ui = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));

            document.StatusCode.Should().Be(HttpStatusCode.NotFound);
            ui.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Enabled_swagger_serves_the_ui_and_a_document_matching_the_committed_one()
        {
            using ApiFactory factory = new ApiFactory(_db.ApiConnection, enableOpenApi: true);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage ui = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative));
            using HttpResponseMessage response = await client.GetAsync(new Uri(DocumentPath, UriKind.Relative));
            response.StatusCode.Should().Be(HttpStatusCode.OK, "document generation failed: {0}", string.Join(" | ", factory.Logs.Lines));
            string generated = Normalize(await response.Content.ReadAsStringAsync());

            ui.StatusCode.Should().Be(HttpStatusCode.OK);
            string committedPath = Path.Join(RepositoryPaths.Root(), "docs", "api", "openapi-v1.json");
            if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
            {
                await File.WriteAllTextAsync(committedPath, generated);
            }

            File.Exists(committedPath).Should().BeTrue("the OpenAPI document is committed (LLD §3)");
            Normalize(await File.ReadAllTextAsync(committedPath)).Should().Be(
                generated,
                "the API changed without updating docs/api/openapi-v1.json; rerun with UPDATE_OPENAPI_SNAPSHOT=1 and review the diff");
        }

        private static string Normalize(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            // Line breaks inside descriptions come from the XML documentation file, which the compiler writes with
            // the OS's line ending (CRLF on Windows, LF on the Linux CI runner). Normalize the escaped ones too.
            return JsonSerializer.Serialize(document.RootElement, Indented)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\\r\\n", "\\n", StringComparison.Ordinal) + "\n";
        }
    }
}
