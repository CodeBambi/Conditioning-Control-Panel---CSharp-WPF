using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

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
                ChkLockdownDose.IsChecked = s.LockdownDoseKeeperEnabled;

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
                s.LockdownDoseKeeperEnabled = ChkLockdownDose.IsChecked == true;
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

        // ==== Emergency Exit =============================================================
        // The huge button's own motion. Deliberately NOT routed through Possession: this is the
        // one control on the page that must behave exactly the same every second of a lockdown,
        // so its animations live here, on the view, and answer only to the photosafe setting.

        private Storyboard? _eePulse;

        /// <summary>
        /// Starts the slow ember breath under the slab. Called when the active panel is shown
        /// (MainWindow.Lab.cs). Silent no-op under LockdownPhotosafe, where the glow simply
        /// holds at its resting value - POSSESSION.md: photosafe means no flicker, not no colour.
        /// </summary>
        internal void StartEmergencyExitPulse()
        {
            try
            {
                StopEmergencyExitPulse();
                if (EEGlow == null) return;
                if (App.Settings?.Current?.LockdownPhotosafe == true) return;
                if (!SystemParameters.ClientAreaAnimation) return;

                var opacity = new DoubleAnimation(0.26, 0.62, new Duration(TimeSpan.FromMilliseconds(1500)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                var blur = new DoubleAnimation(24, 42, new Duration(TimeSpan.FromMilliseconds(1500)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };

                _eePulse = new Storyboard();
                Storyboard.SetTarget(opacity, EEGlow);
                Storyboard.SetTargetProperty(opacity, new PropertyPath(DropShadowEffect.OpacityProperty));
                Storyboard.SetTarget(blur, EEGlow);
                Storyboard.SetTargetProperty(blur, new PropertyPath(DropShadowEffect.BlurRadiusProperty));
                _eePulse.Children.Add(opacity);
                _eePulse.Children.Add(blur);
                _eePulse.Begin();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EmergencyExit: idle pulse failed to start");
            }
        }

        /// <summary>Stops the breath and puts the glow back where the XAML left it.</summary>
        internal void StopEmergencyExitPulse()
        {
            try
            {
                _eePulse?.Stop();
                _eePulse = null;
                if (EEGlow == null) return;
                // BeginAnimation(null) hands the property back to its local value; Stop() alone
                // leaves the storyboard's hold in place on some paths.
                EEGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                EEGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
                EEGlow.Opacity = 0.32;
                EEGlow.BlurRadius = 28;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EmergencyExit: idle pulse failed to stop");
            }
        }

        private void BtnEmergencyExit_Down(object sender, MouseButtonEventArgs e) => PressEmergencyExit(true);

        private void BtnEmergencyExit_Up(object sender, MouseEventArgs e) => PressEmergencyExit(false);

        /// <summary>The slab sinks under the finger and comes back. 60 ms down, 140 ms up.</summary>
        private void PressEmergencyExit(bool down)
        {
            try
            {
                if (EEScale == null) return;
                var to = down ? 0.965 : 1.0;
                var dur = new Duration(TimeSpan.FromMilliseconds(down ? 60 : 140));
                var anim = new DoubleAnimation(to, dur)
                {
                    EasingFunction = new QuadraticEase { EasingMode = down ? EasingMode.EaseOut : EasingMode.EaseInOut },
                };
                EEScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                EEScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EmergencyExit: press animation failed");
            }
        }

        /// <summary>
        /// Opens the Emergency Exit games. The host owns everything after this line - the
        /// tripwire, the game pick, the verdict and whether the lockdown actually ends
        /// (Services/EmergencyExit/EMERGENCY_EXIT.md).
        /// </summary>
        private void BtnEmergencyExit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Services.EmergencyExit.EmergencyExitHostService.Open();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "EmergencyExit: could not open the exit games");
            }
        }
    }
}
