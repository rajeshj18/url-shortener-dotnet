using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UrlShortener.Core.Web
{
    /// <summary>
    /// OpenTelemetry for every service (ADR-0013): traces, metrics and logs over OTLP. Without an endpoint nothing is
    /// registered, so a service runs without a collector.
    /// </summary>
    public static class AppTelemetry
    {
        /// <summary>Name of the application's meter and activity source.</summary>
        public const string Name = "UrlShortener";

        /// <summary>Configuration key for the OTLP endpoint, e.g. <c>http://otel-lgtm:4317</c>.</summary>
        public const string EndpointKey = "OpenTelemetry:Endpoint";

        /// <summary>The application's meter. Instruments are created once, by the classes that own them.</summary>
        public static readonly Meter Meter = new Meter(Name);

        /// <summary>The application's own spans, e.g. one per click batch, linked to the redirects that produced it.</summary>
        public static readonly ActivitySource Source = new ActivitySource(Name);

        /// <summary>Adds OpenTelemetry for the service if an OTLP endpoint is configured.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">Configuration; reads <see cref="EndpointKey"/>.</param>
        /// <param name="serviceName">The service name, e.g. <c>urlshortener-api</c>.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration, string serviceName)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentException.ThrowIfNullOrEmpty(serviceName);

            string? endpointValue = configuration[EndpointKey];
            if (string.IsNullOrWhiteSpace(endpointValue))
            {
                return services;
            }

            Uri endpoint = new Uri(endpointValue, UriKind.Absolute);
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(serviceName))
                .WithTracing(tracing => tracing
                    .AddSource(Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = endpoint))
                .WithMetrics(metrics => metrics
                    .AddMeter(Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = endpoint))
                .WithLogging(logging => logging
                    .AddOtlpExporter(options => options.Endpoint = endpoint));
            return services;
        }
    }
}
