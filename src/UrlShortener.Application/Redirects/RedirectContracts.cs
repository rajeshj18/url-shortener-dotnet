using System;
using System.Threading;
using System.Threading.Tasks;

namespace UrlShortener.Application.Redirects
{
    /// <summary>
    /// <c>ClickEvent v1</c> (LLD §6.5, ADR-0008). No IP address and no full User-Agent (NFR-10): only the referrer's
    /// host and a device class. New fields may be added later only as optional ones.
    /// </summary>
    /// <param name="SchemaVersion">Always 1 for this shape.</param>
    /// <param name="EventId">A UUIDv7, unique per click; the analytics worker uses it for exactly-once counting.</param>
    /// <param name="Code">The short code that was resolved.</param>
    /// <param name="OccurredAt">When the redirect was served (UTC).</param>
    /// <param name="ReferrerHost">The referrer's host only, or <c>null</c>.</param>
    /// <param name="Device"><c>desktop</c>, <c>mobile</c> or <c>tablet</c>.</param>
    public sealed record ClickEvent(int SchemaVersion, Guid EventId, string Code, DateTimeOffset OccurredAt, string? ReferrerHost, string Device);

    /// <summary>A redirect request, as far as the use case needs it (DTO).</summary>
    /// <param name="Code">The path segment, unvalidated.</param>
    /// <param name="IsHead"><c>true</c> for <c>HEAD</c>: same answer, but no click event (ADR-0007).</param>
    /// <param name="UserAgent">The <c>User-Agent</c> header, used only to classify the client.</param>
    /// <param name="Referrer">The <c>Referer</c> header, of which only the host is kept.</param>
    public sealed record ResolveRequest(string? Code, bool IsHead, string? UserAgent, string? Referrer);

    /// <summary>How a redirect request ends.</summary>
    public enum ResolveOutcome
    {
        /// <summary><c>302</c> to the long URL.</summary>
        Redirect,

        /// <summary><c>404</c>: unknown or malformed code.</summary>
        NotFound,

        /// <summary><c>410</c>: deleted (later also expired).</summary>
        Gone,
    }

    /// <summary>The result of resolving a code.</summary>
    public sealed class ResolveResult
    {
        private ResolveResult(ResolveOutcome outcome, Uri? target)
        {
            Outcome = outcome;
            Target = target;
        }

        /// <summary>Unknown or malformed code.</summary>
        public static ResolveResult NotFound { get; } = new ResolveResult(ResolveOutcome.NotFound, null);

        /// <summary>Deleted link.</summary>
        public static ResolveResult Gone { get; } = new ResolveResult(ResolveOutcome.Gone, null);

        /// <summary>What happened.</summary>
        public ResolveOutcome Outcome { get; }

        /// <summary>The long URL, exactly as created; set only for <see cref="ResolveOutcome.Redirect"/>.</summary>
        public Uri? Target { get; }

        /// <summary>A redirect.</summary>
        /// <param name="target">The long URL.</param>
        /// <returns>The result.</returns>
        public static ResolveResult Redirect(Uri target)
        {
            ArgumentNullException.ThrowIfNull(target);
            return new ResolveResult(ResolveOutcome.Redirect, target);
        }
    }

    /// <summary>The redirect use case (LLD §6.2).</summary>
    public interface IResolveShortUrlHandler
    {
        /// <summary>Resolves a code.</summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The outcome.</returns>
        Task<ResolveResult> ResolveAsync(ResolveRequest request, CancellationToken cancellationToken);
    }
}
