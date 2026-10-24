using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Database;
using UrlShortener.Core.Redirects;
using UrlShortener.Core.Web;
using Xunit;

namespace UrlShortener.UnitTests.Core.Redirects
{
    /// <summary>
    /// TSK-09 AC7 (kept): the primary-fallback token bucket on a fake clock, and the cheap format check. The data sources
    /// point at a closed port: these tests must never reach a database.
    /// </summary>
    public sealed class RedirectServiceTests : IDisposable
    {
        private readonly PrimaryDatabase _primary = new PrimaryDatabase(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1"));
        private readonly ReplicaDatabase _replicas = new ReplicaDatabase(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1"));
        private readonly ClickPublisher _clicks = new ClickPublisher(new KafkaOptions());

        [Fact]
        public void Budget_allows_a_burst_of_one_second_then_refuses()
        {
            RedirectService service = NewService(5, new TestTime());

            int allowed = 0;
            for (int i = 0; i < 20; i++)
            {
                allowed += service.TryTakeFallback() ? 1 : 0;
            }

            allowed.Should().Be(5);
        }

        [Fact]
        public void Budget_refills_with_time_but_never_above_its_capacity()
        {
            TestTime time = new TestTime();
            RedirectService service = NewService(10, time);
            Drain(service);

            time.Advance(TimeSpan.FromMilliseconds(300));
            Drain(service).Should().Be(3, "300 ms at 10/s");

            time.Advance(TimeSpan.FromHours(1));
            Drain(service).Should().Be(10, "an idle hour doesn't build up more than one second's burst");
        }

        [Fact]
        public void Budget_counts_granted_and_denied_fallbacks()
        {
            long granted = 0;
            long denied = 0;
            using MeterListener listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AppTelemetry.Name && instrument.Name == RedirectService.FallbackCounterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    granted += tag.Key == "outcome" && (string?)tag.Value == "granted" ? value : 0;
                    denied += tag.Key == "outcome" && (string?)tag.Value == "denied" ? value : 0;
                }
            });
            listener.Start();
            RedirectService service = NewService(3, new TestTime());

            for (int i = 0; i < 5; i++)
            {
                service.TryTakeFallback();
            }

            granted.Should().Be(3);
            denied.Should().Be(2);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("health")]
        [InlineData("has space")]
        [InlineData("../etc")]
        public async Task Malformed_codes_are_404_without_a_database_call(string? code)
        {
            ResolveResult result = await NewService(5, new TestTime()).ResolveAsync(code, false, null, null, CancellationToken.None);

            result.Outcome.Should().Be(ResolveOutcome.NotFound, "the data sources are unreachable, so any database call would have thrown");
        }

        [Fact]
        public void Constructor_rejects_invalid_arguments()
        {
            Action zero = () => NewService(0, new TestTime());
            Action noClock = () => NewService(5, null!);

            zero.Should().Throw<ArgumentOutOfRangeException>();
            noClock.Should().Throw<ArgumentNullException>();
        }

        public void Dispose()
        {
            _primary.Dispose();
            _replicas.Dispose();
            _clicks.Dispose();
        }

        private static int Drain(RedirectService service)
        {
            int count = 0;
            while (service.TryTakeFallback())
            {
                count++;
            }

            return count;
        }

        private RedirectService NewService(int perSecond, TimeProvider time)
        {
            DatabaseGuard guard = new DatabaseGuard("unit-" + Guid.NewGuid().ToString("N"), new ResilienceOptions(), TimeProvider.System, null, NullLogger.Instance);
            return new RedirectService(_replicas, _primary, guard, _clicks, perSecond, time);
        }
    }
}
