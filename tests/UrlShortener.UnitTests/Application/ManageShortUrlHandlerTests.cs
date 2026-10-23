using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UrlShortener.Application.ShortUrls;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Application
{
    /// <summary>TSK-10: owner checks for metadata and delete, with an in-memory store.</summary>
    public sealed class ManageShortUrlHandlerTests
    {
        private readonly FakeRepository _repository = new FakeRepository();
        private readonly FakeTime _time = new FakeTime();
        private readonly GetShortUrlHandler _get;
        private readonly DeleteShortUrlHandler _delete;

        public ManageShortUrlHandlerTests()
        {
            _get = new GetShortUrlHandler(_repository, new Uri("https://sho.rt/"));
            _delete = new DeleteShortUrlHandler(_repository, _time);
            ShortUrl link = ShortUrl.CreateWithAlias(CustomAlias.Create("owned-link"), LongUrl.Create("https://example.com/x"), "owner-1", _time.Now);
            _repository.Links[link.Code.Value] = link;
        }

        [Fact]
        public async Task Owner_gets_the_details()
        {
            ShortUrlDetails? details = await _get.GetAsync("owner-1", "owned-link", CancellationToken.None);

            details.Should().NotBeNull();
            details!.ShortUrl.Should().Be(new Uri("https://sho.rt/owned-link"));
            details.LongUrl.OriginalString.Should().Be("https://example.com/x");
            details.IsCustom.Should().BeTrue();
            details.CreatedAt.Should().Be(_time.Now);
            details.DeletedAt.Should().BeNull();
        }

        [Theory]
        [InlineData("owner-2", "owned-link")]   // someone else's
        [InlineData("owner-1", "missing-link")] // unknown
        [InlineData("owner-1", "../x")]         // malformed
        [InlineData("owner-1", null)]
        public async Task Others_unknown_and_malformed_codes_look_the_same(string owner, string? code)
        {
            (await _get.GetAsync(owner, code, CancellationToken.None)).Should().BeNull();
            (await _delete.DeleteAsync(owner, code, CancellationToken.None)).Should().BeFalse();
            _repository.Links["owned-link"].DeletedAt.Should().BeNull("nobody but the owner can delete");
        }

        [Fact]
        public async Task Delete_marks_the_link_and_is_idempotent()
        {
            (await _delete.DeleteAsync("owner-1", "owned-link", CancellationToken.None)).Should().BeTrue();
            DateTimeOffset? first = _repository.Links["owned-link"].DeletedAt;
            _time.Now = _time.Now.AddMinutes(5);

            (await _delete.DeleteAsync("owner-1", "owned-link", CancellationToken.None)).Should().BeTrue();

            first.Should().NotBeNull();
            _repository.Links["owned-link"].DeletedAt.Should().Be(first, "the first deletion time is kept");
            (await _get.GetAsync("owner-1", "owned-link", CancellationToken.None))!.DeletedAt.Should().Be(first);
        }

        [Fact]
        public async Task Constructors_and_owner_are_guarded()
        {
            Action noRepository = () => _ = new GetShortUrlHandler(null!, new Uri("https://sho.rt/"));
            Action noBase = () => _ = new GetShortUrlHandler(_repository, null!);
            Action noClock = () => _ = new DeleteShortUrlHandler(_repository, null!);
            Func<Task> noOwner = () => _get.GetAsync(null!, "owned-link", CancellationToken.None);

            noRepository.Should().Throw<ArgumentNullException>();
            noBase.Should().Throw<ArgumentNullException>();
            noClock.Should().Throw<ArgumentNullException>();
            await noOwner.Should().ThrowAsync<ArgumentNullException>();
        }
    }
}
