using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using UrlShortener.Core.Codes;
using UrlShortener.Core.Database;
using UrlShortener.Core.Links;
using UrlShortener.Domain;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Core
{
    /// <summary>
    /// The link use cases against real PostgreSQL, as the Api role (TSK-SIMPLE). These replace the old repository tests
    /// and the handler tests that ran on fake ports: with concrete classes, the database is the honest collaborator.
    /// </summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class LinkServiceTests
    {
        private static readonly FeistelPermutation Permutation = FeistelPermutation.FromPassphrase(ApiFactory.TestFeistelPassphrase);

        private readonly PostgresFixture _db;

        public LinkServiceTests(PostgresFixture db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [Fact]
        public async Task Generated_link_round_trips_every_field()
        {
            using Harness harness = new Harness(_db.ApiConnection);

            CreateResult created = await harness.Links.CreateAsync(Command("client-42", "https://example.com/landing?q=1"), CancellationToken.None);
            ShortUrlDetails? loaded = await harness.Links.GetAsync("client-42", created.Body!.Code, CancellationToken.None);

            created.Outcome.Should().Be(CreateOutcome.Created);
            created.Body.Code.Should().HaveLength(7);
            created.Body.ShortUrl.Should().Be(new Uri("https://sho.rt/" + created.Body.Code));
            loaded.Should().NotBeNull();
            loaded!.LongUrl.OriginalString.Should().Be("https://example.com/landing?q=1");
            loaded.IsCustom.Should().BeFalse();
            loaded.CreatedAt.Should().Be(harness.Time.Now);
            loaded.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
            loaded.DeletedAt.Should().BeNull();
        }

        [Fact]
        public async Task Taken_alias_is_a_conflict_that_keeps_the_owner_and_leaves_no_key()
        {
            using Harness harness = new Harness(_db.ApiConnection);
            string alias = TestCodes.NewAlias().Value;
            string key = Guid.NewGuid().ToString();

            CreateResult first = await harness.Links.CreateAsync(Command("client-1", "https://example.com/a", alias), CancellationToken.None);
            CreateResult second = await harness.Links.CreateAsync(Command("client-2", "https://other.example/", alias, key: key), CancellationToken.None);

            first.Outcome.Should().Be(CreateOutcome.Created);
            second.Outcome.Should().Be(CreateOutcome.AliasTaken);
            (await harness.Links.GetAsync("client-1", alias, CancellationToken.None))!.IsCustom.Should().BeTrue();
            (await ScalarAsync($"SELECT count(*) FROM idempotency_keys WHERE idem_key = '{key}'")).Should().Be(0L, "a 409 rolls back the key too");
        }

        [Fact]
        public async Task Concurrent_creates_of_one_alias_give_exactly_one_link()
        {
            using Harness harness = new Harness(_db.ApiConnection);
            string alias = TestCodes.NewAlias().Value;

            CreateResult[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                harness.Links.CreateAsync(Command("client-" + i, "https://example.com/" + i, alias), CancellationToken.None)));

            results.Count(r => r.Outcome == CreateOutcome.Created).Should().Be(1);
            results.Count(r => r.Outcome == CreateOutcome.AliasTaken).Should().Be(7, "the primary key decides; never a 500");
        }

        [Fact]
        public async Task Same_key_replays_different_request_is_rejected_and_an_expired_key_is_taken_over()
        {
            using Harness harness = new Harness(_db.ApiConnection);
            string key = Guid.NewGuid().ToString();

            CreateResult original = await harness.Links.CreateAsync(Command("client-9", "https://example.com/once", key: key), CancellationToken.None);
            CreateResult replay = await harness.Links.CreateAsync(Command("client-9", "https://example.com/once", key: key), CancellationToken.None);
            CreateResult otherBody = await harness.Links.CreateAsync(Command("client-9", "https://example.com/other", key: key), CancellationToken.None);
            CreateResult otherOwner = await harness.Links.CreateAsync(Command("client-10", "https://example.com/other", key: key), CancellationToken.None);
            harness.Time.Now = harness.Time.Now.AddHours(25);
            CreateResult afterWindow = await harness.Links.CreateAsync(Command("client-9", "https://example.com/other", key: key), CancellationToken.None);

            replay.Outcome.Should().Be(CreateOutcome.Replayed);
            replay.Body.Should().BeEquivalentTo(original.Body, "the stored 201 body comes back as it was");
            otherBody.Outcome.Should().Be(CreateOutcome.KeyReused);
            otherOwner.Outcome.Should().Be(CreateOutcome.Created, "keys are scoped per owner");
            afterWindow.Outcome.Should().Be(CreateOutcome.Created, "after 24 hours the key is free again");
            afterWindow.Body!.Code.Should().NotBe(original.Body!.Code);
        }

        [Fact]
        public async Task Concurrent_retries_with_one_key_create_one_link()
        {
            using Harness harness = new Harness(_db.ApiConnection);
            string key = Guid.NewGuid().ToString();

            CreateResult[] results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
                harness.Links.CreateAsync(Command("client-11", "https://example.com/burst", key: key), CancellationToken.None)));

            results.Count(r => r.Outcome == CreateOutcome.Created).Should().Be(1);
            results.Count(r => r.Outcome == CreateOutcome.Replayed).Should().Be(5);
            results.Select(r => r.Body!.Code).Distinct().Should().ContainSingle();
        }

        [Fact]
        public async Task A_taken_generated_code_is_skipped_through_the_savepoint()
        {
            // Can't happen in normal operation (ADR-0005); proves the savepoint keeps the transaction usable if it did.
            await using ScratchDatabase scratch = await _db.CreateScratchDatabaseAsync();
            string next = Base62Encoder.Encode(Permutation.Permute(0)); // B-001 starts at 0, so this is the first code
            await scratch.ExecuteAsync($"INSERT INTO short_urls (code, long_url, owner_id, is_custom, created_at) VALUES ('{next}', 'https://example.com/', 'x', false, now())");
            using Harness harness = new Harness(scratch.ApiConnection);
            string key = Guid.NewGuid().ToString();

            CreateResult result = await harness.Links.CreateAsync(Command("client-12", "https://example.com/collide", key: key), CancellationToken.None);

            result.Outcome.Should().Be(CreateOutcome.Created);
            result.Body!.Code.Should().Be(Base62Encoder.Encode(Permutation.Permute(1)), "the next ID's code");
            (await scratch.ScalarAsync($"SELECT count(*) FROM idempotency_keys WHERE idem_key = '{key}' AND response_body IS NOT NULL")).Should().Be(1L);
        }

        [Fact]
        public async Task Owners_see_only_their_own_links_and_malformed_codes_are_not_found()
        {
            using Harness harness = new Harness(_db.ApiConnection);
            CreateResult created = await harness.Links.CreateAsync(Command("client-13", "https://example.com/mine"), CancellationToken.None);

            (await harness.Links.GetAsync("client-14", created.Body!.Code, CancellationToken.None)).Should().BeNull();
            (await harness.Links.GetAsync("client-13", TestCodes.NewGenerated().Value, CancellationToken.None)).Should().BeNull();
            (await harness.Links.GetAsync("client-13", "x", CancellationToken.None)).Should().BeNull();
            (await harness.Links.DeleteAsync("client-14", created.Body.Code, CancellationToken.None)).Should().BeFalse();
        }

        [Fact]
        public async Task Deletion_is_idempotent_and_keeps_the_first_time()
        {
            using Harness harness = new Harness(_db.ApiConnection);
            CreateResult created = await harness.Links.CreateAsync(Command("client-15", "https://example.com/gone"), CancellationToken.None);
            harness.Time.Now = harness.Time.Now.AddHours(1);
            DateTimeOffset firstDelete = harness.Time.Now;

            (await harness.Links.DeleteAsync("client-15", created.Body!.Code, CancellationToken.None)).Should().BeTrue();
            harness.Time.Now = harness.Time.Now.AddHours(4);
            (await harness.Links.DeleteAsync("client-15", created.Body.Code, CancellationToken.None)).Should().BeTrue();

            (await harness.Links.GetAsync("client-15", created.Body.Code, CancellationToken.None))!.DeletedAt.Should().Be(firstDelete);
        }

        [Theory]
        [InlineData("ftp://example.com/", null, DomainErrorCodes.UrlScheme)]
        [InlineData("https://sho.rt/loop", null, ErrorCodes.UrlSelfLink)]
        [InlineData("https://example.com/", "aZ3k9Qx", DomainErrorCodes.AliasGeneratedShape)]
        public async Task Invalid_input_is_rejected_before_any_database_call(string input, string? alias, string code)
        {
            // An unreachable database: if validation touched it, the error would be a connection failure instead.
            using Harness harness = new Harness("Host=127.0.0.1;Port=1;Database=links;Username=nobody;Password=x;Timeout=1");

            Func<Task> act = () => harness.Links.CreateAsync(Command("client-16", input, alias), CancellationToken.None);

            (await act.Should().ThrowAsync<DomainValidationException>()).Which.Code.Should().Be(code);
        }

        [Fact]
        public async Task Expiry_in_the_past_is_rejected()
        {
            using Harness harness = new Harness(_db.ApiConnection);

            Func<Task> act = () => harness.Links.CreateAsync(
                new CreateLinkCommand("client-17", "https://example.com/", null, harness.Time.Now.AddMinutes(-1), null), CancellationToken.None);

            (await act.Should().ThrowAsync<DomainValidationException>()).Which.Code.Should().Be(DomainErrorCodes.ExpiryPast);
        }

        private static CreateLinkCommand Command(string owner, string url, string? alias = null, string? key = null)
        {
            return new CreateLinkCommand(owner, url, alias, null, key);
        }

        private async Task<object?> ScalarAsync(string sql)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(_db.BootstrapConnection);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }

        /// <summary>A <see cref="LinkService"/> wired the way the Api wires it, on one database, with a test clock.</summary>
        private sealed class Harness : IDisposable
        {
            private readonly PrimaryDatabase _primary;
            private readonly CodeAllocator _codes;

            public Harness(string apiConnection)
            {
                _primary = new PrimaryDatabase(NpgsqlDataSource.Create(apiConnection));
                _codes = new CodeAllocator(_primary, Permutation, 0, 100, NullLogger<CodeAllocator>.Instance);
                DbContextOptions<LinksDbContext> options = new DbContextOptionsBuilder<LinksDbContext>()
                    .UseNpgsql(_primary.DataSource)
                    .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options;
                DatabaseGuard guard = new DatabaseGuard("it-" + Guid.NewGuid().ToString("N"), new ResilienceOptions { RetryDelay = TimeSpan.Zero }, TimeProvider.System, null, NullLogger.Instance);
                Links = new LinkService(new PooledDbContextFactory<LinksDbContext>(options), _codes, guard, new Uri("https://sho.rt/"), Time, NullLogger<LinkService>.Instance);
            }

            public TestClock Time { get; } = new TestClock();

            public LinkService Links { get; }

            public void Dispose()
            {
                _codes.Dispose();
                _primary.Dispose();
            }
        }
    }

    /// <summary>A wall clock the test sets; timestamps advance with the real clock.</summary>
    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(DateTime.UtcNow.Year + 1, 1, 1, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
