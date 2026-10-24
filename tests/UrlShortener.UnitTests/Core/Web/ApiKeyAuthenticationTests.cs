using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Core.Web;
using Xunit;

namespace UrlShortener.UnitTests.Core.Web
{
    /// <summary>TSK-08 (kept): API-key matching and client configuration, validated at startup.</summary>
    public sealed class ApiKeyAuthenticationTests
    {
        private static readonly List<ApiKeyClient> Clients = new List<ApiKeyClient>
        {
            new ApiKeyClient { OwnerId = "one", KeyHash = Hex("caller-one") },
            new ApiKeyClient { OwnerId = "two", KeyHash = LowerHex("caller-two") },
        };

        [Theory]
        [InlineData("caller-one", "one")]
        [InlineData("caller-two", "two")]
        [InlineData("caller-three", null)]
        [InlineData("", null)]
        public void Presented_key_maps_to_its_owner(string presented, string? owner)
        {
            ApiKeyAuthentication.FindOwner(presented, Clients).Should().Be(owner);
        }

        [Fact]
        public void Valid_client_configuration_registers_the_scheme()
        {
            ServiceCollection services = new ServiceCollection();

            ApiKeyAuthentication.Add(services, Config(("ApiKeys:Clients:0:OwnerId", "one"), ("ApiKeys:Clients:0:KeyHash", Hex("k"))));

            services.Should().NotBeEmpty();
        }

        [Fact]
        public void No_clients_fails_at_startup()
        {
            Action act = () => ApiKeyAuthentication.Add(new ServiceCollection(), Config());

            act.Should().Throw<InvalidOperationException>().WithMessage("*at least one client*");
        }

        [Theory]
        [InlineData("", "valid")]
        [InlineData("one", "not-a-hash")]
        public void Invalid_client_fails_at_startup(string owner, string hash)
        {
            string keyHash = hash == "valid" ? Hex("k") : hash;

            Action act = () => ApiKeyAuthentication.Add(new ServiceCollection(), Config(("ApiKeys:Clients:0:OwnerId", owner), ("ApiKeys:Clients:0:KeyHash", keyHash)));

            act.Should().Throw<InvalidOperationException>().WithMessage("ApiKeys:Clients:0*");
        }

        [Fact]
        public void Duplicate_key_hash_fails_at_startup()
        {
            Action act = () => ApiKeyAuthentication.Add(new ServiceCollection(), Config(
                ("ApiKeys:Clients:0:OwnerId", "one"), ("ApiKeys:Clients:0:KeyHash", Hex("k")),
                ("ApiKeys:Clients:1:OwnerId", "two"), ("ApiKeys:Clients:1:KeyHash", LowerHex("k"))));

            act.Should().Throw<InvalidOperationException>().WithMessage("*same key hash twice*");
        }

        internal static IConfiguration Config(params (string Key, string? Value)[] values)
        {
            return new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();
        }

        // Lower-case on purpose: configured hashes are accepted in either case.
        private static string LowerHex(string value)
        {
            return new string(Hex(value).Select(char.ToLowerInvariant).ToArray());
        }

        internal static string Hex(string value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }
}
