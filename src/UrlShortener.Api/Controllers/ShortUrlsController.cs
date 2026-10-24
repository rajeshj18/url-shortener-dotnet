using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Links;

namespace UrlShortener.Api.Controllers
{
    /// <summary>
    /// The management API for short links (LLD §3). Only maps HTTP to the Core services: authentication and error mapping
    /// happen in the middleware pipeline.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/urls")]
    public sealed class ShortUrlsController : ControllerBase
    {
        /// <summary>Header set on a replayed response (ADR-0010).</summary>
        public const string ReplayedHeader = "Idempotent-Replayed";

        private const string ProblemJson = "application/problem+json";

        private readonly LinkService _links;
        private readonly StatsService _stats;

        /// <summary>Creates the controller.</summary>
        /// <param name="links">Create, metadata and delete.</param>
        /// <param name="stats">Click statistics.</param>
        public ShortUrlsController(LinkService links, StatsService stats)
        {
            _links = links ?? throw new ArgumentNullException(nameof(links));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <summary>Creates a short link with a generated code or a custom alias.</summary>
        /// <remarks>
        /// Send an <c>Idempotency-Key</c> (a UUID per user action) to retry safely: within 24 hours the same key and
        /// request return the original response with <c>Idempotent-Replayed: true</c>; the same key with a different
        /// request returns <c>422</c>. Every create without a key makes a new link, even for the same long URL.
        /// </remarks>
        /// <param name="request">The long URL and optional alias.</param>
        /// <param name="idempotencyKey">Optional, 1–128 printable ASCII characters.</param>
        /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
        /// <returns>The new link.</returns>
        [HttpPost]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(CreatedShortUrl), StatusCodes.Status201Created, "application/json")]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, ProblemJson)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, ProblemJson)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, ProblemJson)]
        public async Task<IActionResult> Create(
            [FromBody] CreateShortUrlRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (idempotencyKey != null && !IdempotencyRules.IsValidKey(idempotencyKey))
            {
                return Problem(StatusCodes.Status400BadRequest, "Idempotency-Key must be 1-128 printable ASCII characters.", ErrorCodes.IdempotencyKeyFormat);
            }

            CreateResult result = await _links.CreateAsync(
                new CreateLinkCommand(OwnerId(), request.Destination, request.CustomAlias, request.ExpiresAt, idempotencyKey),
                cancellationToken);
            switch (result.Outcome)
            {
                case CreateOutcome.Created:
                    return CreatedAt(result.Body!);
                case CreateOutcome.Replayed:
                    Response.Headers[ReplayedHeader] = "true";
                    return CreatedAt(result.Body!);
                case CreateOutcome.AliasTaken:
                    return Problem(StatusCodes.Status409Conflict, "The custom alias is already taken.", ErrorCodes.AliasTaken);
                default:
                    return Problem(StatusCodes.Status422UnprocessableEntity, "This Idempotency-Key was already used for a different request.", ErrorCodes.IdempotencyKeyReused);
            }
        }

        /// <summary>Returns the caller's link, including whether and when it was deleted.</summary>
        /// <remarks>Returns <c>404</c> both for unknown codes and for other clients' links, so codes can't be probed.</remarks>
        /// <param name="code">The short code or alias.</param>
        /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
        /// <returns>The link's details.</returns>
        [HttpGet("{code}")]
        [ProducesResponseType(typeof(ShortUrlDetails), StatusCodes.Status200OK, "application/json")]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, ProblemJson)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
        public async Task<IActionResult> Get(string code, CancellationToken cancellationToken)
        {
            ShortUrlDetails? details = await _links.GetAsync(OwnerId(), code, cancellationToken);
            return details != null ? Ok(details) : LinkNotFound();
        }

        /// <summary>Deletes the caller's link. Its short URL then answers <c>410 Gone</c>, and the code is never reused.</summary>
        /// <remarks>Idempotent: deleting again returns <c>204</c> and keeps the first deletion time.</remarks>
        /// <param name="code">The short code or alias.</param>
        /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
        /// <returns>No content.</returns>
        [HttpDelete("{code}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, ProblemJson)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
        public async Task<IActionResult> Delete(string code, CancellationToken cancellationToken)
        {
            return await _links.DeleteAsync(OwnerId(), code, cancellationToken) ? NoContent() : LinkNotFound();
        }

        /// <summary>Returns the click statistics of the caller's link: totals, a time series, top referrers and devices.</summary>
        /// <remarks>
        /// Counts exclude bots and <c>HEAD</c> requests and are kept per hour by the analytics worker, usually less than a
        /// minute behind (<c>dataFreshAsOf</c>). <c>totalClicks</c> covers the requested range; buckets without clicks are
        /// left out of <c>series</c>. Returns <c>404</c> both for unknown codes and for other clients' links.
        /// </remarks>
        /// <param name="code">The short code or alias.</param>
        /// <param name="from">Range start (default: 7 days before <paramref name="to"/>).</param>
        /// <param name="to">Range end, exclusive (default: now). The range may be at most 90 days.</param>
        /// <param name="granularity"><c>hour</c> or <c>day</c> (default <c>day</c>).</param>
        /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
        /// <returns>The statistics.</returns>
        [HttpGet("{code}/stats")]
        [ProducesResponseType(typeof(LinkStats), StatusCodes.Status200OK, "application/json")]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, ProblemJson)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
        public async Task<IActionResult> Stats(string code, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? granularity, CancellationToken cancellationToken)
        {
            LinkStats? stats = await _stats.GetAsync(OwnerId(), code, from, to, granularity, cancellationToken);
            return stats != null ? Ok(stats) : LinkNotFound();
        }

        private string OwnerId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("An authenticated request has no owner.");
        }

        private ObjectResult LinkNotFound()
        {
            return Problem(StatusCodes.Status404NotFound, "No such link.", ErrorCodes.LinkNotFound);
        }

        // Location points at the metadata endpoint. Codes contain only URL-safe characters.
        private CreatedResult CreatedAt(CreatedShortUrl body)
        {
            return Created(new Uri("/api/v1/urls/" + body.Code, UriKind.Relative), body);
        }

        private ObjectResult Problem(int status, string title, string code)
        {
            ProblemDetails problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title);
            problem.Extensions["errors"] = new[] { new { code, message = title } };
            ObjectResult result = new ObjectResult(problem) { StatusCode = status };
            result.ContentTypes.Add(ProblemJson);
            return result;
        }
    }
}
