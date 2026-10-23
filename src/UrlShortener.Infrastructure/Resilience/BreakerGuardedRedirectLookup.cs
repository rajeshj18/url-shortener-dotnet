using System;
using System.Threading;
using System.Threading.Tasks;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.Resilience
{
    /// <summary>
    /// Puts Redirect's primary fallback behind its own breaker (LLD §6.7). Replica lookups pass straight through:
    /// Npgsql already takes a failed replica out of rotation. While the breaker is open, a replica miss fails fast
    /// with <see cref="Polly.CircuitBreaker.BrokenCircuitException"/>, answered as <c>503</c> + <c>Retry-After: 1</c>, not <c>404</c>.
    /// </summary>
    public sealed class BreakerGuardedRedirectLookup : IRedirectLookup
    {
        private readonly IRedirectLookup _inner;
        private readonly DatabaseCircuitBreaker _primaryBreaker;

        /// <summary>Creates the decorator.</summary>
        /// <param name="inner">The real lookup.</param>
        /// <param name="primaryBreaker">The breaker for the primary fallback.</param>
        public BreakerGuardedRedirectLookup(IRedirectLookup inner, DatabaseCircuitBreaker primaryBreaker)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _primaryBreaker = primaryBreaker ?? throw new ArgumentNullException(nameof(primaryBreaker));
        }

        /// <inheritdoc />
        public Task<RedirectTarget?> FindAsync(ShortCode code, CancellationToken cancellationToken)
        {
            return _inner.FindAsync(code, cancellationToken);
        }

        /// <inheritdoc />
        public Task<RedirectTarget?> FindOnPrimaryAsync(ShortCode code, CancellationToken cancellationToken)
        {
            return _primaryBreaker.ExecuteAsync(token => _inner.FindOnPrimaryAsync(code, token), cancellationToken);
        }
    }
}
