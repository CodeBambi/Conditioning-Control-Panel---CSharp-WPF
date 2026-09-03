namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Where a user is sent to get a build. The updater that downloads and runs an installer is
    /// per-platform and stays in a head; the address is not, and every head's "download it
    /// yourself" fallback must point at the same page.
    /// </summary>
    public static class ReleaseLinks
    {
        /// <summary>The GitHub releases page — the manual-download fallback on every head.</summary>
        public const string ReleasesPageUrl =
            "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/latest";
    }
}
