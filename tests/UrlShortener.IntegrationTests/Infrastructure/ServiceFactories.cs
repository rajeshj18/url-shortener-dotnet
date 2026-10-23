using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>
    /// In-memory Api service connected to a given database as the Api role. A dedicated subclass rather than
    /// <c>WithWebHostBuilder</c>, which creates a second factory and leaves the first undisposed (found by CA2000).
    /// </summary>
    public sealed class ApiFactory : WebApplicationFactory<UrlShortener.Api.Program>
    {
        /// <summary>A test-only Feistel passphrase (TSK-07). Real keys come from the secret store.</summary>
        public const string TestFeistelPassphrase = "integration-test-feistel-passphrase-0123";

        /// <summary>The public base URL the test Api builds short links from.</summary>
        public const string PublicBaseUrl = "https://sho.rt";

        /// <summary>Owner ID of the first test caller.</summary>
        public const string CallerOneOwner = "caller-one";

        /// <summary>Owner ID of the second test caller (for owner-scoping tests).</summary>
        public const string CallerTwoOwner = "caller-two";

        /// <summary>The X-Api-Key value of the first test caller (test-only).</summary>
        public const string CallerOneHeader = "integration-test-caller-one";

        /// <summary>The X-Api-Key value of the second test caller (test-only).</summary>
        public const string CallerTwoHeader = "integration-test-caller-two";

        private readonly string _primary;
        private readonly bool _enableOpenApi;

        /// <summary>Every log line the Api wrote (all levels).</summary>
        public CapturingLoggerProvider Logs { get; } = new CapturingLoggerProvider();

        public ApiFactory(string primary, bool enableOpenApi = false)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _enableOpenApi = enableOpenApi;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder
                .UseSetting("ConnectionStrings:" + LinksPersistenceServiceCollectionExtensions.PrimaryConnectionName, _primary)
                .UseSetting("ShortCode:FeistelKey", TestFeistelPassphrase)
                .UseSetting("PublicBaseUrl", PublicBaseUrl)
                .UseSetting("ApiKeys:Clients:0:OwnerId", CallerOneOwner)
                .UseSetting("ApiKeys:Clients:0:KeyHash", Sha256Hex(CallerOneHeader))
                .UseSetting("ApiKeys:Clients:1:OwnerId", CallerTwoOwner)
                .UseSetting("ApiKeys:Clients:1:KeyHash", Sha256Hex(CallerTwoHeader))
                .UseSetting("OpenApi:EnableUi", _enableOpenApi ? "true" : "false")
                .ConfigureServices(services => services.AddSingleton<ILoggerProvider>(Logs));
        }

        /// <summary>A client that sends the given caller's X-Api-Key on every request.</summary>
        /// <param name="header">The X-Api-Key value.</param>
        /// <returns>The client (the caller disposes it).</returns>
        public HttpClient CreateClientFor(string header)
        {
            HttpClient client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", header);
            return client;
        }

        private static string Sha256Hex(string value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>In-memory Redirect service connected as the Redirect role (primary and "replicas" are the same database).</summary>
    public sealed class RedirectFactory : WebApplicationFactory<UrlShortener.Redirect.Program>
    {
        private readonly string _connection;

        public RedirectFactory(string connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder
                .UseSetting("ConnectionStrings:" + LinksPersistenceServiceCollectionExtensions.PrimaryConnectionName, _connection)
                .UseSetting("ConnectionStrings:" + LinksPersistenceServiceCollectionExtensions.ReplicasConnectionName, _connection);
        }
    }
}
