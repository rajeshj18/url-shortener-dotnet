using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Observability;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.ShortCodes;
using UrlShortener.Infrastructure.Web;

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
            builder.Services.AddUrlShortenerWebDefaults();
            builder.Services.AddUrlShortenerHealthChecks();
            builder.Services.AddLinksWriteStore(builder.Configuration);
            builder.Services.AddShortCodeGeneration(builder.Configuration);
            builder.Services.AddUrlShortenerObservability(builder.Configuration, ServiceName);

            WebApplication app = builder.Build();

            app.UseUrlShortenerMiddleware();
            app.MapUrlShortenerHealthEndpoints();
            app.MapControllers();

            app.Run();
        }
    }
}
