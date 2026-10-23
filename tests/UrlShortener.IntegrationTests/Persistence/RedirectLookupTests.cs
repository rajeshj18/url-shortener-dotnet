using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrlShortener.Application.Ports;
using UrlShortener.Domain;
using UrlShortener.Infrastructure.Persistence.Links;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Persistence
{
    /// <summary>TSK-06 AC4: the redirect lookup works through the view, connected as the read-only Redirect role.</summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class RedirectLookupTests
    {
        private static readonly DateTimeOffset Created = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

        private readonly PostgresFixture _db;

        public RedirectLookupTests(PostgresFixture db)
        {
            _db = db;
        }

        [Fact]
        public async Task Active_link_is_found_on_replicas_and_primary()
        {
            ShortUrl url = await InsertAsync(deleted: false);

            using LookupHarness harness = new LookupHarness(_db.RedirectConnection);
            NpgsqlRedirectLookup lookup = harness.Lookup;

            RedirectTarget? fromReplica = await lookup.FindAsync(url.Code, CancellationToken.None);
            RedirectTarget? fromPrimary = await lookup.FindOnPrimaryAsync(url.Code, CancellationToken.None);

            fromReplica.Should().NotBeNull();
            fromReplica!.Target.Should().Be(url.Target);
            fromReplica.State.Should().Be(LinkState.Active);
            fromPrimary.Should().Be(fromReplica);
        }

        [Fact]
        public async Task Deleted_link_is_found_with_state_deleted()
        {
            ShortUrl url = await InsertAsync(deleted: true);

            using LookupHarness harness = new LookupHarness(_db.RedirectConnection);

            RedirectTarget? target = await harness.Lookup.FindAsync(url.Code, CancellationToken.None);

            target!.State.Should().Be(LinkState.Deleted);
        }

        [Fact]
        public async Task Unknown_code_returns_null()
        {
            using LookupHarness harness = new LookupHarness(_db.RedirectConnection);

            RedirectTarget? target = await harness.Lookup.FindAsync(TestCodes.NewGenerated(), CancellationToken.None);

            target.Should().BeNull();
        }

        /// <summary>Owns the data sources of one lookup (as the Redirect role) and disposes them.</summary>
        private sealed class LookupHarness : IDisposable
        {
            private readonly LinksReplicaDataSource _replicas;
            private readonly LinksPrimaryDataSource _primary;

            public LookupHarness(string redirectConnection)
            {
                _replicas = new LinksReplicaDataSource(NpgsqlDataSource.Create(redirectConnection));
                _primary = new LinksPrimaryDataSource(NpgsqlDataSource.Create(redirectConnection));
                Lookup = new NpgsqlRedirectLookup(_replicas, _primary);
            }

            public NpgsqlRedirectLookup Lookup { get; }

            public void Dispose()
            {
                _replicas.Dispose();
                _primary.Dispose();
            }
        }

        private async Task<ShortUrl> InsertAsync(bool deleted)
        {
            ShortUrl url = ShortUrl.CreateGenerated(TestCodes.NewGenerated(), LongUrl.Create("https://example.com/r"), "client-42", Created);
            DbContextOptions<LinksDbContext> options = new DbContextOptionsBuilder<LinksDbContext>().UseNpgsql(_db.ApiConnection).Options;
            await using LinksDbContext context = new LinksDbContext(options);
            EfShortUrlRepository repository = new EfShortUrlRepository(context);
            await repository.TryInsertAsync(url, CancellationToken.None);
            if (deleted)
            {
                url.MarkDeleted(Created.AddMinutes(1));
                await repository.SaveDeletionAsync(url, CancellationToken.None);
            }

            return url;
        }
    }
}
