// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.HeroFx.cs (789 lines), plus
// the header writer it decorates: UpdateLevelDisplay (MainWindow.UiUpdates.cs:52), the bar half of
// AnimateXpDisplay/FillXpBarTo (MainWindow.ChromeFx.cs:801-855), OnXPChanged/OnLevelUp
// (MainWindow.xaml.cs:712,774) and the level-up flash (CelebrateLevelUp, MainWindow.EventFx.cs:249;
// FlashOverlay, MainWindow.Companion.cs:115).
//
// LIVE: the LVL chip (LevelChip/TxtLevelLabel, the one level readout since eac44ef9f), the "xp / needed XP" readout and the XP bar
// read CoreSettings.PlayerLevel/PlayerXP against Core's XpCurve and repaint on ProgressionBank's
// Awarded and LevelUp. The bar tweens to its new width (0.6s QuadraticEaseOut, MotionFx.BarFill)
// and a level-up flashes XPBarFlashOverlay (250ms up, auto-reversed), both off at MotionLevel Off.
// The profile menu's Level/XP rail is painted from the same numbers.
//
// STILL MISSING vs WPF, so the row stays a stub:
//   THE BANK hold (the XP odometer is live: AnimateXpReadout below; the level-up burst is MainShellWindow.LevelUpFx.cs)
//   (the meniscus, the chip pop and the tube are live in MainShellWindow.HudDepth.cs)
//   (MainWindow.BankFx.cs); the rank title (TxtPlayerTitle, a {loc:Str} a code write would lose);
//   the Start button's charge/exhale/ignition/heartbeat set and FlashSaveAbsorb - no caller here
//   (BtnStart_Click and the settings-save half are stubs); level-up sound and toast.
// Sign-in, startup profile restore and logout repaint through UpdateQuickLoginUI
// (MainShellWindow.Login.cs), and the login dialog's ProfileLoad is awaited before a repaint.

using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private double _xpFraction;
        private Transitions? _xpFillTransition;

        /// <summary>WPF ctor subscriptions to App.Progression.XPChanged/LevelUp; the events are static,
        /// so they come off again when the window closes.</summary>
        private void HookLevelDisplay()
        {
            // THE BANK gets the award first (MainShellWindow.BankFx.cs): it arms its hold, repaints, and either
            // hands the readout straight back (weather) or stages it behind a flight of tokens (a completion).
            InitializeBankFx();
            Action<double, string> awarded = (amount, source) => Dispatcher.UIThread.Post(() => OnBankAward(amount, source));
            Action<int> levelUp = _ => Dispatcher.UIThread.Post(() => { FlashLevelUp(); PopLevelChip(); BurstLevelUp(); UpdateLevelDisplay(); });
            ProgressionBank.Awarded += awarded;
            ProgressionBank.LevelUp += levelUp;
            Closed += (_, _) => { ProgressionBank.Awarded -= awarded; ProgressionBank.LevelUp -= levelUp; ShutdownBankFx(); _xpOdometer?.Stop(); };
            if (Named<Border>("XPBar")?.Parent is Control track)
                track.SizeChanged += (_, _) => FillXpBar(animate: false);
            // WPF XPBarTrack_ToolTipOpening (MainWindow.UiUpdates.cs:2897): the ambient-bubble daily
            // budget, computed on open so it is never stale; read-only.
            if (Named<Border>("XPBarTrack") is { } xpTrack)
                xpTrack.AddHandler(ToolTip.ToolTipOpeningEvent, (_, _) => ToolTip.SetTip(xpTrack,
                    ConditioningControlPanel.Localization.Loc.GetF("label_ambient_bubble_xp_budget",
                        Services.AmbientBubbleXp.PaidToday(CoreSettings.Current), Services.AmbientBubbleXp.DailyXpCap)));
            UpdateLevelDisplay();
        }

        /// <summary>WPF MainWindow.UpdateLevelDisplay's header half.</summary>
        internal void UpdateLevelDisplay()
        {
            try
            {
                var s = CoreSettings.Current;
                var level = s.PlayerLevel;
                var xp = s.PlayerXP;
                var needed = XpCurve.GetXPForLevel(level, XpCurve.EpochOf(s));
                if (Named<TextBlock>("TxtLevelLabel") is { } label) label.Text = $"LVL {level}";
                // THE BANK gets first refusal: while a pot is collecting or tokens are in the air the readout and
                // the bar belong to the flight, and BankFx remembers the target instead (WPF AnimateXpDisplay).
                if (!TryHoldXpDisplay(xp, needed, level))
                {
                    if (Named<TextBlock>("TxtXP") is { } txt) AnimateXpReadout(txt, xp, needed, level);
                    _xpFraction = Math.Min(1.0, needed > 0 ? xp / needed : 0);
                    FillXpBar(animate: true);
                }

                // WPF RefreshProfileMenu (MainWindow.ProfileBubble.cs:315-326): same numbers as the bar.
                if (Named<TextBlock>("ProfileMenuLevel") is { } menuLevel) menuLevel.Text = $"{Loc.Get("label_level")} {level}";
                if (Named<TextBlock>("ProfileMenuXp") is { } menuXp) menuXp.Text = $"{xp:F0} / {needed:F0} {Loc.Get("label_xp")}";
                if (Named<Grid>("ProfileMenuXpRail")?.ColumnDefinitions is { Count: 2 } cols)
                {
                    var pct = Math.Clamp(_xpFraction, 0.0, 1.0);
                    cols[0].Width = new GridLength(pct, GridUnitType.Star);
                    cols[1].Width = new GridLength(1.0 - pct, GridUnitType.Star);
                }
                // WPF UpdateXPBarLoginState: the account chip and the bubble ride the same repaint (shell#17/#18).
                RefreshAccountIdentity();
            }
            catch (Exception ex) { Log.Debug("UpdateLevelDisplay: {E}", ex.Message); }
        }

        private double _lastXpShown = double.NaN;
        private int _lastXpLevelShown = -1;
        private DispatcherTimer? _xpOdometer;

        /// <summary>Test seam: the XP odometer is counting.</summary>
        internal bool XpOdometerRunning => _xpOdometer?.IsEnabled == true;

        /// <summary>Test seam: land a running count on its target now.</summary>
        internal void SettleXpOdometerForTests()
        {
            _xpOdometer?.Stop();
            _xpOdometer = null;
            if (_xpOdometerTarget != null && Named<TextBlock>("TxtXP") is { } txt) txt.Text = _xpOdometerTarget;
        }

        private string? _xpOdometerTarget;

        /// <summary>
        /// WPF AnimateXpDisplay's readout half (MainWindow.ChromeFx.cs:672) over MotionFx.Odometer:
        /// the numerator counts from the last shown value (0 on a new level) to the new one in
        /// 0.7 s, quadratic ease out, formatted F0 with the denominator baked in; it snaps with
        /// transitions off, on a step under 0.5 and while the window is not on screen.
        /// </summary>
        private void AnimateXpReadout(TextBlock txt, double xp, double needed, int level)
        {
            double from = (!double.IsNaN(_lastXpShown) && level == _lastXpLevelShown) ? _lastXpShown : 0;
            _lastXpShown = xp;
            _lastXpLevelShown = level;
            RunXpOdometer(txt, from, xp, needed, global::ConditioningControlPanel.Motion.MotionTimings.OdometerSeconds);
        }

        /// <summary>The count itself, shared with THE BANK's landings (StepBankCounter): from one value to
        /// another in <paramref name="seconds"/>, quadratic ease out, the denominator baked into the text.</summary>
        private void RunXpOdometer(TextBlock txt, double from, double xp, double needed, double seconds)
        {
            _xpOdometer?.Stop();
            _xpOdometer = null;
            _xpOdometerTarget = $"{(int)xp} / {(int)needed} XP";
            if (!AmbientFxCanvas.Env.AllowTransitions || !IsVisible || seconds <= 0
                || Math.Abs(xp - from) < global::ConditioningControlPanel.Motion.MotionTimings.OdometerMinStep)
            {
                txt.Text = $"{(int)xp} / {(int)needed} XP";
                return;
            }
            string tail = " / " + ((int)needed) + " XP";
            txt.Text = from.ToString("F0", System.Globalization.CultureInfo.CurrentCulture) + tail;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            DispatcherTimer? timer = null;
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
            {
                try
                {
                    double p = Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds / seconds, 0, 1);
                    double e = 1 - (1 - p) * (1 - p);   // QuadraticEase, EaseOut
                    txt.Text = (from + (xp - from) * e).ToString("F0", System.Globalization.CultureInfo.CurrentCulture) + tail;
                    if (p < 1) return;
                }
                catch (Exception ex) { Log.Debug("XP odometer: {E}", ex.Message); }
                timer!.Stop();
                if (ReferenceEquals(_xpOdometer, timer)) _xpOdometer = null;
            });
            _xpOdometer = timer;
            timer.Start();
        }

        /// <summary>FillXpBarTo: the fill is a fraction of the bar's parent grid (not the rounded track).</summary>
        private void FillXpBar(bool animate)
        {
            if (Named<Border>("XPBar") is not { Parent: Control track } bar) return;
            var available = track.Bounds.Width;
            if (available <= 0) return;
            bar.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions
                ? _xpFillTransition ??= new Transitions
                {
                    new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromSeconds(0.6), Easing = new QuadraticEaseOut() },
                }
                : null;
            bar.Width = _xpFraction * available;
            // Velvet Kit 2: the meniscus rides the same target width on the same clock and curve
            // (MainShellWindow.HudDepth.cs); the tube's bead follows the fill's own bounds.
            AnimateXpMeniscus(bar.Width, animate);
        }

        /// <summary>CelebrateLevelUp's FlashOverlay(XPBarFlashOverlay): 0 -> 1 over 250ms, auto-reversed.</summary>
        private void FlashLevelUp()
        {
            if (!AmbientFxCanvas.Env.AllowTransitions || Named<Border>("XPBarFlashOverlay") is not { } overlay) return;
            _ = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(250),
                IterationCount = new IterationCount(2),
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new QuadraticEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
                },
            }.RunAsync(overlay);
        }
    }
}
