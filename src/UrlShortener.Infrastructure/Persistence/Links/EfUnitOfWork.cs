using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using UrlShortener.Application.Ports;
using UrlShortener.Infrastructure.Resilience;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// <see cref="IUnitOfWork"/> on the scoped <see cref="LinksDbContext"/>. The repository and the idempotency
    /// store use the same context, so they share the connection and this transaction.
    /// </summary>
    public sealed class EfUnitOfWork : IUnitOfWork
    {
        private readonly LinksDbContext _db;

        /// <summary>Creates the unit of work.</summary>
        /// <param name="db">The scoped <c>links</c> context.</param>
        public EfUnitOfWork(LinksDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <inheritdoc />
        public async Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken)
        {
            IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            return new EfUnitOfWorkTransaction(transaction);
        }

        private sealed class EfUnitOfWorkTransaction : IUnitOfWorkTransaction
        {
            private readonly IDbContextTransaction _transaction;

            public EfUnitOfWorkTransaction(IDbContextTransaction transaction)
            {
                _transaction = transaction;
            }

            public async Task CommitAsync(CancellationToken cancellationToken)
            {
                try
                {
                    await _transaction.CommitAsync(cancellationToken);
                }
                catch (Exception exception) when (TransientDatabaseErrors.IsTransient(exception))
                {
                    // COMMIT was sent; the server may have applied it. Callers decide whether a retry is safe (LLD §6.7).
                    throw new CommitOutcomeUnknownException("The commit failed; whether it took effect is unknown.", exception);
                }
            }

            public Task CreateSavepointAsync(string name, CancellationToken cancellationToken)
            {
                return _transaction.CreateSavepointAsync(name, cancellationToken);
            }

            public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken)
            {
                return _transaction.RollbackToSavepointAsync(name, cancellationToken);
            }

            // EF rolls back an uncommitted transaction on dispose.
            public ValueTask DisposeAsync()
            {
                return _transaction.DisposeAsync();
            }
        }
    }
}
