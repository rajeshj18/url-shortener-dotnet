using System;

namespace UrlShortener.UnitTests.Core
{
    /// <summary>A clock the test controls: wall time and a monotonic timestamp, both moved by <see cref="Advance"/>.</summary>
    internal sealed class TestTime : TimeProvider
    {
        private long _ticks;

        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }

        public override long GetTimestamp()
        {
            return _ticks;
        }

        public void Advance(TimeSpan by)
        {
            _ticks += by.Ticks;
            Now += by;
        }
    }
}
