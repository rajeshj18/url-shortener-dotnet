using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Infrastructure.Web;
using Xunit;

namespace UrlShortener.UnitTests.Infrastructure.Web
{
    /// <summary>Proves AC9a of TSK-02: unhandled exceptions become safe RFC 7807 responses.</summary>
    public sealed class ProblemDetailsExceptionHandlerTests
    {
        private const string SecretMessage = "internal-detail-marker-7f3a";

        [Fact]
        public async Task TryHandleAsync_unhandled_exception_returns_500_problem_json_with_trace_id()
        {
            Activity.Current = null;
            DefaultHttpContext context = CreateContext("trace-abc");
            ProblemDetailsExceptionHandler handler = new ProblemDetailsExceptionHandler(NullLogger<ProblemDetailsExceptionHandler>.Instance);

            bool handled = await handler.TryHandleAsync(context, new InvalidOperationException(SecretMessage), CancellationToken.None);

            handled.Should().BeTrue();
            context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            context.Response.ContentType.Should().StartWith(ProblemDetailsExceptionHandler.ProblemJsonContentType);
            using JsonDocument body = await ReadBodyAsync(context);
            body.RootElement.GetProperty("status").GetInt32().Should().Be(500);
            body.RootElement.GetProperty("traceId").GetString().Should().Be("trace-abc");
        }

        [Fact]
        public async Task TryHandleAsync_unhandled_exception_never_leaks_message_or_stack_trace()
        {
            DefaultHttpContext context = CreateContext("trace-def");
            ProblemDetailsExceptionHandler handler = new ProblemDetailsExceptionHandler(NullLogger<ProblemDetailsExceptionHandler>.Instance);

            await handler.TryHandleAsync(context, new InvalidOperationException(SecretMessage), CancellationToken.None);

            string body = await ReadBodyTextAsync(context);
            body.Should().NotContain("internal-detail-marker-7f3a");
            body.Should().NotContain(nameof(InvalidOperationException));
            body.Should().NotContain("   at ");
        }

        [Fact]
        public async Task TryHandleAsync_response_already_started_returns_false()
        {
            DefaultHttpContext context = CreateContext("trace-ghi");
            context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());
            ProblemDetailsExceptionHandler handler = new ProblemDetailsExceptionHandler(NullLogger<ProblemDetailsExceptionHandler>.Instance);

            bool handled = await handler.TryHandleAsync(context, new InvalidOperationException("late"), CancellationToken.None);

            handled.Should().BeFalse();
        }

        [Fact]
        public void Constructor_null_logger_throws()
        {
            Action act = () => _ = new ProblemDetailsExceptionHandler(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        private static DefaultHttpContext CreateContext(string traceIdentifier)
        {
            DefaultHttpContext context = new DefaultHttpContext { TraceIdentifier = traceIdentifier };
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static async Task<JsonDocument> ReadBodyAsync(DefaultHttpContext context)
        {
            context.Response.Body.Position = 0;
            return await JsonDocument.ParseAsync(context.Response.Body);
        }

        private static async Task<string> ReadBodyTextAsync(DefaultHttpContext context)
        {
            context.Response.Body.Position = 0;
            using StreamReader reader = new StreamReader(context.Response.Body);
            return await reader.ReadToEndAsync();
        }

        /// <summary>Simulates a response whose headers have already been sent.</summary>
        private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
        {
            public override bool HasStarted => true;
        }
    }
}
