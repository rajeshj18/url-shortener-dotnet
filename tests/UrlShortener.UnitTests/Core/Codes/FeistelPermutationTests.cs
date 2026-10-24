using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UrlShortener.Core.Codes;
using Xunit;

namespace UrlShortener.UnitTests.Core.Codes
{
    /// <summary>
    /// TSK-07 AC2/AC3: the permutation is one-to-one on <c>[0, 62⁷)</c>, so generated codes can never collide, and
    /// consecutive IDs don't give consecutive-looking codes. Property-style tests with seeded loops (decision D1).
    /// </summary>
    public sealed class FeistelPermutationTests
    {
        private const string Passphrase = "unit-test-feistel-passphrase-0123456789";
        private const long Space = Base62Encoder.CodeSpaceSize;

        private readonly FeistelPermutation _permutation = FeistelPermutation.FromPassphrase(Passphrase);

        [Fact]
        public void Invert_reverses_permute_for_100k_random_ids()
        {
            Random random = new Random(42);
            for (int i = 0; i < 100_000; i++)
            {
                long id = random.NextInt64(Space);
                long permuted = _permutation.Permute(id);

                permuted.Should().BeInRange(0, Space - 1);
                _permutation.Invert(permuted).Should().Be(id);
            }
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(2_097_151L)]           // 2^21 - 1: the lower half full
        [InlineData(2_097_152L)]           // 2^21: first value with a non-zero upper half
        [InlineData(440_201_825_775L)]     // last ID of B-001
        [InlineData(440_201_825_776L)]     // first ID after B-001
        [InlineData(3_521_614_606_207L)]   // 62^7 - 1
        public void Invert_reverses_permute_at_boundaries(long id)
        {
            long permuted = _permutation.Permute(id);

            permuted.Should().BeInRange(0, Space - 1);
            _permutation.Invert(permuted).Should().Be(id);
        }

        [Fact]
        public void No_duplicate_codes_across_1m_consecutive_ids()
        {
            HashSet<long> seen = new HashSet<long>();
            for (long id = 0; id < 1_000_000; id++)
            {
                seen.Add(_permutation.Permute(id)).Should().BeTrue("ID {0} must not collide", id);
            }
        }

        [Fact]
        public void Consecutive_ids_do_not_share_a_prefix_pattern()
        {
            // Sequential codes for 1,000 consecutive IDs would all start with '0'. Scrambled codes start with
            // almost every one of the 62 characters.
            int distinctFirst = Enumerable.Range(0, 1_000)
                .Select(id => Base62Encoder.Encode(_permutation.Permute(id))[0])
                .Distinct()
                .Count();

            distinctFirst.Should().BeGreaterThan(50);
        }

        [Fact]
        public void A_different_key_gives_different_codes()
        {
            FeistelPermutation other = FeistelPermutation.FromPassphrase(Passphrase + "-other");

            int same = Enumerable.Range(0, 1_000).Count(id => _permutation.Permute(id) == other.Permute(id));

            same.Should().BeLessThan(5);
        }

        [Fact]
        public void The_same_passphrase_gives_the_same_codes()
        {
            FeistelPermutation again = FeistelPermutation.FromPassphrase(Passphrase);

            again.Permute(123_456_789).Should().Be(_permutation.Permute(123_456_789));
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(3_521_614_606_208L)]
        public void Values_outside_code_space_throw(long value)
        {
            Action permute = () => _permutation.Permute(value);
            Action invert = () => _permutation.Invert(value);

            permute.Should().Throw<ArgumentOutOfRangeException>();
            invert.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("short-secret-value-31-character")]
        public void Short_passphrase_throws_without_revealing_it(string? passphrase)
        {
            Action act = () => FeistelPermutation.FromPassphrase(passphrase);

            ArgumentException error = act.Should().Throw<ArgumentException>().Which;
            if (!string.IsNullOrEmpty(passphrase))
            {
                error.Message.Should().NotContain(passphrase);
            }
        }

        [Theory]
        [InlineData(16)]
        [InlineData(64)]
        public void Key_of_wrong_length_throws(int length)
        {
            Action act = () => _ = new FeistelPermutation(new byte[length]);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Key_is_copied()
        {
            byte[] key = new byte[32];
            FeistelPermutation permutation = new FeistelPermutation(key);
            long before = permutation.Permute(99);

            key[0] = 0xFF;

            permutation.Permute(99).Should().Be(before);
        }
    }
}
