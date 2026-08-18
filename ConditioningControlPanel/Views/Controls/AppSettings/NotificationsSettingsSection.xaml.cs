using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS · NOTIFICATIONS. Two rows with deliberately different plumbing:
    ///
    /// <c>ChkIntakeNudge</c> (moved out of the collapsed LegacyDashboardHost) has no Changed
    /// handler on purpose - the value is read at launch by
    /// <c>MainWindow.Marquee.cs:CheckIntakePassNudge</c> and round-trips through
    /// <c>MainWindow.Settings.cs</c> LoadSettings/SaveSettings only. Nothing here touches it.
    ///
    /// <c>ChkSuppressPerkNotifications</c> is a LIVE editor: the sites it gates (the lucky-proc
    /// toast, the Pink Rush popup, the quest-complete popup and their sounds) read
    /// <c>App.PerkNotificationsSuppressed</c> at the instant they fire, so the flip has to reach
    /// settings and disk immediately rather than at the next SaveSettings sweep. Same contract as
    /// PerformanceSettingsSection: write, then <c>App.Settings?.Save()</c>.
    ///
    /// Seeding must not look like a user edit. This control is built with the rest of MainWindow,
    /// before <c>App.Settings</c> is guaranteed to exist, and re-seeds every time the Settings door
    /// opens; assigning IsChecked raises the same events a click does, so every assignment happens
    /// inside the <c>_isLoading</c> guard, which starts <c>true</c>.
    /// </summary>
    public partial class NotificationsSettingsSection : UserControl
    {
        private bool _isLoading = true;

        public NotificationsSettingsSection()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings != null) App.Settings.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings != null) App.Settings.CurrentReplaced -= OnCurrentReplaced;
        }

        /// <summary>
        /// A cloud restore swaps the whole <c>Current</c> instance out from under us. Every tab view
        /// stays alive for the app's lifetime, so without this the page would keep showing the
        /// pre-restore value until the next time it became visible.
        /// </summary>
        private void OnCurrentReplaced()
        {
            // DispatcherPriority.Normal (the default) on purpose - Loaded priority gets starved.
            Dispatcher.BeginInvoke(new Action(SyncFromSettings));
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible && visible) SyncFromSettings();
        }

        /// <summary>
        /// Paints the live row from settings without raising an edit. Safe to call at any time,
        /// including before <c>App.Settings</c> exists (render tests, early startup).
        /// ChkIntakeNudge is NOT painted here - LoadSettings owns it and painting it twice from
        /// two owners is how a toggle starts flickering back to its old value.
        /// </summary>
        internal void SyncFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            _isLoading = true;
            try
            {
                ChkSuppressPerkNotifications.IsChecked = s.SuppressPerkNotifications;
            }
            finally { _isLoading = false; }
        }

        private void ChkSuppressPerkNotifications_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            s.SuppressPerkNotifications = ChkSuppressPerkNotifications.IsChecked ?? false;
            App.Settings?.Save();
        }
    }
}
