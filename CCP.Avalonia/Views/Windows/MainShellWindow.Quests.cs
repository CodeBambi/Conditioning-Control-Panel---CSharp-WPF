// PORTED, but not here: MainWindow.Quests.cs's OnQuestCompleted / OnQuestProgressChanged live in
// Views/Tabs/QuestsTabView.Quests.cs, subscribed to App.Quests (the Core QuestService). Every
// control they paint is that view's own x:Name field, so the view owns them.
// CelebrateQuestComplete's on-tab burst is QuestsTabView.Fx.cs; the haptic post is App.PlayQuestCompletionEffects.
// The voice line (WPF App.Flash.PlayRandomSound) and the off-tab rail burst are there too (k2).
// ponytail: still missing from OnQuestCompleted - RefreshQuestStamps.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF MainWindow.TabNavigation.cs NavAnchorForTab: where a burst aimed at a tab lands on the
        /// rail, its section's row. Null for an unknown tab. Never throws.</summary>
        internal global::Avalonia.Controls.Control? NavAnchorForTab(string? tabKey)
        {
            try
            {
                var section = global::ConditioningControlPanel.Nav.NavSections.SectionForTab(tabKey);
                if (section == null) return null;
                foreach (var row in _navSectionRows)
                    if (string.Equals(row.Section, section, System.StringComparison.Ordinal)) return row.Button;
            }
            catch (System.Exception ex) { Serilog.Log.Debug("NavAnchorForTab({Tab}): {E}", tabKey, ex.Message); }
            return null;
        }
    }

}
