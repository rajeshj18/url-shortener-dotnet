using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using UrlShortener.Core.Links;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Core.Links
{
    /// <summary>TSK-08 AC3/AC7 (kept): the request hash and the Idempotency-Key format.</summary>
    public sealed class IdempotencyRulesTests
    {
        private static readonly LongUrl Url = LongUrl.Create("https://example.com/a?b=1");

        [Fact]
        public void Hash_is_the_sha256_of_the_documented_normalized_text()
        {
            // SHA-256 of "longUrl=https://example.com/a?b=1\ncustomAlias=\nexpiresAt=", computed outside .NET (Python
            // hashlib). Pinned: changing the format would break every stored key's retries.
            IdempotencyRules.RequestHash(Url, null, null).Should().Be("8F4DAB1BCE782E391E20005A0C647DD6CC20C342243CF77E6E9F853E096722CB");
        }

        [Fact]
        public void Hash_treats_a_missing_alias_like_an_empty_one()
        {
            IdempotencyRules.RequestHash(Url, null, null).Should().Be(IdempotencyRules.RequestHash(Url, string.Empty, null));
        }

        [Fact]
        public void Hash_ignores_time_zone_notation_and_sub_seconds()
        {
            DateTimeOffset utc = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset plusTwo = new DateTimeOffset(2027, 1, 1, 2, 0, 0, TimeSpan.FromHours(2)).AddMilliseconds(400);

            IdempotencyRules.RequestHash(Url, null, utc).Should().Be(IdempotencyRules.RequestHash(Url, null, plusTwo));
            IdempotencyRules.RequestHash(Url, null, utc).Should().Be(Expected("longUrl=https://example.com/a?b=1\ncustomAlias=\nexpiresAt=2027-01-01T00:00:00Z"));
        }

        [Fact]
        public void Hash_changes_with_any_field_value_and_keeps_the_url_as_sent()
        {
            string baseline = IdempotencyRules.RequestHash(Url, "alias-1", null);

            IdempotencyRules.RequestHash(LongUrl.Create("https://example.com/a?b=2"), "alias-1", null).Should().NotBe(baseline);
            IdempotencyRules.RequestHash(Url, "alias-2", null).Should().NotBe(baseline);
            IdempotencyRules.RequestHash(Url, "alias-1", DateTimeOffset.UnixEpoch).Should().NotBe(baseline);
            IdempotencyRules.RequestHash(LongUrl.Create("https://EXAMPLE.com/a"), null, null)
                .Should().NotBe(IdempotencyRules.RequestHash(LongUrl.Create("https://example.com/a"), null, null), "the URL is never normalized");
        }

        [Fact]
        public void Hash_requires_a_url()
        {
            Action act = () => IdempotencyRules.RequestHash(null!, null, null);

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
            IdempotencyRules.IsValidKey(key).Should().Be(valid);
        }

        [Fact]
        public void Idempotency_key_length_limit_is_128_and_the_window_24_hours()
        {
            IdempotencyRules.IsValidKey(new string('k', 128)).Should().BeTrue();
            IdempotencyRules.IsValidKey(new string('k', 129)).Should().BeFalse();
            IdempotencyRules.Window.Should().Be(TimeSpan.FromHours(24));
        }

        [Fact]
        public void Rows_that_break_a_domain_rule_become_server_errors_not_client_errors()
        {
            ShortUrlRecord badUrl = new ShortUrlRecord { Code = "aZ3k9Qx", Target = "ftp://example.com/", OwnerId = "o", CreatedAt = DateTimeOffset.UnixEpoch };
            ShortUrlRecord badCode = new ShortUrlRecord { Code = "x", Target = "https://example.com/", OwnerId = "o", CreatedAt = DateTimeOffset.UnixEpoch };

            Action url = () => LinkService.ToDomain(badUrl);
            Action code = () => LinkService.ToDomain(badCode);

            url.Should().Throw<UrlShortener.Core.Database.StoredDataInvalidException>().WithMessage("*aZ3k9Qx*url.scheme*").Which.Message.Should().NotContain("ftp://");
            code.Should().Throw<UrlShortener.Core.Database.StoredDataInvalidException>().WithMessage("*code.format*");
        }

        [Fact]
        public void Valid_rows_load_with_their_state()
        {
            ShortUrlRecord row = new ShortUrlRecord
            {
                Code = "spring-sale",
                Target = "https://example.com/",
                OwnerId = "o",
                IsCustom = true,
                CreatedAt = DateTimeOffset.UnixEpoch,
                DeletedAt = DateTimeOffset.UnixEpoch.AddDays(1),
            };

            ShortUrl link = LinkService.ToDomain(row);

            link.IsCustom.Should().BeTrue();
            link.State.Should().Be(LinkState.Deleted);
        }

        private static string Expected(string normalized)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        }
    }
}
