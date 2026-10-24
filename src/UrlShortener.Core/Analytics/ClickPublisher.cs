using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using UrlShortener.Core.Web;

namespace UrlShortener.Core.Analytics
{
    /// <summary>Configuration section <c>Kafka</c> (LLD §8).</summary>
    public sealed class KafkaOptions
    {
        /// <summary>Configuration section name.</summary>
        public const string SectionName = "Kafka";

        /// <summary>Brokers, e.g. <c>redpanda:9092</c>. Empty turns analytics off: clicks are counted as discarded.</summary>
        public string? BootstrapServers { get; set; }

        /// <summary>The click topic.</summary>
        public string Topic { get; set; } = "click-events.v1";

        /// <summary>The worker's consumer group (also the key of its offsets in the database).</summary>
        public string ConsumerGroup { get; set; } = "analytics-worker";

        /// <summary>Most records per worker batch.</summary>
        public int BatchMaxRecords { get; set; } = 5000;

        /// <summary>Longest a worker batch collects records (≤ 10 s keeps the 60 s freshness SLO).</summary>
        public TimeSpan BatchMaxDuration { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Size of the producer's local queue; when full, clicks are dropped, never waited for.</summary>
        public int ProducerQueueMaxMessages { get; set; } = 100_000;

        /// <summary><c>true</c> if brokers are configured.</summary>
        public bool Enabled => !string.IsNullOrWhiteSpace(BootstrapServers);

        /// <summary>Binds the section.</summary>
        /// <param name="configuration">Configuration.</param>
        /// <returns>The options.</returns>
        public static KafkaOptions Read(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            KafkaOptions options = new KafkaOptions();
            configuration.GetSection(SectionName).Bind(options);
            return options;
        }
    }

    /// <summary>The event of <see cref="ClickPublisher.Accepted"/>.</summary>
    public sealed class ClickAcceptedEventArgs : EventArgs
    {
        /// <summary>Creates the arguments.</summary>
        /// <param name="click">The accepted click.</param>
        public ClickAcceptedEventArgs(ClickEvent click)
        {
            Click = click;
        }

        /// <summary>The accepted click.</summary>
        public ClickEvent Click { get; }
    }

    /// <summary>
    /// Publishes click events to Kafka (ADR-0008, LLD §6.5). <see cref="TryPublish"/> <b>never blocks</b>: Kafka's client
    /// queues locally and sends in the background (idempotent producer, <c>acks=all</c>, <c>linger.ms=5</c>). A full queue
    /// drops the click and counts it. The trace context travels in the <c>traceparent</c> header (ADR-0013).
    /// </summary>
    public sealed class ClickPublisher : IDisposable
    {
        private static readonly JsonSerializerOptions EventJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private static readonly Counter<long> Published = AppTelemetry.Meter.CreateCounter<long>(
            "click_events_published", unit: "{event}", description: "Click events handed to the Kafka client.");

        private static readonly Counter<long> Dropped = AppTelemetry.Meter.CreateCounter<long>(
            "click_events_dropped", unit: "{event}", description: "Click events dropped: local queue full or delivery failed.");

        private static readonly Counter<long> Discarded = AppTelemetry.Meter.CreateCounter<long>(
            "click_events_discarded", unit: "{event}", description: "Click events built but discarded (no Kafka configured).");

        private readonly IProducer<string, string>? _producer;
        private readonly string _topic;

        /// <summary>Creates the publisher; without brokers it only counts the events.</summary>
        /// <param name="options">Kafka settings.</param>
        public ClickPublisher(KafkaOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _topic = options.Topic;
            if (options.Enabled)
            {
                _producer = new ProducerBuilder<string, string>(new ProducerConfig
                {
                    BootstrapServers = options.BootstrapServers,
                    EnableIdempotence = true,
                    Acks = Acks.All,
                    LingerMs = 5,
                    QueueBufferingMaxMessages = options.ProducerQueueMaxMessages,
                }).Build();
            }
        }

        /// <summary>Raised for every accepted event (diagnostics and tests).</summary>
        public event EventHandler<ClickAcceptedEventArgs>? Accepted;

        /// <summary>Queues the event, or drops it if the local queue is full.</summary>
        /// <param name="clickEvent">The event.</param>
        /// <returns><c>true</c> if accepted; <c>false</c> if dropped (counted, never retried by the caller).</returns>
        public bool TryPublish(ClickEvent clickEvent)
        {
            ArgumentNullException.ThrowIfNull(clickEvent);
            if (_producer == null)
            {
                Discarded.Add(1);
                Accepted?.Invoke(this, new ClickAcceptedEventArgs(clickEvent));
                return true;
            }

            Message<string, string> message = new Message<string, string>
            {
                Key = clickEvent.Code, // one code → one partition, in order
                Value = JsonSerializer.Serialize(clickEvent, EventJson),
                Headers = new Headers(),
            };
            if (Activity.Current != null)
            {
                message.Headers.Add("traceparent", Encoding.ASCII.GetBytes(Activity.Current.Id!));
            }

            try
            {
                _producer.Produce(_topic, message, report =>
                {
                    if (report.Error.IsError)
                    {
                        Dropped.Add(1, new KeyValuePair<string, object?>("reason", "delivery"));
                    }
                });
            }
            catch (ProduceException<string, string> exception) when (exception.Error.Code == ErrorCode.Local_QueueFull)
            {
                Dropped.Add(1, new KeyValuePair<string, object?>("reason", "queue-full"));
                return false;
            }

            Published.Add(1);
            Accepted?.Invoke(this, new ClickAcceptedEventArgs(clickEvent));
            return true;
        }

        /// <summary>Parses an event as published; <c>null</c> if it isn't a valid v1 event.</summary>
        /// <param name="json">The message value.</param>
        /// <returns>The event, or <c>null</c>.</returns>
        public static ClickEvent? Parse(string? json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                ClickEvent? parsed = JsonSerializer.Deserialize<ClickEvent>(json, EventJson);
                return parsed != null && parsed.SchemaVersion == 1 && !string.IsNullOrEmpty(parsed.Code) && !string.IsNullOrEmpty(parsed.Device) ? parsed : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Serializes an event the way <see cref="TryPublish"/> does (tests replay events with it).</summary>
        /// <param name="clickEvent">The event.</param>
        /// <returns>The JSON.</returns>
        public static string Serialize(ClickEvent clickEvent)
        {
            return JsonSerializer.Serialize(clickEvent, EventJson);
        }

        /// <summary>Sends what is still queued (up to 5 s), then closes the producer.</summary>
        public void Dispose()
        {
            if (_producer != null)
            {
                _producer.Flush(TimeSpan.FromSeconds(5));
                _producer.Dispose();
            }
        }
    }
}
