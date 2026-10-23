using System.Threading;
using System.Threading.Tasks;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// Leases ranges of IDs from this region's active block (ADR-0005, LLD §6.4). Implemented by one atomic
    /// <c>UPDATE … RETURNING</c> on the primary, so concurrent instances can never receive overlapping ranges.
    /// </summary>
    public interface IIdRangeLeaser
    {
        /// <summary>
        /// Leases <paramref name="size"/> IDs. If the active block is exhausted, switches to the next reserved
        /// block. Throws <see cref="IdSpaceExhaustedException"/> if no block has room.
        /// </summary>
        /// <param name="size">Number of IDs to lease.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The leased range.</returns>
        Task<IdRange> LeaseAsync(int size, CancellationToken cancellationToken);
    }
}
