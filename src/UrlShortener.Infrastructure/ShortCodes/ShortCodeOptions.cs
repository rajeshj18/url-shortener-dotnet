namespace UrlShortener.Infrastructure.ShortCodes
{
    /// <summary>
    /// Configuration section <c>ShortCode</c> (LLD §8). <see cref="FeistelKey"/> is a secret: it comes from the
    /// environment or the secret store only, and must <b>never change</b> once codes exist (ADR-0005).
    /// </summary>
    public sealed class ShortCodeOptions
    {
        /// <summary>Configuration section name.</summary>
        public const string SectionName = "ShortCode";

        /// <summary>The deployment region whose ID blocks this instance leases from (ADR-0005; v1 = 0).</summary>
        public short DeploymentRegionIndex { get; set; }

        /// <summary>IDs per lease (default 10,000).</summary>
        public int LeaseSize { get; set; } = 10_000;

        /// <summary>The Feistel passphrase (≥ 32 characters). Required; there is no default.</summary>
        public string? FeistelKey { get; set; }
    }
}
