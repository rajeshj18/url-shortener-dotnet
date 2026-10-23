using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Application.ShortUrls;

namespace UrlShortener.Infrastructure.Resilience
{
    /// <summary>Service key of the undecorated use cases, which the decorators resolve in a fresh scope per attempt.</summary>
    public static class ResilienceKeys
    {
        /// <summary>The key.</summary>
        public const string Inner = "inner";
    }

    /// <summary>
    /// Create with the retry rule (LLD §6.7): a failure before <c>COMMIT</c> is retried once; a failure during
    /// <c>COMMIT</c> only with an <c>Idempotency-Key</c>, because without one a retry could create a second link.
    /// </summary>
    public sealed class ResilientCreateShortUrlHandler : ICreateShortUrlHandler
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly PrimaryDatabaseResilience _resilience;

        /// <summary>Creates the decorator.</summary>
        /// <param name="scopes">Creates a scope (and a fresh <c>DbContext</c>) per attempt.</param>
        /// <param name="resilience">Breaker and retry rule.</param>
        public ResilientCreateShortUrlHandler(IServiceScopeFactory scopes, PrimaryDatabaseResilience resilience)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        }

        /// <inheritdoc />
        public Task<CreateShortUrlResult> HandleAsync(CreateShortUrlCommand command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            return _resilience.ExecuteAsync(
                "create",
                async token =>
                {
                    await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
                    return await scope.ServiceProvider.GetRequiredKeyedService<ICreateShortUrlHandler>(ResilienceKeys.Inner).HandleAsync(command, token);
                },
                retryAfterUnknownCommit: command.IdempotencyKey != null,
                cancellationToken);
        }
    }

    /// <summary>Delete with one retry: a soft delete is naturally idempotent.</summary>
    public sealed class ResilientDeleteShortUrlHandler : IDeleteShortUrlHandler
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly PrimaryDatabaseResilience _resilience;

        /// <summary>Creates the decorator.</summary>
        /// <param name="scopes">Creates a scope per attempt.</param>
        /// <param name="resilience">Breaker and retry rule.</param>
        public ResilientDeleteShortUrlHandler(IServiceScopeFactory scopes, PrimaryDatabaseResilience resilience)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string ownerId, string? code, CancellationToken cancellationToken)
        {
            return _resilience.ExecuteAsync(
                "delete",
                async token =>
                {
                    await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
                    return await scope.ServiceProvider.GetRequiredKeyedService<IDeleteShortUrlHandler>(ResilienceKeys.Inner).DeleteAsync(ownerId, code, token);
                },
                retryAfterUnknownCommit: true,
                cancellationToken);
        }
    }

    /// <summary>Metadata reads through the breaker, with one retry (reads change nothing).</summary>
    public sealed class ResilientGetShortUrlHandler : IGetShortUrlHandler
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly PrimaryDatabaseResilience _resilience;

        /// <summary>Creates the decorator.</summary>
        /// <param name="scopes">Creates a scope per attempt.</param>
        /// <param name="resilience">Breaker and retry rule.</param>
        public ResilientGetShortUrlHandler(IServiceScopeFactory scopes, PrimaryDatabaseResilience resilience)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        }

        /// <inheritdoc />
        public Task<ShortUrlDetails?> GetAsync(string ownerId, string? code, CancellationToken cancellationToken)
        {
            return _resilience.ExecuteAsync(
                "get",
                async token =>
                {
                    await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
                    return await scope.ServiceProvider.GetRequiredKeyedService<IGetShortUrlHandler>(ResilienceKeys.Inner).GetAsync(ownerId, code, token);
                },
                retryAfterUnknownCommit: true,
                cancellationToken);
        }
    }
}
