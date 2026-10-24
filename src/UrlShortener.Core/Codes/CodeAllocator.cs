using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using UrlShortener.Core.Database;
using UrlShortener.Domain;

namespace UrlShortener.Core.Codes
{
    /// <summary>
    /// Hands out generated codes (ADR-0005, LLD §6.4): ID → <see cref="FeistelPermutation"/> → <see cref="Base62Encoder"/>.
    /// <list type="bullet">
    /// <item><b>Leases:</b> IDs come from an in-memory range leased from <c>id_blocks</c> with one atomic
    /// <c>UPDATE … RETURNING</c>, so concurrent instances never get overlapping ranges. At half a lease the next one is
    /// fetched in the background (single-flight), so switching normally needs no database call.</item>
    /// <item><b>Block switch</b> (no room left): under a per-region advisory lock, re-check, mark the block exhausted and
    /// activate the lowest reserved block.</item>
    /// <item><b>Startup check</b> (as a hosted service): refuses to start if this region has no active block, or if the
    /// database holds blocks of another region (a wrong region index would risk duplicate codes). Then takes the first lease.</item>
    /// </list>
    /// Thread-safe; one instance per Api process.
    /// </summary>
    public sealed partial class CodeAllocator : IHostedService, IDisposable
    {
        /// <summary>First key of the two-key advisory lock used for block switches.</summary>
        public const int AdvisoryLockClass = 7005;

        private const string LeaseSql =
            "UPDATE id_blocks SET next_id = next_id + @size " +
            "WHERE region_index = @region AND status = 'active' AND next_id + @size <= range_end " +
            "RETURNING next_id - @size";

        private const string LockSql = "SELECT pg_advisory_xact_lock(@lockClass, @region)";

        private const string ActiveHasRoomSql =
            "SELECT next_id + @size <= range_end FROM id_blocks WHERE region_index = @region AND status = 'active' FOR UPDATE";

        private const string ExhaustActiveSql =
            "UPDATE id_blocks SET status = 'exhausted' WHERE region_index = @region AND status = 'active'";

        private const string ActivateReservedSql =
            "UPDATE id_blocks SET status = 'active' WHERE block_id = (" +
            "SELECT block_id FROM id_blocks WHERE region_index = @region AND status = 'reserved' ORDER BY range_start LIMIT 1) " +
            "RETURNING block_id";

        private const string BlocksSql = "SELECT block_id, region_index, status FROM id_blocks";

        private readonly PrimaryDatabase _primary;
        private readonly FeistelPermutation _permutation;
        private readonly short _regionIndex;
        private readonly int _leaseSize;
        private readonly ILogger<CodeAllocator> _logger;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _refill = new SemaphoreSlim(1, 1);

        private long _nextId;
        private long _endExclusive;
        private Task<long>? _prefetch;

        /// <summary>Creates the allocator.</summary>
        /// <param name="primary">The writer endpoint (Api role).</param>
        /// <param name="permutation">The keyed permutation.</param>
        /// <param name="regionIndex">This deployment's region (ADR-0005).</param>
        /// <param name="leaseSize">IDs per lease (≥ 2).</param>
        /// <param name="logger">Logger.</param>
        public CodeAllocator(PrimaryDatabase primary, FeistelPermutation permutation, short regionIndex, int leaseSize, ILogger<CodeAllocator> logger)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _permutation = permutation ?? throw new ArgumentNullException(nameof(permutation));
            if (leaseSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseSize), "A lease must hold at least 2 IDs.");
            }

            _regionIndex = regionIndex;
            _leaseSize = leaseSize;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Returns the next unique generated code.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A generated code.</returns>
        public async Task<ShortCode> NextAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_nextId < _endExclusive)
                    {
                        long id = _nextId++;
                        if (_prefetch == null && _endExclusive - _nextId <= _leaseSize / 2)
                        {
                            // Background pre-fetch, not tied to one caller's cancellation.
                            _prefetch = Task.Run(() => LeaseAsync(CancellationToken.None));
                        }

                        return ShortCode.FromGenerated(Base62Encoder.Encode(_permutation.Permute(id)));
                    }
                }

                await RefillAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Leases <see cref="_leaseSize"/> IDs, switching to the next reserved block if the active one is full. Throws
        /// <see cref="IdSpaceExhaustedException"/> if no block has room.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The first ID of the leased range.</returns>
        public async Task<long> LeaseAsync(CancellationToken cancellationToken)
        {
            // Two switches in a row would mean a reserved block smaller than one lease, which the runbook forbids.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await using (NpgsqlCommand lease = _primary.DataSource.CreateCommand(LeaseSql))
                {
                    AddParameters(lease);
                    object? start = await lease.ExecuteScalarAsync(cancellationToken);
                    if (start != null)
                    {
                        return (long)start;
                    }
                }

                if (!await TrySwitchBlockAsync(cancellationToken))
                {
                    break;
                }
            }

            throw new IdSpaceExhaustedException();
        }

        /// <summary>The startup check, then the first lease, before the Api accepts traffic.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            List<string> foreign = new List<string>();
            int active = 0;
            await using (NpgsqlCommand command = _primary.DataSource.CreateCommand(BlocksSql))
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.GetInt16(1) != _regionIndex)
                    {
                        foreign.Add(reader.GetString(0));
                    }
                    else if (reader.GetString(2) == "active")
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
                throw new InvalidOperationException($"Refusing to start: no active ID block for region {_regionIndex}. Assign one (LLD §6.4 runbook).");
            }

            await RefillAsync(cancellationToken);
            LogReady(_logger, _regionIndex);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _refill.Dispose();
        }

        private async Task RefillAsync(CancellationToken cancellationToken)
        {
            await _refill.WaitAsync(cancellationToken);
            try
            {
                Task<long> pending;
                lock (_gate)
                {
                    if (_nextId < _endExclusive)
                    {
                        return; // another caller refilled while we waited
                    }

                    if (_prefetch != null && (_prefetch.IsFaulted || _prefetch.IsCanceled))
                    {
                        // A background failure isn't shown to a caller: observe it and lease directly.
                        _ = _prefetch.Exception;
                        _prefetch = null;
                    }

                    pending = _prefetch ?? LeaseAsync(cancellationToken);
                }

                long start;
                try
                {
                    start = await pending;
                }
                finally
                {
                    lock (_gate)
                    {
                        _prefetch = null; // used, or failed and retried on the next call
                    }
                }

                lock (_gate)
                {
                    _nextId = start;
                    _endExclusive = start + _leaseSize;
                }
            }
            finally
            {
                _refill.Release();
            }
        }

        /// <returns><c>true</c> if a block with room is now active (switched by us or by another instance).</returns>
        private async Task<bool> TrySwitchBlockAsync(CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _primary.DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using NpgsqlCommand lockCommand = new NpgsqlCommand(LockSql, connection, transaction);
            await ExecuteAsync(lockCommand, cancellationToken);
            await using NpgsqlCommand check = new NpgsqlCommand(ActiveHasRoomSql, connection, transaction);
            object? hasRoom = await ScalarAsync(check, cancellationToken);
            if (hasRoom is bool room && room)
            {
                await transaction.CommitAsync(cancellationToken); // another instance switched while we waited
                return true;
            }

            await using NpgsqlCommand exhaust = new NpgsqlCommand(ExhaustActiveSql, connection, transaction);
            await ExecuteAsync(exhaust, cancellationToken);
            await using NpgsqlCommand activate = new NpgsqlCommand(ActivateReservedSql, connection, transaction);
            if (await ScalarAsync(activate, cancellationToken) == null)
            {
                await transaction.RollbackAsync(cancellationToken); // nothing to switch to; keep the state unchanged
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        private async Task ExecuteAsync(NpgsqlCommand command, CancellationToken cancellationToken)
        {
            AddParameters(command);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<object?> ScalarAsync(NpgsqlCommand command, CancellationToken cancellationToken)
        {
            AddParameters(command);
            return await command.ExecuteScalarAsync(cancellationToken);
        }

        // Every statement takes the same parameters; PostgreSQL ignores those a statement doesn't reference.
        private void AddParameters(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("size", (long)_leaseSize);
            command.Parameters.AddWithValue("region", _regionIndex);
            command.Parameters.AddWithValue("lockClass", AdvisoryLockClass);
        }

        [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Short-code generation ready for region {RegionIndex}; first lease taken.")]
        private static partial void LogReady(ILogger logger, short regionIndex);
    }

    /// <summary>
    /// No block of this region has room for another lease: <c>503</c>. Operationally the 80% alert was missed and the
    /// runbook (LLD §6.4) must assign a new block.
    /// </summary>
    public sealed class IdSpaceExhaustedException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public IdSpaceExhaustedException()
            : base("No ID block of this region has room for another lease. Assign a new block (LLD §6.4 runbook).")
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">The message.</param>
        public IdSpaceExhaustedException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and an inner exception.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The cause.</param>
        public IdSpaceExhaustedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
