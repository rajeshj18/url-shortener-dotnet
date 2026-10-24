using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Database;

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
        private readonly IReadOnlyDictionary<string, string>? _settings;
        private readonly Action<IServiceCollection>? _testServices;

        public ApiFactory(string primary, bool enableOpenApi = false, IReadOnlyDictionary<string, string>? settings = null, Action<IServiceCollection>? testServices = null)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _enableOpenApi = enableOpenApi;
            _settings = settings;
            _testServices = testServices;
        }

        /// <summary>Every log line the Api wrote (all levels).</summary>
        public CapturingLoggerProvider Logs { get; } = new CapturingLoggerProvider();

        /// <summary>A client that sends the given caller's X-Api-Key on every request.</summary>
        /// <param name="header">The X-Api-Key value.</param>
        /// <returns>The client (the caller disposes it).</returns>
        public HttpClient CreateClientFor(string header)
        {
            HttpClient client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", header);
            return client;
        }

        internal static void ApplySettings(IWebHostBuilder builder, IReadOnlyDictionary<string, string>? settings)
        {
            if (settings == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder
                .UseSetting("ConnectionStrings:" + ConnectionStrings.LinksPrimary, _primary)
                .UseSetting("ShortCode:FeistelKey", TestFeistelPassphrase)
                .UseSetting("PublicBaseUrl", PublicBaseUrl)
                .UseSetting("ApiKeys:Clients:0:OwnerId", CallerOneOwner)
                .UseSetting("ApiKeys:Clients:0:KeyHash", Sha256Hex(CallerOneHeader))
                .UseSetting("ApiKeys:Clients:1:OwnerId", CallerTwoOwner)
                .UseSetting("ApiKeys:Clients:1:KeyHash", Sha256Hex(CallerTwoHeader))
                .UseSetting("OpenApi:EnableUi", _enableOpenApi ? "true" : "false")
                .ConfigureServices(services => services.AddSingleton<ILoggerProvider>(Logs))
                .ConfigureTestServices(services => _testServices?.Invoke(services));
            ApplySettings(builder, _settings);
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
        private readonly string? _primary;
        private readonly IReadOnlyDictionary<string, string>? _settings;

        /// <param name="connection">The Redirect role's connection, used for the replicas (and the primary, unless overridden).</param>
        /// <param name="primary">A different primary, e.g. an unreachable one for breaker tests.</param>
        /// <param name="settings">Extra configuration.</param>
        public RedirectFactory(string connection, string? primary = null, IReadOnlyDictionary<string, string>? settings = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _primary = primary;
            _settings = settings;
        }

        /// <summary>Every click the Redirect service accepted (sent to Kafka only if a test configures brokers).</summary>
        public ClickRecorder Clicks { get; } = new ClickRecorder();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder
                .UseSetting("ConnectionStrings:" + ConnectionStrings.LinksPrimary, _primary ?? _connection)
                .UseSetting("ConnectionStrings:" + ConnectionStrings.LinksReplicas, _connection)
                .ConfigureTestServices(services => services.AddSingleton(provider =>
                    Clicks.Attach(new ClickPublisher(KafkaOptions.Read(provider.GetRequiredService<IConfiguration>())))));
            ApiFactory.ApplySettings(builder, _settings);
        }
    }

    /// <summary>In-memory AnalyticsWorker connected as the worker role.</summary>
    public sealed class WorkerFactory : WebApplicationFactory<UrlShortener.AnalyticsWorker.Program>
    {
        private readonly string _analytics;
        private readonly IReadOnlyDictionary<string, string>? _settings;

        public WorkerFactory(string analytics, IReadOnlyDictionary<string, string>? settings = null)
        {
            _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            _settings = settings;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.UseSetting("ConnectionStrings:" + ConnectionStrings.Analytics, _analytics);
            ApiFactory.ApplySettings(builder, _settings);
        }
    }

    /// <summary>Records the clicks a <see cref="ClickPublisher"/> accepts.</summary>
    public sealed class ClickRecorder
    {
        /// <summary>The events, in publish order.</summary>
        public ConcurrentQueue<ClickEvent> Events { get; } = new ConcurrentQueue<ClickEvent>();

        /// <summary>Subscribes to the publisher.</summary>
        /// <param name="publisher">The publisher.</param>
        /// <returns>The same publisher.</returns>
        public ClickPublisher Attach(ClickPublisher publisher)
        {
            ArgumentNullException.ThrowIfNull(publisher);
            publisher.Accepted += (_, e) => Events.Enqueue(e.Click);
            return publisher;
        }
    }
}
