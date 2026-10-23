using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Observability;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Resilience;
using UrlShortener.Infrastructure.ShortCodes;
using UrlShortener.Infrastructure.ShortUrls;
using UrlShortener.Infrastructure.Web;
using UrlShortener.Infrastructure.Web.Authentication;

namespace UrlShortener.Api
{
    /// <summary>Entry point and composition root of the Api service.</summary>
    public sealed class Program
    {
        /// <summary>The service name reported to OpenTelemetry.</summary>
        public const string ServiceName = "urlshortener-api";

        private Program()
        {
        }

        /// <summary>Starts the Api service.</summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            // LLD §6.7: the whole request gets 3 s; a database outage answers 503 with Retry-After: 5.
            TimeSpan requestTimeout = ResilienceConfiguration.Read(builder.Configuration).RequestTimeout ?? TimeSpan.FromSeconds(3);
            builder.Services.AddUrlShortenerWebDefaults(requestTimeout, unavailableRetryAfterSeconds: 5);
            builder.Services.AddApiKeyAuthentication(builder.Configuration);
            builder.Services.AddUrlShortenerHealthChecks();
            builder.Services.AddLinksWriteStore(builder.Configuration);
            builder.Services.AddShortCodeGeneration(builder.Configuration);
            builder.Services.AddShortUrlUseCases(builder.Configuration);
            builder.Services.AddUrlShortenerOpenApi();
            builder.Services.AddUrlShortenerObservability(builder.Configuration, ServiceName);

            WebApplication app = builder.Build();

            app.UseUrlShortenerMiddleware();
            app.UseUrlShortenerOpenApi();
            app.UseApiKeyAuthentication();
            app.MapUrlShortenerHealthEndpoints();
            app.MapControllers();

            app.Run();
        }
    }
}
