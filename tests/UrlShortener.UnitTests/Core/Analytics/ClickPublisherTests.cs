using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using UrlShortener.Core.Analytics;
using UrlShortener.Core.Web;
using Xunit;

namespace UrlShortener.UnitTests.Core.Analytics
{
    /// <summary>
    /// The click publisher without a broker (counts and discards, the stage-1 behaviour), the v1 event format, and the
    /// promise that publishing never blocks: with an unreachable broker, a burst of clicks returns at once.
    /// </summary>
    public sealed class ClickPublisherTests
    {
        private static readonly ClickEvent Click = new ClickEvent(1, Guid.NewGuid(), "aZ3k9Qx", new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero), "twitter.com", "mobile");

        [Fact]
        public void Without_brokers_events_are_accepted_counted_and_raised()
        {
            long discarded = 0;
            using MeterListener listener = Listen("click_events_discarded", value => discarded += value);
            using ClickPublisher publisher = new ClickPublisher(new KafkaOptions());
            List<ClickEvent> raised = new List<ClickEvent>();
            publisher.Accepted += (_, e) => raised.Add(e.Click);

            bool accepted = publisher.TryPublish(Click);

            accepted.Should().BeTrue();
            discarded.Should().Be(1);
            raised.Should().ContainSingle().Which.Should().Be(Click);
        }

        [Fact]
        public void Publishing_never_blocks_even_when_the_broker_is_unreachable()
        {
            // Port 1 on loopback refuses connections: the client queues locally and sends in the background.
            using ClickPublisher publisher = new ClickPublisher(new KafkaOptions { BootstrapServers = "127.0.0.1:1" });
            Stopwatch watch = Stopwatch.StartNew();

            for (int i = 0; i < 1_000; i++)
            {
                publisher.TryPublish(Click with { EventId = Guid.NewGuid() }).Should().BeTrue();
            }

            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "a redirect must never wait for analytics");
        }

        [Fact]
        public void A_full_local_queue_drops_the_click_instead_of_waiting()
        {
            using ClickPublisher publisher = new ClickPublisher(new KafkaOptions { BootstrapServers = "127.0.0.1:1", ProducerQueueMaxMessages = 1 });

            bool first = publisher.TryPublish(Click);
            bool second = publisher.TryPublish(Click with { EventId = Guid.NewGuid() });

            first.Should().BeTrue();
            second.Should().BeFalse("the queue holds one message and the broker is unreachable");
        }

        [Fact]
        public void Events_round_trip_in_the_documented_v1_json()
        {
            string json = ClickPublisher.Serialize(Click);

            json.Should().Contain("\"schemaVersion\":1").And.Contain("\"referrerHost\":\"twitter.com\"").And.Contain("\"device\":\"mobile\"");
            json.Should().NotContainAny("ip", "userAgent");
            ClickPublisher.Parse(json).Should().Be(Click);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"schemaVersion\":2,\"eventId\":\"0192f0c4-0000-7000-8000-000000000000\",\"code\":\"aZ3k9Qx\",\"occurredAt\":\"2026-10-23T10:00:00Z\",\"device\":\"mobile\"}")]
        [InlineData("{\"schemaVersion\":1,\"eventId\":\"0192f0c4-0000-7000-8000-000000000000\",\"code\":\"\",\"occurredAt\":\"2026-10-23T10:00:00Z\",\"device\":\"mobile\"}")]
        public void Invalid_or_unknown_events_parse_as_null(string? json)
        {
            ClickPublisher.Parse(json).Should().BeNull();
        }

        [Fact]
        public void Kafka_defaults_follow_the_lld()
        {
            KafkaOptions options = new KafkaOptions();

            options.Topic.Should().Be("click-events.v1");
            options.ConsumerGroup.Should().Be("analytics-worker");
            options.BatchMaxDuration.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(10), "the stats freshness SLO is 60 s");
            options.Enabled.Should().BeFalse();
        }

        private static MeterListener Listen(string instrumentName, Action<long> onValue)
        {
            MeterListener listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AppTelemetry.Name && instrument.Name == instrumentName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, _, _) => onValue(value));
            listener.Start();
            return listener;
        }
    }
}
