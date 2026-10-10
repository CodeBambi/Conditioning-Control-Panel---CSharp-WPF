// PORTED from the Lockdown refusals WPF spreads over MainWindow: BtnStart_Click (StartStop.cs:45,
// Stop refused + Stop tripwire), RequestExit (Launcher.cs:184, Exit refused), OnClosing
// (WindowChrome.cs:131, close refused + Close tripwire) and OnGlobalKeyPressed (MainWindow.xaml.cs:888,
// every GLOBAL key ignored). Window and TextBox input are never touched: the secret phrase is typed
// through them (docs/avalonia-decisions.md, Lockdown / Emergency Exit).

using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal static bool LockdownActive => LockdownService.Current?.IsActive == true;

        /// <summary>WPF Lab.cs:635-663/741-745: a control Lockdown holds is greyed at 0.4 with the
        /// "no escape" tooltip, and given back on exit. The control's own refusal stays as well.</summary>
        internal static void HoldUnderLockdown(global::Avalonia.Controls.Control c, bool held)
        {
            c.IsEnabled = !held;
            c.Opacity = held ? 0.4 : 1.0;
            global::Avalonia.Controls.ToolTip.SetTip(c, held ? Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape") : null);
        }

        // Counts Lockdown activations so a re-attached control can tell "same run" from "new run".
        // Static hook on the service only (no control captured), so nothing leaks (P41).
        private static LockdownService? s_runSource;
        private static int s_run;
        private static int LockdownRun()
        {
            if (LockdownService.Current is { } ld && !ReferenceEquals(ld, s_runSource))
            {
                s_runSource = ld;
                ld.LockdownActivated += () => s_run++;
            }
            return s_run;
        }

        /// <summary>HoldUnderLockdown on every Lockdown start/end while <paramref name="c"/> is attached
        /// (P41: subscribe on attach, drop on detach). Like WPF the hold is decided at the Lockdown event,
        /// so a re-attach mid-run (rack switch) repaints that decision instead of re-evaluating it.</summary>
        internal static void HoldWhileLockdown(global::Avalonia.Controls.Control c, System.Func<bool> held)
        {
            LockdownService? ld = null;
            bool decided = false; int decidedRun = -1; // the hold given in run decidedRun
            void Apply() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                decided = held(); decidedRun = LockdownRun();
                HoldUnderLockdown(c, decided);
            });
            c.AttachedToVisualTree += (_, _) =>
            {
                ld = LockdownService.Current;
                if (ld != null) { ld.LockdownActivated += Apply; ld.LockdownDeactivated += Apply; }
                if (decidedRun != LockdownRun()) { decided = held(); decidedRun = LockdownRun(); }
                HoldUnderLockdown(c, LockdownActive && decided);
            };
            c.DetachedFromVisualTree += (_, _) =>
            {
                if (ld != null) { ld.LockdownActivated -= Apply; ld.LockdownDeactivated -= Apply; }
                ld = null;
            };
        }

        /// <summary>WPF OnLockdownActivated/Deactivated (Lab.cs:612/707): the CC Labs door is greyed
        /// as well as refused. Bound to the service current at construction (App seeds it first).</summary>
        private void InitializeLockdownGreys()
        {
            bool shellClosed = false;
            // The four accent brushes live on the APPLICATION: a shell that closes mid-lockdown hands
            // them back, or every window after it (a second shell, a dialog) stays crimson.
            Closed += (_, _) => { shellClosed = true; ApplyLockdownTheme(false); };
            void Refresh() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (shellClosed) return;   // a refresh posted before the close must not repaint the app's brushes
                if (Named<global::Avalonia.Controls.Button>("BtnBackToLauncher") is { } door) door.IsEnabled = !LockdownActive;
                // WPF SetLockdownBadge (Lab.cs:672/753/794): the badge shows for the whole run, seeded.
                if (Named<global::Avalonia.Controls.Border>("LockdownBadge") is { } badge) badge.IsVisible = LockdownActive;
                PaintLockdownBadge();
                Platform.Win32Input.ApplyLockdown(LockdownActive, CoreSettings.Current.LockdownBlockSystemKeys);   // WPF Lab.cs:621/714
                ApplyLockdownTheme(LockdownActive);
            });
            void Flash() => global::Avalonia.Threading.Dispatcher.UIThread.Post(PlayLockdownActivationAnimation);
            // WPF OnLockdownTick (Lab.cs:771) and OnLockdownTimerRestarted (Lab.cs:1039): the badge
            // clock follows the page clock, through the same HideLockdownTimer mask.
            void Tick(System.TimeSpan _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(PaintLockdownBadge);
            void Restarted(string _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(PaintLockdownBadge);
            if (LockdownService.Current is { } ld)
            {
                ld.LockdownActivated += Refresh;
                ld.LockdownActivated += Flash;
                ld.LockdownDeactivated += Refresh;
                ld.CountdownTick += Tick;
                ld.TimerRestarted += Restarted;
                Closed += (_, _) =>
                {
                    ld.LockdownActivated -= Refresh; ld.LockdownActivated -= Flash; ld.LockdownDeactivated -= Refresh;
                    ld.CountdownTick -= Tick; ld.TimerRestarted -= Restarted;
                };
            }
            Refresh();
        }

        // --- WPF ApplyLockdownTheme / RestoreLockdownTheme (Lab.cs:1147/1203) ----------------
        // Window, title bar and the title glow are overridden at Animation priority, so disposing the
        // override hands each back to its XAML DynamicResource (a mod switch mid-run still lands);
        // the four accent brushes WPF rewrites are put back as they were, then re-derived from the mod.
        private static readonly global::Avalonia.Media.Color LockdownCrimson = global::Avalonia.Media.Color.Parse("#DC143C");
        private static readonly string[] LockdownThemeKeys = { "PinkBrush", "DarkPinkBrush", "TransparentPinkBrush", "PinkButtonHoveredBrush" };
        private readonly System.Collections.Generic.List<System.IDisposable> _lockdownThemeHolds = new();
        // The four brushes live on the Application, so their originals are kept ONCE for the process: a second
        // shell that themes while the first is still crimson must not record crimson as "what was there".
        private static System.Collections.Generic.Dictionary<string, object?>? s_preLockdownBrushes;
        private static int s_lockdownThemedShells;
        private bool _lockdownThemed;

        internal bool LockdownThemed => _lockdownThemed;

        private void ApplyLockdownTheme(bool on)
        {
            try
            {
                var res = global::Avalonia.Application.Current?.Resources;
                if (on == LockdownThemed || res == null) return;
                if (on)
                {
                    _lockdownThemed = true;
                    s_lockdownThemedShells++;
                    if (s_preLockdownBrushes == null)
                    {
                        s_preLockdownBrushes = new();
                        foreach (var k in LockdownThemeKeys)
                            s_preLockdownBrushes[k] = res.TryGetValue(k, out var v) ? v : null;
                    }
                    void Hold<T>(global::Avalonia.AvaloniaObject? o, global::Avalonia.StyledProperty<T> p, T v)
                    {
                        if (o?.SetValue(p, v, global::Avalonia.Data.BindingPriority.Animation) is { } d) _lockdownThemeHolds.Add(d);
                    }
                    Hold(this, BackgroundProperty, (global::Avalonia.Media.IBrush?)new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#100505")));
                    Hold(Named<global::Avalonia.Controls.Border>("TitleBarBorder"), global::Avalonia.Controls.Border.BackgroundProperty,
                        (global::Avalonia.Media.IBrush?)new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#8B0000")));
                    Hold(Named<global::Avalonia.Controls.TextBlock>("TxtPlayerTitle")?.Effect as global::Avalonia.Media.DropShadowEffect,
                        global::Avalonia.Media.DropShadowEffect.ColorProperty, LockdownCrimson);
                    // PinkBrush also repaints TxtPlayerTitle, XPBar (TxtHeaderVersion is gone, eac44ef9f) and both banner lines.
                    res["PinkBrush"] = new global::Avalonia.Media.SolidColorBrush(LockdownCrimson);
                    res["DarkPinkBrush"] = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#8B0000"));
                    res["TransparentPinkBrush"] = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(0x30, 0xDC, 0x14, 0x3C));
                    res["PinkButtonHoveredBrush"] = new global::Avalonia.Media.SolidColorBrush(LockdownCrimson);
                }
                else
                {
                    foreach (var d in _lockdownThemeHolds) d.Dispose();
                    _lockdownThemeHolds.Clear();
                    _lockdownThemed = false;
                    if (--s_lockdownThemedShells <= 0 && s_preLockdownBrushes is { } pre)
                    {
                        s_lockdownThemedShells = 0;
                        foreach (var (k, v) in pre)
                            if (v != null) res[k] = v; else res.Remove(k);
                        s_preLockdownBrushes = null;
                    }
                    RefreshThemeAwareElements();
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "Lockdown theme failed");
            }
        }

        // WPF PlayLockdownActivationAnimation (Lab.cs:1254): a crimson (180,220,20,60) veil over the
        // whole window fading 1 -> 0 over 600 ms (QuadraticEase out), then removed. Stepped by a
        // short-lived ~30 fps DispatcherTimer like the Emergency Exit breath, so tests drive frames.
        private global::Avalonia.Controls.Border? _lockdownFlash;
        private global::Avalonia.Threading.DispatcherTimer? _lockdownFlashTimer;
        internal global::Avalonia.Controls.Border? LockdownFlash => _lockdownFlash;

        private void PlayLockdownActivationAnimation()
        {
            try
            {
                if (Named<global::Avalonia.Controls.Grid>("RootGrid") is not { } root) return;
                PaintLockdownFlash(double.MaxValue);   // a second activation restarts it
                _lockdownFlash = new global::Avalonia.Controls.Border
                {
                    Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromArgb(180, 220, 20, 60)),
                    IsHitTestVisible = false,
                    ZIndex = int.MaxValue,
                };
                if (root.RowDefinitions.Count > 1) global::Avalonia.Controls.Grid.SetRowSpan(_lockdownFlash, root.RowDefinitions.Count);
                if (root.ColumnDefinitions.Count > 1) global::Avalonia.Controls.Grid.SetColumnSpan(_lockdownFlash, root.ColumnDefinitions.Count);
                root.Children.Add(_lockdownFlash);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                _lockdownFlashTimer = new global::Avalonia.Threading.DispatcherTimer(System.TimeSpan.FromMilliseconds(33),
                    global::Avalonia.Threading.DispatcherPriority.Render, (_, _) => PaintLockdownFlash(clock.Elapsed.TotalMilliseconds));
                _lockdownFlashTimer.Start();
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to play lockdown animation");
            }
        }

        /// <summary>One frame of the flash at <paramref name="ms"/> in; at 600 ms it is removed.</summary>
        internal void PaintLockdownFlash(double ms)
        {
            if (_lockdownFlash is not { } flash) return;
            var t = System.Math.Min(1, ms / 600);
            flash.Opacity = (1 - t) * (1 - t);   // 1 - QuadraticEase.EaseOut(t)
            if (t < 1) return;
            _lockdownFlashTimer?.Stop();
            _lockdownFlashTimer = null;
            (flash.Parent as global::Avalonia.Controls.Panel)?.Children.Remove(flash);
            _lockdownFlash = null;
        }

        /// <summary>WPF FormatLockdownClock for the badge: only while a lockdown runs (WPF seeds it on
        /// activate and leaves it alone once hidden).</summary>
        private void PaintLockdownBadge()
        {
            if (LockdownService.Current is not { IsActive: true } ld) return;
            if (Named<global::Avalonia.Controls.TextBlock>("TxtLockdownBadgeTime") is { } t)
                t.Text = SessionClockLabel.LockdownClock(ld.Remaining, CoreSettings.Current.HideLockdownTimer);
        }

        /// <summary>P17: the badge is a Border (as on WPF), so Enter/Space give it the click.</summary>
        private void LockdownBadge_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key is not (global::Avalonia.Input.Key.Enter or global::Avalonia.Input.Key.Space)) return;
            e.Handled = true;
            ShowTab("lockdown");
        }

        /// <summary>WPF StartStop.cs:45: under Lockdown a Stop (button, tray Stop everything) is
        /// refused with the WPF message, after the Stop tripwire. True when refused.</summary>
        internal static bool RefuseStopUnderLockdown()
        {
            if (!LockdownActive) return false;
            try { LockdownService.Current?.NotifyEscapeAttempt(EscapeKinds.Stop); } catch { }
            Serilog.Log.Information("Lockdown: Stop refused");
            if (Current is { } owner)
                _ = MessageDialog.ShowAsync(owner, Loc.Get("title_lockdown"), Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_stop_dur"));
            return true;
        }

        /// <summary>The live shell window, for dialogs raised from static paths (tray).</summary>
        internal static MainShellWindow? Current =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow as MainShellWindow;
    }
}
