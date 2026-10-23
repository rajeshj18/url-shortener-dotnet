using UrlShortener.Application.Redirects;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// Hands click events to the analytics pipeline (ADR-0008, LLD §6.5). <b>Synchronous and never blocks</b>: a
    /// redirect must not wait for analytics. Stage 1 discards the events (no Kafka yet); TSK-23 adds a Kafka publisher.
    /// </summary>
    public interface IClickEventPublisher
    {
        /// <summary>Queues the event, or drops it if the local queue is full.</summary>
        /// <param name="clickEvent">The event.</param>
        /// <returns><c>true</c> if accepted; <c>false</c> if dropped (counted, never retried by the caller).</returns>
        bool TryPublish(ClickEvent clickEvent);
    }

    /// <summary>
    /// Caps how often a replica miss may fall back to the primary (LLD §6.2, §8 <c>Redirect:PrimaryFallbackPerSecond</c>),
    /// so that scans of random codes can't overload the primary.
    /// </summary>
    public interface IPrimaryFallbackBudget
    {
        /// <summary>Takes one fallback from the budget if any is left.</summary>
        /// <returns><c>true</c> if the primary may be queried now.</returns>
        bool TryAcquire();
    }
}
