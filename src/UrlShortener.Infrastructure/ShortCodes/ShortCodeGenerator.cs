using System;
using System.Threading;
using System.Threading.Tasks;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;

namespace UrlShortener.Infrastructure.ShortCodes
{
    /// <summary>
    /// Hands out generated codes from an in-memory lease: ID → <see cref="FeistelPermutation"/> →
    /// <see cref="Base62Encoder"/> (ADR-0005). Taking an ID is a short lock. When half of the lease is used, the
    /// next lease is fetched in the background, so the switch normally needs no database call. Only one refill
    /// runs at a time (single-flight). Thread-safe; one instance per service process.
    /// </summary>
    public sealed class ShortCodeGenerator : IShortCodeGenerator, IDisposable
    {
        private readonly IIdRangeLeaser _leaser;
        private readonly FeistelPermutation _permutation;
        private readonly int _leaseSize;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _refill = new SemaphoreSlim(1, 1);

        private IdRange? _current;
        private long _nextId;
        private Task<IdRange>? _prefetch;

        /// <summary>Creates the generator.</summary>
        /// <param name="leaser">Leases ID ranges from the region's block.</param>
        /// <param name="permutation">The keyed permutation.</param>
        /// <param name="leaseSize">IDs per lease.</param>
        public ShortCodeGenerator(IIdRangeLeaser leaser, FeistelPermutation permutation, int leaseSize)
        {
            _leaser = leaser ?? throw new ArgumentNullException(nameof(leaser));
            _permutation = permutation ?? throw new ArgumentNullException(nameof(permutation));
            if (leaseSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseSize), "A lease must hold at least 2 IDs.");
            }

            _leaseSize = leaseSize;
        }

        /// <inheritdoc />
        public async Task<ShortCode> NextAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                lock (_gate)
                {
                    if (TryTakeId(out long id))
                    {
                        return ShortCode.FromGenerated(Base62Encoder.Encode(_permutation.Permute(id)));
                    }
                }

                await RefillAsync(cancellationToken);
            }
        }

        /// <summary>Takes the first lease, before the service accepts traffic (startup check).</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when a lease is held.</returns>
        public Task WarmUpAsync(CancellationToken cancellationToken)
        {
            return RefillAsync(cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _refill.Dispose();
        }

        // Caller holds _gate.
        private bool TryTakeId(out long id)
        {
            if (_current == null || _nextId >= _current.EndExclusive)
            {
                id = 0;
                return false;
            }

            id = _nextId++;
            if (_prefetch == null && _current.EndExclusive - _nextId <= _leaseSize / 2)
            {
                // Background pre-fetch, not tied to one caller's cancellation.
                _prefetch = Task.Run(() => _leaser.LeaseAsync(_leaseSize, CancellationToken.None));
            }

            return true;
        }

        private async Task RefillAsync(CancellationToken cancellationToken)
        {
            await _refill.WaitAsync(cancellationToken);
            try
            {
                Task<IdRange> pending;
                lock (_gate)
                {
                    if (_current != null && _nextId < _current.EndExclusive)
                    {
                        return; // another caller refilled while we waited
                    }

                    if (_prefetch != null && (_prefetch.IsFaulted || _prefetch.IsCanceled))
                    {
                        // A background failure (e.g. a transient database error) is not shown to a caller:
                        // observe it and lease directly. A real problem, like exhaustion, surfaces from that call.
                        _ = _prefetch.Exception;
                        _prefetch = null;
                    }

                    pending = _prefetch ?? _leaser.LeaseAsync(_leaseSize, cancellationToken);
                }

                IdRange range;
                try
                {
                    range = await pending;
                }
                catch
                {
                    lock (_gate)
                    {
                        _prefetch = null; // a failed pre-fetch is retried on the next call
                    }

                    throw;
                }

                lock (_gate)
                {
                    _current = range;
                    _nextId = range.Start;
                    _prefetch = null;
                }
            }
            finally
            {
                _refill.Release();
            }
        }
    }
}
