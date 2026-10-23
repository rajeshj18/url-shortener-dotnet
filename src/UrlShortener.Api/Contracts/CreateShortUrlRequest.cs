using System;
using System.Text.Json.Serialization;

namespace UrlShortener.Api.Contracts
{
    /// <summary>
    /// Body of <c>POST /api/v1/urls</c> (LLD §3). Fields are nullable on purpose: missing or invalid values reach
    /// the use case, which answers with a stable error code instead of a generic model-binding error.
    /// </summary>
    /// <param name="Destination">JSON <c>longUrl</c>: the URL to shorten, absolute <c>http</c>/<c>https</c>, at most 2,048 characters.
    /// A string, not <see cref="Uri"/>, so invalid input reaches validation and gets its error code.</param>
    /// <param name="CustomAlias">Optional alias: 4–30 characters <c>[A-Za-z0-9_-]</c>, not exactly 7 Base62 characters, not a reserved path.</param>
    /// <param name="ExpiresAt">Not supported yet (link expiry arrives later); sending it returns <c>400 expiry.unsupported</c>.</param>
    public sealed record CreateShortUrlRequest([property: JsonPropertyName("longUrl")] string? Destination, string? CustomAlias, DateTimeOffset? ExpiresAt);
}
