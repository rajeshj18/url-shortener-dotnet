namespace UrlShortener.Domain
{
    /// <summary>The lifecycle state of a short link.</summary>
    public enum LinkState
    {
        /// <summary>The link redirects (302).</summary>
        Active = 0,

        /// <summary>The owner deleted the link. It returns 410, and its code is never reused (A-25).</summary>
        Deleted = 1,

        /// <summary>The link's <c>expiresAt</c> has passed (FR-04, TSK-14). It returns 410; the code is never reused.</summary>
        Expired = 2,
    }
}
