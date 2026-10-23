using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace UrlShortener.Infrastructure.Observability
{
    /// <summary>
    /// OpenTelemetry wiring shared by all services (ADR-0013): traces, metrics and logs, exported over OTLP.
    /// If no endpoint is configured, observability is <b>off</b> and nothing is registered, so a service can
    /// run without a collector.
    /// </summary>
    public static class ObservabilityServiceCollectionExtensions
    {
        /// <summary>Configuration key for the OTLP endpoint, e.g. <c>http://otel-lgtm:4317</c>.</summary>
        public const string EndpointKey = "OpenTelemetry:Endpoint";

        /// <summary>Adds OpenTelemetry for the given service, if an OTLP endpoint is configured.</summary>
        /// <param name="services">The service collection of the composition root.</param>
        /// <param name="configuration">Application configuration; reads <see cref="EndpointKey"/>.</param>
        /// <param name="serviceName">The service name reported to the collector, e.g. <c>urlshortener-api</c>.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddUrlShortenerObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
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
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = endpoint))
                .WithMetrics(metrics => metrics
                    .AddMeter(UrlShortenerMeters.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = endpoint))
                .WithLogging(logging => logging
                    .AddOtlpExporter(options => options.Endpoint = endpoint));

            return services;
        }
    }
}
