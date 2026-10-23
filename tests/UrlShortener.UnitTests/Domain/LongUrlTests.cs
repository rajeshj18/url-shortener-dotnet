using System;
using FluentAssertions;
using UrlShortener.Domain;
using Xunit;

namespace UrlShortener.UnitTests.Domain
{
    /// <summary>TSK-05 AC3 (URL rules) and AC5 (stable codes, no echo of the input).</summary>
    public sealed class LongUrlTests
    {
        [Theory]
        [InlineData("https://example.com")]
        [InlineData("http://example.com/some/long/path?q=1&x=%20#frag")]
        [InlineData("HTTPS://Example.COM/Path")]           // scheme in any letter case
        [InlineData("https://example.com:8443/a")]
        [InlineData("https://bücher.example/katalog")]     // international domain name
        [InlineData("https://[2001:db8::1]/ipv6")]
        public void Create_valid_url_keeps_original_string(string value)
        {
            LongUrl url = LongUrl.Create(value);

            url.Value.Should().Be(value);
            url.Uri.IsAbsoluteUri.Should().BeTrue();
        }

        [Theory]
        [InlineData("example.com", DomainErrorCodes.UrlScheme)]                   // no scheme
        [InlineData("/relative/path", DomainErrorCodes.UrlScheme)]                // relative (file:// on Linux)
        [InlineData("javascript:alert(1)", DomainErrorCodes.UrlScheme)]
        [InlineData("data:text/html;base64,PHNjcmlwdD4=", DomainErrorCodes.UrlScheme)]
        [InlineData("ftp://example.com/file", DomainErrorCodes.UrlScheme)]
        [InlineData("file:///etc/passwd", DomainErrorCodes.UrlScheme)]
        [InlineData("mailto:someone@example.com", DomainErrorCodes.UrlScheme)]
        [InlineData("http:/example.com", DomainErrorCodes.UrlScheme)]             // single slash
        [InlineData("https://", DomainErrorCodes.UrlFormat)]                      // no host
        [InlineData("https://user:pass@example.com/", DomainErrorCodes.UrlCredentials)]
        [InlineData("https://user@example.com/", DomainErrorCodes.UrlCredentials)]
        [InlineData("https://exa mple.com/", DomainErrorCodes.UrlCharacters)]
        [InlineData("https://example.com/\r\nSet-Cookie:x=1", DomainErrorCodes.UrlCharacters)] // header injection
        [InlineData("https://example.com/\ttab", DomainErrorCodes.UrlCharacters)]
        [InlineData(" https://example.com/", DomainErrorCodes.UrlCharacters)]     // leading space
        public void Create_invalid_url_throws_with_stable_code(string value, string expectedCode)
        {
            Action act = () => LongUrl.Create(value);

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(expectedCode);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Create_missing_url_throws_required(string? value)
        {
            Action act = () => LongUrl.Create(value);

            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.UrlRequired);
        }

        [Fact]
        public void Create_exactly_2048_characters_is_accepted_and_2049_rejected()
        {
            const string Prefix = "https://example.com/";
            string atLimit = Prefix + new string('a', LongUrl.MaxLength - Prefix.Length);
            string overLimit = atLimit + "a";

            LongUrl.Create(atLimit).Value.Length.Should().Be(2048);
            Action act = () => LongUrl.Create(overLimit);
            act.Should().Throw<DomainValidationException>().Which.Code.Should().Be(DomainErrorCodes.UrlLength);
        }

        [Fact]
        public void Create_error_message_never_contains_the_rejected_input()
        {
            const string Rejected = "javascript:alert('secret-token-123')";

            Action act = () => LongUrl.Create(Rejected);

            act.Should().Throw<DomainValidationException>().Which.Message.Should().NotContain("secret-token-123");
        }

        [Fact]
        public void Equality_is_ordinal_on_the_original_string()
        {
            LongUrl url = LongUrl.Create("https://example.com/A");

            url.Should().Be(LongUrl.Create("https://example.com/A"));
            url.Should().NotBe(LongUrl.Create("https://example.com/a"));
            url.GetHashCode().Should().Be(LongUrl.Create("https://example.com/A").GetHashCode());
            url.Equals((object)"https://example.com/A").Should().BeFalse();   // same text, different type
            url.ToString().Should().Be("https://example.com/A");
        }
    }
}
