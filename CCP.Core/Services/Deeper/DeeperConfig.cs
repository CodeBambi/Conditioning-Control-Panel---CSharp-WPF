namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Central configuration for Deeper. Anything that's currently a magic
    /// string spread across multiple files should live here so adding the
    /// next supported site (or tweaking a cap) only changes one place.
    /// </summary>
    internal static class DeeperConfig
    {
        /// <summary>
        /// Hosts the Deeper editor and player WebView2s are allowed to
        /// navigate to. Used by both the editor's preview pane and the
        /// player's video pane via UrlSafety.HostMatches (subdomain match).
        /// Sites added here MUST also be safe to embed (no clickjacking
        /// concerns, sane CSP, no auth-token URL params).
        /// </summary>
        public static readonly string[] PreviewHostAllowlist =
        {
            "hypnotube.com",
            "tiktok.com",
        };

        /// <summary>
        /// Hosts the metadata scraper (HtMetadataFetcher) is allowed to
        /// fetch from for title/description/poster auto-discovery.
        /// Narrower than PreviewHostAllowlist because TikTok actively
        /// blocks server-side scraping anyway.
        /// </summary>
        public static readonly string[] MetadataScrapeAllowlist =
        {
            "hypnotube.com",
        };
    }
}
