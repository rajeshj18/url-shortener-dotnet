namespace UrlShortener.Domain.Proof
{
    /// <summary>PROOF ONLY (TSK-04 AC3c, never merged): Domain code with no tests, so coverage drops below 80%.</summary>
    public static class CoverageProbe
    {
        /// <summary>Uncovered logic.</summary>
        /// <param name="value">Any value.</param>
        /// <returns>A classification.</returns>
        public static string Classify(int value)
        {
            if (value < 0)
            {
                return "negative";
            }

            if (value == 0)
            {
                return "zero";
            }

            return "positive";
        }
    }
}
