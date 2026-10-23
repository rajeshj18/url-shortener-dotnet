using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;
using UrlShortener.Infrastructure.ShortUrls;
using UrlShortener.Infrastructure.Web;
using UrlShortener.Infrastructure.Web.Authentication;
using Xunit;

namespace UrlShortener.UnitTests.Infrastructure.Web
{
    /// <summary>TSK-08: API-key matching and configuration, the 400/503 problem handlers, and PublicBaseUrl validation.</summary>
    public sealed class ApiKeyAndProblemHandlerTests
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
            ApiKeyAuthenticationHandler.FindOwner(presented, Clients).Should().Be(owner);
        }

        [Fact]
        public void Valid_client_configuration_registers_the_scheme()
        {
            ServiceCollection services = new ServiceCollection();

            services.AddApiKeyAuthentication(Config(("ApiKeys:Clients:0:OwnerId", "one"), ("ApiKeys:Clients:0:KeyHash", Hex("k"))));

            services.Should().NotBeEmpty();
        }

        [Fact]
        public void No_clients_fails_at_startup()
        {
            Action act = () => new ServiceCollection().AddApiKeyAuthentication(Config());

            act.Should().Throw<InvalidOperationException>().WithMessage("*at least one client*");
        }

        [Theory]
        [InlineData("", "valid")]
        [InlineData("one", "not-a-hash")]
        public void Invalid_client_fails_at_startup(string owner, string hash)
        {
            string keyHash = hash == "valid" ? Hex("k") : hash;

            Action act = () => new ServiceCollection().AddApiKeyAuthentication(
                Config(("ApiKeys:Clients:0:OwnerId", owner), ("ApiKeys:Clients:0:KeyHash", keyHash)));

            act.Should().Throw<InvalidOperationException>().WithMessage("ApiKeys:Clients:0*");
        }

        [Fact]
        public void Duplicate_key_hash_fails_at_startup()
        {
            Action act = () => new ServiceCollection().AddApiKeyAuthentication(Config(
                ("ApiKeys:Clients:0:OwnerId", "one"), ("ApiKeys:Clients:0:KeyHash", Hex("k")),
                ("ApiKeys:Clients:1:OwnerId", "two"), ("ApiKeys:Clients:1:KeyHash", LowerHex("k"))));

            act.Should().Throw<InvalidOperationException>().WithMessage("*same key hash twice*");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("not a url")]
        [InlineData("ftp://sho.rt")]
        public void Invalid_public_base_url_fails_at_startup(string? value)
        {
            Action act = () => new ServiceCollection().AddShortUrlUseCases(Config(("PublicBaseUrl", value)));

            act.Should().Throw<InvalidOperationException>().WithMessage("PublicBaseUrl*");
        }

        [Fact]
        public async Task Domain_validation_becomes_400_with_its_code()
        {
            DefaultHttpContext context = NewContext();

            bool handled = await new DomainValidationExceptionHandler().TryHandleAsync(
                context, new DomainValidationException(DomainErrorCodes.UrlScheme, "Bad scheme."), CancellationToken.None);

            handled.Should().BeTrue();
            context.Response.StatusCode.Should().Be(400);
            context.Response.ContentType.Should().StartWith(ProblemResponses.ProblemJsonContentType);
            JsonElement body = await Body(context);
            body.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be(DomainErrorCodes.UrlScheme);
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Exhausted_id_space_becomes_503_with_retry_after()
        {
            DefaultHttpContext context = NewContext();

            bool handled = await new IdSpaceExhaustedExceptionHandler(NullLogger<IdSpaceExhaustedExceptionHandler>.Instance)
                .TryHandleAsync(context, new IdSpaceExhaustedException(), CancellationToken.None);

            handled.Should().BeTrue();
            context.Response.StatusCode.Should().Be(503);
            context.Response.Headers.RetryAfter.ToString().Should().Be("5");
        }

        [Fact]
        public async Task Handlers_ignore_other_exceptions()
        {
            InvalidOperationException other = new InvalidOperationException("x");

            (await new DomainValidationExceptionHandler().TryHandleAsync(NewContext(), other, CancellationToken.None)).Should().BeFalse();
            (await new IdSpaceExhaustedExceptionHandler(NullLogger<IdSpaceExhaustedExceptionHandler>.Instance)
                .TryHandleAsync(NewContext(), other, CancellationToken.None)).Should().BeFalse();
        }

        private static DefaultHttpContext NewContext()
        {
            DefaultHttpContext context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static async Task<JsonElement> Body(DefaultHttpContext context)
        {
            context.Response.Body.Position = 0;
            using JsonDocument document = await JsonDocument.ParseAsync(context.Response.Body);
            return document.RootElement.Clone();
        }

        private static IConfiguration Config(params (string Key, string? Value)[] values)
        {
            Dictionary<string, string?> data = new Dictionary<string, string?>();
            foreach ((string key, string? value) in values)
            {
                data[key] = value;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        }

        // Lower-case on purpose: configured hashes are accepted in either case.
        private static string LowerHex(string value)
        {
            return new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Hex(value), char.ToLowerInvariant)));
        }

        private static string Hex(string value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }
}
