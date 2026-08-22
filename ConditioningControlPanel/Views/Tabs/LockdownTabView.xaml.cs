using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class LockdownTabView : UserControl
    {
        /// <summary>
        /// True while LoadPossessionSettings is writing the controls, so the Checked/Unchecked
        /// handlers do not write the value they were just given straight back into settings (and,
        /// worse, re-enter through the master switch's grey-out pass).
        /// </summary>
        private bool _loadingPossession;

        public LockdownTabView()
        {
            InitializeComponent();

            // Tabs are shown and hidden rather than rebuilt, so Loaded fires once. Re-read on every
            // show for the same reason BambiTakeoverTabView does: something else can move these
            // behind our back (a settings import replaces the whole object, a future safety panic
            // clears a flag) and a stale toggle here is a toggle that lies.
            Loaded += (_, _) => LoadPossessionSettings();
            IsVisibleChanged += (_, _) => { if (IsVisible) LoadPossessionSettings(); };
        }

        // ==== Possession + Safeties ======================================================

        /// <summary>Paints every control on the card from AppSettings. Never writes anything back.</summary>
        private void LoadPossessionSettings()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                _loadingPossession = true;

                ChkPossessionEnabled.IsChecked = s.LockdownPossessionEnabled;
                ChkPossTripwires.IsChecked = s.LockdownTripwiresEnabled;
                ChkPossWarden.IsChecked = s.LockdownWardenEnabled;
                ChkPossPhotosafe.IsChecked = s.LockdownPhotosafe;

                ChkLockdownStrict.IsChecked = s.LockdownForceStrictLock;
                ChkLockdownNoPanic.IsChecked = s.LockdownDisablePanicKey;
                ChkLockdownSysKeys.IsChecked = s.LockdownBlockSystemKeys;

                ApplyIntensityPills(s.LockdownPossessionIntensity);
                ApplyPossessionEnabledLook(s.LockdownPossessionEnabled);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown card: failed to load possession settings");
            }
            finally
            {
                _loadingPossession = false;
            }
        }

        /// <summary>The three pills behave as radio buttons: exactly one is lit, and clicking the lit
        /// one cannot turn the setting off, because there is no "no intensity".</summary>
        private void ApplyIntensityPills(int intensity)
        {
            BtnPossGentle.IsChecked = intensity == 0;
            BtnPossEerie.IsChecked = intensity == 1;
            BtnPossFullDoki.IsChecked = intensity == 2;
        }

        /// <summary>Greys rather than hides: see the XAML comment on the Possession block.</summary>
        private void ApplyPossessionEnabledLook(bool on)
        {
            if (PossessionBlock == null) return;
            PossessionBlock.IsEnabled = on;
            PossessionBlock.Opacity = on ? 1.0 : 0.4;
        }

        private void ChkPossessionEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                s.LockdownPossessionEnabled = ChkPossessionEnabled.IsChecked == true;
                ApplyPossessionEnabledLook(s.LockdownPossessionEnabled);
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown card: failed to write LockdownPossessionEnabled");
            }
        }

        private void PossIntensity_Click(object sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                // Tag carries the value so all three pills share one handler and the mapping lives
                // next to the label the user actually reads.
                if (sender is not ToggleButton tb || tb.Tag is not string tag || !int.TryParse(tag, out var value))
                    return;

                s.LockdownPossessionIntensity = value;

                _loadingPossession = true;
                try { ApplyIntensityPills(s.LockdownPossessionIntensity); }
                finally { _loadingPossession = false; }

                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown card: failed to write LockdownPossessionIntensity");
            }
        }

        private void ChkPossTripwires_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                if (App.Settings?.Current == null) return;
                App.Settings.Current.LockdownTripwiresEnabled = ChkPossTripwires.IsChecked == true;
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown card: failed to write LockdownTripwiresEnabled");
            }
        }

        private void ChkPossWarden_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                if (App.Settings?.Current == null) return;
                App.Settings.Current.LockdownWardenEnabled = ChkPossWarden.IsChecked == true;
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown card: failed to write LockdownWardenEnabled");
            }
        }

        private void ChkPossPhotosafe_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                if (App.Settings?.Current == null) return;
                App.Settings.Current.LockdownPhotosafe = ChkPossPhotosafe.IsChecked == true;
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown card: failed to write LockdownPhotosafe");
            }
        }

        /// <summary>
        /// All three safeties share one handler: they are read together on Activate and none of them
        /// does anything until then, so there is nothing per-toggle to react to.
        /// </summary>
        private void ChkLockdownSafety_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                s.LockdownForceStrictLock = ChkLockdownStrict.IsChecked == true;
                s.LockdownDisablePanicKey = ChkLockdownNoPanic.IsChecked == true;
                s.LockdownBlockSystemKeys = ChkLockdownSysKeys.IsChecked == true;
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown card: failed to write the lockdown safeties");
            }
        }

        // ==== forwarded to MainWindow ====================================================

        private void BtnActivateLockdown_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnActivateLockdown_Click(sender, e);
        }
        private void BtnGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGateUnlock_Click(sender, e);
        }
        private void TxtLockdownExit_KeyDown(object sender, KeyEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtLockdownExit_KeyDown(sender, e);
        }
        private void TxtLockdownTimer_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtLockdownTimer_Click(sender, e);
        }
    }
}
