using System;
using System.Diagnostics.Metrics;
using UrlShortener.Application.Ports;
using UrlShortener.Application.Redirects;
using UrlShortener.Infrastructure.Observability;

namespace UrlShortener.Infrastructure.Redirects
{
    /// <summary>
    /// Stage 1 publisher (TSK-09 D1): accepts and discards click events, counting them as
    /// <c>click_events_discarded</c>, so dashboards show the redirect path produces events. TSK-23 replaces it with
    /// a Kafka publisher (a new implementation; callers don't change).
    /// </summary>
    public sealed class NoOpClickEventPublisher : IClickEventPublisher
    {
        /// <summary>Name of the counter.</summary>
        public const string DiscardedCounterName = "click_events_discarded";

        private static readonly Counter<long> Discarded = UrlShortenerMeters.Meter.CreateCounter<long>(
            DiscardedCounterName, unit: "{event}", description: "Click events built but discarded (no analytics pipeline in stage 1).");

        /// <inheritdoc />
        public bool TryPublish(ClickEvent clickEvent)
        {
            ArgumentNullException.ThrowIfNull(clickEvent);
            Discarded.Add(1);
            return true;
        }
    }
}
