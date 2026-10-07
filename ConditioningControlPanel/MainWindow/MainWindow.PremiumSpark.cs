using System;
using System.Windows;
using ConditioningControlPanel.Controls.Header;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The header Premium spark (polish 12). The control reads the tier and the motion level and
    /// parks itself; MainWindow only routes its click and pokes it from the two choke points that
    /// already repaint tier and motion chrome (UpdatePatreonUI's profile refresh, and
    /// CmbMotionLevel_SelectionChanged).
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Opens Premium. Once the nav table knows "premium" that key, otherwise the old
        /// "exclusives" key, which redirects to wherever this build keeps the vault.</summary>
        private void HeaderPremiumSpark_Click(object sender, RoutedEventArgs e)
        {
            try { ShowTab(PremiumSparkRules.TargetTab(NavSections.SectionForTab)); }
            catch (Exception ex) { App.Logger?.Debug("Premium spark click failed: {E}", ex.Message); }
        }

        /// <summary>Re-reads tier and motion. Cheap and idempotent.</summary>
        internal void RefreshPremiumSpark()
        {
            try { HeaderPremiumSpark?.Refresh(); }
            catch (Exception ex) { App.Logger?.Debug("Premium spark refresh failed: {E}", ex.Message); }
        }
    }
}
