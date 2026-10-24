using System;
using FluentAssertions;
using UrlShortener.Core.Codes;
using Xunit;

namespace UrlShortener.UnitTests.Core.Codes
{
    /// <summary>TSK-07 AC1: fixed-width Base62 over the 7-character code space.</summary>
    public sealed class Base62EncoderTests
    {
        [Fact]
        public void Code_space_is_62_to_the_power_7()
        {
            Base62Encoder.CodeSpaceSize.Should().Be((long)Math.Pow(62, 7));
        }

        [Theory]
        [InlineData(0L, "0000000")]
        [InlineData(61L, "000000z")]
        [InlineData(62L, "0000010")]
        [InlineData(3_521_614_606_207L, "zzzzzzz")]
        public void Encode_known_values(long value, string expected)
        {
            Base62Encoder.Encode(value).Should().Be(expected);
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(916_132_831L)]
        [InlineData(440_201_825_775L)]
        [InlineData(3_521_614_606_207L)]
        public void Decode_reverses_encode(long value)
        {
            Base62Encoder.Decode(Base62Encoder.Encode(value)).Should().Be(value);
        }

        [Fact]
        public void Round_trip_over_seeded_random_values()
        {
            Random random = new Random(7007);
            for (int i = 0; i < 10_000; i++)
            {
                long value = random.NextInt64(Base62Encoder.CodeSpaceSize);
                Base62Encoder.Decode(Base62Encoder.Encode(value)).Should().Be(value);
            }
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(3_521_614_606_208L)]
        [InlineData(long.MaxValue)]
        public void Encode_outside_code_space_throws(long value)
        {
            Action act = () => Base62Encoder.Encode(value);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Theory]
        [InlineData("")]
        [InlineData("000000")]
        [InlineData("00000000")]
        [InlineData("000000-")]
        [InlineData("000000é")]
        public void Decode_wrong_shape_throws(string code)
        {
            Action act = () => Base62Encoder.Decode(code);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Decode_null_throws()
        {
            Action act = () => Base62Encoder.Decode(null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
