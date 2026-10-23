using System.Diagnostics.Metrics;

namespace UrlShortener.Infrastructure.Observability
{
    /// <summary>The application's own OpenTelemetry meter (ADR-0013). Exported next to the ASP.NET Core metrics.</summary>
    public static class UrlShortenerMeters
    {
        /// <summary>Meter name.</summary>
        public const string Name = "UrlShortener";

        /// <summary>The process-wide meter. Instruments are created once, by the classes that own them.</summary>
        public static readonly Meter Meter = new Meter(Name);
    }
}
