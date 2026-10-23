using System.Threading;
using System.Threading.Tasks;
using UrlShortener.Domain;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// Produces generated short codes: exactly 7 Base62 characters, unique for ever (NFR-05), and not
    /// enumerable in order (NFR-09). See ADR-0005.
    /// </summary>
    public interface IShortCodeGenerator
    {
        /// <summary>Returns the next unique generated code.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A generated <see cref="ShortCode"/>.</returns>
        Task<ShortCode> NextAsync(CancellationToken cancellationToken);
    }
}
