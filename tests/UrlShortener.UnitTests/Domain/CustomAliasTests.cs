using System;
using FluentAssertions;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Domain
{
    /// <summary>TSK-05 AC1 (alias rules) and AC5 (stable codes, no echo of the input).</summary>
    public sealed class CustomAliasTests
    {
        [Theory]
        [InlineData("abcd")]                              // 4: lower bound
        [InlineData("abcdefghijklmnopqrstuvwxyz0123")]    // 30: upper bound
        [InlineData("abc-123")]                           // 7 chars, but not all Base62
        [InlineData("summer_sale")]
        [InlineData("Spring-Sale-2027")]
        public void Create_valid_alias_keeps_value(string value)
        {
            CustomAlias.Create(value).Value.Should().Be(value);
        }

        [Theory]
        [InlineData("abc", DomainErrorCodes.AliasLength)]                              // 3
        [InlineData("abcdefghijklmnopqrstuvwxyz01234", DomainErrorCodes.AliasLength)]  // 31
        [InlineData("abc.def", DomainErrorCodes.AliasCharacters)]
        [InlineData("abc def", DomainErrorCodes.AliasCharacters)]
        [InlineData("abc/def", DomainErrorCodes.AliasCharacters)]
        [InlineData("café-1", DomainErrorCodes.AliasCharacters)]                       // non-ASCII letter
        [InlineData("abc1234", DomainErrorCodes.AliasGeneratedShape)]                  // exactly 7 Base62
        [InlineData("ABCDEFG", DomainErrorCodes.AliasGeneratedShape)]
        [InlineData("health", DomainErrorCodes.AliasReserved)]
        [InlineData("HEALTH", DomainErrorCodes.AliasReserved)]
        [InlineData("Swagger", DomainErrorCodes.AliasGeneratedShape)]   // 7 letters: the generated-shape rule fires first
        public void Create_invalid_alias_throws_with_stable_code(string value, string expectedCode)
        {
            Action act = () => CustomAlias.Create(value);

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(expectedCode);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Create_missing_alias_throws_required(string? value)
        {
            Action act = () => CustomAlias.Create(value);

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.AliasRequired);
        }

        [Theory]
        [InlineData("api")]
        [InlineData("health")]
        [InlineData("swagger")]
        [InlineData("favicon.ico")]
        [InlineData("robots.txt")]
        [InlineData("HEALTH")]
        public void Create_every_reserved_path_is_rejected(string value)
        {
            // The rule that fires first differs (length, characters, generated shape or reserved),
            // but no reserved path may ever become an alias, whatever its letter case.
            CustomAlias.TryCreate(value, out _).Should().BeFalse();
        }

        [Fact]
        public void Create_api_is_rejected_by_length_before_reserved_check()
        {
            // "api" is reserved but only 3 characters long, so the length rule fires first. It's still rejected.
            Action act = () => CustomAlias.Create("api");

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.AliasLength);
        }

        [Fact]
        public void Create_error_message_never_contains_the_rejected_input()
        {
            const string Rejected = "evil<script>";

            Action act = () => CustomAlias.Create(Rejected);

            act.Should().Throw<DomainValidationException>().Which.Message.Should().NotContain(Rejected);
        }

        [Fact]
        public void TryCreate_valid_and_invalid()
        {
            CustomAlias.TryCreate("my-link", out CustomAlias? valid).Should().BeTrue();
            valid!.Value.Should().Be("my-link");

            CustomAlias.TryCreate("abc1234", out CustomAlias? invalid).Should().BeFalse();
            invalid.Should().BeNull();
        }

        [Fact]
        public void Equality_is_ordinal_and_case_sensitive()
        {
            CustomAlias a = CustomAlias.Create("My-Link");

            a.Should().Be(CustomAlias.Create("My-Link"));
            a.Should().NotBe(CustomAlias.Create("my-link"));
            a.GetHashCode().Should().Be(CustomAlias.Create("My-Link").GetHashCode());
            a.Equals((object)"My-Link").Should().BeFalse();   // a string with the same text is a different type
            a.ToString().Should().Be("My-Link");
        }
    }
}
