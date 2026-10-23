using System;
using System.Threading;
using System.Threading.Tasks;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;

namespace UrlShortener.Application.Redirects
{
    /// <summary>
    /// The redirect use case (LLD §6.2, ADR-0007). A malformed code is answered without any database call. A
    /// replica miss falls back to the primary while the budget allows (read-your-writes, NFR-07). A click event is
    /// published only for <c>GET</c> requests from non-bots, and publishing never blocks or fails the redirect.
    /// </summary>
    public sealed class ResolveShortUrlHandler : IResolveShortUrlHandler
    {
        private readonly IRedirectLookup _lookup;
        private readonly IPrimaryFallbackBudget _fallbackBudget;
        private readonly IClickEventPublisher _publisher;
        private readonly IUserAgentClassifier _classifier;
        private readonly TimeProvider _time;

        /// <summary>Creates the handler.</summary>
        /// <param name="lookup">Replica and primary lookups.</param>
        /// <param name="fallbackBudget">Caps primary fallbacks.</param>
        /// <param name="publisher">Click events.</param>
        /// <param name="classifier">Bot and device classification.</param>
        /// <param name="time">Clock.</param>
        public ResolveShortUrlHandler(
            IRedirectLookup lookup,
            IPrimaryFallbackBudget fallbackBudget,
            IClickEventPublisher publisher,
            IUserAgentClassifier classifier,
            TimeProvider time)
        {
            _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
            _fallbackBudget = fallbackBudget ?? throw new ArgumentNullException(nameof(fallbackBudget));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
            _time = time ?? throw new ArgumentNullException(nameof(time));
        }

        /// <inheritdoc />
        public async Task<ResolveResult> ResolveAsync(ResolveRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!ShortCode.TryParse(request.Code, out ShortCode? code))
            {
                return ResolveResult.NotFound; // no database call for malformed codes
            }

            RedirectTarget? target = await _lookup.FindAsync(code, cancellationToken);
            if (target == null && _fallbackBudget.TryAcquire())
            {
                // Possibly created a moment ago and not yet on the replica.
                target = await _lookup.FindOnPrimaryAsync(code, cancellationToken);
            }

            if (target == null)
            {
                return ResolveResult.NotFound;
            }

            if (target.State != LinkState.Active)
            {
                return ResolveResult.Gone;
            }

            if (!request.IsHead)
            {
                ClientKind kind = _classifier.Classify(request.UserAgent);
                if (kind != ClientKind.Bot)
                {
                    DateTimeOffset now = _time.GetUtcNow();
                    _publisher.TryPublish(new ClickEvent(
                        1, UuidV7.Create(now), code.Value, now, ClickFields.ReferrerHost(request.Referrer), ClickFields.DeviceName(kind)));
                }
            }

            return ResolveResult.Redirect(target.Target.Uri);
        }
    }
}
