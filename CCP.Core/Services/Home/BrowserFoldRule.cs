// PORTED verbatim from WPF 7.1.5 ConditioningControlPanel/Services/BrowserFoldRule.cs (parity lane E3).
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
        /// <summary>Shut: the arrow points down, because clicking it opens the card. Segoe MDL2
        /// ChevronDown (E70D); the glyph TextBlock carries the font fallback.</summary>
        public const string ChevronShut = "\uE70D";

        /// <summary>Open: the arrow points up, because clicking it shuts the card. Segoe MDL2
        /// ChevronUp (E70E).</summary>
        public const string ChevronOpen = "\uE70E";

        /// <summary>The arrow pill's paint, as alpha over Home's hue (owner, 2026-10-06: "an
        /// unmissable dropdown arrow"): 24% fill, 40% on hover, a 70% border.</summary>
        public const double ArrowFill = 0.24;
        public const double ArrowHoverFill = 0.40;
        public const double ArrowBorder = 0.70;

        /// <summary>The breathing glow while folded: low to high and back over one breath.
        /// Static at <see cref="GlowStatic"/> when ambient loops are refused, none at Motion Off,
        /// none while open.</summary>
        public const double GlowLow = 0.35;
        public const double GlowHigh = 0.80;
        public const double GlowStatic = 0.60;
        public const int GlowBreathMs = 2400;

        public static bool Toggle(bool collapsed) => !collapsed;

        public static string Chevron(bool collapsed) => collapsed ? ChevronShut : ChevronOpen;

        public static string TooltipKey(bool collapsed) =>
            collapsed ? "tooltip_browser_unfold" : "tooltip_browser_fold";

        /// <summary>The pill's label says what the click will do.</summary>
        public static string LabelKey(bool collapsed) =>
            collapsed ? "btn_browser_fold_show" : "btn_browser_fold_hide";

        /// <summary>The glow only ever marks a SHUT card: a closed browser has to read as openable.</summary>
        public static bool Glows(bool collapsed) => collapsed;

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
