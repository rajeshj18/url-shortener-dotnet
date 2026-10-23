using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;

namespace UrlShortener.UnitTests.Application
{
    /// <summary>A transaction that records what happened to it. Changes are applied only on commit.</summary>
    internal sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Begun { get; private set; }

        public int Commits { get; private set; }

        public int Rollbacks { get; private set; }

        public List<string> Events { get; } = new List<string>();

        public Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken)
        {
            Begun++;
            return Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this));
        }

        private sealed class Transaction : IUnitOfWorkTransaction
        {
            private readonly FakeUnitOfWork _owner;
            private bool _committed;

            public Transaction(FakeUnitOfWork owner)
            {
                _owner = owner;
            }

            public Task CommitAsync(CancellationToken cancellationToken)
            {
                _committed = true;
                _owner.Commits++;
                _owner.Events.Add("commit");
                return Task.CompletedTask;
            }

            public Task CreateSavepointAsync(string name, CancellationToken cancellationToken)
            {
                _owner.Events.Add("savepoint:" + name);
                return Task.CompletedTask;
            }

            public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken)
            {
                _owner.Events.Add("rollback-to:" + name);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                if (!_committed)
                {
                    _owner.Rollbacks++;
                    _owner.Events.Add("rollback");
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>An in-memory links table with optional forced conflicts.</summary>
    internal sealed class FakeRepository : IShortUrlRepository
    {
        public Dictionary<string, ShortUrl> Links { get; } = new Dictionary<string, ShortUrl>(StringComparer.Ordinal);

        /// <summary>Number of upcoming generated-code inserts that report a conflict (simulated defect).</summary>
        public int ForcedGeneratedConflicts { get; set; }

        public Task<bool> TryInsertAsync(ShortUrl shortUrl, CancellationToken cancellationToken)
        {
            if (shortUrl.Code.IsGenerated && ForcedGeneratedConflicts > 0)
            {
                ForcedGeneratedConflicts--;
                return Task.FromResult(false);
            }

            return Task.FromResult(Links.TryAdd(shortUrl.Code.Value, shortUrl));
        }

        public Task<ShortUrl?> FindByCodeAsync(ShortCode code, CancellationToken cancellationToken)
        {
            Links.TryGetValue(code.Value, out ShortUrl? found);
            return Task.FromResult(found);
        }

        public Task SaveDeletionAsync(ShortUrl shortUrl, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Hands out generated codes 0000001, 0000002, …</summary>
    internal sealed class FakeGenerator : IShortCodeGenerator
    {
        private int _next;

        public int Calls => _next;

        public Exception? Failure { get; set; }

        public Task<ShortCode> NextAsync(CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                return Task.FromException<ShortCode>(Failure);
            }

            _next++;
            return Task.FromResult(ShortCode.FromGenerated(_next.ToString("D7", System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>Idempotency semantics of ADR-0010 in memory: rows become visible only when completed.</summary>
    internal sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public Dictionary<(string Owner, string Key), (string Hash, string? Body, DateTimeOffset CreatedAt)> Rows { get; } =
            new Dictionary<(string Owner, string Key), (string Hash, string? Body, DateTimeOffset CreatedAt)>();

        public int Completed { get; private set; }

        public Task<IdempotencyBeginResult> TryBeginAsync(string ownerId, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (!Rows.TryGetValue((ownerId, key), out (string Hash, string? Body, DateTimeOffset CreatedAt) row) || row.CreatedAt < now - TimeSpan.FromHours(24))
            {
                Rows[(ownerId, key)] = (requestHash, null, now);
                return Task.FromResult(IdempotencyBeginResult.New);
            }

            return Task.FromResult(row.Hash == requestHash ? IdempotencyBeginResult.Replay(row.Body!) : IdempotencyBeginResult.Mismatch);
        }

        public Task CompleteAsync(string ownerId, string key, string responseBody, CancellationToken cancellationToken)
        {
            (string Hash, string? Body, DateTimeOffset CreatedAt) row = Rows[(ownerId, key)];
            Rows[(ownerId, key)] = (row.Hash, responseBody, row.CreatedAt);
            Completed++;
            return Task.CompletedTask;
        }
    }

    /// <summary>A clock the test controls.</summary>
    internal sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
