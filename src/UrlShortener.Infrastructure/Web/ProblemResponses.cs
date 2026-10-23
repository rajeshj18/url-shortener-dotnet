using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace UrlShortener.Infrastructure.Web
{
    /// <summary>Writes RFC 7807 problem responses with a <c>traceId</c>, the same way for every handler.</summary>
    public static class ProblemResponses
    {
        /// <summary>The media type of RFC 7807 problem responses.</summary>
        public const string ProblemJsonContentType = "application/problem+json";

        /// <summary>The trace ID of the current request: the W3C trace ID if tracing is on, otherwise ASP.NET Core's.</summary>
        /// <param name="httpContext">The request.</param>
        /// <returns>The trace ID.</returns>
        public static string TraceId(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            Activity? activity = Activity.Current;
            return activity != null ? activity.TraceId.ToString() : httpContext.TraceIdentifier;
        }

        /// <summary>Creates a problem with a trace ID and, optionally, one stable error code.</summary>
        /// <param name="httpContext">The request.</param>
        /// <param name="status">HTTP status.</param>
        /// <param name="title">Short, human-readable summary.</param>
        /// <param name="code">A stable error code, or <c>null</c>.</param>
        /// <returns>The problem.</returns>
        public static ProblemDetails Create(HttpContext httpContext, int status, string title, string? code)
        {
            ProblemDetails problem = new ProblemDetails { Status = status, Title = title };
            problem.Extensions["traceId"] = TraceId(httpContext);
            if (code != null)
            {
                problem.Extensions["errors"] = new[] { new Dictionary<string, string> { ["code"] = code, ["message"] = title } };
            }

            return problem;
        }

        /// <summary>Writes a problem response.</summary>
        /// <param name="httpContext">The request.</param>
        /// <param name="problem">The problem; its status becomes the response status.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when written.</returns>
        public static Task WriteAsync(HttpContext httpContext, ProblemDetails problem, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(problem);
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            return httpContext.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, ProblemJsonContentType, cancellationToken);
        }
    }
}
