using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS · NOTIFICATIONS. Holds <c>ChkIntakeNudge</c>, moved out of the collapsed
    /// LegacyDashboardHost. No shims: the checkbox deliberately has no Changed handler - the value
    /// is read at launch by <c>MainWindow.Marquee.cs:CheckIntakePassNudge</c> and round-trips
    /// through LoadSettings/SaveSettings only.
    /// </summary>
    public partial class NotificationsSettingsSection : UserControl
    {
        public NotificationsSettingsSection()
        {
            InitializeComponent();
        }
    }
}
