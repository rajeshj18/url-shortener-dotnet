using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Contracts;
using UrlShortener.Application.ShortUrls;

namespace UrlShortener.Api.Controllers
{
    /// <summary>
    /// The management API for short links (LLD §3). Only maps HTTP to the Application use cases: authentication
    /// and error mapping happen in the middleware pipeline.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/urls")]
    public sealed class ShortUrlsController : ControllerBase
    {
        /// <summary>Header set on a replayed response (ADR-0010).</summary>
        public const string ReplayedHeader = "Idempotent-Replayed";

        private const string ProblemJson = "application/problem+json";

        private readonly ICreateShortUrlHandler _create;

        /// <summary>Creates the controller.</summary>
        /// <param name="create">The create use case.</param>
        public ShortUrlsController(ICreateShortUrlHandler create)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
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
            if (idempotencyKey != null && !IdempotencyKeyFormat.IsValid(idempotencyKey))
            {
                return Problem(StatusCodes.Status400BadRequest, "Idempotency-Key must be 1-128 printable ASCII characters.", ApplicationErrorCodes.IdempotencyKeyFormat);
            }

            string ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("An authenticated request has no owner.");
            CreateShortUrlResult result = await _create.HandleAsync(
                new CreateShortUrlCommand(ownerId, request.Destination, request.CustomAlias, request.ExpiresAt, idempotencyKey),
                cancellationToken);

            switch (result.Outcome)
            {
                case CreateShortUrlOutcome.Created:
                    return CreatedAt(result.Body!);
                case CreateShortUrlOutcome.Replayed:
                    Response.Headers[ReplayedHeader] = "true";
                    return CreatedAt(result.Body!);
                case CreateShortUrlOutcome.AliasTaken:
                    return Problem(StatusCodes.Status409Conflict, "The custom alias is already taken.", ApplicationErrorCodes.AliasTaken);
                default:
                    return Problem(StatusCodes.Status422UnprocessableEntity, "This Idempotency-Key was already used for a different request.", ApplicationErrorCodes.IdempotencyKeyReused);
            }
        }

        // Location points at the metadata endpoint (served from TSK-10). Codes contain only URL-safe characters.
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
