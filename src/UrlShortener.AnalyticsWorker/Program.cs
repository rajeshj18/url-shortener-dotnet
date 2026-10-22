using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Observability;
using UrlShortener.Infrastructure.Web;

namespace UrlShortener.AnalyticsWorker
{
    /// <summary>Entry point and composition root of the AnalyticsWorker service.</summary>
    public sealed class Program
    {
        /// <summary>The service name reported to OpenTelemetry.</summary>
        public const string ServiceName = "urlshortener-worker";

        private Program()
        {
        }

        /// <summary>Starts the AnalyticsWorker service.</summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddHostedService<PlaceholderWorker>();
            builder.Services.AddUrlShortenerHealthChecks();
            builder.Services.AddUrlShortenerObservability(builder.Configuration, ServiceName);

            WebApplication app = builder.Build();

            app.MapUrlShortenerHealthEndpoints();

            app.Run();
        }
    }
}
