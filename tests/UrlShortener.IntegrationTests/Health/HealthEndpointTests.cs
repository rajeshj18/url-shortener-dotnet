using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Infrastructure.Web;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Health
{
    /// <summary>
    /// TSK-02 AC7/AC8 and TSK-06 AC6. Every service starts and answers its health endpoints. From TSK-06,
    /// readiness includes the database: the Api checks the primary, Redirect checks the replicas.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class HealthEndpointTests
    {
        // A closed port: connection refused immediately, so the "database down" tests stay fast.
        private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=links;Username=nobody;Password=x;Timeout=2";

        private readonly PostgresFixture _db;

        public HealthEndpointTests(PostgresFixture db)
        {
            _db = db;
        }

        [Theory]
        [InlineData(HealthEndpointExtensions.LivePath)]
        [InlineData(HealthEndpointExtensions.ReadyPath)]
        public async Task Api_health_endpoint_returns_200(string path)
        {
            using ApiFactory factory = new ApiFactory(_db.ApiConnection);
            using HttpClient client = factory.CreateClient();
            await AssertStatusAsync(client, path, HttpStatusCode.OK);
        }

        [Theory]
        [InlineData(HealthEndpointExtensions.LivePath)]
        [InlineData(HealthEndpointExtensions.ReadyPath)]
        public async Task Redirect_health_endpoint_returns_200(string path)
        {
            using RedirectFactory factory = new RedirectFactory(_db.RedirectConnection);
            using HttpClient client = factory.CreateClient();
            await AssertStatusAsync(client, path, HttpStatusCode.OK);
        }

        [Theory]
        [InlineData(HealthEndpointExtensions.LivePath)]
        [InlineData(HealthEndpointExtensions.ReadyPath)]
        public async Task AnalyticsWorker_health_endpoint_returns_200(string path)
        {
            using WebApplicationFactory<UrlShortener.AnalyticsWorker.Program> factory = new WebApplicationFactory<UrlShortener.AnalyticsWorker.Program>();
            using HttpClient client = factory.CreateClient();
            await AssertStatusAsync(client, path, HttpStatusCode.OK);
        }

        [Fact]
        public async Task Api_database_down_is_live_but_not_ready()
        {
            using ApiFactory factory = new ApiFactory(UnreachableDatabase);
            using HttpClient client = factory.CreateClient();

            await AssertStatusAsync(client, HealthEndpointExtensions.LivePath, HttpStatusCode.OK);
            await AssertStatusAsync(client, HealthEndpointExtensions.ReadyPath, HttpStatusCode.ServiceUnavailable);
        }

        [Fact]
        public async Task Redirect_database_down_is_live_but_not_ready()
        {
            using RedirectFactory factory = new RedirectFactory(UnreachableDatabase);
            using HttpClient client = factory.CreateClient();

            await AssertStatusAsync(client, HealthEndpointExtensions.LivePath, HttpStatusCode.OK);
            await AssertStatusAsync(client, HealthEndpointExtensions.ReadyPath, HttpStatusCode.ServiceUnavailable);
        }

        /// <summary>Asserts the status code. The caller owns, and disposes, the client (CodeQL "missed using" note).</summary>
        private static async Task AssertStatusAsync(HttpClient client, string path, HttpStatusCode expected)
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
            response.StatusCode.Should().Be(expected);
        }
    }
}
