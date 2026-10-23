using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using UrlShortener.Infrastructure.Persistence.Links;

namespace UrlShortener.Infrastructure.ShortCodes
{
    /// <summary>
    /// The startup check of ADR-0005, run before the Api accepts traffic. It fails, and the instance refuses to start,
    /// if this region has no active block, or if the database holds blocks of <b>another</b> region (a
    /// misconfigured region index would otherwise risk duplicate codes). It then leases the first range.
    /// </summary>
    public sealed partial class ShortCodeStartupCheck : IHostedService
    {
        private const string BlocksSql = "SELECT block_id, region_index, status FROM id_blocks";

        private readonly LinksPrimaryDataSource _primary;
        private readonly ShortCodeGenerator _generator;
        private readonly short _regionIndex;
        private readonly ILogger<ShortCodeStartupCheck> _logger;

        /// <summary>Creates the check.</summary>
        /// <param name="primary">The writer endpoint.</param>
        /// <param name="generator">The generator to warm up.</param>
        /// <param name="regionIndex">This deployment's region.</param>
        /// <param name="logger">Logger.</param>
        public ShortCodeStartupCheck(LinksPrimaryDataSource primary, ShortCodeGenerator generator, short regionIndex, ILogger<ShortCodeStartupCheck> logger)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _regionIndex = regionIndex;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            List<string> foreign = new List<string>();
            int active = 0;

            await using (NpgsqlCommand command = _primary.DataSource.CreateCommand(BlocksSql))
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    string blockId = reader.GetString(0);
                    short region = reader.GetInt16(1);
                    string status = reader.GetString(2);
                    if (region != _regionIndex)
                    {
                        foreign.Add(blockId);
                    }
                    else if (status == "active")
                    {
                        active++;
                    }
                }
            }

            if (foreign.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to start: id_blocks contains blocks of another region ({string.Join(", ", foreign)}); " +
                    $"this instance is configured for region {_regionIndex}. Check ShortCode:DeploymentRegionIndex (ADR-0005).");
            }

            if (active == 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to start: no active ID block for region {_regionIndex}. Assign one (LLD §6.4 runbook).");
            }

            await _generator.WarmUpAsync(cancellationToken);
            LogReady(_logger, _regionIndex);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Short-code generation ready for region {RegionIndex}; first lease taken.")]
        private static partial void LogReady(ILogger logger, short regionIndex);
    }
}
