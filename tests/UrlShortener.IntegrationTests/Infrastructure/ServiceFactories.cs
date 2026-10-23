using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

        private readonly string _primary;

        public ApiFactory(string primary)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder
                .UseSetting("ConnectionStrings:" + LinksPersistenceServiceCollectionExtensions.PrimaryConnectionName, _primary)
                .UseSetting("ShortCode:FeistelKey", TestFeistelPassphrase);
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
