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
    /// <param name="State">Active links redirect (302); deleted links return 410.</param>
    public sealed record RedirectTarget(ShortCode Code, LongUrl Target, LinkState State);
}
