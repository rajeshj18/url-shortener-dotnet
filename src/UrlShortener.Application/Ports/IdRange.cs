using System;

namespace UrlShortener.Application.Ports
{
    /// <summary>
    /// A leased, contiguous range of IDs <c>[Start, EndExclusive)</c>, handed out by one Api instance from memory
    /// (ADR-0005). Ranges from the same region never overlap; the atomic lease statement guarantees that.
    /// </summary>
    public sealed class IdRange
    {
        /// <summary>Creates a range.</summary>
        /// <param name="start">First ID (inclusive), ≥ 0.</param>
        /// <param name="endExclusive">End (exclusive), greater than <paramref name="start"/>.</param>
        public IdRange(long start, long endExclusive)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "A range must start at 0 or above.");
            }

            if (endExclusive <= start)
            {
                throw new ArgumentOutOfRangeException(nameof(endExclusive), "A range must contain at least one ID.");
            }

            Start = start;
            EndExclusive = endExclusive;
        }

        /// <summary>First ID (inclusive).</summary>
        public long Start { get; }

        /// <summary>End of the range (exclusive).</summary>
        public long EndExclusive { get; }

        /// <summary>Number of IDs in the range.</summary>
        public long Count => EndExclusive - Start;
    }
}
