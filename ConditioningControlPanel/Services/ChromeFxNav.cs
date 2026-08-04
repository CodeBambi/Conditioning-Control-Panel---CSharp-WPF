using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The pure decisions behind the chrome tab transition (PR-1): which way a tab should slide
    /// in, and which tabs must not slide at all. Kept out of MainWindow so it can be reasoned
    /// about - and tested - without a window, a dispatcher or a render pass.
    /// </summary>
    public static class ChromeFxNav
    {
        /// <summary>
        /// Tab keys in nav-strip order: row one left-to-right, then row two. The incoming tab's
        /// position relative to the outgoing one decides the slide direction, so this list is the
        /// visual order the user sees, not an arbitrary enum order.
        /// </summary>
        public static readonly string[] NavOrder =
        {
            "settings", "presets", "quests", "programs", "enhancements", "deeper",
            "availablesubjects", "assets",
            "achievements", "leaderboard", "companion", "discord", "lab",
        };

        /// <summary>
        /// Tabs that host a WebView2. It is a native HWND in its own airspace: it ignores WPF
        /// opacity and does NOT follow a RenderTransform, so sliding or staggering one of these
        /// would tear the browser away from its card for the length of the transition. They get
        /// the crossfade only.
        /// </summary>
        private static readonly HashSet<string> Airspace =
            new(StringComparer.OrdinalIgnoreCase) { "settings", "progression", "gradedintake" };

        public static bool IsAirspaceTab(string? tab) =>
            !string.IsNullOrEmpty(tab) && Airspace.Contains(tab);

        /// <summary>Position in the nav strip, or -1 for a tab reachable only from a submenu.</summary>
        public static int IndexOf(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return -1;
            // "progression" is a legacy alias that lands on the Dashboard.
            if (string.Equals(tab, "progression", StringComparison.OrdinalIgnoreCase)) tab = "settings";
            return Array.FindIndex(NavOrder, k => string.Equals(k, tab, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The offset the incoming tab starts from, in px, before easing to (0,0). A tab further
        /// right in the strip enters from the right and one further left enters from the left, so
        /// the movement matches the direction the user's eye just travelled. When either tab is
        /// off the strip (or it is the same tab) there is no honest direction to imply, and the
        /// tab rises instead.
        /// </summary>
        public static (double X, double Y) EntranceOffset(string? fromTab, string? toTab, double px)
        {
            int from = IndexOf(fromTab);
            int to = IndexOf(toTab);
            if (from < 0 || to < 0 || from == to) return (0, px);
            return (to > from ? px : -px, 0);
        }
    }
}
