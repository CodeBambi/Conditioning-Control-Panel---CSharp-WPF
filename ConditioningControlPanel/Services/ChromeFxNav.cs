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
        /// Tab keys in nav order, derived from <see cref="ConditioningControlPanel.Nav.NavSections"/>
        /// (nav rework 2026-10-06): each section's Tab and Zone keys in pill order, hidden pages
        /// included (they still get a slide direction), windows and launchers left out (they have
        /// no tab transition), and the Settings gear as its page key "appsettings", last. The
        /// incoming tab's position relative to the outgoing one decides the slide direction, so
        /// this is the visual order the user reads: rail top to bottom, then pills left to right.
        /// Old keys ("lab", "progression") score through the aliases in <see cref="IndexOf"/>;
        /// "exclusives" is a redirect now and is off the strip.
        /// </summary>
        public static readonly string[] NavOrder = BuildNavOrder();

        private static string[] BuildNavOrder()
        {
            var keys = new List<string>();
            foreach (var section in ConditioningControlPanel.Nav.NavSections.Order)
            {
                if (section.Key == ConditioningControlPanel.Nav.NavSections.Settings)
                {
                    keys.Add(section.DefaultTab);   // "appsettings": its pills are Settings zones
                    continue;
                }
                foreach (var t in section.Tabs)
                    if (t.Key != "spiral" && t.Kind is ConditioningControlPanel.Nav.NavTabKind.Tab
                               or ConditioningControlPanel.Nav.NavTabKind.Zone)
                        keys.Add(t.Key);
            }
            // "spiral" stays APPENDED: it is an airspace tab (never slides) and SpiralRoomTests
            // pins it last.
            keys.Add("spiral");
            return keys.ToArray();
        }

        /// <summary>
        /// Tabs that host a WebView2. It is a native HWND in its own airspace: it ignores WPF
        /// opacity and does NOT follow a RenderTransform, so sliding or staggering one of these
        /// would tear the browser away from its card for the length of the transition. They get
        /// the crossfade only.
        /// </summary>
        /// <remarks>"justdrop" was here while the shop was a tab. It is a separate window now, so
        /// it has no tab transition to be exempted from at all.</remarks>
        private static readonly HashSet<string> Airspace =
            new(StringComparer.OrdinalIgnoreCase) { "settings", "progression", "gradedintake", "spiral" };

        public static bool IsAirspaceTab(string? tab) =>
            !string.IsNullOrEmpty(tab) && Airspace.Contains(tab);

        /// <summary>Position in the nav strip, or -1 for a tab reachable only from a submenu.</summary>
        public static int IndexOf(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return -1;
            // "progression" is a legacy alias that lands on the Dashboard.
            if (string.Equals(tab, "progression", StringComparison.OrdinalIgnoreCase)) tab = "settings";
            // "lab" is a legacy alias that lands on the Play door's card wall (Phase 6). Without
            // it every caller still passing "lab" would score -1 and get the fallback "rise"
            // entrance instead of the horizontal slide its neighbours get.
            if (string.Equals(tab, "lab", StringComparison.OrdinalIgnoreCase)) tab = "play";
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
