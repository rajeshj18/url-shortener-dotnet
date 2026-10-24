using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Core;
using UrlShortener.Core.Web;

namespace UrlShortener.AnalyticsWorker
{
    /// <summary>Entry point and composition root of the AnalyticsWorker service (ADR-0008).</summary>
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

            builder.Services.AddWorkerCore(builder.Configuration);
            builder.Services.AddTelemetry(builder.Configuration, ServiceName);

            WebApplication app = builder.Build();

            // Health endpoints only; the work is the ClickConsumer hosted service.
            app.MapHealthChecks(WebDefaults.LivePath, new HealthCheckOptions { Predicate = _ => false });
            app.MapHealthChecks(WebDefaults.ReadyPath, new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(WebDefaults.ReadyTag) });

            app.Run();
        }
    }
}
