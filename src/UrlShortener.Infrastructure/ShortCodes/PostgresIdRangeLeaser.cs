using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UrlShortener.Application.Ports;
using UrlShortener.Infrastructure.Persistence.Links;

namespace UrlShortener.Infrastructure.ShortCodes
{
    /// <summary>
    /// Leases ID ranges from <c>id_blocks</c> on the primary, as the Api role (LLD §6.4).
    /// <list type="bullet">
    /// <item><b>Lease:</b> one atomic <c>UPDATE … RETURNING</c>. PostgreSQL re-checks the condition on the row it locks,
    /// so concurrent instances can never receive overlapping ranges.</item>
    /// <item><b>Block switch</b> (the active block has no room): under a per-region advisory lock, re-check, mark it
    /// exhausted, activate the lowest reserved block, then lease again. The final partial lease of a block is
    /// skipped, which wastes at most one lease of IDs per block.</item>
    /// </list>
    /// </summary>
    public sealed class PostgresIdRangeLeaser : IIdRangeLeaser
    {
        /// <summary>First key of the two-key advisory lock used for block switches (an arbitrary, fixed value).</summary>
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

        private readonly LinksPrimaryDataSource _primary;
        private readonly short _regionIndex;

        /// <summary>Creates the leaser.</summary>
        /// <param name="primary">The writer endpoint (Api role).</param>
        /// <param name="regionIndex">This deployment's region (ADR-0005).</param>
        public PostgresIdRangeLeaser(LinksPrimaryDataSource primary, short regionIndex)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _regionIndex = regionIndex;
        }

        /// <inheritdoc />
        public async Task<IdRange> LeaseAsync(int size, CancellationToken cancellationToken)
        {
            if (size < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "A lease must hold at least 1 ID.");
            }

            // Lease, or switch to the next block and lease again. Two switches in a row would mean a reserved
            // block smaller than one lease, which the runbook forbids, so give up after that.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                long? start = await TryLeaseAsync(size, cancellationToken);
                if (start.HasValue)
                {
                    return new IdRange(start.Value, start.Value + size);
                }

                if (!await TrySwitchBlockAsync(size, cancellationToken))
                {
                    throw new IdSpaceExhaustedException();
                }
            }

            throw new IdSpaceExhaustedException();
        }

        private async Task<long?> TryLeaseAsync(int size, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand command = _primary.DataSource.CreateCommand(LeaseSql);
            command.Parameters.AddWithValue("size", (long)size);
            command.Parameters.AddWithValue("region", _regionIndex);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return result == null ? null : (long)result;
        }

        /// <returns><c>true</c> if a block with room is now active (switched by us or by another instance).</returns>
        private async Task<bool> TrySwitchBlockAsync(int size, CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _primary.DataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (NpgsqlCommand lockCommand = new NpgsqlCommand(LockSql, connection, transaction))
            {
                AddParameters(lockCommand, size);
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            // Another instance may have switched while we waited for the lock.
            await using (NpgsqlCommand check = new NpgsqlCommand(ActiveHasRoomSql, connection, transaction))
            {
                AddParameters(check, size);
                object? hasRoom = await check.ExecuteScalarAsync(cancellationToken);
                if (hasRoom is bool room && room)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return true;
                }
            }

            await using (NpgsqlCommand exhaust = new NpgsqlCommand(ExhaustActiveSql, connection, transaction))
            {
                AddParameters(exhaust, size);
                await exhaust.ExecuteNonQueryAsync(cancellationToken);
            }

            await using NpgsqlCommand activate = new NpgsqlCommand(ActivateReservedSql, connection, transaction);
            AddParameters(activate, size);
            object? activated = await activate.ExecuteScalarAsync(cancellationToken);
            if (activated == null)
            {
                await transaction.RollbackAsync(cancellationToken); // keep the state unchanged; nothing to switch to
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        // Every statement takes the same parameters; PostgreSQL ignores those a statement does not reference.
        private void AddParameters(NpgsqlCommand command, int size)
        {
            command.Parameters.AddWithValue("size", (long)size);
            command.Parameters.AddWithValue("region", _regionIndex);
            command.Parameters.AddWithValue("lockClass", AdvisoryLockClass);
        }
    }
}
