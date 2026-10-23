using System;
using FluentAssertions;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Domain
{
    /// <summary>TSK-05 AC2: generated vs alias shapes, the cheap redirect parse, case-sensitive equality.</summary>
    public sealed class ShortCodeTests
    {
        [Theory]
        [InlineData("aZ3k9Qx")]
        [InlineData("0000000")]
        [InlineData("zzzzzzz")]
        public void FromGenerated_seven_base62_characters_is_generated(string value)
        {
            ShortCode code = ShortCode.FromGenerated(value);

            code.Value.Should().Be(value);
            code.IsGenerated.Should().BeTrue();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("aZ3k9Q")]     // 6
        [InlineData("aZ3k9Qx1")]   // 8
        [InlineData("aZ3k9Q-")]    // 7, not all Base62
        [InlineData("aZ3k9Qé")]    // 7, non-ASCII
        public void FromGenerated_wrong_shape_throws_code_format(string? value)
        {
            Action act = () => ShortCode.FromGenerated(value);

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.CodeFormat);
        }

        [Fact]
        public void FromAlias_is_not_generated()
        {
            ShortCode code = ShortCode.FromAlias(CustomAlias.Create("spring-sale"));

            code.Value.Should().Be("spring-sale");
            code.IsGenerated.Should().BeFalse();
        }

        [Fact]
        public void FromAlias_null_throws()
        {
            Action act = () => ShortCode.FromAlias(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Theory]
        [InlineData("aZ3k9Qx", true)]
        [InlineData("spring-sale", false)]
        [InlineData("abc-123", false)]
        public void TryParse_valid_codes(string value, bool expectedGenerated)
        {
            ShortCode.TryParse(value, out ShortCode? code).Should().BeTrue();

            code!.Value.Should().Be(value);
            code.IsGenerated.Should().Be(expectedGenerated);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]                                     // too short for an alias, not generated
        [InlineData("abcdefghijklmnopqrstuvwxyz0123456789")]    // too long
        [InlineData("../etc/passwd")]
        [InlineData("a b c d")]
        [InlineData("health")]                                  // reserved path
        public void TryParse_invalid_input_returns_false_without_throwing(string? value)
        {
            ShortCode.TryParse(value, out ShortCode? code).Should().BeFalse();
            code.Should().BeNull();
        }

        [Fact]
        public void Equality_is_case_sensitive()
        {
            ShortCode lower = ShortCode.FromGenerated("abcdefg");

            lower.Should().Be(ShortCode.FromGenerated("abcdefg"));
            lower.Should().NotBe(ShortCode.FromGenerated("ABCDEFG"));
            lower.GetHashCode().Should().Be(ShortCode.FromGenerated("abcdefg").GetHashCode());
            lower.Equals((object)"abcdefg").Should().BeFalse();   // same text, different type
            lower.ToString().Should().Be("abcdefg");
        }
    }
}
