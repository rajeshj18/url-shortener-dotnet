using System;
using FluentAssertions;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Domain
{
    /// <summary>TSK-14 AC1/AC2: the expiry rules and the time-dependent state.</summary>
    public sealed class ShortUrlExpiryTests
    {
        private static readonly DateTimeOffset Created = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        private static readonly LongUrl Target = LongUrl.Create("https://example.com/x");

        [Fact]
        public void A_link_without_expiry_never_expires()
        {
            ShortUrl url = ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), Target, "owner", Created);

            url.ExpiresAt.Should().BeNull();
            url.StateAt(Created.AddYears(100)).Should().Be(LinkState.Active);
        }

        [Fact]
        public void State_turns_expired_exactly_at_the_expiry()
        {
            DateTimeOffset expiresAt = Created.AddHours(1);
            ShortUrl url = ShortUrl.CreateWithAlias(CustomAlias.Create("summer-sale"), Target, "owner", Created, expiresAt);

            url.StateAt(expiresAt.AddTicks(-1)).Should().Be(LinkState.Active);
            url.StateAt(expiresAt).Should().Be(LinkState.Expired);
            url.State.Should().Be(LinkState.Active, "State is the deletion state only, unchanged for existing callers");
        }

        [Fact]
        public void Deleted_wins_over_expired()
        {
            ShortUrl url = ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), Target, "owner", Created, Created.AddHours(1));
            url.MarkDeleted(Created.AddMinutes(5));

            url.StateAt(Created.AddDays(1)).Should().Be(LinkState.Deleted);
        }

        [Theory]
        [InlineData(0, DomainErrorCodes.ExpiryPast)]
        [InlineData(-1, DomainErrorCodes.ExpiryPast)]
        public void Expiry_must_be_after_creation(int secondsAfterCreation, string expectedCode)
        {
            Action act = () => ShortUrl.ValidateExpiry(Created, Created.AddSeconds(secondsAfterCreation));

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(expectedCode);
        }

        [Fact]
        public void Expiry_may_be_at_most_ten_years_ahead()
        {
            Action exactlyTen = () => ShortUrl.ValidateExpiry(Created, Created.AddYears(10));
            Action beyond = () => ShortUrl.ValidateExpiry(Created, Created.AddYears(10).AddSeconds(1));

            exactlyTen.Should().NotThrow();
            beyond.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.ExpiryTooFar);
        }

        [Fact]
        public void Expiry_is_stored_in_utc_and_compared_across_offsets()
        {
            DateTimeOffset inTwoHoursAsUtcPlusTwo = new DateTimeOffset(2026, 10, 23, 16, 0, 0, TimeSpan.FromHours(2)); // 14:00 UTC
            ShortUrl url = ShortUrl.CreateGenerated(ShortCode.FromGenerated("aZ3k9Qx"), Target, "owner", Created, inTwoHoursAsUtcPlusTwo);

            url.ExpiresAt!.Value.Offset.Should().Be(TimeSpan.Zero);
            url.ExpiresAt.Value.Should().Be(Created.AddHours(2));
        }

        [Fact]
        public void Rehydrate_keeps_a_stored_expiry_even_if_it_has_passed()
        {
            ShortUrl url = ShortUrl.Rehydrate(ShortCode.FromGenerated("aZ3k9Qx"), Target, "owner", Created, null, Created.AddMinutes(1));

            url.StateAt(Created.AddDays(1)).Should().Be(LinkState.Expired);
        }

        [Fact]
        public void Compute_state_is_the_shared_rule()
        {
            ShortUrl.ComputeState(null, null, Created).Should().Be(LinkState.Active);
            ShortUrl.ComputeState(Created, null, Created).Should().Be(LinkState.Deleted);
            ShortUrl.ComputeState(null, Created, Created).Should().Be(LinkState.Expired);
        }
    }
}
