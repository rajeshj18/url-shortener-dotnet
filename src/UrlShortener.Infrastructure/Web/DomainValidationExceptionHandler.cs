using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.Web
{
    /// <summary>
    /// Maps <see cref="DomainValidationException"/> to <c>400</c> with its stable error code
    /// (<c>errors: [{ code, message }]</c>). Not logged as an error: it is the client's input.
    /// </summary>
    public sealed class DomainValidationExceptionHandler : IExceptionHandler
    {
        /// <inheritdoc />
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            if (exception is not DomainValidationException validation || httpContext.Response.HasStarted)
            {
                return false;
            }

            await ProblemResponses.WriteAsync(
                httpContext,
                ProblemResponses.Create(httpContext, StatusCodes.Status400BadRequest, validation.Message, validation.Code),
                cancellationToken);
            return true;
        }
    }
}
