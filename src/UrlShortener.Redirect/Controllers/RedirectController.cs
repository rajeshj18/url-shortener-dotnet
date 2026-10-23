using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Application.Redirects;

namespace UrlShortener.Redirect.Controllers
{
    /// <summary>
    /// <c>GET</c> and <c>HEAD /{code}</c> (LLD §3, ADR-0007). Maps the use case's outcome to a status and the
    /// caching header ADR-0007 fixes for it. Single-segment paths only, so <c>/health/*</c> is never captured.
    /// </summary>
    [ApiController]
    public sealed class RedirectController : ControllerBase
    {
        /// <summary><c>302</c> and <c>404</c>: never stored, so clicks are counted and new links resolve at once.</summary>
        public const string NoStore = "no-store";

        /// <summary><c>410</c>: permanent (codes are never reused, A-25), so caches may keep it for a day.</summary>
        public const string GoneCaching = "public, max-age=86400";

        private readonly IResolveShortUrlHandler _resolve;

        /// <summary>Creates the controller.</summary>
        /// <param name="resolve">The redirect use case.</param>
        public RedirectController(IResolveShortUrlHandler resolve)
        {
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        }

        /// <summary>Redirects to the long URL.</summary>
        /// <param name="code">The short code or alias.</param>
        /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
        /// <returns><c>302</c>, <c>404</c> or <c>410</c>.</returns>
        [AcceptVerbs("GET", "HEAD")]
        [Route("{code}")]
        public async Task<IActionResult> Resolve(string code, CancellationToken cancellationToken)
        {
            ResolveResult result = await _resolve.ResolveAsync(
                new ResolveRequest(
                    code,
                    HttpMethods.IsHead(Request.Method),
                    Request.Headers.UserAgent.ToString(),
                    Request.Headers.Referer.ToString()),
                cancellationToken);

            switch (result.Outcome)
            {
                case ResolveOutcome.Redirect:
                    Response.Headers.CacheControl = NoStore;
                    return Redirect(result.Target!.OriginalString);
                case ResolveOutcome.Gone:
                    Response.Headers.CacheControl = GoneCaching;
                    return StatusCode(StatusCodes.Status410Gone);
                default:
                    Response.Headers.CacheControl = NoStore;
                    return NotFound();
            }
        }
    }
}
