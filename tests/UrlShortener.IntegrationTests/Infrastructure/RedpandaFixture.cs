using System;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Redpanda;
using Xunit;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>
    /// A real Kafka-compatible broker (Redpanda, the same image as docker-compose) with the click topic created the way
    /// <c>redpanda-init</c> creates it: 3 partitions.
    /// </summary>
    public sealed class RedpandaFixture : IAsyncLifetime
    {
        /// <summary>The click topic (LLD §6.5).</summary>
        public const string Topic = "click-events.v1";

        private readonly RedpandaContainer _container = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v26.2.4").Build();

        /// <summary>The broker address for clients on the test host.</summary>
        public string BootstrapServers => _container.GetBootstrapAddress();

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            await _container.StartAsync();
            using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
            await admin.CreateTopicsAsync(new[] { new TopicSpecification { Name = Topic, NumPartitions = 3, ReplicationFactor = 1 } });
        }

        /// <inheritdoc />
        public async Task DisposeAsync()
        {
            await _container.DisposeAsync();
        }
    }
}
