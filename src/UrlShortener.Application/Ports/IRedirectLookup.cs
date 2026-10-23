using System.Threading;
using System.Threading.Tasks;
using UrlShortener.Domain;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// Read side for redirects. It reads only the <c>redirect_lookup</c> view, as the read-only Redirect user
    /// (ADR-0009). Kept separate from <see cref="IShortUrlRepository"/> (interface segregation): the Redirect
    /// service can't write, and can't see owner data.
    /// </summary>
    public interface IRedirectLookup
    {
        /// <summary>Looks a code up on the <b>read replicas</b>, the normal redirect path (ADR-0002).</summary>
        /// <param name="code">The short code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The redirect target, or <c>null</c> if the replica doesn't have the code.</returns>
        Task<RedirectTarget?> FindAsync(ShortCode code, CancellationToken cancellationToken);

        /// <summary>
        /// Looks a code up on the <b>primary</b>. This is the read-your-writes fallback after a replica miss
        /// (NFR-07); callers cap its rate (TSK-09).
        /// </summary>
        /// <param name="code">The short code.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The redirect target, or <c>null</c> if no link has this code.</returns>
        Task<RedirectTarget?> FindOnPrimaryAsync(ShortCode code, CancellationToken cancellationToken);
    }
}
