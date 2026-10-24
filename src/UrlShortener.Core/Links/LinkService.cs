using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using UrlShortener.Core.Codes;
using UrlShortener.Core.Database;
using UrlShortener.Domain;

namespace UrlShortener.Core.Links
{
    /// <summary>
    /// The link use cases of the Api: create (FR-01, FR-02, FR-04, FR-08, FR-10), metadata (FR-06) and delete (FR-05),
    /// each owner-scoped (FR-09). All of them run on the <b>primary</b> through the <c>links-primary</c> guard: a breaker
    /// plus one retry, each attempt with a fresh <see cref="LinksDbContext"/>.
    /// </summary>
    public sealed partial class LinkService
    {
        /// <summary>Attempts for a generated code. A conflict is a defect: codes are unique by construction.</summary>
        public const int MaxGeneratedAttempts = 3;

        private const string UniqueViolation = "23505";
        private const string GeneratedCodeSavepoint = "generated_code";

        private static readonly JsonSerializerOptions BodyJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly IDbContextFactory<LinksDbContext> _contexts;
        private readonly CodeAllocator _codes;
        private readonly DatabaseGuard _guard;
        private readonly Uri _publicBaseUrl;
        private readonly TimeProvider _time;
        private readonly ILogger<LinkService> _logger;

        /// <summary>Creates the service.</summary>
        /// <param name="contexts">Creates a <see cref="LinksDbContext"/> on the primary per attempt.</param>
        /// <param name="codes">Generated codes.</param>
        /// <param name="guard">Breaker and retry rule for the primary.</param>
        /// <param name="publicBaseUrl">Base of the short links, ending with <c>/</c>.</param>
        /// <param name="time">Clock.</param>
        /// <param name="logger">Logger. Never receives long URLs.</param>
        public LinkService(IDbContextFactory<LinksDbContext> contexts, CodeAllocator codes, DatabaseGuard guard, Uri publicBaseUrl, TimeProvider time, ILogger<LinkService> logger)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _codes = codes ?? throw new ArgumentNullException(nameof(codes));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _publicBaseUrl = publicBaseUrl ?? throw new ArgumentNullException(nameof(publicBaseUrl));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a link, or replays a completed request. Validate → hash → <b>one transaction</b>: claim the
        /// idempotency key, insert the link, store the response, commit. Anything other than a new link rolls back, so a
        /// <c>409</c> leaves no key behind. A failed <c>COMMIT</c> is retried only with an idempotency key (LLD §6.7).
        /// </summary>
        /// <param name="command">The request.</param>
        /// <param name="cancellationToken">The request's token.</param>
        /// <returns>The outcome. Invalid input throws <see cref="DomainValidationException"/> before any database call.</returns>
        public Task<CreateResult> CreateAsync(CreateLinkCommand command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            LongUrl longUrl = LongUrl.Create(command.Destination);
            if (string.Equals(longUrl.Uri.IdnHost, _publicBaseUrl.IdnHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainValidationException(ErrorCodes.UrlSelfLink, "A URL must not point to this short-link service.");
            }

            CustomAlias? alias = command.CustomAlias == null ? null : CustomAlias.Create(command.CustomAlias);
            DateTimeOffset now = _time.GetUtcNow();
            ShortUrl.ValidateExpiry(now, command.ExpiresAt);
            string requestHash = IdempotencyRules.RequestHash(longUrl, command.CustomAlias, command.ExpiresAt);

            return _guard.ExecuteWithRetryAsync(
                "create",
                token => CreateOnceAsync(command, longUrl, alias, now, requestHash, token),
                retryAfterUnknownCommit: command.IdempotencyKey != null,
                cancellationToken);
        }

        /// <summary>The caller's link, or <c>null</c> if it doesn't exist <b>or is someone else's</b> (no existence leak).</summary>
        /// <param name="ownerId">The authenticated caller.</param>
        /// <param name="code">The path segment, unvalidated.</param>
        /// <param name="cancellationToken">The request's token.</param>
        /// <returns>The details, or <c>null</c>.</returns>
        public Task<ShortUrlDetails?> GetAsync(string ownerId, string? code, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ownerId);
            return _guard.ExecuteWithRetryAsync(
                "get",
                async token =>
                {
                    await using LinksDbContext db = await _contexts.CreateDbContextAsync(token);
                    ShortUrl? link = await FindOwnedAsync(db, ownerId, code, token);
                    return link == null
                        ? null
                        : new ShortUrlDetails(link.Code.Value, ShortLink(link.Code), link.Target.Uri, link.IsCustom, link.CreatedAt, link.DeletedAt, link.ExpiresAt);
                },
                retryAfterUnknownCommit: true,
                cancellationToken);
        }

        /// <summary>Soft-deletes the caller's link. Idempotent: deleting again succeeds and keeps the first deletion time.</summary>
        /// <param name="ownerId">The authenticated caller.</param>
        /// <param name="code">The path segment, unvalidated.</param>
        /// <param name="cancellationToken">The request's token.</param>
        /// <returns><c>true</c> if the link is (now) deleted; <c>false</c> if it doesn't exist or isn't the caller's.</returns>
        public Task<bool> DeleteAsync(string ownerId, string? code, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ownerId);
            return _guard.ExecuteWithRetryAsync(
                "delete",
                async token =>
                {
                    await using LinksDbContext db = await _contexts.CreateDbContextAsync(token);
                    ShortUrl? link = await FindOwnedAsync(db, ownerId, code, token);
                    if (link == null)
                    {
                        return false;
                    }

                    if (link.DeletedAt.HasValue)
                    {
                        return true;
                    }

                    link.MarkDeleted(_time.GetUtcNow());
                    DateTimeOffset deletedAt = link.DeletedAt!.Value;
                    string value = link.Code.Value;

                    // "AND deleted_at IS NULL" keeps the first deletion time if two deletes race (A-25).
                    await db.ShortUrls
                        .Where(x => x.Code == value && x.DeletedAt == null)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DeletedAt, deletedAt), token);
                    return true;
                },
                retryAfterUnknownCommit: true,
                cancellationToken);
        }

        /// <summary>
        /// Row → domain. Loading goes through the validated factories, so a row that breaks a domain rule becomes a
        /// <see cref="StoredDataInvalidException"/> (a <c>500</c>), never a client error (TSK-QW #7).
        /// </summary>
        /// <param name="record">The stored row.</param>
        /// <returns>The domain link.</returns>
        public static ShortUrl ToDomain(ShortUrlRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            try
            {
                ShortCode code = record.IsCustom ? ShortCode.FromAlias(CustomAlias.Create(record.Code)) : ShortCode.FromGenerated(record.Code);
                return ShortUrl.Rehydrate(code, LongUrl.Create(record.Target), record.OwnerId, record.CreatedAt, record.DeletedAt, record.ExpiresAt);
            }
            catch (DomainValidationException exception)
            {
                throw new StoredDataInvalidException($"Stored link '{record.Code}' breaks the domain rule '{exception.Code}'.", exception);
            }
        }

        private static ShortUrlRecord ToRecord(ShortUrl link)
        {
            return new ShortUrlRecord
            {
                Code = link.Code.Value,
                Target = link.Target.Value,
                OwnerId = link.OwnerId,
                IsCustom = link.IsCustom,
                CreatedAt = link.CreatedAt,
                DeletedAt = link.DeletedAt,
                ExpiresAt = link.ExpiresAt,
            };
        }

        private static async Task<ShortUrl?> FindOwnedAsync(LinksDbContext db, string ownerId, string? code, CancellationToken cancellationToken)
        {
            if (!ShortCode.TryParse(code, out ShortCode? parsed))
            {
                return null;
            }

            ShortUrlRecord? record = await db.ShortUrls.AsNoTracking().SingleOrDefaultAsync(x => x.Code == parsed.Value, cancellationToken);
            if (record == null || !string.Equals(record.OwnerId, ownerId, StringComparison.Ordinal))
            {
                return null;
            }

            return ToDomain(record);
        }

        private static async Task<bool> TryInsertAsync(LinksDbContext db, ShortUrl link, CancellationToken cancellationToken)
        {
            ShortUrlRecord record = ToRecord(link);
            db.ShortUrls.Add(record);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
            {
                db.Entry(record).State = EntityState.Detached; // so the context stays usable
                return false;
            }
        }

        private static async Task CommitAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
        {
            try
            {
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (TransientDatabaseErrors.IsTransient(exception))
            {
                // COMMIT was sent; the server may have applied it. The retry rule decides what happens next.
                throw new CommitOutcomeUnknownException("The commit failed; whether it took effect is unknown.", exception);
            }
        }

        private async Task<CreateResult> CreateOnceAsync(CreateLinkCommand command, LongUrl longUrl, CustomAlias? alias, DateTimeOffset now, string requestHash, CancellationToken cancellationToken)
        {
            await using LinksDbContext db = await _contexts.CreateDbContextAsync(cancellationToken);
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            if (command.IdempotencyKey != null)
            {
                CreateResult? earlier = await ClaimKeyAsync(db, command.OwnerId, command.IdempotencyKey, requestHash, now, cancellationToken);
                if (earlier != null)
                {
                    return earlier; // disposing rolls back; nothing was written
                }
            }

            ShortUrl? link;
            if (alias != null)
            {
                // The primary key is the guard: no read first, so concurrent requests for one alias give one 201 and 409s.
                link = ShortUrl.CreateWithAlias(alias, longUrl, command.OwnerId, now, command.ExpiresAt);
                if (!await TryInsertAsync(db, link, cancellationToken))
                {
                    return new CreateResult(CreateOutcome.AliasTaken, null); // rolls back, including the key row
                }
            }
            else
            {
                link = await InsertGeneratedAsync(db, transaction, longUrl, command.OwnerId, now, command.ExpiresAt, cancellationToken);
            }

            CreatedShortUrl body = new CreatedShortUrl(link.Code.Value, ShortLink(link.Code), longUrl.Uri, link.CreatedAt, link.ExpiresAt);
            if (command.IdempotencyKey != null)
            {
                string json = JsonSerializer.Serialize(body, BodyJson);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE idempotency_keys SET response_body = CAST({json} AS jsonb) WHERE owner_id = {command.OwnerId} AND idem_key = {command.IdempotencyKey}",
                    cancellationToken);
            }

            await CommitAsync(transaction, cancellationToken);
            return new CreateResult(CreateOutcome.Created, body);
        }

        /// <summary>
        /// Claims the key inside the transaction (ADR-0010). <c>ON CONFLICT DO NOTHING</c>: a concurrent request with the
        /// same key waits on the primary key until the first transaction ends. A key older than 24 hours is taken over.
        /// </summary>
        /// <returns><c>null</c> to go on creating; otherwise the replay or mismatch result.</returns>
        private static async Task<CreateResult?> ClaimKeyAsync(LinksDbContext db, string ownerId, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            int inserted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO idempotency_keys (owner_id, idem_key, request_hash, response_body, created_at) VALUES ({ownerId}, {key}, {requestHash}, NULL, {now}) ON CONFLICT DO NOTHING",
                cancellationToken);
            if (inserted == 1)
            {
                return null;
            }

            // ToListAsync doesn't compose around the SQL, so FOR UPDATE stays at the top level.
            List<IdempotencyKeyRecord> rows = await db.IdempotencyKeys
                .FromSql($"SELECT * FROM idempotency_keys WHERE owner_id = {ownerId} AND idem_key = {key} FOR UPDATE")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            IdempotencyKeyRecord stored = rows.Single();

            if (stored.CreatedAt < now - IdempotencyRules.Window)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE idempotency_keys SET request_hash = {requestHash}, response_body = NULL, created_at = {now} WHERE owner_id = {ownerId} AND idem_key = {key}",
                    cancellationToken);
                return null;
            }

            if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new CreateResult(CreateOutcome.KeyReused, null);
            }

            CreatedShortUrl? body = stored.ResponseBody == null ? null : JsonSerializer.Deserialize<CreatedShortUrl>(stored.ResponseBody, BodyJson);
            return body == null
                ? throw new InvalidOperationException("A committed idempotency key has no stored response.")
                : new CreateResult(CreateOutcome.Replayed, body);
        }

        // PostgreSQL aborts the whole transaction after a failed statement; the savepoint keeps it usable for the next code.
        private async Task<ShortUrl> InsertGeneratedAsync(LinksDbContext db, IDbContextTransaction transaction, LongUrl longUrl, string ownerId, DateTimeOffset now, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxGeneratedAttempts; attempt++)
            {
                ShortCode code = await _codes.NextAsync(cancellationToken);
                ShortUrl link = ShortUrl.CreateGenerated(code, longUrl, ownerId, now, expiresAt);

                await transaction.CreateSavepointAsync(GeneratedCodeSavepoint, cancellationToken);
                if (await TryInsertAsync(db, link, cancellationToken))
                {
                    return link;
                }

                await transaction.RollbackToSavepointAsync(GeneratedCodeSavepoint, cancellationToken);
                LogGeneratedCodeConflict(_logger, code.Value, attempt);
            }

            throw new InvalidOperationException("Generated short codes collided repeatedly; check the ID blocks and the Feistel key.");
        }

        private Uri ShortLink(ShortCode code)
        {
            return new Uri(_publicBaseUrl, code.Value);
        }

        [LoggerMessage(EventId = 4000, Level = LogLevel.Error, Message = "DEFECT: generated code {Code} already exists (attempt {Attempt}). Check id_blocks and the Feistel key (ADR-0005).")]
        private static partial void LogGeneratedCodeConflict(ILogger logger, string code, int attempt);
    }
}
