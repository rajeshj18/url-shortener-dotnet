using System;

namespace UrlShortener.Core.Analytics
{
    /// <summary>
    /// <c>ClickEvent v1</c> (LLD §6.5, ADR-0008). No IP address and no full User-Agent (NFR-10): only the referrer's host
    /// and a device class. New fields may be added later only as optional ones.
    /// </summary>
    /// <param name="SchemaVersion">Always 1 for this shape.</param>
    /// <param name="EventId">A UUIDv7, unique per click.</param>
    /// <param name="Code">The short code that was resolved.</param>
    /// <param name="OccurredAt">When the redirect was served (UTC).</param>
    /// <param name="ReferrerHost">The referrer's host only, or <c>null</c>.</param>
    /// <param name="Device"><c>desktop</c>, <c>mobile</c> or <c>tablet</c>.</param>
    public sealed record ClickEvent(int SchemaVersion, Guid EventId, string Code, DateTimeOffset OccurredAt, string? ReferrerHost, string Device);

    /// <summary>
    /// Turns request headers into the privacy-preserving click fields (NFR-10). A small token list (TSK-09 D3, A-34) keeps
    /// crawlers and chat-app link previews out of the counts.
    /// </summary>
    public static class ClickClassifier
    {
        /// <summary>Device class of a desktop browser (or anything not recognised as mobile, tablet or bot).</summary>
        public const string Desktop = "desktop";

        /// <summary>Device class of a phone.</summary>
        public const string Mobile = "mobile";

        /// <summary>Device class of a tablet.</summary>
        public const string Tablet = "tablet";

        private static readonly string[] BotTokens =
        {
            "bot", "crawler", "spider", "preview", "facebookexternalhit", "slurp", "curl/", "wget/", "python-requests",
            "headlesschrome", "embedly", "whatsapp", "skypeuripreview",
        };

        /// <summary>The device class, or <c>null</c> for a bot (which gets the redirect, but no click is counted).</summary>
        /// <param name="userAgent">The <c>User-Agent</c> header, or <c>null</c>.</param>
        /// <returns><see cref="Desktop"/>, <see cref="Mobile"/>, <see cref="Tablet"/> or <c>null</c>.</returns>
        public static string? Device(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                return null; // browsers always send one
            }

            foreach (string token in BotTokens)
            {
                if (userAgent.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
                || (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)))
            {
                return Tablet;
            }

            if (userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            {
                return Mobile;
            }

            return Desktop;
        }

        /// <summary>The referrer's host only (no path or query, which can carry personal data), or <c>null</c>.</summary>
        /// <param name="referrer">The <c>Referer</c> header.</param>
        /// <returns>The lower-case ASCII (punycode) host, or <c>null</c> if absent or not an absolute http(s) URL.</returns>
        public static string? ReferrerHost(string? referrer)
        {
            if (!Uri.TryCreate(referrer, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.IdnHost))
            {
                return null;
            }

            return uri.IdnHost;
        }
    }
}
