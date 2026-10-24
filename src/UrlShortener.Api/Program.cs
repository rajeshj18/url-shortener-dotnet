using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Core;
using UrlShortener.Core.Web;

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
            builder.Services.AddApiCore(builder.Configuration);
            builder.Services.AddUrlShortenerOpenApi();
            builder.Services.AddTelemetry(builder.Configuration, ServiceName);

            WebApplication app = builder.Build();

            app.UseWebDefaults();
            app.UseUrlShortenerOpenApi();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
