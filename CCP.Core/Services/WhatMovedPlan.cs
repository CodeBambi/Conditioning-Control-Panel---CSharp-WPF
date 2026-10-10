using System.Collections.Generic;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    // Moved from ConditioningControlPanel/Windows/WhatMovedCard.xaml.cs (unchanged) so both heads
    // draw the same table and apply the same one-time rule.

    /// <summary>One move on the "What moved" card: what the user sees, and where Show me goes.
    /// <paramref name="Tab"/> is a ShowTab key; <paramref name="SettingsSection"/>, when set, is an
    /// AppSettingsTabView section opened inside the Settings gear instead.</summary>
    public sealed record WhatMovedRow(string Id, string Glyph, string TextKey, string Tab, string? SettingsSection = null)
    {
        /// <summary>Resolved at display time, like every other caption.</summary>
        public string Text => Loc.Get(TextKey);

        /// <summary>The key the rail/strip glows after Show me: the Settings section when there is
        /// one, the tab key otherwise.</summary>
        public string GlowKey => SettingsSection ?? Tab;

        /// <summary>Screen-reader name of the row's Show me button: the verb plus where it goes,
        /// so five buttons do not all read "Show me".</summary>
        public string ShowMeName => Loc.GetF("whatmoved_show_named", Text);

        /// <summary>UI Automation names an ItemsControl item by ToString; a record's generated one
        /// read "WhatMovedRow { Id = premium, ... }" to a screen reader (desk run 2026-10-06).</summary>
        public override string ToString() => Text;
    }

    /// <summary>
    /// The card's rows and its one-time rule. Pure, so the tests read the same table the card draws.
    /// </summary>
    public static class WhatMovedPlan
    {
        /// <summary>Five moves, the ones support will be asked about first. Order is the order on the card.</summary>
        public static readonly IReadOnlyList<WhatMovedRow> Rows = new[]
        {
            new WhatMovedRow("premium", "⭐", "whatmoved_row_premium", "appsettings", "account"),
            new WhatMovedRow("lobby", "🛰️", "whatmoved_row_lobby", "availablesubjects"),
            new WhatMovedRow("monitors", "🖥️", "whatmoved_row_monitors", "appsettings", "monitors"),
            new WhatMovedRow("tabs", "📑", "whatmoved_row_tabs", "studio"),
            new WhatMovedRow("games", "🎮", "whatmoved_row_games", "play"),
        };

        /// <summary>
        /// Offered once, and only to someone who used the app before this version: a fresh
        /// install learns the new layout as the only layout, so nothing "moved" for them.
        /// </summary>
        public static bool ShouldOffer(int shownCount, bool freshInstall) => !freshInstall && shownCount <= 0;
    }
}
