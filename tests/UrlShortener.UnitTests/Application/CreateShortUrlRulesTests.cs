using System;
using FluentAssertions;
using UrlShortener.Application.Ports;
using UrlShortener.Application.ShortUrls;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Application
{
    /// <summary>TSK-08 AC3/AC7: the request hash, the Idempotency-Key format and the self-link rule.</summary>
    public sealed class CreateShortUrlRulesTests
    {
        private static readonly LongUrl Url = LongUrl.Create("https://example.com/a?b=1");

        [Fact]
        public void Hash_is_the_sha256_of_the_documented_normalized_text()
        {
            // SHA-256 of "longUrl=https://example.com/a?b=1\ncustomAlias=\nexpiresAt=", computed outside .NET (Python
            // hashlib). Pinned: changing the format would break every stored key's retries.
            RequestHash.Compute(Url, null, null).Should().Be("8F4DAB1BCE782E391E20005A0C647DD6CC20C342243CF77E6E9F853E096722CB");
        }

        [Fact]
        public void Hash_treats_a_missing_alias_like_an_empty_one()
        {
            RequestHash.Compute(Url, null, null).Should().Be(RequestHash.Compute(Url, string.Empty, null));
        }

        [Fact]
        public void Hash_ignores_time_zone_notation_and_sub_seconds()
        {
            DateTimeOffset utc = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset plusTwo = new DateTimeOffset(2027, 1, 1, 2, 0, 0, TimeSpan.FromHours(2)).AddMilliseconds(400);

            RequestHash.Compute(Url, null, utc).Should().Be(RequestHash.Compute(Url, null, plusTwo));
            RequestHash.Compute(Url, null, utc).Should().Be(Expected("longUrl=https://example.com/a?b=1\ncustomAlias=\nexpiresAt=2027-01-01T00:00:00Z"));
        }

        [Fact]
        public void Hash_changes_with_any_field_value()
        {
            string baseline = RequestHash.Compute(Url, "alias-1", null);

            RequestHash.Compute(LongUrl.Create("https://example.com/a?b=2"), "alias-1", null).Should().NotBe(baseline);
            RequestHash.Compute(Url, "alias-2", null).Should().NotBe(baseline);
            RequestHash.Compute(Url, "alias-1", DateTimeOffset.UnixEpoch).Should().NotBe(baseline);
        }

        [Fact]
        public void Hash_keeps_the_url_exactly_as_sent()
        {
            RequestHash.Compute(LongUrl.Create("https://EXAMPLE.com/a"), null, null)
                .Should().NotBe(RequestHash.Compute(LongUrl.Create("https://example.com/a"), null, null), "the URL is never normalized");
        }

        [Fact]
        public void Hash_requires_a_url()
        {
            Action act = () => RequestHash.Compute(null!, null, null);

            act.Should().Throw<ArgumentNullException>();
        }

        [Theory]
        [InlineData("6f1c2b3a-0000-4000-8000-000000000001", true)]
        [InlineData("a", true)]
        [InlineData("with space and ~!@#", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("tab\there", false)]
        [InlineData("café", false)]
        public void Idempotency_key_format(string? key, bool valid)
        {
            IdempotencyKeyFormat.IsValid(key).Should().Be(valid);
        }

        [Fact]
        public void Idempotency_key_length_limit_is_128()
        {
            IdempotencyKeyFormat.IsValid(new string('k', 128)).Should().BeTrue();
            IdempotencyKeyFormat.IsValid(new string('k', 129)).Should().BeFalse();
        }

        [Theory]
        [InlineData("https://sho.rt/x")]
        [InlineData("http://Sho.Rt:8080/y")]
        public void Self_link_is_rejected(string input)
        {
            Action act = () => new SelfLinkCheck(new Uri("https://sho.rt/")).Ensure(LongUrl.Create(input));

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(ApplicationErrorCodes.UrlSelfLink);
        }

        [Theory]
        [InlineData("https://example.com/sho.rt")]
        [InlineData("https://sub.sho.rt/x")]
        public void Other_hosts_are_allowed(string input)
        {
            Action act = () => new SelfLinkCheck(new Uri("https://sho.rt/")).Ensure(LongUrl.Create(input));

            act.Should().NotThrow();
        }

        [Fact]
        public void Self_link_check_rejects_null_arguments()
        {
            Action noBase = () => _ = new SelfLinkCheck(null!);
            Action noUrl = () => new SelfLinkCheck(new Uri("https://sho.rt/")).Ensure(null!);

            noBase.Should().Throw<ArgumentNullException>();
            noUrl.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Result_factories_reject_missing_bodies()
        {
            Action created = () => CreateShortUrlResult.Created(null!);
            Action replayed = () => CreateShortUrlResult.Replayed(null!);
            Action replay = () => IdempotencyBeginResult.Replay(null!);

            created.Should().Throw<ArgumentNullException>();
            replayed.Should().Throw<ArgumentNullException>();
            replay.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Begin_results_expose_their_outcome()
        {
            IdempotencyBeginResult.New.Outcome.Should().Be(IdempotencyBeginOutcome.New);
            IdempotencyBeginResult.Mismatch.Outcome.Should().Be(IdempotencyBeginOutcome.Mismatch);
            IdempotencyBeginResult.Replay("{}").StoredResponseBody.Should().Be("{}");
        }

        private static string Expected(string normalized)
        {
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
        }
    }
}
