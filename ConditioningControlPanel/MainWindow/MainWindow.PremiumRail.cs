using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>Features surfaced as quick-toggle chips on the dashboard premium rail.</summary>
    public enum PremiumFeature { Takeover, Awareness, Haptics, Lockdown, Blink, Remote, Voice, GradedIntake, Fyp }

    // Dashboard premium quick-toggle rail (left of the feature grid).
    public partial class MainWindow
    {
        private static readonly Brush PremiumDotOn = CreateFrozenBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly Brush PremiumDotOff = CreateFrozenBrush(Color.FromRgb(0x55, 0x55, 0x55));
        private bool _premiumRailSubscribed;
        private bool _lockdownEventsBound;
        private int _railLockdownMinutes = 15;
        private bool _blinkEventsBound;
        private bool _railIntakePassBound;
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
            EnsureIntakePassRailHooked();
            RefreshPremiumRail();
        }

        /// <summary>
        /// The Graded Intake lockband follows the weekly pass, not the tier, so it has to repaint
        /// when the pass is spent or reissued - otherwise a band claiming "patron only" would sit
        /// over a chip a free account may legitimately open (or vanish a week late).
        ///
        /// Hooked lazily and idempotently rather than once at startup, the same way
        /// <c>EnsureIntakePassHooked</c> (MainWindow.Lab.cs) does: <see cref="InitPremiumRail"/>
        /// may run before App.IntakePass exists, and a rail refresh is the natural retry. This is a
        /// second, independent subscriber to that event - the Lab one repaints the intake page's
        /// four-state gate, this one repaints the band; neither owns the other.
        /// </summary>
        private void EnsureIntakePassRailHooked()
        {
            var pass = App.IntakePass;
            if (_railIntakePassBound || pass == null) return;
            _railIntakePassBound = true;
            try
            {
                pass.PassStateChanged += (_, __) =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        if (Dispatcher.HasShutdownStarted) return;
                        Dispatcher.BeginInvoke(new Action(RefreshRailLockbands));
                    }
                    catch (Exception ex) { App.Logger?.Debug("Rail intake-pass repaint: {E}", ex.Message); }
                };
            }
            catch (Exception ex) { App.Logger?.Debug("EnsureIntakePassRailHooked: {E}", ex.Message); }
        }

        // --- Blink Trainer chip: +/- duration, consent-gated start/stop, countdown ---

        internal void PremiumBlinkAdjust(int deltaMinutes)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            // Silently inert behind the lockband. The whole rail used to be IsEnabled=false for
            // non-patrons, so these +/- buttons were unreachable; now that the card is live under a
            // band, the guard has to live here or a locked account could still write the setting.
            // Deliberately silent rather than a TierGate toast: the band already says why, and a
            // stepper is the one control people click five times in a row.
            if (!TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Blink)).Allowed) return;
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

            // Start is the premium half, and only the start half: stopping something that is
            // already running must never be gated (the same rule ToggleVoiceMic follows for the
            // mic). The Blink tab gates by disabling its panel behind the gate overlay; this rail
            // entry reaches App.BlinkTrainer.Start() directly, so it needs its own bar.
            if (!TierGate.DemandPremium(RailFeatureName(PremiumFeature.Blink))) return;

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
            // Refuse before the flyout, not after: letting a locked account pick a difficulty,
            // write a listing message and tag it, only to be told no by ChkRemoteControlEnabled's
            // own gate at the end, is the worst version of this. Turning Remote OFF above stays
            // free for the same reason stopping Blink does.
            if (!TierGate.DemandPremium(RailFeatureName(PremiumFeature.Remote))) return;
            if (SettingsTab?.RemoteFlyout != null) SettingsTab.RemoteFlyout.IsOpen = true;
        }

        internal void PremiumRemoteStart()
        {
            if (SettingsTab == null || RemoteControlTab == null) return;

            // Refused mid-session, and not for purity: shared control cannot actually WORK while a
            // session runs. RemoteControlService writes FlashEnabled / SubliminalEnabled /
            // PinkFilter* / Spiral* / LockCardEnabled, but SessionEngine.UpdateRampingValues
            // rewrites the ramped ones about once a second - so a partner's changes to flash
            // opacity/frequency or the overlays get stomped within a tick while the unramped ones
            // stick. Half-working control split between two people is worse than a clear refusal.
            //
            // If handing over mid-session ever becomes a wanted feature, the fix is to make remote
            // writes authoritative over the ramp - not to remove this guard.
            if (RefuseActionIfSessionLocked("remote-start")) return;

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
            // Same silent guard as PremiumBlinkAdjust - see the note there.
            if (!TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Lockdown)).Allowed) return;
            _railLockdownMinutes = Math.Clamp(_railLockdownMinutes + deltaMinutes, 5, 180);
            if (SettingsTab?.TxtLockdownMins != null)
                SettingsTab.TxtLockdownMins.Text = _railLockdownMinutes + "m";
        }

        internal void PremiumLockdownActivate()
        {
            if (App.Lockdown == null || App.Lockdown.IsActive) return;
            // The same bar BtnActivateLockdown_Click applies on the Lockdown tab, and the same
            // feature name so both refusals read identically. This handler is the other one that
            // takes the keys away for an hour.
            if (!TierGate.DemandPremium(RailFeatureName(PremiumFeature.Lockdown))) return;
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
            // The tier bar comes FIRST, and it is a real check now. Until Phase 3 the gate was
            // PremiumRailContent.IsEnabled = false under a blanket overlay: nothing here refused
            // anything, the rail was simply dead to the mouse. The per-card lockbands replaced the
            // blanket, so each chip is live again and this is where a locked account is told why -
            // through TierGate, whose toast carries the "See tiers" button into the App Info popup.
            //
            // Three deliberate absences:
            //  * Voice - ToggleVoiceMic demands premium on the ARM half only. Disarming a live mic
            //    is a privacy control and must never be gated.
            //  * GradedIntake and Fyp - navigation shortcuts whose destinations own the gate
            //    (IntakePassService's four states / OpenFypFeed, which already opens the same
            //    upsell). Gating the chip would refuse a free weekly pass the logo tile just
            //    handed out, and would double the refusal for For You.
            //
            // Then the session lock: a running session owns the prescribed dose
            // (MainWindow.SessionFeatureLock.cs), and Takeover / Awareness / Haptics are part of
            // that mix, so the rail cannot flip them mid-session. Tier first on purpose - telling
            // a locked account "not during a session" would answer the wrong question.
            switch (feature)
            {
                case PremiumFeature.Takeover:
                case PremiumFeature.Awareness:
                case PremiumFeature.Haptics:
                    if (!TierGate.DemandPremium(RailFeatureName(feature))) return;
                    if (RefuseIfSessionFeatureLocked($"chip:{feature}")) return;
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
                case PremiumFeature.Fyp:
                    // Navigation shortcut too, but the destination is a window rather than a tab:
                    // ShowTab intercepts "fyp" and calls OpenFypFeed, which owns the premium gate.
                    // Routed through ShowTab on purpose so the dashboard chip and the Exclusives
                    // spotlight card share one launch path.
                    ShowTab("fyp");
                    break;
            }
            RefreshPremiumRail();
        }

        private static void ToggleTabCheckBox(System.Windows.Controls.CheckBox? cb)
        {
            if (cb == null) return;
            cb.IsChecked = !(cb.IsChecked ?? false);
        }

        /// <summary>Repaints the rail: per-card lockbands + chip state dots.</summary>
        internal void RefreshPremiumRail()
        {
            if (SettingsTab == null) return;

            EnsureIntakePassRailHooked();
            RefreshRailLockbands();

            // Tombstone. The rail used to be gated by ONE blanket: PremiumRailLock draped over
            // everything with PremiumRailContent.IsEnabled = false underneath it. That answered
            // "no" for eight different features with one padlock, and clicking it did nothing at
            // all - an ad with no destination. Phase 3 replaced it with per-card lockbands that
            // name their own bar and route the click into TierGate's upsell.
            //
            // The Border and the StackPanel keep their names because live code still reads them
            // (and Phase 8 owns deleting dead dashboard chrome). The overlay is force-collapsed
            // here rather than deleted so a stale visibility from an older state cannot survive.
            if (SettingsTab.PremiumRailLock != null)
                SettingsTab.PremiumRailLock.Visibility = Visibility.Collapsed;
            if (SettingsTab.PremiumRailContent != null)
                SettingsTab.PremiumRailContent.IsEnabled = true;

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

        /// <summary>
        /// Paints one chip's live-state dot. This is also the event-driven trigger for the dot's
        /// pulse (MainWindow.DashboardFx.cs): a dot only breathes while its feature is genuinely
        /// on, so a rail with nothing running holds no clocks.
        /// </summary>
        private void SetDot(System.Windows.Shapes.Ellipse? dot, bool on)
        {
            if (dot == null) return;
            dot.Fill = on ? PremiumDotOn : PremiumDotOff;
            ApplyRailDotPulse(dot, on);
        }

        // --- Lockbands: the rail as a strip of honest ads -------------------------------

        /// <summary>
        /// Display names handed to <see cref="TierGate"/>. They end up inside the refusal copy
        /// ("{0} is for supporters..."), so they are the names as the user sees them on the card,
        /// and the two that already exist elsewhere - "Lockdown Mode" (BtnActivateLockdown_Click)
        /// and "Remote Control" (ChkRemoteControlEnabled_Changed) - resolve through the SAME loc
        /// keys, so the dashboard and the tab never phrase the same refusal two ways.
        ///
        /// Resolved, not literal: the refusal template is translated into nine languages, so a
        /// hardcoded English subject left every non-English toast half-English. Each name is the
        /// key the card itself renders, which is why no new translation rows were needed.
        /// </summary>
        private static string RailFeatureName(PremiumFeature feature) => Localization.Loc.Get(feature switch
        {
            PremiumFeature.Takeover => "tab_takeover",
            PremiumFeature.Awareness => "tab_awareness",
            PremiumFeature.Haptics => "tab_haptics",
            PremiumFeature.Voice => "tab_shelistening",
            PremiumFeature.Lockdown => "tab_lockdown_mode",
            PremiumFeature.Blink => "tab_blink_trainer",
            PremiumFeature.Remote => "tab_remote_control",
            PremiumFeature.GradedIntake => "tab_gradedintake",
            PremiumFeature.Fyp => "tab_fyp",
            _ => "rf_feature_generic",
        });

        /// <summary>
        /// Paints the per-card lockbands: one <see cref="TierVerdict"/> per rail entry, so the band
        /// on the card, the refusal the click produces and the copy in the toast cannot drift apart
        /// the way the Lab smokescreen and the launch handlers once did.
        ///
        /// Bands are pure decoration - <c>IsHitTestVisible="False"</c> in the view - so the card
        /// underneath stays live: hover lift and art nudge still run (a dead ad is a worse ad), and
        /// the click lands on the normal handler, which is where TierGate refuses and offers the
        /// tiers. Nothing here starts a clock: a lockband is a static state, and a new Forever loop
        /// would have to be threaded into the motion kill-switch to stay legal.
        ///
        /// Every rail entry is a tier-1 bar today, so every band is the gold padlock. A Lab-only
        /// card joining the strip swaps two attributes in the view (flask glyph + the violet
        /// brush the rail already defines) and calls <see cref="TierGate.RequiresLab"/> here.
        /// </summary>
        private void RefreshRailLockbands()
        {
            var tab = SettingsTab;
            if (tab == null) return;
            try
            {
                SetLockband(tab.LockBandTakeover, TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Takeover)));
                SetLockband(tab.LockBandAwareness, TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Awareness)));
                SetLockband(tab.LockBandHaptics, TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Haptics)));
                SetLockband(tab.LockBandVoice, TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Voice)));
                SetLockband(tab.LockBandFyp, TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Fyp)));
                SetLockband(tab.LockBandLockdown, TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Lockdown)));
                SetLockband(tab.LockBandBlink, TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Blink)));
                SetLockband(tab.LockBandRemote, TierGate.RequiresPremium(RailFeatureName(PremiumFeature.Remote)));

                // Graded Intake is the one entry that is not a straight tier question: a free
                // account holding this week's pass may take it, and the pass is exactly what the
                // logo tile's flip ceremony hands out. IntakePassService is the same truth the
                // intake page's four-state gate reads, so the band and the page agree. Fail
                // CLOSED-ish on a missing service, mirroring RefreshGradedIntakeGate's fallback:
                // premium keeps its open chip, everyone else sees the band.
                var intakeOpen = App.IntakePass?.CanStartIntake
                                 ?? (App.Patreon?.HasPremiumAccess == true);
                SetLockbandVisible(tab.LockBandIntake, !intakeOpen);
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshRailLockbands: {E}", ex.Message); }
        }

        // internal rather than private since Phase 6: the Play door's wall paints the same bands
        // from the same TierVerdicts (MainWindow.PlayTab.cs). Widened rather than duplicated on
        // purpose - two copies of "locked means Visible" is how a band ends up inverted on one
        // surface and nobody notices for a release.
        internal static void SetLockband(FrameworkElement? band, in TierVerdict verdict)
            => SetLockbandVisible(band, !verdict.Allowed);

        internal static void SetLockbandVisible(FrameworkElement? band, bool locked)
        {
            if (band == null) return;
            band.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
