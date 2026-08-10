namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Settings · NOTIFICATIONS compat surface. <c>ChkIntakeNudge</c> is read by
    /// <c>MainWindow.Settings.cs:LoadSettings</c> and written by <c>SaveSettings</c>; it used to
    /// resolve as <c>SettingsTab.ChkIntakeNudge</c> inside the collapsed LegacyDashboardHost.
    /// </summary>
    public partial class AppSettingsTabView
    {
        internal System.Windows.Controls.CheckBox ChkIntakeNudge => SectionNotifications.ChkIntakeNudge;
    }
}
