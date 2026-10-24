using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Polly.CircuitBreaker;
using UrlShortener.Core.Database;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Core.Database
{
    /// <summary>
    /// TSK-11 AC1/AC2 (kept): which failures are transient, the retry rule (before vs during commit, with and without a
    /// key), the breaker's states on a fake clock, and connection-string defaults.
    /// </summary>
    public sealed class DatabaseGuardTests
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

            DatabaseGuard.MayRetry(beforeCommit, retryAfterUnknownCommit: false, CancellationToken.None).Should().BeTrue("PostgreSQL has rolled back");
            DatabaseGuard.MayRetry(duringCommit, retryAfterUnknownCommit: true, CancellationToken.None).Should().BeTrue("with a key the retry replays or creates, never duplicates");
            DatabaseGuard.MayRetry(duringCommit, retryAfterUnknownCommit: false, CancellationToken.None).Should().BeFalse("without a key a retry could create a second link");
            DatabaseGuard.MayRetry(new BrokenCircuitException(), true, CancellationToken.None).Should().BeFalse("the dependency is known to be down");
            DatabaseGuard.MayRetry(new OperationCanceledException(), true, CancellationToken.None).Should().BeFalse("the client went away");
            DatabaseGuard.MayRetry(new DomainValidationException("url.scheme", "bad"), true, CancellationToken.None).Should().BeFalse("4xx answers don't change");
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            DatabaseGuard.MayRetry(beforeCommit, true, cancelled.Token).Should().BeFalse();
        }

        [Fact]
        public async Task Retries_once_then_gives_up()
        {
            DatabaseGuard guard = NewGuard();
            int calls = 0;

            Func<Task> alwaysFails = () => guard.ExecuteWithRetryAsync<int>(
                "test", _ => { calls++; throw new NpgsqlException("x", new TimeoutException()); }, false, CancellationToken.None);

            await alwaysFails.Should().ThrowAsync<NpgsqlException>();
            calls.Should().Be(2, "one attempt plus exactly one retry");
        }

        [Fact]
        public async Task Succeeds_on_the_retry()
        {
            DatabaseGuard guard = NewGuard();
            int calls = 0;

            int result = await guard.ExecuteWithRetryAsync(
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
        public async Task An_unknown_commit_is_retried_only_with_a_key()
        {
            DatabaseGuard guard = NewGuard();
            int withoutKey = 0;
            int withKey = 0;

            Func<Task> noKey = () => guard.ExecuteWithRetryAsync<int>(
                "test", _ => { withoutKey++; throw new CommitOutcomeUnknownException("c", new TimeoutException()); }, false, CancellationToken.None);
            int replayed = await guard.ExecuteWithRetryAsync(
                "test", _ => ++withKey == 1 ? throw new CommitOutcomeUnknownException("c", new TimeoutException()) : Task.FromResult(7), true, CancellationToken.None);

            await noKey.Should().ThrowAsync<CommitOutcomeUnknownException>();
            withoutKey.Should().Be(1);
            replayed.Should().Be(7);
            withKey.Should().Be(2);
        }

        [Fact]
        public async Task Breaker_opens_rejects_fast_half_opens_and_closes()
        {
            TestTime time = new TestTime();
            ResilienceOptions options = new ResilienceOptions { BreakerMinimumThroughput = 4, BreakerBreakJitter = TimeSpan.Zero };
            int cleared = 0;
            DatabaseGuard counting = new DatabaseGuard("unit-" + Guid.NewGuid().ToString("N"), options, time, () => cleared++, NullLogger.Instance);
            DatabaseGuard other = new DatabaseGuard("unit-" + Guid.NewGuid().ToString("N"), options, time, null, NullLogger.Instance);

            for (int i = 0; i < 4; i++)
            {
                Func<Task> failing = () => counting.ExecuteAsync<int>(_ => throw new NpgsqlException("x", new TimeoutException()), CancellationToken.None);
                await failing.Should().ThrowAsync<NpgsqlException>();
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

            for (int i = 0; i < 10; i++)
            {
                Func<Task> invalid = () => other.ExecuteAsync<int>(_ => throw new DomainValidationException("x", "y"), CancellationToken.None);
                await invalid.Should().ThrowAsync<DomainValidationException>();
            }

            CircuitStateMetrics.Get(other.Dependency).Should().Be(CircuitState.Closed, "non-transient failures never open it");
        }

        [Fact]
        public void Connection_defaults_are_added_unless_the_connection_string_sets_them()
        {
            ResilienceOptions options = new ResilienceOptions();

            NpgsqlConnectionStringBuilder plain = new NpgsqlConnectionStringBuilder(
                ConnectionStrings.ApplyDefaults("Host=db;Database=links", options, 30, withStatementTimeout: true));
            NpgsqlConnectionStringBuilder explicitValues = new NpgsqlConnectionStringBuilder(ConnectionStrings.ApplyDefaults(
                "Host=db;Timeout=9;Command Timeout=8;Maximum Pool Size=7;Options=-c statement_timeout=7000", options, 30, withStatementTimeout: true));
            NpgsqlConnectionStringBuilder replica = new NpgsqlConnectionStringBuilder(
                ConnectionStrings.ApplyDefaults("Host=db", options, 10, withStatementTimeout: false));
            Action noPool = () => ConnectionStrings.ApplyDefaults("Host=db", options, 0, false);

            plain.Timeout.Should().Be(2);
            plain.CommandTimeout.Should().Be(1);
            plain.MaxPoolSize.Should().Be(30, "uncapped pools exhausted max_connections under load (TSK-12)");
            plain.Options.Should().Be("-c statement_timeout=2000");
            explicitValues.Timeout.Should().Be(9);
            explicitValues.CommandTimeout.Should().Be(8);
            explicitValues.MaxPoolSize.Should().Be(7);
            explicitValues.Options.Should().Be("-c statement_timeout=7000");
            replica.MaxPoolSize.Should().Be(10);
            replica.Options.Should().BeNullOrEmpty();
            noPool.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void Default_pool_caps_leave_headroom_under_max_connections()
        {
            // One Api, one Redirect and one worker instance stay well below PostgreSQL's default 100 connections.
            ResilienceOptions options = new ResilienceOptions();

            (options.ApiPrimaryMaxPoolSize + options.AnalyticsMaxPoolSize + options.RedirectReplicaMaxPoolSize
                + options.RedirectPrimaryMaxPoolSize + options.AnalyticsMaxPoolSize).Should().BeLessThanOrEqualTo(90);
        }

        private static DatabaseGuard NewGuard()
        {
            return new DatabaseGuard("unit-" + Guid.NewGuid().ToString("N"), NoDelay, TimeProvider.System, null, NullLogger.Instance);
        }

        private static PostgresException Postgres(string sqlState)
        {
            return new PostgresException("test", "ERROR", "ERROR", sqlState);
        }
    }
}
