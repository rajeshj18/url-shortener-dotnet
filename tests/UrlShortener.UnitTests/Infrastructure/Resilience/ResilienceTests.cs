using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Polly.CircuitBreaker;
using UrlShortener.Application.Ports;
using UrlShortener.Application.ShortUrls;
using UrlShortener.Domain;
using UrlShortener.Infrastructure.Resilience;
using UrlShortener.Infrastructure.Web;
using UrlShortener.UnitTests.Application;
using Xunit;

namespace UrlShortener.UnitTests.Infrastructure.Resilience
{
    /// <summary>
    /// TSK-11 AC1/AC2 and the building blocks: which failures are transient, the retry rule (before vs during
    /// commit, with and without a key), the breaker's states on a fake clock, decorators, connection timeouts, 503s.
    /// </summary>
    public sealed class ResilienceTests
    {
        private static readonly ResilienceOptions NoDelay = new ResilienceOptions { RetryDelay = TimeSpan.Zero };

        [Theory]
        [InlineData("57P01")] // admin_shutdown
        [InlineData("57P03")] // cannot_connect_now (in recovery)
        [InlineData("40001")] // serialization_failure
        [InlineData("40P01")] // deadlock
        [InlineData("57014")] // statement_timeout
        [InlineData("08006")] // connection failure
        public void Postgres_errors_that_may_pass_are_transient(string sqlState)
        {
            TransientDatabaseErrors.IsTransient(Postgres(sqlState)).Should().BeTrue();
        }

        [Theory]
        [InlineData("23505")] // unique_violation: a conflict, never retried
        [InlineData("42501")] // insufficient_privilege: a bug or misconfiguration
        [InlineData("42P01")] // undefined_table
        public void Postgres_errors_that_wont_pass_are_not_transient(string sqlState)
        {
            TransientDatabaseErrors.IsTransient(Postgres(sqlState)).Should().BeFalse();
        }

        [Fact]
        public void Driver_timeouts_and_wrapped_failures_are_transient_others_are_not()
        {
            TransientDatabaseErrors.IsTransient(new NpgsqlException("x", new TimeoutException())).Should().BeTrue();
            TransientDatabaseErrors.IsTransient(new NpgsqlException("x", new IOException("reset"))).Should().BeTrue();
            TransientDatabaseErrors.IsTransient(new NpgsqlException("x", new SocketException(10061))).Should().BeTrue();
            TransientDatabaseErrors.IsTransient(new DbUpdateException("save", Postgres("40P01"))).Should().BeTrue();
            TransientDatabaseErrors.IsTransient(new CommitOutcomeUnknownException("c", new TimeoutException())).Should().BeTrue();
            TransientDatabaseErrors.IsTransient(new DomainValidationException("url.scheme", "bad")).Should().BeFalse();
            TransientDatabaseErrors.IsTransient(new InvalidOperationException("bug")).Should().BeFalse();
            TransientDatabaseErrors.IsTransient(null).Should().BeFalse();
        }

        [Fact]
        public void Retry_rule_follows_the_commit_phase_and_the_key()
        {
            Exception beforeCommit = new NpgsqlException("x", new TimeoutException());
            Exception duringCommit = new CommitOutcomeUnknownException("c", new TimeoutException());

            PrimaryDatabaseResilience.MayRetry(beforeCommit, retryAfterUnknownCommit: false, CancellationToken.None).Should().BeTrue("PostgreSQL has rolled back");
            PrimaryDatabaseResilience.MayRetry(duringCommit, retryAfterUnknownCommit: true, CancellationToken.None).Should().BeTrue("with a key the retry replays or creates, never duplicates");
            PrimaryDatabaseResilience.MayRetry(duringCommit, retryAfterUnknownCommit: false, CancellationToken.None).Should().BeFalse("without a key a retry could create a second link");
            PrimaryDatabaseResilience.MayRetry(new BrokenCircuitException(), true, CancellationToken.None).Should().BeFalse("the dependency is known to be down");
            PrimaryDatabaseResilience.MayRetry(new OperationCanceledException(), true, CancellationToken.None).Should().BeFalse("the client went away");
            PrimaryDatabaseResilience.MayRetry(new DomainValidationException("url.scheme", "bad"), true, CancellationToken.None).Should().BeFalse("4xx answers don't change");
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            PrimaryDatabaseResilience.MayRetry(beforeCommit, true, cancelled.Token).Should().BeFalse();
        }

        [Fact]
        public async Task Executor_retries_once_then_gives_up()
        {
            PrimaryDatabaseResilience resilience = NewExecutor();
            int calls = 0;

            Func<Task> alwaysFails = () => resilience.ExecuteAsync<int>(
                "test", _ => { calls++; throw new NpgsqlException("x", new TimeoutException()); }, false, CancellationToken.None);

            await alwaysFails.Should().ThrowAsync<NpgsqlException>();
            calls.Should().Be(2, "one attempt plus exactly one retry");
        }

        [Fact]
        public async Task Executor_succeeds_on_the_retry()
        {
            PrimaryDatabaseResilience resilience = NewExecutor();
            int calls = 0;

            int result = await resilience.ExecuteAsync(
                "test",
                _ =>
                {
                    calls++;
                    return calls == 1 ? throw Postgres("57P01") : Task.FromResult(42);
                },
                false,
                CancellationToken.None);

            result.Should().Be(42);
            calls.Should().Be(2);
        }

        [Fact]
        public async Task Executor_does_not_retry_an_unknown_commit_without_a_key()
        {
            PrimaryDatabaseResilience resilience = NewExecutor();
            int calls = 0;

            Func<Task> act = () => resilience.ExecuteAsync<int>(
                "test", _ => { calls++; throw new CommitOutcomeUnknownException("c", new TimeoutException()); }, false, CancellationToken.None);

            await act.Should().ThrowAsync<CommitOutcomeUnknownException>();
            calls.Should().Be(1);
        }

        [Fact]
        public async Task Breaker_opens_rejects_fast_half_opens_and_closes()
        {
            FakeTime time = new FakeTime();
            ResilienceOptions options = new ResilienceOptions { BreakerMinimumThroughput = 4, BreakerBreakJitter = TimeSpan.Zero };
            DatabaseCircuitBreaker breaker = new DatabaseCircuitBreaker("unit-" + Guid.NewGuid().ToString("N"), options, time, null);
            int cleared = 0;
            DatabaseCircuitBreaker counting = new DatabaseCircuitBreaker("unit-" + Guid.NewGuid().ToString("N"), options, time, () => cleared++);

            for (int i = 0; i < 4; i++)
            {
                await Call(counting, fail: true).Should().ThrowAsync<NpgsqlException>();
            }

            CircuitStateMetrics.Get(counting.Dependency).Should().Be(CircuitState.Open);
            cleared.Should().Be(1, "the pool is cleared once when the breaker opens");
            int invoked = 0;
            Func<Task> whileOpen = () => counting.ExecuteAsync(_ => { invoked++; return Task.FromResult(1); }, CancellationToken.None);
            await whileOpen.Should().ThrowAsync<BrokenCircuitException>();
            invoked.Should().Be(0, "an open breaker rejects without touching the database");

            time.Now = time.Now.AddSeconds(6);
            (await counting.ExecuteAsync(_ => Task.FromResult(7), CancellationToken.None)).Should().Be(7, "the trial call after the break");
            CircuitStateMetrics.Get(counting.Dependency).Should().Be(CircuitState.Closed);

            // Non-transient failures never open it.
            for (int i = 0; i < 10; i++)
            {
                Func<Task> invalid = () => breaker.ExecuteAsync<int>(_ => throw new DomainValidationException("x", "y"), CancellationToken.None);
                await invalid.Should().ThrowAsync<DomainValidationException>();
            }

            CircuitStateMetrics.Get(breaker.Dependency).Should().Be(CircuitState.Closed);
        }

        [Fact]
        public async Task Create_decorator_uses_a_fresh_scope_per_attempt_and_the_key_rule()
        {
            int scopesCreated = 0;
            ServiceCollection services = new ServiceCollection();
            FlakyCreate flaky = new FlakyCreate();
            services.AddKeyedScoped<ICreateShortUrlHandler>(ResilienceKeys.Inner, (_, _) =>
            {
                scopesCreated++;
                return flaky;
            });
            using ServiceProvider provider = services.BuildServiceProvider();
            ResilientCreateShortUrlHandler handler = new ResilientCreateShortUrlHandler(provider.GetRequiredService<IServiceScopeFactory>(), NewExecutor());

            flaky.FailuresLeft = 1;
            CreateShortUrlResult withKey = await handler.HandleAsync(new CreateShortUrlCommand("o", "https://example.com/", null, null, "key"), CancellationToken.None);
            flaky.FailuresLeft = 1;
            Func<Task> withoutKey = () => handler.HandleAsync(new CreateShortUrlCommand("o", "https://example.com/", null, null, null), CancellationToken.None);

            withKey.Outcome.Should().Be(CreateShortUrlOutcome.KeyReused);
            scopesCreated.Should().Be(2, "the retry ran in a new scope, with a new DbContext");
            await withoutKey.Should().ThrowAsync<CommitOutcomeUnknownException>();
        }

        [Fact]
        public void Database_timeouts_are_added_unless_the_connection_string_sets_them()
        {
            ResilienceOptions options = new ResilienceOptions();

            NpgsqlConnectionStringBuilder plain = new NpgsqlConnectionStringBuilder(
                ResilienceConfiguration.ApplyDatabaseTimeouts("Host=db;Database=links", options, withStatementTimeout: true));
            NpgsqlConnectionStringBuilder explicitValues = new NpgsqlConnectionStringBuilder(
                ResilienceConfiguration.ApplyDatabaseTimeouts("Host=db;Timeout=9;Command Timeout=8;Options=-c statement_timeout=7000", options, withStatementTimeout: true));
            NpgsqlConnectionStringBuilder replica = new NpgsqlConnectionStringBuilder(
                ResilienceConfiguration.ApplyDatabaseTimeouts("Host=db", options, withStatementTimeout: false));

            plain.Timeout.Should().Be(2);
            plain.CommandTimeout.Should().Be(1);
            plain.Options.Should().Be("-c statement_timeout=2000");
            explicitValues.Timeout.Should().Be(9);
            explicitValues.CommandTimeout.Should().Be(8);
            explicitValues.Options.Should().Be("-c statement_timeout=7000");
            replica.Options.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task Unavailable_database_and_open_breaker_become_503_with_retry_after()
        {
            DatabaseUnavailableExceptionHandler handler = new DatabaseUnavailableExceptionHandler(1, NullLogger<DatabaseUnavailableExceptionHandler>.Instance);
            DefaultHttpContext transient = Context();
            DefaultHttpContext open = Context();
            DefaultHttpContext bug = Context();

            (await handler.TryHandleAsync(transient, Postgres("57P01"), CancellationToken.None)).Should().BeTrue();
            (await handler.TryHandleAsync(open, new BrokenCircuitException(), CancellationToken.None)).Should().BeTrue();
            (await handler.TryHandleAsync(bug, new InvalidOperationException("bug"), CancellationToken.None)).Should().BeFalse();

            transient.Response.StatusCode.Should().Be(503);
            open.Response.Headers.RetryAfter.ToString().Should().Be("1");
            Action invalid = () => _ = new DatabaseUnavailableExceptionHandler(0, NullLogger<DatabaseUnavailableExceptionHandler>.Instance);
            invalid.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static PrimaryDatabaseResilience NewExecutor()
        {
            DatabaseCircuitBreaker breaker = new DatabaseCircuitBreaker("unit-" + Guid.NewGuid().ToString("N"), NoDelay, TimeProvider.System, null);
            return new PrimaryDatabaseResilience(breaker, NoDelay, NullLogger<PrimaryDatabaseResilience>.Instance);
        }

        private static Func<Task> Call(DatabaseCircuitBreaker breaker, bool fail)
        {
            return () => breaker.ExecuteAsync(_ => fail ? throw new NpgsqlException("x", new TimeoutException()) : Task.FromResult(1), CancellationToken.None);
        }

        private static PostgresException Postgres(string sqlState)
        {
            return new PostgresException("test", "ERROR", "ERROR", sqlState);
        }

        private static DefaultHttpContext Context()
        {
            DefaultHttpContext context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            return context;
        }

        /// <summary>Fails with an unknown commit outcome, then answers.</summary>
        private sealed class FlakyCreate : ICreateShortUrlHandler
        {
            public int FailuresLeft { get; set; }

            public Task<CreateShortUrlResult> HandleAsync(CreateShortUrlCommand command, CancellationToken cancellationToken)
            {
                if (FailuresLeft > 0)
                {
                    FailuresLeft--;
                    throw new CommitOutcomeUnknownException("c", new TimeoutException());
                }

                return Task.FromResult(CreateShortUrlResult.KeyReused);
            }
        }
    }
}
