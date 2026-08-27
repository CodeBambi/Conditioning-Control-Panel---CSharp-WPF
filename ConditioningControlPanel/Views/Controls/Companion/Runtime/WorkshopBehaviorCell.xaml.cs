using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · BEHAVIOR. See the XAML header. Pure forwarding, like every re-parented cell.</summary>
    public partial class WorkshopBehaviorCell : UserControl
    {
        /// <summary>Re-entrancy guard: seeding <c>IsChecked</c> raises Checked/Unchecked, and
        /// writing the setting back from inside that handler would be a save per reveal.</summary>
        private bool _syncingMidnight;

        public WorkshopBehaviorCell()
        {
            InitializeComponent();

            // The midnight glass is the one row here whose ENABLED state can change while the app
            // is running (the player buys it at the Prize Counter in another window), so it is
            // re-read on every reveal instead of seeded once at startup like its neighbours.
            Loaded += (_, __) => SyncTubeMidnightRow();
            IsVisibleChanged += (_, e) => { if (e.NewValue is true) SyncTubeMidnightRow(); };
        }

        /// <summary>Paint the midnight-glass row from the two facts that govern it: does the
        /// player own <c>tube_midnight</c>, and did they ask for it. Ownership failing to read
        /// answers "no" (ArcademyHostService.WalletOwnsSku never throws), which greys the row -
        /// the honest state for a prize we cannot prove was sold.</summary>
        private void SyncTubeMidnightRow()
        {
            try
            {
                bool owned = Services.Arcademy.ArcademyHostService.WalletOwnsSku(
                    Services.Arcademy.ArcademyEconomy.SkuTubeMidnight);
                bool on = App.Settings?.Current?.TubeMidnightGlass == true;

                _syncingMidnight = true;
                ChkTubeMidnightGlass.IsEnabled = owned;
                ChkTubeMidnightGlass.IsChecked = owned && on;
            }
            catch { /* a cosmetic row never gets to break the Workshop */ }
            finally { _syncingMidnight = false; }
        }

        private void ChkTubeMidnightGlass_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncingMidnight) return;
            try
            {
                if (App.Settings?.Current == null) return;
                App.Settings.Current.TubeMidnightGlass = ChkTubeMidnightGlass.IsChecked == true;
                App.Settings.Save();
                // Repaint the glass now rather than at the next attach/detach. The tube marshals
                // itself onto its own dispatcher (RefreshTubeGlass), and a tube that is not up
                // yet simply picks the new pane at its first SetTubeStyle.
                App.AvatarWindow?.RefreshTubeGlass();
            }
            catch (System.Exception ex)
            {
                App.Logger?.Debug("ChkTubeMidnightGlass_Changed: {E}", ex.Message);
            }
        }

        private void SliderIdleInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderIdleInterval_ValueChanged(sender, e);
        }

        private void SliderBubbleDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderBubbleDuration_ValueChanged(sender, e);
        }

        private void BtnChatShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnChatShortcut_Click(sender, e);
        }

        private void BtnCameraShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnCameraShortcut_Click(sender, e);
        }

        private void ChkMuteWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkMuteWhispers_Changed(sender, e);
        }

        private void ChkPauseBrowser_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkPauseBrowser_Changed(sender, e);
        }

        private void ChkVoiceLines_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkVoiceLines_Changed(sender, e);
        }
    }
}
