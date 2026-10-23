using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.ShortCodes;
using Xunit;

namespace UrlShortener.UnitTests.Infrastructure.ShortCodes
{
    /// <summary>TSK-07: configuration is validated at startup, and error messages never contain the key.</summary>
    public sealed class ShortCodeServiceCollectionExtensionsTests
    {
        private const string ValidPassphrase = "unit-test-feistel-passphrase-0123456789";

        [Fact]
        public void Valid_configuration_registers_generation()
        {
            ServiceCollection services = new ServiceCollection();

            services.AddShortCodeGeneration(Configuration(ValidPassphrase));

            services.Should().Contain(d => d.ServiceType == typeof(UrlShortener.Application.Ports.IShortCodeGenerator));
            services.Should().Contain(d => d.ServiceType == typeof(UrlShortener.Application.Ports.IIdRangeLeaser));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("too-short-secret-31-characters!")]
        public void Missing_or_short_key_fails_without_revealing_it(string? key)
        {
            Action act = () => new ServiceCollection().AddShortCodeGeneration(Configuration(key));

            InvalidOperationException error = act.Should().Throw<InvalidOperationException>().Which;
            error.Message.Should().Contain("ShortCode:FeistelKey");
            if (key != null)
            {
                error.Message.Should().NotContain(key);
            }
        }

        [Theory]
        [InlineData("99")]
        [InlineData("1000001")]
        public void Lease_size_out_of_range_fails(string leaseSize)
        {
            Action act = () => new ServiceCollection().AddShortCodeGeneration(
                Configuration(ValidPassphrase, ("ShortCode:LeaseSize", leaseSize)));

            act.Should().Throw<InvalidOperationException>().WithMessage("*LeaseSize*");
        }

        [Fact]
        public void Negative_region_fails()
        {
            Action act = () => new ServiceCollection().AddShortCodeGeneration(
                Configuration(ValidPassphrase, ("ShortCode:DeploymentRegionIndex", "-1")));

            act.Should().Throw<InvalidOperationException>().WithMessage("*DeploymentRegionIndex*");
        }

        [Fact]
        public void Defaults_are_region_0_and_10000_ids_per_lease()
        {
            ShortCodeOptions options = new ShortCodeOptions();

            options.DeploymentRegionIndex.Should().Be(0);
            options.LeaseSize.Should().Be(10_000);
            options.FeistelKey.Should().BeNull();
        }

        [Fact]
        public void Null_arguments_throw()
        {
            Action noServices = () => ShortCodeServiceCollectionExtensions.AddShortCodeGeneration(null!, Configuration(ValidPassphrase));
            Action noConfiguration = () => new ServiceCollection().AddShortCodeGeneration(null!);

            noServices.Should().Throw<ArgumentNullException>();
            noConfiguration.Should().Throw<ArgumentNullException>();
        }

        private static IConfiguration Configuration(string? key, params (string Key, string Value)[] extra)
        {
            Dictionary<string, string?> values = new Dictionary<string, string?> { ["ShortCode:FeistelKey"] = key };
            foreach ((string name, string value) in extra)
            {
                values[name] = value;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }
    }
}
