using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>Features surfaced as quick-toggle chips on the dashboard premium rail.</summary>
    public enum PremiumFeature { Takeover, Awareness, Haptics, Lockdown, Blink, Remote, Voice, GradedIntake }

    // Dashboard premium quick-toggle rail (left of the feature grid).
    public partial class MainWindow
    {
        private static readonly Brush PremiumDotOn = CreateFrozenBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly Brush PremiumDotOff = CreateFrozenBrush(Color.FromRgb(0x55, 0x55, 0x55));
        private bool _premiumRailSubscribed;
        private bool _lockdownEventsBound;
        private int _railLockdownMinutes = 15;
        private bool _blinkEventsBound;
        private DispatcherTimer? _blinkCountdownTimer;

        private static Brush CreateFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Subscribe to patron-status changes and paint the rail for the first time.</summary>
        internal void InitPremiumRail()
        {
            if (!_premiumRailSubscribed && App.Patreon != null)
            {
                try { App.Patreon.TierChanged += (s, e) => Dispatcher.BeginInvoke(new Action(RefreshPremiumRail)); }
                catch { }
                _premiumRailSubscribed = true;
            }
            if (!_lockdownEventsBound && App.Lockdown != null)
            {
                App.Lockdown.LockdownActivated += () => Dispatcher.BeginInvoke(new Action(() => SetLockdownActiveUi(true)));
                App.Lockdown.LockdownDeactivated += () => Dispatcher.BeginInvoke(new Action(() => SetLockdownActiveUi(false)));
                App.Lockdown.CountdownTick += ts => Dispatcher.BeginInvoke(new Action(() => UpdateLockdownCountdown(ts)));
                _lockdownEventsBound = true;
            }
            if (!_blinkEventsBound && App.BlinkTrainer != null)
            {
                App.BlinkTrainer.StateChanged += () => Dispatcher.BeginInvoke(new Action(() => SetBlinkActiveUi(App.BlinkTrainer?.IsRunning == true)));
                _blinkEventsBound = true;
            }
            RefreshPremiumRail();
        }

        // --- Blink Trainer chip: +/- duration, consent-gated start/stop, countdown ---

        internal void PremiumBlinkAdjust(int deltaMinutes)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BlinkTrainerDurationMinutes = Math.Clamp(s.BlinkTrainerDurationMinutes + deltaMinutes, 1, 120);
            App.Settings?.Save();
            if (SettingsTab?.TxtBlinkMins != null)
                SettingsTab.TxtBlinkMins.Text = s.BlinkTrainerDurationMinutes + "m";
        }

        internal void PremiumBlinkToggle()
        {
            if (App.BlinkTrainer == null) return;
            if (App.BlinkTrainer.IsRunning)
            {
                App.BlinkTrainer.Stop();
                return;
            }

            // Webcam consent first — same flow the Blink tab uses.
            if (!WebcamTrackingService.IsConsentCurrent())
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                var ok = dlg.ShowDialog();
                if (ok != true || !dlg.ConsentGiven) return;
            }

            if (!App.BlinkTrainer.Start())
            {
                MessageBox.Show(App.BlinkTrainer.LastError ?? "Could not start Blink Trainer.",
                    "Blink Trainer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SetBlinkActiveUi(bool active)
        {
            if (SettingsTab == null) return;
            if (SettingsTab.BlinkSetRow != null)
                SettingsTab.BlinkSetRow.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            if (SettingsTab.TxtBlinkCountdown != null)
                SettingsTab.TxtBlinkCountdown.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (SettingsTab.BtnBlinkGo != null)
                SettingsTab.BtnBlinkGo.Content = active ? "Stop" : "Start";

            if (active)
            {
                UpdateBlinkCountdown();
                if (_blinkCountdownTimer == null)
                {
                    _blinkCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _blinkCountdownTimer.Tick += (s, e) => UpdateBlinkCountdown();
                }
                _blinkCountdownTimer.Start();
            }
            else
            {
                _blinkCountdownTimer?.Stop();
            }
        }

        private void UpdateBlinkCountdown()
        {
            if (App.BlinkTrainer?.IsRunning != true)
            {
                _blinkCountdownTimer?.Stop();
                return;
            }
            var ts = App.BlinkTrainer.Remaining;
            if (SettingsTab?.TxtBlinkCountdown != null)
                SettingsTab.TxtBlinkCountdown.Text = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        // --- Remote chip: a flyout that composes a listing and reuses the Remote tab's
        //     full enable flow (login + waiver + StartSessionAsync + opt-in + code/PIN). ---

        internal void PremiumRemoteOpenFlyout()
        {
            // State-aware chip: if Remote is already ON, the chip turns it OFF from the dashboard. The
            // start-only flyout had no stop path, so the chip appeared stuck in "turn on" with no way to
            // disable Remote without going to the Remote tab (#440).
            if (RemoteControlTab?.ChkRemoteControlEnabled?.IsChecked == true)
            {
                RemoteControlTab.ChkRemoteControlEnabled.IsChecked = false; // runs the existing disable flow
                RefreshPremiumRail();
                return;
            }
            if (SettingsTab?.RemoteFlyout != null) SettingsTab.RemoteFlyout.IsOpen = true;
        }

        internal void PremiumRemoteStart()
        {
            if (SettingsTab == null || RemoteControlTab == null) return;

            // Difficulty bubble → tier combo index (0 light/easy, 1 standard/medium, 2 full/hard).
            int tierIdx = SettingsTab.RemoteDiffHard?.IsChecked == true ? 2
                        : SettingsTab.RemoteDiffEasy?.IsChecked == true ? 0 : 1;
            if (RemoteControlTab.CmbRemoteTier != null) RemoteControlTab.CmbRemoteTier.SelectedIndex = tierIdx;

            var share = SettingsTab.RemoteShareCheck?.IsChecked == true;
            // Setting this fires ChkOptIntoDirectory_Changed, which populates the form from
            // saved settings — so we set our flyout values AFTER, letting them win.
            if (RemoteControlTab.ChkOptIntoDirectory != null) RemoteControlTab.ChkOptIntoDirectory.IsChecked = share;

            if (share)
            {
                if (RemoteControlTab.TxtOptInStatus != null)
                    RemoteControlTab.TxtOptInStatus.Text = SettingsTab.RemoteMessageBox?.Text ?? "";

                // Map flyout tag toggles → the Remote tab's fixed tag checkboxes by index
                // (both lists share the same order). The opt-in chain caps to 5 defensively.
                var remoteTags = OptInTagCheckBoxes();
                if (SettingsTab.RemoteTagPanel != null)
                {
                    int i = 0;
                    foreach (var child in SettingsTab.RemoteTagPanel.Children)
                    {
                        if (child is System.Windows.Controls.Primitives.ToggleButton tb && i < remoteTags.Length)
                            remoteTags[i].IsChecked = tb.IsChecked == true;
                        i++;
                    }
                }
            }

            if (SettingsTab.RemoteFlyout != null) SettingsTab.RemoteFlyout.IsOpen = false;

            // Show the Remote tab so the user can see the pairing code/PIN once it starts.
            ShowTab("remotecontrol");

            // Trigger the full existing enable flow (login check, waiver, start, opt-in, code/PIN).
            if (RemoteControlTab.ChkRemoteControlEnabled != null && RemoteControlTab.ChkRemoteControlEnabled.IsChecked != true)
                RemoteControlTab.ChkRemoteControlEnabled.IsChecked = true;

            RefreshPremiumRail();
        }

        // --- Lockdown chip: +/- duration, double-warning activate, live countdown ---

        internal void PremiumLockdownAdjust(int deltaMinutes)
        {
            _railLockdownMinutes = Math.Clamp(_railLockdownMinutes + deltaMinutes, 5, 180);
            if (SettingsTab?.TxtLockdownMins != null)
                SettingsTab.TxtLockdownMins.Text = _railLockdownMinutes + "m";
        }

        internal void PremiumLockdownActivate()
        {
            if (App.Lockdown == null || App.Lockdown.IsActive) return;
            var minutes = _railLockdownMinutes;
            var confirmed = WarningDialog.ShowDoubleWarning(this, "Lockdown Mode",
                "- You will be LOCKED IN for " + minutes + " minutes\n" +
                "- Strict Lock will be FORCED ON\n" +
                "- Panic Key will be DISABLED\n" +
                "- Alt+F4, Alt+Tab, Windows key, and Escape will be BLOCKED\n" +
                "- You CANNOT close or minimize the application\n" +
                "- The only escape is waiting for the timer to expire\n" +
                "  (or Ctrl+Alt+Del → Task Manager as a safety valve)");
            if (!confirmed) return;
            App.Lockdown.Activate(TimeSpan.FromMinutes(minutes));
        }

        private void SetLockdownActiveUi(bool active)
        {
            if (SettingsTab == null) return;
            if (SettingsTab.LockdownSetRow != null)
                SettingsTab.LockdownSetRow.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            if (SettingsTab.BtnLockdownGo != null)
                SettingsTab.BtnLockdownGo.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            if (SettingsTab.TxtLockdownCountdown != null)
                SettingsTab.TxtLockdownCountdown.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active) UpdateLockdownCountdown(App.Lockdown?.Remaining ?? TimeSpan.Zero);
        }

        private void UpdateLockdownCountdown(TimeSpan ts)
        {
            if (SettingsTab?.TxtLockdownCountdown != null)
                SettingsTab.TxtLockdownCountdown.Text = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        /// <summary>
        /// Quick-toggle handler for the simple on/off premium chips. Each mirrors its
        /// tab checkbox so the existing consent / patreon-gating / service-start logic
        /// runs unchanged — we just flip it and read the result back into the dots.
        /// </summary>
        internal void PremiumChip_Click(PremiumFeature feature)
        {
            // A Training Program session owns the day's feature mix (MainWindow.ProgramLock.cs).
            // Takeover / Awareness / Haptics are part of that mix, so the rail cannot flip them
            // mid-session. Deliberately NOT gated: Voice (the mic is a privacy control - the user
            // must always be able to disarm it) and Graded Intake (navigation, not a toggle).
            switch (feature)
            {
                case PremiumFeature.Takeover:
                case PremiumFeature.Awareness:
                case PremiumFeature.Haptics:
                    if (RefuseIfProgramFeatureLocked($"chip:{feature}")) return;
                    break;
            }

            switch (feature)
            {
                case PremiumFeature.Takeover:
                    ToggleTabCheckBox(BambiTakeoverTab?.ChkAutonomyEnabled);
                    break;
                case PremiumFeature.Awareness:
                    ToggleTabCheckBox(AwarenessTab?.ChkAwarenessMaster);
                    break;
                case PremiumFeature.Haptics:
                    ToggleTabCheckBox(HapticsTab?.ChkHapticsEnabled);
                    break;
                case PremiumFeature.Voice:
                    // Quick-start the She's Listening mic via the shared master toggle (consent +
                    // enable wake word + arm / or disarm). Decoupled from Takeover, so it works alone.
                    ToggleVoiceMic();
                    break;
                case PremiumFeature.GradedIntake:
                    // Navigation shortcut, not a toggle — the intake starts from its own page.
                    ShowTab("gradedintake");
                    break;
            }
            RefreshPremiumRail();
        }

        private static void ToggleTabCheckBox(System.Windows.Controls.CheckBox? cb)
        {
            if (cb == null) return;
            cb.IsChecked = !(cb.IsChecked ?? false);
        }

        /// <summary>Repaints the rail: chip state dots + the patron lock overlay (ad).</summary>
        internal void RefreshPremiumRail()
        {
            if (SettingsTab == null) return;

            var premium = App.Patreon?.HasPremiumAccess == true;
            if (SettingsTab.PremiumRailLock != null)
                SettingsTab.PremiumRailLock.Visibility = premium ? Visibility.Collapsed : Visibility.Visible;

            // Block the chips when not entitled. The lighter lock overlay provides the
            // greyed/locked visual (and ghosts the feature images through); disabling the
            // content makes it non-clickable even if the overlay hit-test is ever bypassed.
            if (SettingsTab.PremiumRailContent != null)
                SettingsTab.PremiumRailContent.IsEnabled = premium;

            SetDot(SettingsTab.DotTakeover, BambiTakeoverTab?.ChkAutonomyEnabled?.IsChecked == true);
            SetDot(SettingsTab.DotAwareness, AwarenessTab?.ChkAwarenessMaster?.IsChecked == true);
            SetDot(SettingsTab.DotHaptics, HapticsTab?.ChkHapticsEnabled?.IsChecked == true);

            if (SettingsTab.TxtLockdownMins != null)
                SettingsTab.TxtLockdownMins.Text = _railLockdownMinutes + "m";
            SetLockdownActiveUi(App.Lockdown?.IsActive == true);

            if (SettingsTab.TxtBlinkMins != null && App.Settings?.Current != null)
                SettingsTab.TxtBlinkMins.Text = App.Settings.Current.BlinkTrainerDurationMinutes + "m";
            SetBlinkActiveUi(App.BlinkTrainer?.IsRunning == true);

            SetDot(SettingsTab.DotRemote, RemoteControlTab?.ChkRemoteControlEnabled?.IsChecked == true);
            SetDot(SettingsTab.DotVoice, MicIsArmed());
        }

        private static void SetDot(System.Windows.Shapes.Ellipse? dot, bool on)
        {
            if (dot != null) dot.Fill = on ? PremiumDotOn : PremiumDotOff;
        }
    }
}
