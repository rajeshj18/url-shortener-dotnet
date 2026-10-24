using System;
using System.Linq;
using FluentAssertions;
using UrlShortener.Core.Analytics;
using Xunit;

namespace UrlShortener.UnitTests.Core.Analytics
{
    /// <summary>TSK-09 AC5 (kept): device and bot classification, host-only referrers, and UUIDv7 event IDs.</summary>
    public sealed class ClickClassifierTests
    {
        [Theory]
        [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36", "desktop")]
        [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 Version/18.0 Safari/605.1.15", "desktop")]
        [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148", "mobile")]
        [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome/130.0 Mobile Safari/537.36", "mobile")]
        [InlineData("Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15", "tablet")]
        [InlineData("Mozilla/5.0 (Linux; Android 14; SM-X710) Chrome/130.0 Safari/537.36", "tablet")]
        [InlineData("Twitterbot/1.0", null)]
        [InlineData("WhatsApp/2.24.1 A", null)]
        [InlineData("python-requests/2.32", null)]
        [InlineData(null, null)]
        [InlineData("   ", null)]
        public void Classifies_user_agents_and_returns_null_for_bots(string? userAgent, string? expected)
        {
            ClickClassifier.Device(userAgent).Should().Be(expected);
        }

        [Theory]
        [InlineData("https://Twitter.com/user/status/1?ref=abc", "twitter.com")]
        [InlineData("http://news.example.org/", "news.example.org")]
        [InlineData("https://bücher.example/x", "xn--bcher-kva.example")]
        [InlineData("android-app://com.example", null)]
        [InlineData("not a url", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Keeps_only_the_referrer_host(string? referrer, string? expected)
        {
            ClickClassifier.ReferrerHost(referrer).Should().Be(expected);
        }

        [Fact]
        public void UuidV7_has_version_7_the_rfc_variant_and_the_time_prefix()
        {
            DateTimeOffset at = new DateTimeOffset(2026, 10, 23, 10, 0, 0, 123, TimeSpan.Zero);

            string text = UuidV7.Create(at).ToString("N");

            text[12].Should().Be('7');
            "89ab".Should().Contain(text[16].ToString());
            Convert.ToInt64(text.Substring(0, 12), 16).Should().Be(at.ToUnixTimeMilliseconds());
        }

        [Fact]
        public void UuidV7_values_are_unique_and_sort_by_time()
        {
            DateTimeOffset start = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

            string[] ids = Enumerable.Range(0, 1_000).Select(i => UuidV7.Create(start.AddMilliseconds(i)).ToString("N")).ToArray();

            ids.Distinct().Should().HaveCount(1_000);
            ids.Should().BeInAscendingOrder(StringComparer.Ordinal);
        }
    }
}
