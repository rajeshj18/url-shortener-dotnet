using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using UrlShortener.Infrastructure.Observability;
using UrlShortener.Infrastructure.Persistence.Links;
using UrlShortener.Infrastructure.Resilience;

namespace UrlShortener.Infrastructure.Housekeeping
{
    /// <summary>
    /// Deletes idempotency keys older than 24 hours (LLD §6.6, ADR-0010), inside the Api, which owns the schema.
    /// Every run tries <c>pg_try_advisory_lock</c>; only the instance that gets it works, the others skip that run.
    /// The lock is session-level on a dedicated connection, so it is released if the instance dies. Deletes go in
    /// batches (index <c>ix_idempotency_keys_created</c>) so no single statement holds locks for long.
    /// </summary>
    public sealed partial class IdempotencyKeyCleanupService : BackgroundService
    {
        /// <summary>First key of the job's advisory lock (second key 1 = idempotency cleanup).</summary>
        public const int AdvisoryLockClass = 7006;

        private const string TryLockSql = "SELECT pg_try_advisory_lock(7006, 1)";
        private const string UnlockSql = "SELECT pg_advisory_unlock(7006, 1)";
        private const string DeleteBatchSql =
            "DELETE FROM idempotency_keys WHERE ctid IN (SELECT ctid FROM idempotency_keys WHERE created_at < @cutoff LIMIT @batch)";

        private static readonly Counter<long> Deleted = UrlShortenerMeters.Meter.CreateCounter<long>(
            "idempotency_keys_deleted", unit: "{row}", description: "Expired idempotency keys deleted by the cleanup job.");

        private static readonly Histogram<double> Duration = UrlShortenerMeters.Meter.CreateHistogram<double>(
            "idempotency_cleanup_duration", unit: "s", description: "Duration of a cleanup run that held the lock.");

        private readonly LinksPrimaryDataSource _primary;
        private readonly ResilienceOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<IdempotencyKeyCleanupService> _logger;

        /// <summary>Creates the job.</summary>
        /// <param name="primary">The writer endpoint (Api role).</param>
        /// <param name="options">Interval, batch size and timeout.</param>
        /// <param name="time">Clock.</param>
        /// <param name="logger">Logger.</param>
        public IdempotencyKeyCleanupService(LinksPrimaryDataSource primary, ResilienceOptions options, TimeProvider time, ILogger<IdempotencyKeyCleanupService> logger)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>One run: take the lock or skip, then delete expired keys batch by batch.</summary>
        /// <param name="cancellationToken">Stops the run.</param>
        /// <returns>Rows deleted, or <c>null</c> if another instance holds the lock.</returns>
        public async Task<long?> RunOnceAsync(CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _primary.DataSource.OpenConnectionAsync(cancellationToken);
            await using (NpgsqlCommand tryLock = new NpgsqlCommand(TryLockSql, connection))
            {
                if (!(bool)(await tryLock.ExecuteScalarAsync(cancellationToken))!)
                {
                    return null;
                }
            }

            Stopwatch watch = Stopwatch.StartNew();
            long total = 0;
            try
            {
                DateTimeOffset cutoff = _time.GetUtcNow() - EfIdempotencyStore.Window;
                int deleted;
                do
                {
                    await using NpgsqlCommand delete = new NpgsqlCommand(DeleteBatchSql, connection)
                    {
                        CommandTimeout = _options.IdempotencyCleanupBatchTimeoutSeconds,
                    };
                    delete.Parameters.AddWithValue("cutoff", cutoff);
                    delete.Parameters.AddWithValue("batch", _options.IdempotencyCleanupBatchSize);
                    deleted = await delete.ExecuteNonQueryAsync(cancellationToken);
                    total += deleted;
                }
                while (deleted == _options.IdempotencyCleanupBatchSize && !cancellationToken.IsCancellationRequested);
            }
            finally
            {
                await using NpgsqlCommand unlock = new NpgsqlCommand(UnlockSql, connection);
                await unlock.ExecuteScalarAsync(CancellationToken.None);
            }

            Deleted.Add(total);
            Duration.Record(watch.Elapsed.TotalSeconds);
            LogRun(_logger, total, watch.Elapsed.TotalMilliseconds);
            return total;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new PeriodicTimer(_options.IdempotencyCleanupInterval, _time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (TransientDatabaseErrors.IsTransient(exception))
                {
                    // The next run tries again; deleting twice is harmless (LLD §6.7).
                    LogFailed(_logger, exception.GetType().Name);
                }
            }
        }

        [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "Idempotency cleanup deleted {Rows} expired keys in {Milliseconds} ms.")]
        private static partial void LogRun(ILogger logger, long rows, double milliseconds);

        [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "Idempotency cleanup failed ({ExceptionType}); retrying at the next run.")]
        private static partial void LogFailed(ILogger logger, string exceptionType);
    }
}
