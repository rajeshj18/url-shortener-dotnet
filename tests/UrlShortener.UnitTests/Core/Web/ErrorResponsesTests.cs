using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Polly.CircuitBreaker;
using UrlShortener.Core.Codes;
using UrlShortener.Core.Database;
using UrlShortener.Core.Web;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Core.Web
{
    /// <summary>
    /// TSK-02 AC9a, TSK-08 and TSK-11 (kept, one handler now): each kind of exception becomes the right RFC 7807 response,
    /// and nothing internal ever reaches the client.
    /// </summary>
    public sealed class ErrorResponsesTests
    {
        private const string InternalDetailMarker = "internal-detail-marker-7f3a";

        private readonly ErrorResponses _handler = new ErrorResponses(1, NullLogger<ErrorResponses>.Instance);

        [Fact]
        public async Task Domain_validation_becomes_400_with_its_code()
        {
            DefaultHttpContext context = NewContext("trace-1");

            (await _handler.TryHandleAsync(context, new DomainValidationException(DomainErrorCodes.UrlScheme, "Bad scheme."), CancellationToken.None)).Should().BeTrue();

            context.Response.StatusCode.Should().Be(400);
            context.Response.ContentType.Should().StartWith(ErrorResponses.ProblemJsonContentType);
            JsonElement body = await Body(context);
            body.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be(DomainErrorCodes.UrlScheme);
            body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Exhausted_id_space_becomes_503_with_retry_after_5()
        {
            DefaultHttpContext context = NewContext("trace-2");

            await _handler.TryHandleAsync(context, new IdSpaceExhaustedException(), CancellationToken.None);

            context.Response.StatusCode.Should().Be(503);
            context.Response.Headers.RetryAfter.ToString().Should().Be("5");
        }

        [Fact]
        public async Task Unavailable_database_and_open_breaker_become_503_with_the_configured_retry_after()
        {
            DefaultHttpContext transient = NewContext("t");
            DefaultHttpContext open = NewContext("o");

            await _handler.TryHandleAsync(transient, new PostgresException("x", "ERROR", "ERROR", "57P01"), CancellationToken.None);
            await _handler.TryHandleAsync(open, new BrokenCircuitException(), CancellationToken.None);

            transient.Response.StatusCode.Should().Be(503);
            open.Response.StatusCode.Should().Be(503);
            open.Response.Headers.RetryAfter.ToString().Should().Be("1");
        }

        [Fact]
        public async Task Anything_else_becomes_500_with_the_trace_id_and_no_internal_detail()
        {
            Activity.Current = null;
            DefaultHttpContext context = NewContext("trace-abc");

            await _handler.TryHandleAsync(context, new InvalidOperationException(InternalDetailMarker), CancellationToken.None);

            context.Response.StatusCode.Should().Be(500);
            context.Response.Body.Position = 0;
            using StreamReader reader = new StreamReader(context.Response.Body);
            string text = await reader.ReadToEndAsync();
            text.Should().Contain("\"traceId\":\"trace-abc\"");
            text.Should().NotContain(InternalDetailMarker).And.NotContain(nameof(InvalidOperationException)).And.NotContain("   at ");
        }

        [Fact]
        public async Task Stored_data_errors_are_500s_not_400s()
        {
            DefaultHttpContext context = NewContext("trace-3");

            await _handler.TryHandleAsync(context, new StoredDataInvalidException("Stored link 'x' breaks the domain rule 'url.scheme'."), CancellationToken.None);

            context.Response.StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task A_started_response_is_left_alone()
        {
            DefaultHttpContext context = NewContext("late");
            context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

            (await _handler.TryHandleAsync(context, new InvalidOperationException("late"), CancellationToken.None)).Should().BeFalse();
        }

        [Fact]
        public void Constructor_rejects_invalid_arguments()
        {
            Action zero = () => _ = new ErrorResponses(0, NullLogger<ErrorResponses>.Instance);
            Action noLogger = () => _ = new ErrorResponses(1, null!);

            zero.Should().Throw<ArgumentOutOfRangeException>();
            noLogger.Should().Throw<ArgumentNullException>();
        }

        private static DefaultHttpContext NewContext(string traceIdentifier)
        {
            DefaultHttpContext context = new DefaultHttpContext { TraceIdentifier = traceIdentifier };
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static async Task<JsonElement> Body(DefaultHttpContext context)
        {
            context.Response.Body.Position = 0;
            using JsonDocument document = await JsonDocument.ParseAsync(context.Response.Body);
            return document.RootElement.Clone();
        }

        private sealed class StartedResponseFeature : HttpResponseFeature
        {
            public override bool HasStarted => true;
        }
    }
}
