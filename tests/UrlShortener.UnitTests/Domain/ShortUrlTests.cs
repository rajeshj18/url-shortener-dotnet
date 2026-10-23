using System;
using FluentAssertions;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Domain
{
    /// <summary>TSK-05 AC4: factories, owner rules, state, idempotent soft delete.</summary>
    public sealed class ShortUrlTests
    {
        private static readonly DateTimeOffset Created = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        private static readonly LongUrl Target = LongUrl.Create("https://example.com/landing");

        [Fact]
        public void CreateGenerated_sets_every_field_and_is_active()
        {
            ShortCode code = ShortCode.FromGenerated("aZ3k9Qx");

            ShortUrl url = ShortUrl.CreateGenerated(code, Target, "client-42", Created);

            url.Code.Should().Be(code);
            url.Target.Should().Be(Target);
            url.OwnerId.Should().Be("client-42");
            url.IsCustom.Should().BeFalse();
            url.CreatedAt.Should().Be(Created);
            url.DeletedAt.Should().BeNull();
            url.State.Should().Be(LinkState.Active);
        }

        [Fact]
        public void CreateWithAlias_is_custom()
        {
            ShortUrl url = ShortUrl.CreateWithAlias(CustomAlias.Create("spring-sale"), Target, "client-42", Created);

            url.IsCustom.Should().BeTrue();
            url.Code.Value.Should().Be("spring-sale");
        }

        [Fact]
        public void CreateGenerated_with_an_alias_code_is_rejected()
        {
            ShortCode aliasCode = ShortCode.FromAlias(CustomAlias.Create("spring-sale"));

            Action act = () => ShortUrl.CreateGenerated(aliasCode, Target, "client-42", Created);

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.CodeKind);
        }

        [Fact]
        public void Create_normalises_creation_time_to_utc()
        {
            DateTimeOffset localTime = new DateTimeOffset(2026, 10, 23, 15, 30, 0, TimeSpan.FromHours(5.5));

            ShortUrl url = ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), Target, "client-42", localTime);

            url.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
            url.CreatedAt.Should().Be(localTime);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Create_missing_owner_throws_required(string? owner)
        {
            Action act = () => ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), Target, owner, Created);

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.OwnerRequired);
        }

        [Fact]
        public void Create_owner_of_64_characters_is_accepted_and_65_rejected()
        {
            ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), Target, new string('o', 64), Created)
                .OwnerId.Length.Should().Be(64);

            Action act = () => ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), Target, new string('o', 65), Created);
            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.OwnerLength);
        }

        [Fact]
        public void Create_null_arguments_throw()
        {
            Action nullCode = () => ShortUrl.CreateGenerated(null!, Target, "client-42", Created);
            Action nullAlias = () => ShortUrl.CreateWithAlias(null!, Target, "client-42", Created);
            Action nullTarget = () => ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), null!, "client-42", Created);

            nullCode.Should().Throw<ArgumentNullException>();
            nullAlias.Should().Throw<ArgumentNullException>();
            nullTarget.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void MarkDeleted_sets_state_deleted()
        {
            ShortUrl url = NewUrl();
            DateTimeOffset deletedAt = Created.AddHours(1);

            url.MarkDeleted(deletedAt);

            url.State.Should().Be(LinkState.Deleted);
            url.DeletedAt.Should().Be(deletedAt);
        }

        [Fact]
        public void MarkDeleted_twice_keeps_the_first_deletion_time()
        {
            ShortUrl url = NewUrl();
            DateTimeOffset first = Created.AddHours(1);

            url.MarkDeleted(first);
            url.MarkDeleted(Created.AddHours(2));

            url.DeletedAt.Should().Be(first);
            url.State.Should().Be(LinkState.Deleted);
        }

        [Fact]
        public void MarkDeleted_at_creation_time_is_allowed()
        {
            ShortUrl url = NewUrl();

            url.MarkDeleted(Created);

            url.State.Should().Be(LinkState.Deleted);
        }

        [Fact]
        public void MarkDeleted_before_creation_is_rejected()
        {
            ShortUrl url = NewUrl();

            Action act = () => url.MarkDeleted(Created.AddSeconds(-1));

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.TimeOrder);
            url.State.Should().Be(LinkState.Active);
        }

        [Fact]
        public void Rehydrate_active_generated_link_restores_every_field()
        {
            ShortCode code = ShortCode.FromGenerated("aZ3k9Qx");

            ShortUrl url = ShortUrl.Rehydrate(code, Target, "client-42", Created, deletedAt: null);

            url.Code.Should().Be(code);
            url.Target.Should().Be(Target);
            url.OwnerId.Should().Be("client-42");
            url.IsCustom.Should().BeFalse();
            url.CreatedAt.Should().Be(Created);
            url.State.Should().Be(LinkState.Active);
        }

        [Fact]
        public void Rehydrate_deleted_alias_link_restores_deletion()
        {
            ShortCode code = ShortCode.FromAlias(CustomAlias.Create("spring-sale"));
            DateTimeOffset deletedAt = Created.AddDays(1);

            ShortUrl url = ShortUrl.Rehydrate(code, Target, "client-42", Created, deletedAt);

            url.IsCustom.Should().BeTrue();
            url.State.Should().Be(LinkState.Deleted);
            url.DeletedAt.Should().Be(deletedAt);
        }

        [Fact]
        public void Rehydrate_invalid_record_is_rejected()
        {
            ShortCode code = ShortCode.FromGenerated("aZ3k9Qx");

            Action blankOwner = () => ShortUrl.Rehydrate(code, Target, " ", Created, null);
            Action deletedBeforeCreated = () => ShortUrl.Rehydrate(code, Target, "client-42", Created, Created.AddMinutes(-1));
            Action nullCode = () => ShortUrl.Rehydrate(null!, Target, "client-42", Created, null);

            blankOwner.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.OwnerRequired);
            deletedBeforeCreated.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.TimeOrder);
            nullCode.Should().Throw<ArgumentNullException>();
        }

        private static ShortUrl NewUrl()
        {
            return ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), Target, "client-42", Created);
        }
    }
}
