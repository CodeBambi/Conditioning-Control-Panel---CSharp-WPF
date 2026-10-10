using System;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Controls.Header;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>WPF MainWindow.PremiumSpark.cs: the header spark reads tier and motion itself; the
    /// shell routes its click and pokes it from the tier choke point (RefreshProfileBubble, WPF
    /// UpdatePatreonUI). Motion changes reach it through AmbientFxCanvas.Env.MotionGateChanged.</summary>
    public partial class MainShellWindow
    {
        private void InitializePremiumSpark()
        {
            if (Named<PremiumSpark>("HeaderPremiumSpark") is { } spark) spark.Click += (_, _) => HeaderPremiumSpark_Click();
        }

        /// <summary>WPF HeaderPremiumSpark_Click: "premium" once the nav table knows it, else "exclusives".</summary>
        internal void HeaderPremiumSpark_Click()
        {
            try { ShowTab(PremiumSparkRules.TargetTab(NavSections.SectionForTab)); }
            catch (Exception ex) { Log.Debug("Premium spark click failed: {E}", ex.Message); }
        }

        internal void RefreshPremiumSpark()
        {
            try { Named<PremiumSpark>("HeaderPremiumSpark")?.Refresh(); }
            catch (Exception ex) { Log.Debug("Premium spark refresh failed: {E}", ex.Message); }
        }
    }
}
