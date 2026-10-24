using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Core;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Links;
using UrlShortener.Core.Redirects;
using UrlShortener.Core.Web;
using UrlShortener.UnitTests.Core.Web;
using Xunit;

namespace UrlShortener.UnitTests.Core
{
    /// <summary>
    /// TSK-07, TSK-08, TSK-09 and TSK-02 AC8 (kept): each service's wiring validates its configuration at startup, and
    /// error messages never contain a secret. Nothing here connects to a database.
    /// </summary>
    public sealed class CoreServicesTests
    {
        private const string ValidPassphrase = "unit-test-feistel-passphrase-0123456789";

        [Fact]
        public void Valid_api_configuration_registers_the_services()
        {
            ServiceCollection services = new ServiceCollection();

            services.AddApiCore(ApiConfig());

            services.Should().Contain(d => d.ServiceType == typeof(LinkService));
            services.Should().Contain(d => d.ServiceType == typeof(StatsService));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("too-short-secret-31-characters!")]
        public void Missing_or_short_feistel_key_fails_without_revealing_it(string? key)
        {
            Action act = () => new ServiceCollection().AddApiCore(ApiConfig(("ShortCode:FeistelKey", key)));

            InvalidOperationException error = act.Should().Throw<InvalidOperationException>().Which;
            error.Message.Should().Contain("ShortCode:FeistelKey");
            if (key != null)
            {
                error.Message.Should().NotContain(key);
            }
        }

        [Theory]
        [InlineData("ShortCode:LeaseSize", "99", "*LeaseSize*")]
        [InlineData("ShortCode:LeaseSize", "1000001", "*LeaseSize*")]
        [InlineData("ShortCode:DeploymentRegionIndex", "-1", "*DeploymentRegionIndex*")]
        [InlineData("PublicBaseUrl", "not a url", "PublicBaseUrl*")]
        [InlineData("PublicBaseUrl", "ftp://sho.rt", "PublicBaseUrl*")]
        [InlineData("ConnectionStrings:LinksPrimary", "", "*LinksPrimary*")]
        public void Invalid_api_settings_fail_at_startup(string key, string value, string message)
        {
            Action act = () => new ServiceCollection().AddApiCore(ApiConfig((key, value)));

            act.Should().Throw<InvalidOperationException>().WithMessage(message);
        }

        [Fact]
        public void Redirect_wiring_reads_the_budget_and_rejects_a_zero_budget()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddRedirectCore(RedirectConfig(null));
            Action zero = () => new ServiceCollection().AddRedirectCore(RedirectConfig("0"));

            services.Should().Contain(d => d.ServiceType == typeof(RedirectService));
            zero.Should().Throw<InvalidOperationException>().WithMessage("Redirect:PrimaryFallbackPerSecond*");
        }

        [Fact]
        public void Worker_wiring_needs_the_analytics_connection()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddWorkerCore(Config(("ConnectionStrings:Analytics", "Host=db")));
            Action missing = () => new ServiceCollection().AddWorkerCore(Config());

            services.Should().Contain(d => d.ServiceType == typeof(ClickConsumer));
            missing.Should().Throw<InvalidOperationException>().WithMessage("*Analytics*");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Telemetry_without_an_endpoint_registers_nothing(string? endpoint)
        {
            ServiceCollection services = new ServiceCollection();

            services.AddTelemetry(Config((AppTelemetry.EndpointKey, endpoint)), "test-service");

            services.Should().BeEmpty();
        }

        [Fact]
        public void Telemetry_with_an_endpoint_registers_opentelemetry_and_needs_a_name()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddTelemetry(Config((AppTelemetry.EndpointKey, "http://localhost:4317")), "test-service");
            Action noName = () => new ServiceCollection().AddTelemetry(Config(), string.Empty);

            services.Should().NotBeEmpty();
            noName.Should().Throw<ArgumentException>();
        }

        private static IConfiguration ApiConfig(params (string Key, string? Value)[] overrides)
        {
            Dictionary<string, string?> values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:LinksPrimary"] = "Host=db;Database=links",
                ["ShortCode:FeistelKey"] = ValidPassphrase,
                ["PublicBaseUrl"] = "https://sho.rt",
                ["ApiKeys:Clients:0:OwnerId"] = "one",
                ["ApiKeys:Clients:0:KeyHash"] = ApiKeyAuthenticationTests.Hex("k"),
            };
            foreach ((string key, string? value) in overrides)
            {
                values[key] = value;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static IConfiguration RedirectConfig(string? perSecond)
        {
            return Config(
                ("ConnectionStrings:LinksPrimary", "Host=db"),
                ("ConnectionStrings:LinksReplicas", "Host=db"),
                (CoreServices.FallbackPerSecondKey, perSecond));
        }

        private static IConfiguration Config(params (string Key, string? Value)[] values)
        {
            return new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();
        }
    }
}
