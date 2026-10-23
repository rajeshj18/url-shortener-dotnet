using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Application.Redirects;
using UrlShortener.Infrastructure.Observability;
using UrlShortener.Infrastructure.Redirects;
using Xunit;

namespace UrlShortener.UnitTests.Infrastructure.Redirects
{
    /// <summary>TSK-09 AC7 and D1: the primary-fallback token bucket on a fake clock, and the no-op publisher's counter.</summary>
    public sealed class RedirectInfrastructureTests
    {
        [Fact]
        public void Budget_allows_a_burst_of_one_second_then_refuses()
        {
            SteppedTime time = new SteppedTime();
            TokenBucketFallbackBudget budget = new TokenBucketFallbackBudget(5, time);

            int allowed = 0;
            for (int i = 0; i < 20; i++)
            {
                allowed += budget.TryAcquire() ? 1 : 0;
            }

            allowed.Should().Be(5);
        }

        [Fact]
        public void Budget_refills_with_time_but_never_above_its_capacity()
        {
            SteppedTime time = new SteppedTime();
            TokenBucketFallbackBudget budget = new TokenBucketFallbackBudget(10, time);
            Drain(budget);

            time.Advance(TimeSpan.FromMilliseconds(300));
            Drain(budget).Should().Be(3, "300 ms at 10/s");

            time.Advance(TimeSpan.FromHours(1));
            Drain(budget).Should().Be(10, "an idle hour doesn't build up more than one second's burst");
        }

        [Fact]
        public void Budget_counts_granted_and_denied_fallbacks()
        {
            long granted = 0;
            long denied = 0;
            using MeterListener listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == UrlShortenerMeters.Name && instrument.Name == TokenBucketFallbackBudget.DecisionsCounterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    if (tag.Key == "outcome" && (string?)tag.Value == "granted")
                    {
                        granted += value;
                    }
                    else if (tag.Key == "outcome" && (string?)tag.Value == "denied")
                    {
                        denied += value;
                    }
                }
            });
            listener.Start();
            TokenBucketFallbackBudget budget = new TokenBucketFallbackBudget(3, new SteppedTime());

            for (int i = 0; i < 5; i++)
            {
                budget.TryAcquire();
            }

            granted.Should().Be(3);
            denied.Should().Be(2);
        }

        [Fact]
        public void Budget_rejects_invalid_arguments()
        {
            Action zero = () => _ = new TokenBucketFallbackBudget(0, new SteppedTime());
            Action noClock = () => _ = new TokenBucketFallbackBudget(5, null!);

            zero.Should().Throw<ArgumentOutOfRangeException>();
            noClock.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void No_op_publisher_accepts_and_counts_every_event()
        {
            long counted = 0;
            using MeterListener listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == UrlShortenerMeters.Name && instrument.Name == NoOpClickEventPublisher.DiscardedCounterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, _, _) => counted += value);
            listener.Start();

            bool accepted = new NoOpClickEventPublisher().TryPublish(new ClickEvent(1, Guid.NewGuid(), "aZ3k9Qx", DateTimeOffset.UtcNow, null, "desktop"));

            accepted.Should().BeTrue();
            counted.Should().Be(1);
        }

        [Fact]
        public void No_op_publisher_rejects_null()
        {
            Action act = () => new NoOpClickEventPublisher().TryPublish(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Wiring_reads_the_budget_and_rejects_a_zero_budget()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddRedirectUseCases(Config(null));
            Action zero = () => new ServiceCollection().AddRedirectUseCases(Config("0"));

            services.Should().Contain(d => d.ServiceType == typeof(IResolveShortUrlHandler));
            zero.Should().Throw<InvalidOperationException>().WithMessage("Redirect:PrimaryFallbackPerSecond*");
        }

        private static int Drain(TokenBucketFallbackBudget budget)
        {
            int count = 0;
            while (budget.TryAcquire())
            {
                count++;
            }

            return count;
        }

        private static IConfiguration Config(string? perSecond)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RedirectUseCaseServiceCollectionExtensions.FallbackPerSecondKey] = perSecond })
                .Build();
        }

        /// <summary>A monotonic clock the test advances by hand.</summary>
        private sealed class SteppedTime : TimeProvider
        {
            private long _ticks;

            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override long GetTimestamp()
            {
                return _ticks;
            }

            public void Advance(TimeSpan by)
            {
                _ticks += by.Ticks;
            }
        }
    }
}
