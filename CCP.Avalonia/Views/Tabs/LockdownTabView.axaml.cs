using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/LockdownTabView.xaml.cs.
    ///
    /// <para>The settings half is restored against <see cref="CoreSettings"/>: the Possession
    /// master switch, the three intensity pills, tripwires / warden / photosafe and the four
    /// Safeties all read and write <c>AppSettings</c> for real, one for one with the WPF bodies,
    /// and the master switch still greys the Possession block rather than hiding it.</para>
    ///
    /// <para>The running half (MainWindow.Lab.cs on WPF) drives the Core <see cref="LockdownService"/>:
    /// Activate, the panel swap, the clock and the secret phrase. The Emergency Exit slab has no exit
    /// games here and follows docs/avalonia-decisions.md (Lockdown / Emergency Exit).</para>
    /// </summary>
    public partial class LockdownTabView : UserControl
    {
        /// <summary>
        /// True while LoadPossessionSettings is writing the controls, so the change handlers do not
        /// write the value they were just given straight back into settings (and, worse, re-enter
        /// through the master switch's grey-out pass).
        ///
        /// Starts true: the XAML wires <c>IsCheckedChanged</c> itself, so a handler can fire from
        /// inside InitializeComponent, before any field is assigned and before the seed has run.
        /// Cleared by the first LoadPossessionSettings.
        /// </summary>
        private bool _loadingPossession = true;

        public LockdownTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields this code-behind reads.
            InitializeComponent();

            LoadPossessionSettings();
            UpdateQuestHint();

            // Tabs are shown and hidden rather than rebuilt, so the first attach fires once.
            // Re-read on every show for the same reason BambiTakeoverTabView does: something else
            // can move these behind our back (a settings import replaces the whole object, a
            // future safety panic clears a flag) and a stale toggle here is a toggle that lies.
            // WPF's Loaded + IsVisibleChanged pair maps to Avalonia's AttachedToVisualTree +
            // the IsVisible property changing.
            AttachedToVisualTree += (_, _) =>
            {
                LoadPossessionSettings();
                // P01: a minimised window draws nothing, so the breath stops with it.
                _host = TopLevel.GetTopLevel(this) as Window;
                if (_host != null) _host.PropertyChanged += OnHostChanged;
                UpdateEmergencyExitPulse();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                if (_host != null) _host.PropertyChanged -= OnHostChanged;
                _host = null;
                StopEmergencyExitPulse();
            };
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (IsVisible) LoadPossessionSettings();
                UpdateEmergencyExitPulse();
            };

            // WPF MainWindow.Lab.cs InitializeLockdown: the panels follow the running lockdown. The
            // service raises from its timer thread, so every handler hops to the UI thread.
            if (Lockdown is { } ld)
            {
                ld.LockdownActivated += () => Dispatcher.UIThread.Post(ShowLockdownState);
                ld.LockdownDeactivated += () => Dispatcher.UIThread.Post(ShowLockdownState);
                ld.CountdownTick += r => Dispatcher.UIThread.Post(() => { TxtLockdownTimer.Text = Clock(r); PaintPossessionReadout(); });
                ld.TimerRestarted += _ => Dispatcher.UIThread.Post(() => { TxtLockdownTimer.Text = Clock(ld.Remaining); PaintPossessionReadout(); });
                ShowLockdownState();
            }
        }

        // ==== Duration (WPF LockdownTabView.xaml.cs:36) =====================================

        private void CmbLockdownDuration_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateQuestHint();

        /// <summary>Shows the "does not count for quests" line while the picked duration is too short.</summary>
        internal void UpdateQuestHint()
        {
            // SelectionChanged fires inside InitializeComponent (SelectedIndex="1"), before the hint exists.
            if (TxtLockdownQuestHint == null || CmbLockdownDuration == null) return;
            var minutes = (CmbLockdownDuration.SelectedItem as ComboBoxItem)?.Tag is string tag
                          && int.TryParse(tag, out var m) ? m : 0;
            TxtLockdownQuestHint.IsVisible = minutes > 0 && !QuestService.LockdownCountsForQuests(TimeSpan.FromMinutes(minutes));
        }

        /// <summary>WPF ChkLockdownHideTimer_Changed: read on every clock repaint, so nothing to push.</summary>
        private void ChkLockdownHideTimer_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.HideLockdownTimer = v, ChkLockdownHideTimer, "HideLockdownTimer");

        // ==== Possession + Safeties ======================================================

        /// <summary>Paints every control on the card from AppSettings. Never writes anything back.</summary>
        private void LoadPossessionSettings()
        {
            try
            {
                var s = CoreSettings.Current;

                _loadingPossession = true;

                Set(ChkLockdownHideTimer, s.HideLockdownTimer);
                Set(ChkPossessionEnabled, s.LockdownPossessionEnabled);
                Set(ChkPossTripwires, s.LockdownTripwiresEnabled);
                Set(ChkPossWarden, s.LockdownWardenEnabled);
                Set(ChkPossPhotosafe, s.LockdownPhotosafe);

                Set(ChkLockdownStrict, s.LockdownForceStrictLock);
                Set(ChkLockdownNoPanic, s.LockdownDisablePanicKey);
                Set(ChkLockdownSysKeys, s.LockdownBlockSystemKeys);
                Set(ChkLockdownDose, s.LockdownDoseKeeperEnabled);

                ApplyIntensityPills(s.LockdownPossessionIntensity);
                ApplyPossessionEnabledLook(s.LockdownPossessionEnabled);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to load possession settings");
            }
            finally
            {
                _loadingPossession = false;
            }

            // Assign only on a real difference: Avalonia raises IsCheckedChanged on a programmatic
            // set too, and every handler below is a live editor.
            static void Set(CheckBox box, bool value)
            {
                if ((box.IsChecked ?? false) != value) box.IsChecked = value;
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

        private void ChkPossessionEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                var s = CoreSettings.Current;
                s.LockdownPossessionEnabled = ChkPossessionEnabled.IsChecked == true;
                ApplyPossessionEnabledLook(s.LockdownPossessionEnabled);
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to write LockdownPossessionEnabled");
            }
        }

        private void PossIntensity_Click(object? sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                // Tag carries the value so all three pills share one handler and the mapping lives
                // next to the label the user actually reads.
                if (sender is not ToggleButton tb || tb.Tag is not string tag || !int.TryParse(tag, out var value))
                    return;

                var s = CoreSettings.Current;
                s.LockdownPossessionIntensity = value;

                _loadingPossession = true;
                try { ApplyIntensityPills(s.LockdownPossessionIntensity); }
                finally { _loadingPossession = false; }

                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to write LockdownPossessionIntensity");
            }
        }

        private void ChkPossTripwires_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.LockdownTripwiresEnabled = v,
                         ChkPossTripwires, "LockdownTripwiresEnabled");

        private void ChkPossWarden_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.LockdownWardenEnabled = v,
                         ChkPossWarden, "LockdownWardenEnabled");

        private void ChkPossPhotosafe_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.LockdownPhotosafe = v,
                         ChkPossPhotosafe, "LockdownPhotosafe");

        /// <summary>One box, one flag, one save - the shape three of the toggles share.</summary>
        private void WriteFlag(Action<bool> write, CheckBox box, string name)
        {
            if (_loadingPossession) return;
            try
            {
                write(box.IsChecked == true);
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to write {Setting}", name);
            }
        }

        /// <summary>
        /// All four safeties share one handler: they are read together on Activate and none of them
        /// does anything until then, so there is nothing per-toggle to react to.
        /// </summary>
        private void ChkLockdownSafety_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                var s = CoreSettings.Current;
                s.LockdownForceStrictLock = ChkLockdownStrict.IsChecked == true;
                s.LockdownDisablePanicKey = ChkLockdownNoPanic.IsChecked == true;
                s.LockdownBlockSystemKeys = ChkLockdownSysKeys.IsChecked == true;
                s.LockdownDoseKeeperEnabled = ChkLockdownDose.IsChecked == true;
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to write the lockdown safeties");
            }
        }

        // ==== the running lockdown (WPF MainWindow.Lab.cs) ================================
        // ponytail: ported: premium gate, double warning, Activate, panel swap, clock, the secret
        // phrase, the Emergency Exit breath; the title-bar badge, blood-red theme and activation flash
        // are MainShellWindow.Lockdown.cs; the Strict toggles grey themselves (HoldWhileLockdown).
        // The Dose keeper is Core LockdownDoseKeeper, installed by MainShellWindow.LockdownDose.cs.
        // The Possession readout is PaintPossessionReadout (director = Core PossessionDirector, head =
        // MainShellWindow.Possession.cs). Not on this head:
        // the system-key hook (Linux has none, so the warning does not promise it).

        /// <summary>The head's live Lockdown (null in renders and tests that set none).</summary>
        internal static LockdownService? Lockdown => LockdownService.Current;

        private int _lockdownTimerClickCount;
        private DateTime _lockdownTimerLastClick;

        private static string Clock(TimeSpan remaining) =>
            SessionClockLabel.LockdownClock(remaining, CoreSettings.Current.HideLockdownTimer);

        /// <summary>WPF MainWindow.Lab.cs HookPossessionReadout / UpdatePossessionReadout / Unhook: a word
        /// plus five pips, shown only while a lockdown runs with Possession on. Everything starts at
        /// Settle. Painted from the lockdown's own tick and restart (the director moves the rung on the
        /// same tick, ahead of this post), so there is no second subscription to leak.</summary>
        internal void PaintPossessionReadout()
        {
            try
            {
                var on = Lockdown?.IsActive == true && CoreSettings.Current.LockdownPossessionEnabled;
                TxtPossessionRung.IsVisible = on;
                PossessionPips.IsVisible = on;
                int index = on ? (int)(PossessionDirector.Current is { IsHaunting: true } d ? d.CurrentRung : PossessionRung.Settle) : -1;
                TxtPossessionRung.Text = on ? Loc.GetF("lockdown_poss_readout_fmt", Loc.Get("lockdown_poss_rung_" + index)) : "";
                for (int i = 0; i < PossessionPips.Children.Count; i++)
                {
                    if (PossessionPips.Children[i] is not Border pip) continue;
                    pip.Background = new SolidColorBrush(i <= index ? Views.Windows.MainShellWindow.PossessionEmber : Views.Windows.MainShellWindow.PossessionEmberDim);
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Possession: failed to paint the rung readout"); }
        }

        /// <summary>WPF OnLockdownActivated / OnLockdownDeactivated, the panel half: swap Setup and
        /// Active, seed the clock, reset the secret exit and the slab notice.</summary>
        internal void ShowLockdownState()
        {
            var active = Lockdown?.IsActive == true;
            LockdownSetupPanel.IsVisible = !active;
            LockdownActivePanel.IsVisible = active;
            TxtLockdownTimer.Text = Clock(Lockdown?.Remaining ?? TimeSpan.Zero);
            _lockdownTimerClickCount = 0;
            TxtLockdownExit.IsVisible = false;
            TxtLockdownExit.Text = "";
            TxtEmergencyExitNotice.IsVisible = false;
            PaintPossessionReadout();
            UpdateEmergencyExitPulse();
            // WPF ApplyLockdownTheme / RestoreLockdownTheme (Lab.cs:1188/1211): the card goes crimson
            // on #1A0A0A; the override is dropped on exit, which leaves the XAML gradients (the values
            // WPF's restore writes).
            if (active && _cardHolds.Count == 0)
            {
                if (LockdownCardBorder.SetValue(Border.BorderBrushProperty, (IBrush?)new SolidColorBrush(Color.Parse("#DC143C")),
                        global::Avalonia.Data.BindingPriority.Animation) is { } a) _cardHolds.Add(a);
                if (LockdownCardBorder.SetValue(Border.BackgroundProperty, (IBrush?)new SolidColorBrush(Color.Parse("#1A0A0A")),
                        global::Avalonia.Data.BindingPriority.Animation) is { } b) _cardHolds.Add(b);
            }
            else if (!active)
            {
                foreach (var d in _cardHolds) d.Dispose();
                _cardHolds.Clear();
            }
        }

        private readonly List<IDisposable> _cardHolds = new();

        /// <summary>WPF MainWindow.Lab.cs:51 BtnActivateLockdown_Click. The consent lists only what
        /// this head enforces.</summary>
        private async void BtnActivateLockdown_Click(object? sender, RoutedEventArgs e)
        {
            if (Lockdown is not { } ld) return;
            // Hard gate: the overlay Border only hides the card, this handler takes the keys away.
            if (!TierGate.DemandPremium(Loc.Get("tab_lockdown_mode"))) return;
            if ((CmbLockdownDuration.SelectedItem as ComboBoxItem)?.Tag is not string tag || !int.TryParse(tag, out var minutes))
                return;
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var confirmed = await Dialogs.WarningDialog.ShowDoubleWarningAsync(owner, "Lockdown Mode", LockdownWarning(minutes, CoreSettings.Current));
            if (!confirmed) return;
            ld.Activate(TimeSpan.FromMinutes(minutes));
        }

        /// <summary>WPF's consent text, minus the lines this head would be lying about (Dose keeper,
        /// Possession: none run here; system keys off Windows, where nothing blocks them) and with the
        /// platform's safety valve.</summary>
        internal static string LockdownWarning(int minutes, Models.AppSettings cfg) => LockdownWarning(minutes, cfg, OperatingSystem.IsWindows());

        internal static string LockdownWarning(int minutes, Models.AppSettings cfg, bool windows)
        {
            var warn = new System.Text.StringBuilder();
            warn.Append("- You will be LOCKED IN for ").Append(minutes).Append(" minutes\n");
            if (cfg.LockdownForceStrictLock) warn.Append("- Strict Lock will be FORCED ON\n");
            if (cfg.LockdownDisablePanicKey) warn.Append("- Panic Key will be DISABLED\n");
            // WPF MainWindow.Lab.cs:80; Platform/Win32PanicKey enforces it on Windows only.
            if (windows && cfg.LockdownBlockSystemKeys) warn.Append("- Alt+F4, Alt+Tab, the Windows key and Ctrl+Esc will be BLOCKED\n");
            warn.Append("- You CANNOT close the application (minimizing still works)\n");
            warn.Append("- The only escape is waiting for the timer to expire\n");
            warn.Append(windows
                ? "  (or Ctrl+Alt+Del → Task Manager as a safety valve)"   // WPF's line verbatim
                : "  (or ending the app from a system monitor / terminal as a safety valve)");
            return warn.ToString();
        }

        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.BtnGateUnlock_Click(sender, e);

        /// <summary>WPF MainWindow.Lab.cs TxtLockdownExit_KeyDown: Enter tries the phrase; a wrong one
        /// clears, hides and trips the wire.</summary>
        private void TxtLockdownExit_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var success = Lockdown?.TryExitWithPhrase(TxtLockdownExit.Text ?? "") ?? false;
            if (success) return;
            TxtLockdownExit.Text = "";
            TxtLockdownExit.IsVisible = false;
            try { Lockdown?.NotifyEscapeAttempt(EscapeKinds.WrongPhrase); }
            catch (Exception ex) { Log.Warning(ex, "Lockdown: wrong-phrase tripwire failed"); }
        }

        /// <summary>WPF TxtLockdownTimer_Click: five taps, each within a second of the last, reveal the box.</summary>
        private void TxtLockdownTimer_Click(object? sender, PointerPressedEventArgs e) => TapTimer(DateTime.Now);

        internal void TapTimer(DateTime now)
        {
            if ((now - _lockdownTimerLastClick).TotalMilliseconds > 1000) _lockdownTimerClickCount = 0;
            _lockdownTimerLastClick = now;
            _lockdownTimerClickCount++;
            if (_lockdownTimerClickCount >= 5)
            {
                TxtLockdownExit.IsVisible = true;
                TxtLockdownExit.Focus();
                _lockdownTimerClickCount = 0;
            }
        }

        // ==== Emergency Exit =============================================================
        // The huge button's own motion. Deliberately NOT routed through Possession: this is the
        // one control on the page that must behave exactly the same every second of a lockdown,
        // so its animations live here, on the view, and answer only to the photosafe setting.

        // WPF StartEmergencyExitPulse (LockdownTabView.xaml.cs:267): opacity 0.26->0.62 and blur
        // 24->42, 1500 ms SineEase in-out, auto-reverse, forever; resting glow 0.32 / 28. Skipped
        // under LockdownPhotosafe (POSSESSION.md: no flicker, the resting glow is the photosafe
        // state) and when motion is off (WPF: SystemParameters.ClientAreaAnimation). Animation.RunAsync
        // throws on a code-held Effect, so one ~30 fps DispatcherTimer steps it, and only while the
        // active panel is on screen (P01).
        private DispatcherTimer? _eePulse;
        private readonly System.Diagnostics.Stopwatch _eeClock = new();

        internal bool EmergencyExitPulsing => _eePulse != null;

        private Window? _host;

        private void OnHostChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty) UpdateEmergencyExitPulse();
        }

        /// <summary>Starts or stops the breath to match what is on screen. Called from every state
        /// change the WPF host called Start/StopEmergencyExitPulse from, plus show/hide/attach.</summary>
        internal void UpdateEmergencyExitPulse()
        {
            var want = Lockdown?.IsActive == true && IsVisible && LockdownActivePanel.IsVisible
                && VisualRoot is not null && _host?.WindowState != WindowState.Minimized
                && !CoreSettings.Current.LockdownPhotosafe
                && CoreSettings.Current.MotionLevel != Models.MotionLevel.Off;
            if (want) StartEmergencyExitPulse(); else StopEmergencyExitPulse();
        }

        internal void StartEmergencyExitPulse()
        {
            if (_eePulse != null) return;
            _eeClock.Restart();
            _eePulse = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render,
                (_, _) => PaintEmergencyExitGlow(_eeClock.Elapsed.TotalMilliseconds));
            _eePulse.Start();
        }

        /// <summary>Stops the breath and puts the glow back where the XAML left it.</summary>
        internal void StopEmergencyExitPulse()
        {
            _eePulse?.Stop();
            _eePulse = null;
            SetGlow(0.32, 28);
        }

        /// <summary>One frame of the breath at <paramref name="ms"/> into it (3000 ms per cycle).</summary>
        internal void PaintEmergencyExitGlow(double ms)
        {
            var f = (1 - Math.Cos(2 * Math.PI * (ms % 3000) / 3000)) / 2;   // SineEase in-out, auto-reversed
            SetGlow(0.26 + 0.36 * f, 24 + 18 * f);
        }

        private void SetGlow(double opacity, double blur)
        {
            // The XAML instance may be frozen/shared; a code-owned one is mutated in place.
            if (_eeGlow is null || !ReferenceEquals(EEPlate.Effect, _eeGlow))
                EEPlate.Effect = _eeGlow = new DropShadowEffect { Color = Color.Parse("#FF8A5C"), OffsetX = 0, OffsetY = 0 };
            _eeGlow.Opacity = opacity;
            _eeGlow.BlurRadius = blur;
        }

        private DropShadowEffect? _eeGlow;

        // The slab still sinks under the finger and comes back, but with no code: the WPF pair of
        // DoubleAnimations on a named ScaleTransform (60 ms down / 140 ms up) is a :pressed style
        // plus one transition in the XAML. Two reasons, both hard: Avalonia cannot name a transform
        // (AVLN2000), and Button marks PointerPressed/Released handled in its class handler, so the
        // ported handlers would have been dead code that renders and reviews as if it worked.

        /// <summary>
        /// WPF opens the Emergency Exit games here (EmergencyExitHostService, WebView2; not on this
        /// head). docs/avalonia-decisions.md, Lockdown / Emergency Exit: only while a lockdown runs,
        /// fire the EmergencyExit tripwire and Chaster's safety hold, then state the phrase steps
        /// and the time left. Never RestartTimer, never Deactivate, never open the phrase box.
        /// </summary>
        private void BtnEmergencyExit_Click(object? sender, RoutedEventArgs e)
        {
            if (Lockdown is not { IsActive: true } ld) return;
            try { ld.NotifyEscapeAttempt(EscapeKinds.EmergencyExit); } catch (Exception ex) { Log.Debug(ex, "EmergencyExit tripwire"); }
            try { Platform.ChasterHead.Service?.NoteSafetyExit(); } catch (Exception ex) { Log.Debug(ex, "EmergencyExit chaster hold"); }
            // The real time left even under HideLockdownTimer: this notice is the way out, it must not hide it.
            TxtEmergencyExitNotice.Text = Loc.GetF("lockdown_ee_phrase_steps_fmt", SessionClockLabel.LockdownClock(ld.Remaining, false));
            TxtEmergencyExitNotice.IsVisible = true;
        }
    }
}
