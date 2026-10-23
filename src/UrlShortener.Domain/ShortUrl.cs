using System;

namespace UrlShortener.Domain
{
    /// <summary>
    /// A short link: its code, its destination and its lifecycle (LLD §5). State changes only through
    /// <see cref="MarkDeleted"/>. Deletion is a soft delete, the code is never reused (A-25), and deleting
    /// again changes nothing.
    /// </summary>
    public sealed class ShortUrl
    {
        /// <summary>Maximum owner ID length (matches the <c>owner_id varchar(64)</c> column).</summary>
        public const int OwnerIdMaxLength = 64;

        private ShortUrl(ShortCode code, LongUrl target, string ownerId, DateTimeOffset createdAt)
        {
            Code = code;
            Target = target;
            OwnerId = ownerId;
            CreatedAt = createdAt;
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

        /// <summary>Creates a link with a generated code.</summary>
        /// <param name="code">A generated short code.</param>
        /// <param name="target">The destination.</param>
        /// <param name="ownerId">The owner.</param>
        /// <param name="createdAt">The creation time.</param>
        /// <returns>The new, active link.</returns>
        public static ShortUrl CreateGenerated(ShortCode code, LongUrl target, string? ownerId, DateTimeOffset createdAt)
        {
            ArgumentNullException.ThrowIfNull(code);
            if (!code.IsGenerated)
            {
                throw new DomainValidationException(DomainErrorCodes.CodeKind, "A generated link requires a generated code.");
            }

            return Create(code, target, ownerId, createdAt);
        }

        /// <summary>Creates a link with a custom alias.</summary>
        /// <param name="alias">The validated alias.</param>
        /// <param name="target">The destination.</param>
        /// <param name="ownerId">The owner.</param>
        /// <param name="createdAt">The creation time.</param>
        /// <returns>The new, active link.</returns>
        public static ShortUrl CreateWithAlias(CustomAlias alias, LongUrl target, string? ownerId, DateTimeOffset createdAt)
        {
            ArgumentNullException.ThrowIfNull(alias);
            return Create(ShortCode.FromAlias(alias), target, ownerId, createdAt);
        }

        /// <summary>
        /// The current state: <see cref="LinkState.Deleted"/> once deleted, otherwise <see cref="LinkState.Active"/>.
        /// A property for now, because nothing time-dependent exists yet. Expiry (TSK-14) makes state depend on
        /// the current time, which turns this into <c>GetState(now)</c>.
        /// </summary>
        public LinkState State => DeletedAt.HasValue ? LinkState.Deleted : LinkState.Active;

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

        private static ShortUrl Create(ShortCode code, LongUrl target, string? ownerId, DateTimeOffset createdAt)
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

            return new ShortUrl(code, target, ownerId, createdAt.ToUniversalTime());
        }
    }
}
