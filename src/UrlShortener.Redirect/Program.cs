using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Core;
using UrlShortener.Core.Web;

namespace UrlShortener.Redirect
{
    /// <summary>Entry point and composition root of the Redirect service.</summary>
    public sealed class Program
    {
        /// <summary>The service name reported to OpenTelemetry.</summary>
        public const string ServiceName = "urlshortener-redirect";

        private Program()
        {
        }

        /// <summary>Starts the Redirect service.</summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddRedirectCore(builder.Configuration);
            builder.Services.AddTelemetry(builder.Configuration, ServiceName);

            WebApplication app = builder.Build();

            app.UseWebDefaults();
            app.MapControllers();

            app.Run();
        }
    }
}
