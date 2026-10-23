using System;

namespace UrlShortener.Domain
{
    /// <summary>
    /// A short link: its code, its destination and its lifecycle (LLD §5). State changes only through
    /// <see cref="MarkDeleted"/> and, optionally, the passage of time past <see cref="ExpiresAt"/> (TSK-14).
    /// Deletion is a soft delete, the code is never reused (A-25), and deleting again changes nothing.
    /// </summary>
    public sealed class ShortUrl
    {
        /// <summary>Maximum owner ID length (matches the <c>owner_id varchar(64)</c> column).</summary>
        public const int OwnerIdMaxLength = 64;

        /// <summary>The furthest an expiry may lie after creation (TSK-14 D1).</summary>
        public const int MaxExpiryYears = 10;

        private ShortUrl(ShortCode code, LongUrl target, string ownerId, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
        {
            Code = code;
            Target = target;
            OwnerId = ownerId;
            CreatedAt = createdAt;
            ExpiresAt = expiresAt;
        }

        /// <summary>The short code.</summary>
        public ShortCode Code { get; }

        /// <summary>The destination URL.</summary>
        public LongUrl Target { get; }

        /// <summary>The API key's owner, who alone may read metadata and stats or delete the link (A-24).</summary>
        public string OwnerId { get; }

        /// <summary><c>true</c> if the code is a custom alias.</summary>
        public bool IsCustom => !Code.IsGenerated;

        /// <summary>Creation time (UTC).</summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>Deletion time (UTC), or <c>null</c> while the link is active.</summary>
        public DateTimeOffset? DeletedAt { get; private set; }

        /// <summary>Expiry time (UTC), or <c>null</c> for a link that never expires (A-22, the default).</summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary>Creates a link with a generated code.</summary>
        /// <param name="code">A generated short code.</param>
        /// <param name="target">The destination.</param>
        /// <param name="ownerId">The owner.</param>
        /// <param name="createdAt">The creation time.</param>
        /// <returns>The new, active link.</returns>
        public static ShortUrl CreateGenerated(ShortCode code, LongUrl target, string? ownerId, DateTimeOffset createdAt)
        {
            return CreateGenerated(code, target, ownerId, createdAt, null);
        }

        /// <summary>Creates a link with a generated code and an optional expiry.</summary>
        /// <param name="code">A generated short code.</param>
        /// <param name="target">The destination.</param>
        /// <param name="ownerId">The owner.</param>
        /// <param name="createdAt">The creation time.</param>
        /// <param name="expiresAt">The expiry, or <c>null</c> (see <see cref="ValidateExpiry"/>).</param>
        /// <returns>The new, active link.</returns>
        public static ShortUrl CreateGenerated(ShortCode code, LongUrl target, string? ownerId, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
        {
            ArgumentNullException.ThrowIfNull(code);
            if (!code.IsGenerated)
            {
                throw new DomainValidationException(DomainErrorCodes.CodeKind, "A generated link requires a generated code.");
            }

            return Create(code, target, ownerId, createdAt, expiresAt);
        }

        /// <summary>Creates a link with a custom alias.</summary>
        /// <param name="alias">The validated alias.</param>
        /// <param name="target">The destination.</param>
        /// <param name="ownerId">The owner.</param>
        /// <param name="createdAt">The creation time.</param>
        /// <returns>The new, active link.</returns>
        public static ShortUrl CreateWithAlias(CustomAlias alias, LongUrl target, string? ownerId, DateTimeOffset createdAt)
        {
            return CreateWithAlias(alias, target, ownerId, createdAt, null);
        }

        /// <summary>Creates a link with a custom alias and an optional expiry.</summary>
        /// <param name="alias">The validated alias.</param>
        /// <param name="target">The destination.</param>
        /// <param name="ownerId">The owner.</param>
        /// <param name="createdAt">The creation time.</param>
        /// <param name="expiresAt">The expiry, or <c>null</c> (see <see cref="ValidateExpiry"/>).</param>
        /// <returns>The new, active link.</returns>
        public static ShortUrl CreateWithAlias(CustomAlias alias, LongUrl target, string? ownerId, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
        {
            ArgumentNullException.ThrowIfNull(alias);
            return Create(ShortCode.FromAlias(alias), target, ownerId, createdAt, expiresAt);
        }

        /// <summary>
        /// Rebuilds a stored link (TSK-06). Every value goes through the same validated value objects and owner
        /// rules as a new link, so an invalid record can't become a domain object.
        /// </summary>
        /// <param name="code">The stored code (generated or alias).</param>
        /// <param name="target">The stored destination.</param>
        /// <param name="ownerId">The stored owner.</param>
        /// <param name="createdAt">The stored creation time.</param>
        /// <param name="deletedAt">The stored deletion time, or <c>null</c>.</param>
        /// <returns>The link, in its stored state.</returns>
        public static ShortUrl Rehydrate(ShortCode code, LongUrl target, string? ownerId, DateTimeOffset createdAt, DateTimeOffset? deletedAt)
        {
            return Rehydrate(code, target, ownerId, createdAt, deletedAt, null);
        }

        /// <summary>Rebuilds a stored link, including its expiry (TSK-14).</summary>
        /// <param name="code">The stored code (generated or alias).</param>
        /// <param name="target">The stored destination.</param>
        /// <param name="ownerId">The stored owner.</param>
        /// <param name="createdAt">The stored creation time.</param>
        /// <param name="deletedAt">The stored deletion time, or <c>null</c>.</param>
        /// <param name="expiresAt">The stored expiry, or <c>null</c>.</param>
        /// <returns>The link, in its stored state.</returns>
        public static ShortUrl Rehydrate(ShortCode code, LongUrl target, string? ownerId, DateTimeOffset createdAt, DateTimeOffset? deletedAt, DateTimeOffset? expiresAt)
        {
            ArgumentNullException.ThrowIfNull(code);
            ShortUrl shortUrl = Create(code, target, ownerId, createdAt, expiresAt);
            if (deletedAt.HasValue)
            {
                shortUrl.MarkDeleted(deletedAt.Value);
            }

            return shortUrl;
        }

        /// <summary>
        /// The deletion state only: <see cref="LinkState.Deleted"/> once deleted, otherwise <see cref="LinkState.Active"/>.
        /// It ignores expiry, which depends on the time; use <see cref="StateAt"/> to decide how a link answers.
        /// Kept unchanged by TSK-14 so existing callers keep their meaning.
        /// </summary>
        public LinkState State => DeletedAt.HasValue ? LinkState.Deleted : LinkState.Active;

        /// <summary>The state at a point in time: deleted first, then expired (at or after <see cref="ExpiresAt"/>), else active.</summary>
        /// <param name="now">The current time.</param>
        /// <returns>The state.</returns>
        public LinkState StateAt(DateTimeOffset now)
        {
            return ComputeState(DeletedAt, ExpiresAt, now);
        }

        /// <summary>The state rule, shared with read models that carry the same two timestamps.</summary>
        /// <param name="deletedAt">Deletion time, or <c>null</c>.</param>
        /// <param name="expiresAt">Expiry, or <c>null</c>.</param>
        /// <param name="now">The current time.</param>
        /// <returns>The state.</returns>
        public static LinkState ComputeState(DateTimeOffset? deletedAt, DateTimeOffset? expiresAt, DateTimeOffset now)
        {
            if (deletedAt.HasValue)
            {
                return LinkState.Deleted;
            }

            return expiresAt.HasValue && now >= expiresAt.Value ? LinkState.Expired : LinkState.Active;
        }

        /// <summary>
        /// Checks a requested expiry against the creation time: it must be later (<c>expiry.past</c>) and at most
        /// <see cref="MaxExpiryYears"/> years later (<c>expiry.too-far</c>). Public so the create use case can
        /// validate before it touches the database; the factories apply it too.
        /// </summary>
        /// <param name="createdAt">The creation time.</param>
        /// <param name="expiresAt">The requested expiry, or <c>null</c> (always valid).</param>
        public static void ValidateExpiry(DateTimeOffset createdAt, DateTimeOffset? expiresAt)
        {
            if (!expiresAt.HasValue)
            {
                return;
            }

            DateTimeOffset expiry = expiresAt.Value.ToUniversalTime();
            DateTimeOffset created = createdAt.ToUniversalTime();
            if (expiry <= created)
            {
                throw new DomainValidationException(DomainErrorCodes.ExpiryPast, "expiresAt must be in the future.");
            }

            if (expiry > created.AddYears(MaxExpiryYears))
            {
                throw new DomainValidationException(DomainErrorCodes.ExpiryTooFar, "expiresAt may be at most 10 years ahead.");
            }
        }

        /// <summary>Soft-deletes the link. Idempotent: a second call keeps the first deletion time.</summary>
        /// <param name="at">The deletion time; must not be before <see cref="CreatedAt"/>.</param>
        public void MarkDeleted(DateTimeOffset at)
        {
            DateTimeOffset deletedAt = at.ToUniversalTime();
            if (deletedAt < CreatedAt)
            {
                throw new DomainValidationException(DomainErrorCodes.TimeOrder, "A link cannot be deleted before it was created.");
            }

            if (DeletedAt.HasValue)
            {
                return;
            }

            DeletedAt = deletedAt;
        }

        private static ShortUrl Create(ShortCode code, LongUrl target, string? ownerId, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
        {
            ArgumentNullException.ThrowIfNull(target);

            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new DomainValidationException(DomainErrorCodes.OwnerRequired, "An owner is required.");
            }

            if (ownerId.Length > OwnerIdMaxLength)
            {
                throw new DomainValidationException(DomainErrorCodes.OwnerLength, "An owner ID may be at most 64 characters long.");
            }

            ValidateExpiry(createdAt, expiresAt);
            return new ShortUrl(code, target, ownerId, createdAt.ToUniversalTime(), expiresAt?.ToUniversalTime());
        }
    }
}
