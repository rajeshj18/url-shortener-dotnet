using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Observability;
using Xunit;

namespace UrlShortener.UnitTests.Infrastructure.Observability
{
    /// <summary>Proves AC8 of TSK-02: without an OTLP endpoint, observability is off.</summary>
    public sealed class ObservabilityServiceCollectionExtensionsTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void AddUrlShortenerObservability_no_endpoint_registers_nothing(string? endpoint)
        {
            ServiceCollection services = new ServiceCollection();
            IConfiguration configuration = BuildConfiguration(endpoint);

            services.AddUrlShortenerObservability(configuration, "test-service");

            services.Should().BeEmpty();
        }

        [Fact]
        public void AddUrlShortenerObservability_with_endpoint_registers_opentelemetry()
        {
            ServiceCollection services = new ServiceCollection();
            IConfiguration configuration = BuildConfiguration("http://localhost:4317");

            services.AddUrlShortenerObservability(configuration, "test-service");

            services.Should().NotBeEmpty();
        }

        [Fact]
        public void AddUrlShortenerObservability_empty_service_name_throws()
        {
            ServiceCollection services = new ServiceCollection();

            Action act = () => services.AddUrlShortenerObservability(BuildConfiguration(null), string.Empty);

            act.Should().Throw<ArgumentException>();
        }

        private static IConfiguration BuildConfiguration(string? endpoint)
        {
            Dictionary<string, string?> values = new Dictionary<string, string?>
            {
                [ObservabilityServiceCollectionExtensions.EndpointKey] = endpoint,
            };
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }
    }
}
