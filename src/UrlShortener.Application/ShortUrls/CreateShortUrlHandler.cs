using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;

namespace UrlShortener.Application.ShortUrls
{
    /// <summary>
    /// The create use case (LLD §6.1, ADR-0010). Validate → hash → <b>one transaction</b>: claim the idempotency
    /// key, insert the link, store the response, commit. Anything other than a new link rolls back, so a
    /// <c>409</c> leaves no key behind and a retry runs again from the start.
    /// </summary>
    public sealed partial class CreateShortUrlHandler : ICreateShortUrlHandler
    {
        /// <summary>Attempts for a generated code. A conflict is a defect (codes are unique by construction).</summary>
        public const int MaxGeneratedAttempts = 3;

        private const string GeneratedCodeSavepoint = "generated_code";

        private static readonly JsonSerializerOptions BodyJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly IUnitOfWork _unitOfWork;
        private readonly IShortUrlRepository _repository;
        private readonly IShortCodeGenerator _generator;
        private readonly IIdempotencyStore _idempotency;
        private readonly SelfLinkCheck _selfLinkCheck;
        private readonly Uri _publicBaseUrl;
        private readonly TimeProvider _time;
        private readonly ILogger<CreateShortUrlHandler> _logger;

        /// <summary>Creates the handler.</summary>
        /// <param name="unitOfWork">Transactions on the primary.</param>
        /// <param name="repository">The links store.</param>
        /// <param name="generator">Generated codes.</param>
        /// <param name="idempotency">The idempotency-key store.</param>
        /// <param name="selfLinkCheck">The self-link rule.</param>
        /// <param name="publicBaseUrl">Base of the short links (<c>PublicBaseUrl</c>).</param>
        /// <param name="time">Clock.</param>
        /// <param name="logger">Logger. Never receives long URLs.</param>
        public CreateShortUrlHandler(
            IUnitOfWork unitOfWork,
            IShortUrlRepository repository,
            IShortCodeGenerator generator,
            IIdempotencyStore idempotency,
            SelfLinkCheck selfLinkCheck,
            Uri publicBaseUrl,
            TimeProvider time,
            ILogger<CreateShortUrlHandler> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
            _selfLinkCheck = selfLinkCheck ?? throw new ArgumentNullException(nameof(selfLinkCheck));
            _publicBaseUrl = publicBaseUrl ?? throw new ArgumentNullException(nameof(publicBaseUrl));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<CreateShortUrlResult> HandleAsync(CreateShortUrlCommand command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            // 1. Validate everything before touching the database (nothing is stored for a 400).
            if (command.ExpiresAt.HasValue)
            {
                throw new DomainValidationException(ApplicationErrorCodes.ExpiryUnsupported, "Link expiry is not available yet; omit expiresAt.");
            }

            LongUrl longUrl = LongUrl.Create(command.Destination);
            _selfLinkCheck.Ensure(longUrl);
            CustomAlias? alias = command.CustomAlias == null ? null : CustomAlias.Create(command.CustomAlias);
            string requestHash = RequestHash.Compute(longUrl, command.CustomAlias, null);
            DateTimeOffset now = _time.GetUtcNow();

            // 2. One transaction: key, link, response.
            await using IUnitOfWorkTransaction transaction = await _unitOfWork.BeginAsync(cancellationToken);
            if (command.IdempotencyKey != null)
            {
                IdempotencyBeginResult begin = await _idempotency.TryBeginAsync(
                    command.OwnerId, command.IdempotencyKey, requestHash, now, cancellationToken);
                if (begin.Outcome == IdempotencyBeginOutcome.Replay)
                {
                    return CreateShortUrlResult.Replayed(Deserialize(begin.StoredResponseBody!));
                }

                if (begin.Outcome == IdempotencyBeginOutcome.Mismatch)
                {
                    return CreateShortUrlResult.KeyReused;
                }
            }

            ShortUrl? shortUrl = alias != null
                ? await InsertAliasAsync(alias, longUrl, command.OwnerId, now, cancellationToken)
                : await InsertGeneratedAsync(transaction, longUrl, command.OwnerId, now, cancellationToken);
            if (shortUrl == null)
            {
                return CreateShortUrlResult.AliasTaken; // disposing rolls back, including the key row
            }

            CreatedShortUrl body = new CreatedShortUrl(
                shortUrl.Code.Value,
                new Uri(_publicBaseUrl, shortUrl.Code.Value),
                longUrl.Uri, // serialized from OriginalString: exactly as sent
                shortUrl.CreatedAt,
                null);
            if (command.IdempotencyKey != null)
            {
                await _idempotency.CompleteAsync(command.OwnerId, command.IdempotencyKey, JsonSerializer.Serialize(body, BodyJson), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return CreateShortUrlResult.Created(body);
        }

        private static CreatedShortUrl Deserialize(string storedBody)
        {
            return JsonSerializer.Deserialize<CreatedShortUrl>(storedBody, BodyJson)
                ?? throw new InvalidOperationException("A stored idempotent response is empty.");
        }

        // The primary key is the guard: no read before the insert, so concurrent requests for one alias give
        // exactly one 201 and 409s, never a 500 (TSK-08 D2).
        private async Task<ShortUrl?> InsertAliasAsync(CustomAlias alias, LongUrl longUrl, string ownerId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ShortUrl shortUrl = ShortUrl.CreateWithAlias(alias, longUrl, ownerId, now);
            return await _repository.TryInsertAsync(shortUrl, cancellationToken) ? shortUrl : null;
        }

        private async Task<ShortUrl> InsertGeneratedAsync(IUnitOfWorkTransaction transaction, LongUrl longUrl, string ownerId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxGeneratedAttempts; attempt++)
            {
                ShortCode code = await _generator.NextAsync(cancellationToken);
                ShortUrl shortUrl = ShortUrl.CreateGenerated(code, longUrl, ownerId, now);

                await transaction.CreateSavepointAsync(GeneratedCodeSavepoint, cancellationToken);
                if (await _repository.TryInsertAsync(shortUrl, cancellationToken))
                {
                    return shortUrl;
                }

                // Can't happen in normal operation (ADR-0005). Undo the failed insert and use the next code.
                await transaction.RollbackToSavepointAsync(GeneratedCodeSavepoint, cancellationToken);
                LogGeneratedCodeConflict(_logger, code.Value, attempt);
            }

            throw new InvalidOperationException("Generated short codes collided repeatedly; check the ID blocks and the Feistel key.");
        }

        [LoggerMessage(EventId = 4000, Level = LogLevel.Error, Message = "DEFECT: generated code {Code} already exists (attempt {Attempt}). Check id_blocks and the Feistel key (ADR-0005).")]
        private static partial void LogGeneratedCodeConflict(ILogger logger, string code, int attempt);
    }
}
