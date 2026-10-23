using System;

namespace UrlShortener.Application.Redirects
{
    /// <summary>What kind of client sent a request.</summary>
    public enum ClientKind
    {
        /// <summary>A desktop browser (or anything not recognised as mobile, tablet or bot).</summary>
        Desktop,

        /// <summary>A phone.</summary>
        Mobile,

        /// <summary>A tablet.</summary>
        Tablet,

        /// <summary>A crawler or link-preview fetcher: gets the redirect, but no click is counted (A-34).</summary>
        Bot,
    }

    /// <summary>Classifies a client from its <c>User-Agent</c>.</summary>
    public interface IUserAgentClassifier
    {
        /// <summary>Classifies the client.</summary>
        /// <param name="userAgent">The header value, or <c>null</c>.</param>
        /// <returns>The client kind.</returns>
        ClientKind Classify(string? userAgent);
    }

    /// <summary>
    /// A small token list (TSK-09 D3, A-34): good enough to keep crawlers and chat-app link previews out of the
    /// counts. Better detection is a later improvement, behind the same interface.
    /// </summary>
    public sealed class TokenUserAgentClassifier : IUserAgentClassifier
    {
        private static readonly string[] BotTokens =
        {
            "bot", "crawler", "spider", "preview", "facebookexternalhit", "slurp", "curl/", "wget/", "python-requests",
            "headlesschrome", "embedly", "whatsapp", "skypeuripreview",
        };

        /// <inheritdoc />
        public ClientKind Classify(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                return ClientKind.Bot; // browsers always send one
            }

            foreach (string token in BotTokens)
            {
                if (userAgent.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return ClientKind.Bot;
                }
            }

            if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
                || (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)))
            {
                return ClientKind.Tablet;
            }

            if (userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            {
                return ClientKind.Mobile;
            }

            return ClientKind.Desktop;
        }
    }

    /// <summary>Helpers for the privacy-preserving click fields (NFR-10).</summary>
    public static class ClickFields
    {
        /// <summary>The referrer's host only (no path or query, which can carry personal data), or <c>null</c>.</summary>
        /// <param name="referrer">The <c>Referer</c> header.</param>
        /// <returns>The lower-case host, or <c>null</c> if absent or not an absolute http(s) URL.</returns>
        public static string? ReferrerHost(string? referrer)
        {
            if (!Uri.TryCreate(referrer, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.IdnHost))
            {
                return null;
            }

            return uri.IdnHost; // Uri lower-cases http(s) hosts; IdnHost is the ASCII (punycode) form
        }

        /// <summary>The device name used in <see cref="ClickEvent.Device"/>.</summary>
        /// <param name="kind">A non-bot client kind.</param>
        /// <returns><c>desktop</c>, <c>mobile</c> or <c>tablet</c>.</returns>
        public static string DeviceName(ClientKind kind)
        {
            switch (kind)
            {
                case ClientKind.Mobile:
                    return "mobile";
                case ClientKind.Tablet:
                    return "tablet";
                default:
                    return "desktop";
            }
        }
    }
}
