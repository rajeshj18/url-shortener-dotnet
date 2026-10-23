using FluentAssertions;
using Xunit;

namespace UrlShortener.UnitTests.Proof
{
    /// <summary>PROOF ONLY (TSK-04 AC3a/AC3b, never merged).</summary>
    public sealed class GateProofTests
    {
        [Fact]
        public void Deliberately_failing_test_for_gate_proof()
        {
            (1 + 1).Should().Be(3);
        }

        [Fact(Skip = "TSK-04 AC3b: a skipped test must fail the gate")]
        public void Deliberately_skipped_test_for_gate_proof()
        {
            true.Should().BeTrue();
        }
    }
}
