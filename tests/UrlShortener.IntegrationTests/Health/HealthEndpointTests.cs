using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Infrastructure.Web;
using Xunit;

namespace UrlShortener.IntegrationTests.Health
{
    /// <summary>
    /// Proves AC7 and AC8 of TSK-02. Every service starts (with no OTLP endpoint configured) and answers
    /// its health endpoints. Runs in-memory, so no Docker is needed.
    /// </summary>
    public sealed class HealthEndpointTests
    {
        [Theory]
        [InlineData(HealthEndpointExtensions.LivePath)]
        [InlineData(HealthEndpointExtensions.ReadyPath)]
        public async Task Api_health_endpoint_returns_200(string path)
        {
            using WebApplicationFactory<UrlShortener.Api.Program> factory = new WebApplicationFactory<UrlShortener.Api.Program>();
            await AssertHealthyAsync(factory.CreateClient(), path);
        }

        [Theory]
        [InlineData(HealthEndpointExtensions.LivePath)]
        [InlineData(HealthEndpointExtensions.ReadyPath)]
        public async Task Redirect_health_endpoint_returns_200(string path)
        {
            using WebApplicationFactory<UrlShortener.Redirect.Program> factory = new WebApplicationFactory<UrlShortener.Redirect.Program>();
            await AssertHealthyAsync(factory.CreateClient(), path);
        }

        [Theory]
        [InlineData(HealthEndpointExtensions.LivePath)]
        [InlineData(HealthEndpointExtensions.ReadyPath)]
        public async Task AnalyticsWorker_health_endpoint_returns_200(string path)
        {
            using WebApplicationFactory<UrlShortener.AnalyticsWorker.Program> factory = new WebApplicationFactory<UrlShortener.AnalyticsWorker.Program>();
            await AssertHealthyAsync(factory.CreateClient(), path);
        }

        private static async Task AssertHealthyAsync(HttpClient client, string path)
        {
            using (client)
            {
                using HttpResponseMessage response = await client.GetAsync(new System.Uri(path, System.UriKind.Relative));
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
            }
        }
    }
}
