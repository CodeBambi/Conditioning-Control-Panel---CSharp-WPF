using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · NOTIFICATIONS, ported from the WPF head. <c>ChkIntakeNudge</c> has no handler on
    /// purpose (read at launch, round-trips through Load/SaveSettings). <c>ChkSuppressPerkNotifications</c>
    /// is a live editor on WPF; here it is a stub until a settings surface exists on this head.
    /// </summary>
    public partial class NotificationsSettingsSection : UserControl
    {
        private bool _isLoading = true;

        public NotificationsSettingsSection()
        {
            InitializeComponent();
            ChkSuppressPerkNotifications.IsCheckedChanged += ChkSuppressPerkNotifications_Changed;
            SyncFromSettings();
        }

        /// <summary>Paints the live row from settings without raising an edit.</summary>
        internal void SyncFromSettings()
        {
            _isLoading = true;
            try
            {
                // ponytail: needs App.Settings (SuppressPerkNotifications), wired when it moves to Core
            }
            finally { _isLoading = false; }
        }

        private void ChkSuppressPerkNotifications_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            // ponytail: needs App.Settings (SuppressPerkNotifications) + Save, wired when it moves to Core
        }
    }
}
