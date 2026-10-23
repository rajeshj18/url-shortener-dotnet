using System;
using System.Threading;
using System.Threading.Tasks;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// Starts a transaction on the <c>links</c> primary that the repository and the idempotency store share
    /// (LLD §6.1: key, link and stored response commit together or not at all).
    /// </summary>
    public interface IUnitOfWork
    {
        /// <summary>Begins a transaction. Disposing it without <see cref="IUnitOfWorkTransaction.CommitAsync"/> rolls back.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The open transaction.</returns>
        Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken);
    }

    /// <summary>An open transaction. Dispose rolls back unless it was committed.</summary>
    public interface IUnitOfWorkTransaction : IAsyncDisposable
    {
        /// <summary>Commits the transaction.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when committed.</returns>
        Task CommitAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Marks a point to return to. Needed because PostgreSQL aborts the whole transaction after a failed
        /// statement, unless it is rolled back to a savepoint.
        /// </summary>
        /// <param name="name">Savepoint name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the savepoint exists.</returns>
        Task CreateSavepointAsync(string name, CancellationToken cancellationToken);

        /// <summary>Undoes everything after the savepoint, keeping the transaction usable.</summary>
        /// <param name="name">Savepoint name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when rolled back to the savepoint.</returns>
        Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken);
    }
}
