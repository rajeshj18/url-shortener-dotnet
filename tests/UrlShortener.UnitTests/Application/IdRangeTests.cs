using System;
using FluentAssertions;
using UrlShortener.Application.Ports;
using Xunit;

namespace UrlShortener.UnitTests.Application
{
    /// <summary>TSK-07: the leased range and the exhaustion exception of the short-code ports.</summary>
    public sealed class IdRangeTests
    {
        [Fact]
        public void Range_exposes_bounds_and_count()
        {
            IdRange range = new IdRange(10_000, 20_000);

            range.Start.Should().Be(10_000);
            range.EndExclusive.Should().Be(20_000);
            range.Count.Should().Be(10_000);
        }

        [Fact]
        public void Negative_start_throws()
        {
            Action act = () => _ = new IdRange(-1, 10);

            act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("start");
        }

        [Theory]
        [InlineData(5, 5)]
        [InlineData(5, 4)]
        public void Empty_or_reversed_range_throws(long start, long endExclusive)
        {
            Action act = () => _ = new IdRange(start, endExclusive);

            act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("endExclusive");
        }

        [Fact]
        public void Exhausted_exception_default_message_points_to_the_runbook()
        {
            new IdSpaceExhaustedException().Message.Should().Contain("runbook");
        }

        [Fact]
        public void Exhausted_exception_keeps_message_and_cause()
        {
            InvalidOperationException cause = new InvalidOperationException("cause");

            new IdSpaceExhaustedException("custom").Message.Should().Be("custom");
            new IdSpaceExhaustedException("custom", cause).InnerException.Should().BeSameAs(cause);
        }
    }
}
