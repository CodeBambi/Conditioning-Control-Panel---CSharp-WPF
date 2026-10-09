namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The dashboard browser fold, as pure state. One bool is the whole truth -
    /// <c>AppSettings.DashboardBrowserCollapsed</c> - and every surface the fold owns is derived
    /// from it here rather than being set independently somewhere in the paint.
    ///
    /// <para>This exists because the first cut had five surfaces (two row heights, the body's
    /// Visibility, the chevron glyph, the tooltip, the billboard's Visibility) each written at its
    /// own moment, some of them inside an animation's Completed handler. Any path that missed one
    /// left the card looking folded while the setting said open. Now
    /// <c>MainWindow.DashboardFold.cs</c> re-reads the bool and re-derives all of them in one
    /// idempotent settle, and an interrupted animation can only ever land on the truth.</para>
    /// </summary>
    public static class BrowserFoldRule
    {
        /// <summary>Shut: the arrow points down, because clicking it opens the card.</summary>
        public const string ChevronShut = "▾";

        /// <summary>Open: the arrow points up, because clicking it shuts the card.</summary>
        public const string ChevronOpen = "▴";

        public static bool Toggle(bool collapsed) => !collapsed;

        public static string Chevron(bool collapsed) => collapsed ? ChevronShut : ChevronOpen;

        public static string TooltipKey(bool collapsed) =>
            collapsed ? "tooltip_browser_unfold" : "tooltip_browser_fold";

        /// <summary>The card below the header, and with it the WebView2's native window.</summary>
        public static bool BodyShown(bool collapsed) => !collapsed;

        /// <summary>The billboard only exists in the row a shut card gives back.</summary>
        public static bool BillboardShown(bool collapsed) => collapsed;

        /// <summary>Open the card takes the star row; shut it is Auto and the fold row takes it.</summary>
        public static bool CardRowIsStar(bool collapsed) => !collapsed;

        /// <summary>The two are never both true and never both false: one row owns the space.</summary>
        public static bool FoldRowIsStar(bool collapsed) => collapsed;
    }
}
