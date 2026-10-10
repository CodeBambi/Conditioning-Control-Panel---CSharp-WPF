// What is left of ConditioningControlPanel/MainWindow/MainWindow.NavPremiumTags.cs. The gold ★ rode
// after a rail entry's label; the section rail (cd426fe36) has no entries, so WPF deleted the tags
// and the strip's pills wear the tier sign instead (SectionTabStrip.PillLocked). What stays is the
// lock answer (WPF MainWindow.NavRail.cs:936) and the repaint hook App.axaml.cs RepaintVeils calls
// (either provider's TierChanged + DailyFreeService.TodayChanged + IntakePass.PassStateChanged).

using System.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>A tier change repaints the dashboard's favorites chips, which read the same
        /// lock answer (WPF :158).</summary>
        internal void RefreshNavPremiumTags() => RefreshFavoritesRail();

        /// <summary>WPF MainWindow.NavRail.cs:936, over Core's roster and the entitlement seam.
        /// Fails to unlocked on anything unexpected, as WPF does.</summary>
        private static bool IsNavEntryLocked(string exclusiveKey)
        {
            try
            {
                var feature = Models.ExclusiveFeature.All.FirstOrDefault(f => f.Key == exclusiveKey);
                if (feature == null) return false;
                var state = feature.GateState();
                return state == Models.ExclusiveGateState.Locked && !feature.IsFreeToday(state);
            }
            catch { return false; }
        }
    }
}
