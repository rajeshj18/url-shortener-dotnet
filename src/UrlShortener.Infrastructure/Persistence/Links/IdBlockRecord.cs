namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// A region block of IDs (ADR-0005, LLD §6.4). The table and its constraints are created here (TSK-06); seeding
    /// block <c>B-001</c> and leasing from it belong to short-code generation (TSK-07).
    /// </summary>
    public sealed class IdBlockRecord
    {
        /// <summary>Block ID from the registry, e.g. <c>B-001</c>. Unique across all regions.</summary>
        public string BlockId { get; set; } = string.Empty;

        /// <summary>The deployment region that owns the block.</summary>
        public short RegionIndex { get; set; }

        /// <summary>First ID of the block (inclusive).</summary>
        public long RangeStart { get; set; }

        /// <summary>End of the block (exclusive).</summary>
        public long RangeEnd { get; set; }

        /// <summary>The next ID not yet leased.</summary>
        public long NextId { get; set; }

        /// <summary><c>active</c>, <c>reserved</c> or <c>exhausted</c>.</summary>
        public string Status { get; set; } = string.Empty;
    }
}
