using System;
using System.Threading;
using System.Threading.Tasks;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// The <c>idempotency_keys</c> table (ADR-0010, LLD §6.1). Both calls run inside the caller's
    /// <see cref="IUnitOfWork"/> transaction, so a failed create leaves no key behind.
    /// </summary>
    public interface IIdempotencyStore
    {
        /// <summary>
        /// Claims the key for this request. A concurrent request with the same key waits until the first
        /// transaction ends. A key older than 24 hours is treated as absent and taken over.
        /// </summary>
        /// <param name="ownerId">The caller (keys are scoped per owner).</param>
        /// <param name="key">The client's <c>Idempotency-Key</c>.</param>
        /// <param name="requestHash">SHA-256 of the normalized request (<see cref="ShortUrls.RequestHash"/>).</param>
        /// <param name="now">The current time; the 24-hour window is measured from the stored creation time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>New, replay (with the stored body) or mismatch.</returns>
        Task<IdempotencyBeginResult> TryBeginAsync(string ownerId, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken);

        /// <summary>Stores the <c>201</c> response for later replays, in the same transaction as the link.</summary>
        /// <param name="ownerId">The caller.</param>
        /// <param name="key">The client's key.</param>
        /// <param name="responseBody">The response body as JSON.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when stored.</returns>
        Task CompleteAsync(string ownerId, string key, string responseBody, CancellationToken cancellationToken);
    }

    /// <summary>What <see cref="IIdempotencyStore.TryBeginAsync"/> found.</summary>
    public enum IdempotencyBeginOutcome
    {
        /// <summary>The key is new (or expired): process the request.</summary>
        New,

        /// <summary>The same request was already completed: return the stored response.</summary>
        Replay,

        /// <summary>The key was used for a different request: <c>422</c>.</summary>
        Mismatch,
    }

    /// <summary>The result of claiming an idempotency key.</summary>
    public sealed class IdempotencyBeginResult
    {
        private IdempotencyBeginResult(IdempotencyBeginOutcome outcome, string? storedResponseBody)
        {
            Outcome = outcome;
            StoredResponseBody = storedResponseBody;
        }

        /// <summary>New (process the request).</summary>
        public static IdempotencyBeginResult New { get; } = new IdempotencyBeginResult(IdempotencyBeginOutcome.New, null);

        /// <summary>Mismatch (the key belongs to a different request).</summary>
        public static IdempotencyBeginResult Mismatch { get; } = new IdempotencyBeginResult(IdempotencyBeginOutcome.Mismatch, null);

        /// <summary>What was found.</summary>
        public IdempotencyBeginOutcome Outcome { get; }

        /// <summary>The stored response body; set only for <see cref="IdempotencyBeginOutcome.Replay"/>.</summary>
        public string? StoredResponseBody { get; }

        /// <summary>Replay of a completed request.</summary>
        /// <param name="storedResponseBody">The stored response body.</param>
        /// <returns>The result.</returns>
        public static IdempotencyBeginResult Replay(string storedResponseBody)
        {
            ArgumentNullException.ThrowIfNull(storedResponseBody);
            return new IdempotencyBeginResult(IdempotencyBeginOutcome.Replay, storedResponseBody);
        }
    }
}
