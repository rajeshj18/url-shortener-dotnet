namespace UrlShortener.Domain
{
    /// <summary>The lifecycle state of a short link. <c>Expired</c> is added by the brownfield expiry task (TSK-14).</summary>
    public enum LinkState
    {
        /// <summary>The link redirects (302).</summary>
        Active = 0,

        /// <summary>The owner deleted the link. It returns 410, and its code is never reused (A-25).</summary>
        Deleted = 1,
    }
}
