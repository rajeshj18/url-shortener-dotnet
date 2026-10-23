using System;
using UrlShortener.Domain;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// What the redirect path needs, and nothing more: the code, the destination and the state. A read model
    /// (a DTO, so a <c>record</c> is fine under ADR-0003), built from the <c>redirect_lookup</c> view, which
    /// deliberately leaves out owner data.
    /// </summary>
    /// <param name="Code">The short code.</param>
    /// <param name="Target">The destination URL.</param>
    /// <param name="State">The deletion state: active links redirect (302); deleted links return 410.</param>
    /// <param name="ExpiresAt">The link's expiry, or <c>null</c> (TSK-14); see <see cref="StateAt"/>.</param>
    public sealed record RedirectTarget(ShortCode Code, LongUrl Target, LinkState State, DateTimeOffset? ExpiresAt = null)
    {
        /// <summary>The state at a point in time, with the same rule as <see cref="ShortUrl.StateAt"/>.</summary>
        /// <param name="now">The current time.</param>
        /// <returns>Active, deleted or expired.</returns>
        public LinkState StateAt(DateTimeOffset now)
        {
            return ShortUrl.ComputeState(State == LinkState.Deleted ? now : null, ExpiresAt, now);
        }
    }
}
