using System;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Health
{
    /// <summary>
    /// TSK-QW #6 (ADR-0013: logs go out through OpenTelemetry "plus JSON console output"): every service's console
    /// logger uses the JSON formatter with UTC timestamps, so `docker compose logs` gives one JSON object per line.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class ConsoleLogFormatTests
    {
        private readonly PostgresFixture _db;

        public ConsoleLogFormatTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public void Api_console_logs_are_json()
        {
            AssertJsonConsole(_db.SharedApi.Services);
        }

        [Fact]
        public void Redirect_console_logs_are_json()
        {
            using RedirectFactory redirect = new RedirectFactory(_db.RedirectConnection);
            AssertJsonConsole(redirect.Services);
        }

        [Fact]
        public void AnalyticsWorker_console_logs_are_json()
        {
            using WorkerFactory worker = new WorkerFactory(_db.WorkerConnection);
            AssertJsonConsole(worker.Services);
        }

        private static void AssertJsonConsole(IServiceProvider services)
        {
            services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName.Should().Be(ConsoleFormatterNames.Json);
            JsonConsoleFormatterOptions json = services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue; // what the formatter reads
            json.UseUtcTimestamp.Should().BeTrue();
            json.TimestampFormat.Should().NotBeNullOrEmpty();
        }
    }
}
