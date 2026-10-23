using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain;
using UrlShortener.Infrastructure.Persistence.Links;
using UrlShortener.IntegrationTests.Infrastructure;
using Xunit;

namespace UrlShortener.IntegrationTests.Persistence
{
    /// <summary>TSK-06 AC2 and AC3: round-trips, duplicate codes and idempotent deletes, as the Api role.</summary>
    [Collection(PostgresCollectionDefinition.Name)]
    public sealed class ShortUrlRepositoryTests
    {
        private static readonly DateTimeOffset Created = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        private static readonly LongUrl Target = LongUrl.Create("https://example.com/landing?q=1");

        private readonly PostgresFixture _db;

        public ShortUrlRepositoryTests(PostgresFixture db)
        {
            _db = db;
        }

        [Fact]
        public async Task Generated_link_round_trips_every_field()
        {
            ShortUrl original = ShortUrl.CreateGenerated(TestCodes.NewGenerated(), Target, "client-42", Created);

            (await WithRepository(r => r.TryInsertAsync(original, CancellationToken.None))).Should().BeTrue();
            ShortUrl? loaded = await WithRepository(r => r.FindByCodeAsync(original.Code, CancellationToken.None));

            loaded.Should().NotBeNull();
            loaded!.Code.Should().Be(original.Code);
            loaded.Code.IsGenerated.Should().BeTrue();
            loaded.Target.Should().Be(Target);
            loaded.OwnerId.Should().Be("client-42");
            loaded.CreatedAt.Should().Be(Created);
            loaded.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
            loaded.State.Should().Be(LinkState.Active);
        }

        [Fact]
        public async Task Alias_link_round_trips_as_custom()
        {
            ShortUrl original = ShortUrl.CreateWithAlias(TestCodes.NewAlias(), Target, "client-42", Created);

            await WithRepository(r => r.TryInsertAsync(original, CancellationToken.None));
            ShortUrl? loaded = await WithRepository(r => r.FindByCodeAsync(original.Code, CancellationToken.None));

            loaded!.IsCustom.Should().BeTrue();
            loaded.Code.Value.Should().Be(original.Code.Value);
        }

        [Fact]
        public async Task Duplicate_code_returns_false_without_throwing()
        {
            CustomAlias alias = TestCodes.NewAlias();
            ShortUrl first = ShortUrl.CreateWithAlias(alias, Target, "client-1", Created);
            ShortUrl second = ShortUrl.CreateWithAlias(alias, LongUrl.Create("https://other.example/"), "client-2", Created);

            (await WithRepository(r => r.TryInsertAsync(first, CancellationToken.None))).Should().BeTrue();
            (await WithRepository(r => r.TryInsertAsync(second, CancellationToken.None))).Should().BeFalse();

            ShortUrl? stored = await WithRepository(r => r.FindByCodeAsync(first.Code, CancellationToken.None));
            stored!.OwnerId.Should().Be("client-1");
        }

        [Fact]
        public async Task Unknown_code_returns_null()
        {
            (await WithRepository(r => r.FindByCodeAsync(TestCodes.NewGenerated(), CancellationToken.None))).Should().BeNull();
        }

        [Fact]
        public async Task Deletion_is_stored_and_a_second_delete_keeps_the_first_time()
        {
            ShortUrl url = ShortUrl.CreateGenerated(TestCodes.NewGenerated(), Target, "client-42", Created);
            await WithRepository(r => r.TryInsertAsync(url, CancellationToken.None));
            DateTimeOffset firstDelete = Created.AddHours(1);

            url.MarkDeleted(firstDelete);
            await WithRepository(async r => { await r.SaveDeletionAsync(url, CancellationToken.None); return true; });

            ShortUrl reloaded = (await WithRepository(r => r.FindByCodeAsync(url.Code, CancellationToken.None)))!;
            ShortUrl secondCopy = ShortUrl.Rehydrate(reloaded.Code, reloaded.Target, reloaded.OwnerId, reloaded.CreatedAt, null);
            secondCopy.MarkDeleted(Created.AddHours(5));
            await WithRepository(async r => { await r.SaveDeletionAsync(secondCopy, CancellationToken.None); return true; });

            ShortUrl final = (await WithRepository(r => r.FindByCodeAsync(url.Code, CancellationToken.None)))!;
            final.State.Should().Be(LinkState.Deleted);
            final.DeletedAt.Should().Be(firstDelete);
        }

        [Fact]
        public async Task Saving_a_deletion_without_MarkDeleted_is_rejected()
        {
            ShortUrl url = ShortUrl.CreateGenerated(TestCodes.NewGenerated(), Target, "client-42", Created);

            Func<Task> act = () => WithRepository(async r => { await r.SaveDeletionAsync(url, CancellationToken.None); return true; });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        private async Task<T> WithRepository<T>(Func<EfShortUrlRepository, Task<T>> work)
        {
            DbContextOptions<LinksDbContext> options = new DbContextOptionsBuilder<LinksDbContext>()
                .UseNpgsql(_db.ApiConnection)
                .Options;
            await using LinksDbContext context = new LinksDbContext(options);
            return await work(new EfShortUrlRepository(context));
        }
    }
}
