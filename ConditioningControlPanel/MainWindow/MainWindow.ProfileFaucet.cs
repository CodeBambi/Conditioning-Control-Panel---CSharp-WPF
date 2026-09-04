using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE FAUCET on the Trainer Card's vat — the interactive half of the XP hold
    /// (pour/hold DECISIONS live in <see cref="VatFaucetHold"/>, folded in by
    /// MainWindow.ProfileVat.cs).
    ///
    /// THE GESTURE IS A CHARGE-HOLD, not a click (pitch "The tap holds",
    /// owner-approved 2026-08-30). Press and keep pressing: the tap dips (THE
    /// BOUNCE), a ring fills around its head, a six-rung tone ladder climbs with the
    /// ring, and when the ring closes the handle takes THE THUD on the same frame as
    /// the pour clip, the stream lands, the liquid rises over 2.1s and the readout
    /// counts up WITH it (THE BANK) instead of ahead of it. Let go early and the tap
    /// SHIVERS, drips once, and keeps every point that was waiting — there is no
    /// punishment and no guilt copy, because nothing was lost: the server block is
    /// still the only ledger and the XP was banked the moment it was earned.
    ///
    /// THE BUDGET SCALES WITH WHAT IS WAITING: 700ms + held/4, capped at 1400ms
    /// (owner call). A trickle is a tap; a whole evening is a real, deliberate
    /// press. House book THE CHARGE-HOLD: the wait IS the reward's setup, so it must
    /// be long enough to feel earned and short enough that nobody's thumb aches.
    ///
    /// MOTION LAW (house book VI, IX, X):
    ///   • Ambient loops — wobble, spout sparkle, the chip's breath — ask
    ///     <see cref="MotionFx.AllowAmbientLoops"/> and degrade to a still tap with a
    ///     badge dot. They are gated on the tab being on screen as well.
    ///   • The GESTURE outranks the ambient cap. The charge, the thud, the pour and
    ///     every sound still happen on a Performance tier; only genuine reduced
    ///     motion (MotionLevel.Reduced/Off) collapses the ring to a 120ms opacity
    ///     change — and the tone ladder still carries the beat.
    ///   • After three completed pours in a day the celebration takes the COMPACT
    ///     CUT: shorter ring, no sparks, same thud. The fourth pour of an evening
    ///     must not cost as much attention as the first.
    /// </summary>
    public partial class MainWindow
    {
        // ---- geometry ---------------------------------------------------------
        // The faucet art box is 40x42 (DiscordTabView.xaml); the spout mouth
        // centre sits at (27, 22) inside it. Everything else is derived from the
        // jar ratios in ArmVat so the tap cannot drift off the lip if the jar is
        // ever retuned.

        private const double FaucetSpoutXInBox = 27;
        private const double FaucetSpoutYInBox = 22;
        private const double FaucetBoxWidth = 40;
        private const double FaucetBoxHeight = 42;

        /// <summary>The charge ring's art box and its drawn radius, in DIPs.</summary>
        private const double FaucetRingBox = 34;
        private const double FaucetRingRadius = 15;

        // ---- the charge-hold's numbers -----------------------------------------

        /// <summary>Floor of the hold budget: a press below this reads as a slip, not a decision.</summary>
        private const double ChargeFloorMs = 700;

        /// <summary>Ceiling of the hold budget (owner call): past this a hold is a chore.</summary>
        private const double ChargeCapMs = 1400;

        /// <summary>XP per extra millisecond of hold — the "held/4" half of the budget.</summary>
        private const double ChargeXpPerMs = 4.0;

        /// <summary>Rungs in the rising tone ladder (faucet_charge_1..6.wav).</summary>
        private const int ChargeRungs = 6;

        /// <summary>Completed pours in a day past which the celebration takes the compact cut.</summary>
        private const int ChargeCompactAfter = 3;

        // ---- state ------------------------------------------------------------

        /// <summary>
        /// THE HOLD, backed by the persisted watermark (pitch "The tap holds",
        /// 2026-08-30). The ledger is what makes the hold survive a tab switch, an
        /// app restart, and XP earned on web/mobile/Discord: held is derived from the
        /// server's today_xp every reading, never accumulated in this process.
        /// </summary>
        private readonly VatFaucetHold _faucetHold = new(new AppSettingsVatPourLedger());
        private bool _faucetWired;
        private bool _faucetWobbling;
        private bool _faucetSparkling;
        private bool _faucetChipBreathing;
        private DispatcherTimer? _faucetTickTimer;
        private DispatcherTimer? _faucetSettleTimer;

        // charge-hold runtime
        private DispatcherTimer? _faucetChargeTimer;
        private DateTime _faucetChargeStart;
        private double _faucetChargeBudgetMs;
        private int _faucetChargeRungsPlayed;
        private bool _faucetCharging;
        private bool _faucetChargeCompact;

        /// <summary>
        /// Completed pours on <see cref="_faucetPourDayUtc"/> — the compact-cut
        /// counter. In-memory on purpose: forgetting it on restart only ever spends
        /// one more sparkle burst, and the alternative is a settings row describing
        /// something nobody would ever want to read back.
        /// </summary>
        private int _faucetPoursToday;
        private string _faucetPourDayUtc = string.Empty;

        private static readonly Random FaucetRng = new();

        // ============================== arm / disarm ===========================

        /// <summary>
        /// Perch the faucet on the vat's top-left lip, park the charge ring around
        /// its head, centre the chip in the jar's headspace, hang the tick legend on
        /// the meter's own lines, and hand the canvas the spout x so the pour stream
        /// falls from OUR tap instead of the built-in art. Called from ArmVat with
        /// the jar box it just sized.
        /// </summary>
        private void ArmFaucet(VatGlassCanvas glass, double jarW, double jarH)
        {
            try
            {
                var faucet = DiscordTab?.ProfileVatFaucet;
                if (faucet == null) return;

                WireFaucet(faucet);

                double x0 = jarW * 0.10;            // the jar's left wall (VatGlassCanvas.JarX0)
                double yT = jarH * 0.175;           // the lip line (VatGlassCanvas.JarYTop)
                double left = Math.Round(x0 - 8);   // flange straddles the corner (owner nudge 0813: +4px right)
                double top = Math.Round(yT - (FaucetBoxHeight - 6));  // base sits ON the lip

                faucet.Margin = new Thickness(left, top, 0, 0);
                faucet.Visibility = Visibility.Visible;

                // THE RING is centred on the tap's column rather than on the tee
                // itself: a ring around the 4px tee would have to reach above the
                // hero card's top edge to be legible, and a clipped progress ring
                // reads as a bug. Centre (15, 18) inside the art box keeps the whole
                // circle inside the bay at every jar size we ship.
                var ring = DiscordTab?.ProfileVatChargeRing;
                if (ring != null)
                {
                    ring.Margin = new Thickness(
                        left + 15 - FaucetRingBox / 2,
                        top + 18 - FaucetRingBox / 2, 0, 0);
                }

                // The sparks canvas IS the jar box, so a mote's coordinates are
                // simply glass coordinates.
                var sparks = DiscordTab?.ProfileVatSparks;
                if (sparks != null)
                {
                    sparks.Width = jarW;
                    sparks.Height = jarH;
                }

                // THE CHIP sits in the jar's headspace, centred on the glass and
                // clear of the tap's art box — high enough to be read against the
                // empty top of the jar rather than against the portrait.
                var chip = DiscordTab?.ProfileVatChip;
                if (chip != null) chip.Margin = new Thickness(0, Math.Round(yT) + 8, 0, 0);

                glass.ExternalSpoutXFraction = (left + FaucetSpoutXInBox) / jarW;

                PositionVatTickGlyphs();
                UpdateFaucetPresentation();
            }
            catch (Exception ex) { App.Logger?.Debug("ArmFaucet: {E}", ex.Message); }
        }

        /// <summary>
        /// Hang the moon / mortarboard / crown on the meter's tick lines, asking the
        /// canvas where they are rather than re-deriving them. Re-run after every
        /// SetLip, because the MAX mark MOVES with the lip (a deeper subject's taller
        /// lip pushes CAP down the glass) and a legend that stays put while its line
        /// walks away is worse than no legend.
        /// </summary>
        private void PositionVatTickGlyphs()
        {
            try
            {
                var glass = DiscordTab?.ProfileVatGlass;
                if (glass == null) return;

                double innerX = glass.TickInnerX;

                Place(DiscordTab?.ProfileVatTickDrain, VatGlassCanvas.VatTickMark.Drain);
                Place(DiscordTab?.ProfileVatTickCap, VatGlassCanvas.VatTickMark.Cap);
                Place(DiscordTab?.ProfileVatTickMax, VatGlassCanvas.VatTickMark.Max);

                void Place(Path? glyph, VatGlassCanvas.VatTickMark mark)
                {
                    if (glyph == null) return;
                    double? y = glass.TickCenterY(mark);
                    if (y == null || innerX <= 0)
                    {
                        glyph.Visibility = Visibility.Collapsed;
                        return;
                    }
                    glyph.Margin = new Thickness(
                        Math.Round(innerX - 3 - glyph.Width),
                        Math.Round(y.Value - glyph.Height / 2), 0, 0);
                    glyph.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("PositionVatTickGlyphs: {E}", ex.Message); }
        }

        /// <summary>Put everything away — vat disarmed (dial narrowed, logout).</summary>
        private void DisarmFaucet()
        {
            try
            {
                CancelFaucetCharge(silent: true);
                _faucetHold.Reset();
                StopFaucetWobble();
                StopFaucetSettleWatch();
                StopChipBreath();

                var faucet = DiscordTab?.ProfileVatFaucet;
                if (faucet != null) faucet.Visibility = Visibility.Collapsed;

                Hide(DiscordTab?.ProfileVatChip);
                Hide(DiscordTab?.ProfileVatChargeRing);
                Hide(DiscordTab?.ProfileVatTickDrain);
                Hide(DiscordTab?.ProfileVatTickCap);
                Hide(DiscordTab?.ProfileVatTickMax);
                DiscordTab?.ProfileVatSparks?.Children.Clear();

                var glass = DiscordTab?.ProfileVatGlass;
                if (glass != null) glass.ExternalSpoutXFraction = null;

                static void Hide(UIElement? e) { if (e != null) e.Visibility = Visibility.Collapsed; }
            }
            catch (Exception ex) { App.Logger?.Debug("DisarmFaucet: {E}", ex.Message); }
        }

        /// <summary>
        /// Tab navigated away: park every clock and abandon any charge in progress.
        /// The HELD XP itself is untouched — it lives in the persisted watermark now,
        /// so it is still waiting when the user comes back (pitch "The tap holds";
        /// the 2026-08-13 rule that re-entry threw the hold away is gone).
        /// </summary>
        private void OnFaucetVatOffScreen()
        {
            try
            {
                CancelFaucetCharge(silent: true);
                StopFaucetWobble();
                StopFaucetSettleWatch();
                StopChipBreath();
            }
            catch (Exception ex) { App.Logger?.Debug("OnFaucetVatOffScreen: {E}", ex.Message); }
        }

        private void WireFaucet(UIElement faucet)
        {
            if (_faucetWired) return;
            _faucetWired = true;
            faucet.MouseEnter += OnFaucetMouseEnter;
            faucet.MouseLeave += OnFaucetMouseLeave;
            faucet.MouseLeftButtonDown += OnFaucetMouseDown;
            faucet.MouseLeftButtonUp += OnFaucetMouseUp;
            faucet.LostMouseCapture += OnFaucetLostCapture;
            faucet.KeyDown += OnFaucetKeyDown;
            faucet.KeyUp += OnFaucetKeyUp;
            faucet.LostKeyboardFocus += OnFaucetLostFocus;
        }

        // ============================== presentation ===========================

        /// <summary>
        /// Fold the hold's current state into the visual: wobble + sparkle + the gold
        /// chip while XP waits (motion allowing), the still badge dot under reduced
        /// motion, and the tooltip that says both how much is waiting and what the
        /// jar even is. Idempotent — called after every reading, pour and visibility
        /// change.
        /// </summary>
        private void UpdateFaucetPresentation()
        {
            try
            {
                var faucet = DiscordTab?.ProfileVatFaucet;
                if (faucet == null || faucet.Visibility != Visibility.Visible) return;

                int held = _faucetHold.HeldXp;
                bool pouring = DiscordTab?.ProfileVatGlass?.IsPouring == true;
                bool ambient = MotionFx.AllowAmbientLoops;
                bool waiting = held > 0 && !pouring && !_faucetCharging;

                faucet.ToolTip = BuildFaucetTooltip(held);

                if (waiting && ambient && _vatOnScreen) StartFaucetWobble();
                else StopFaucetWobble();

                // THE CHIP: the one thing on this screen allowed to breathe (house
                // book law III), and only while there is genuinely something to say.
                var chip = DiscordTab?.ProfileVatChip;
                var chipText = DiscordTab?.ProfileVatChipText;
                if (chip != null)
                {
                    if (waiting)
                    {
                        if (chipText != null) chipText.Text = Loc.GetF("profile_vat_chip", held);
                        chip.Visibility = Visibility.Visible;
                        if (ambient && _vatOnScreen) StartChipBreath(); else StopChipBreath();
                    }
                    else
                    {
                        chip.Visibility = Visibility.Collapsed;
                        StopChipBreath();
                    }
                }

                var badge = DiscordTab?.ProfileVatFaucetBadge;
                if (badge != null)
                    badge.Visibility = held > 0 && !ambient
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("UpdateFaucetPresentation: {E}", ex.Message); }
        }

        /// <summary>
        /// Two lines: what is waiting (or that nothing is), and what the jar IS. The
        /// second line used to live behind a "?" badge floating beside the tap; it is
        /// on the tap itself now, because the tap is the only thing here anybody
        /// hovers and a jar that has to be interrogated is not explained. Rebuilt
        /// each pass so a language switch cannot leave it stale.
        /// </summary>
        private static ToolTip BuildFaucetTooltip(int held)
        {
            var stack = new StackPanel { MaxWidth = 320 };
            stack.Children.Add(new TextBlock
            {
                Text = held > 0
                    ? Loc.GetF("profile_faucet_tip_held", held)
                    : Loc.Get("profile_faucet_tip_empty"),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
            });
            stack.Children.Add(new TextBlock
            {
                Text = Loc.Get("profile_vat_help_tip"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.72,
                Margin = new Thickness(0, 5, 0, 0),
            });
            return new ToolTip { Content = stack, MaxWidth = 340 };
        }

        /// <summary>
        /// The wobble: a small rock around the base pivot, plus the droplet sparkle
        /// at the spout (glow tier allowing) and the sparse tick clock. Caller has
        /// already checked AllowAmbientLoops.
        /// </summary>
        private void StartFaucetWobble()
        {
            if (_faucetWobbling) return;
            var tilt = DiscordTab?.ProfileVatFaucetTilt;
            if (tilt == null) return;
            _faucetWobbling = true;

            var rock = new DoubleAnimation(-2.2, 2.2, TimeSpan.FromSeconds(0.55))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(rock, AmbientFrameRate);
            tilt.BeginAnimation(RotateTransform.AngleProperty, rock);

            if (PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier)) StartFaucetSparkle();

            // The tick is SPARSE by design: one soft metallic tick every few
            // seconds, never continuous noise. It only sounds while the window is
            // actually in front of somebody — same presence test as the vat poll.
            _faucetTickTimer ??= CreateFaucetTickTimer();
            _faucetTickTimer.Start();
        }

        private void StopFaucetWobble()
        {
            if (!_faucetWobbling)
            {
                StopFaucetSparkle();
                _faucetTickTimer?.Stop();
                return;
            }
            _faucetWobbling = false;
            _faucetTickTimer?.Stop();
            StopFaucetSparkle();

            var tilt = DiscordTab?.ProfileVatFaucetTilt;
            if (tilt != null)
            {
                tilt.BeginAnimation(RotateTransform.AngleProperty, null);
                tilt.Angle = 0;
            }
        }

        private DispatcherTimer CreateFaucetTickTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(3.8),
            };
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (Application.Current?.Dispatcher == null) return;
                    if (!_faucetWobbling || !VatGlassCanvas.WindowIsPresenting(this))
                        return;
                    PlayFaucetSfx("faucet_tick.wav", 0.15f);
                }
                catch (Exception ex) { App.Logger?.Debug("Faucet tick: {E}", ex.Message); }
            };
            return timer;
        }

        /// <summary>Droplet shimmer at the spout: fade in, fall ~7px, fade out, loop.</summary>
        private void StartFaucetSparkle()
        {
            if (_faucetSparkling) return;
            var drop = DiscordTab?.ProfileVatFaucetDrop;
            var fall = DiscordTab?.ProfileVatFaucetDropFall;
            if (drop == null || fall == null) return;
            _faucetSparkling = true;

            var dur = TimeSpan.FromSeconds(1.8);

            var fade = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = dur };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0.25)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromPercent(0.7)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Timeline.SetDesiredFrameRate(fade, AmbientFrameRate);
            drop.BeginAnimation(OpacityProperty, fade);

            var descend = new DoubleAnimation(0, 7, dur) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(descend, AmbientFrameRate);
            fall.BeginAnimation(TranslateTransform.YProperty, descend);
        }

        private void StopFaucetSparkle()
        {
            if (!_faucetSparkling) return;
            _faucetSparkling = false;
            var drop = DiscordTab?.ProfileVatFaucetDrop;
            var fall = DiscordTab?.ProfileVatFaucetDropFall;
            if (drop != null)
            {
                drop.BeginAnimation(OpacityProperty, null);
                drop.Opacity = 0;
            }
            if (fall != null)
            {
                fall.BeginAnimation(TranslateTransform.YProperty, null);
                fall.Y = 0;
            }
        }

        /// <summary>THE BREATH on the chip: 1.00 -> 1.05 over 2.8s, in and out.
        /// House book law III — one breather per screen, and this is it.</summary>
        private void StartChipBreath()
        {
            if (_faucetChipBreathing) return;
            var scale = DiscordTab?.ProfileVatChipScale;
            if (scale == null) return;
            _faucetChipBreathing = true;

            var breath = new DoubleAnimation(1.0, 1.05, TimeSpan.FromSeconds(2.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(breath, AmbientFrameRate);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, breath);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, breath);
        }

        private void StopChipBreath()
        {
            if (!_faucetChipBreathing) return;
            _faucetChipBreathing = false;
            var scale = DiscordTab?.ProfileVatChipScale;
            if (scale == null) return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        // ================================ hover ================================

        private void OnFaucetMouseEnter(object sender, MouseEventArgs e)
        {
            if (_faucetCharging) return;
            AnimateFaucetScale(1.16);
        }

        private void OnFaucetMouseLeave(object sender, MouseEventArgs e)
        {
            // Sliding off the tap mid-charge is a release, and an EARLY one: the ring
            // has not closed, so the hold is kept and the tap shivers.
            if (_faucetCharging) { CancelFaucetCharge(silent: false); return; }
            AnimateFaucetScale(1.0);
        }

        private void AnimateFaucetScale(double to, double ms = 150)
        {
            try
            {
                var scale = DiscordTab?.ProfileVatFaucetScale;
                if (scale == null) return;

                if (!MotionFx.AllowTransitions)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = to;
                    scale.ScaleY = to;
                    return;
                }

                var grow = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
            catch (Exception ex) { App.Logger?.Debug("AnimateFaucetScale: {E}", ex.Message); }
        }

        // ============================ the charge-hold ==========================

        private void OnFaucetMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                var faucet = DiscordTab?.ProfileVatFaucet;
                faucet?.Focus();                       // so the keyboard can finish what the mouse started
                if (faucet != null && !faucet.IsMouseCaptured) faucet.CaptureMouse();
                BeginFaucetCharge();
            }
            catch (Exception ex) { App.Logger?.Debug("OnFaucetMouseDown: {E}", ex.Message); }
        }

        private void OnFaucetMouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                var faucet = DiscordTab?.ProfileVatFaucet;
                if (faucet != null && faucet.IsMouseCaptured) faucet.ReleaseMouseCapture();
                if (_faucetCharging) CancelFaucetCharge(silent: false);
            }
            catch (Exception ex) { App.Logger?.Debug("OnFaucetMouseUp: {E}", ex.Message); }
        }

        private void OnFaucetLostCapture(object sender, MouseEventArgs e)
        {
            // Alt-tab, a modal stealing input, the window deactivating. Not a
            // completed pour, so the hold survives — silently, because nothing the
            // user did deserves a shiver.
            if (_faucetCharging) CancelFaucetCharge(silent: true);
        }

        private void OnFaucetKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space && e.Key != Key.Enter) return;
            e.Handled = true;
            if (e.IsRepeat) return;                   // auto-repeat is one long press, not many
            BeginFaucetCharge();
        }

        private void OnFaucetKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space && e.Key != Key.Enter) return;
            e.Handled = true;
            if (_faucetCharging) CancelFaucetCharge(silent: false);
        }

        private void OnFaucetLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_faucetCharging) CancelFaucetCharge(silent: true);
        }

        /// <summary>
        /// PRESS. Dip the tap (THE BOUNCE), open the ring, sound the ladder's bottom
        /// rung, and start the clock that fills the ring over the budget.
        /// </summary>
        private void BeginFaucetCharge()
        {
            try
            {
                if (_faucetCharging) return;
                var glass = DiscordTab?.ProfileVatGlass;
                if (glass == null || !_vatArmed) return;
                if (glass.IsPouring) return;             // a stream is already running

                int held = _faucetHold.HeldXp;
                if (held <= 0) return;                   // nothing waiting — the tooltip already says so

                _faucetCharging = true;
                _faucetChargeRungsPlayed = 0;
                _faucetChargeCompact = FaucetPourCountToday() >= ChargeCompactAfter;

                // THE BUDGET: 700ms + held/4, capped at 1400ms (owner call). The
                // compact cut shortens the ring rather than removing it — the fourth
                // pour of an evening still answers the press, it just stops making a
                // ceremony of it (house book law IX).
                double budget = Math.Min(ChargeCapMs, ChargeFloorMs + held / ChargeXpPerMs);
                if (_faucetChargeCompact) budget *= 0.6;
                _faucetChargeBudgetMs = Math.Max(220, budget);
                _faucetChargeStart = DateTime.UtcNow;

                StopFaucetWobble();
                StopChipBreath();
                AnimateFaucetScale(0.94, 90);            // THE BOUNCE, on the way down
                ShowChargeRing();
                PlayChargeRung(_faucetChargeRungsPlayed++);   // the low tone, on the press frame

                _faucetChargeTimer ??= CreateFaucetChargeTimer();
                _faucetChargeTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BeginFaucetCharge: {E}", ex.Message);
                CancelFaucetCharge(silent: true);
            }
        }

        private DispatcherTimer CreateFaucetChargeTimer()
        {
            // 33ms is the cadence the vat canvas runs its own clock at — the ring and
            // the liquid should never disagree about what a frame is.
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33),
            };
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (Application.Current?.Dispatcher == null) { _faucetChargeTimer?.Stop(); return; }
                    if (!_faucetCharging) { _faucetChargeTimer?.Stop(); return; }

                    double t = (DateTime.UtcNow - _faucetChargeStart).TotalMilliseconds / _faucetChargeBudgetMs;
                    t = Math.Clamp(t, 0, 1);

                    UpdateChargeRing(t);

                    // THE TONE RISES WITH THE RING, one rung at a time. WPF cannot
                    // pitch-shift a clip and the budget is variable, so one sweeping
                    // tone would desynchronise from the ring on every hold of a
                    // different size; six pre-rendered rungs stepped by ring progress
                    // cannot (and it is the house book's chime-ladder idiom anyway).
                    int want = Math.Min(ChargeRungs, (int)(t * ChargeRungs) + 1);
                    while (_faucetChargeRungsPlayed < want)
                        PlayChargeRung(_faucetChargeRungsPlayed++);

                    if (t >= 1) CompleteFaucetCharge();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Faucet charge: {E}", ex.Message);
                    CancelFaucetCharge(silent: true);
                }
            };
            return timer;
        }

        /// <summary>
        /// RELEASED EARLY. THE SHIVER (420ms of decaying x-jitter), one drip, one
        /// falling tone — and the hold is untouched. No guilt copy anywhere: nothing
        /// was lost, the XP was banked when it was earned, and the tap is still full.
        /// </summary>
        private void CancelFaucetCharge(bool silent)
        {
            try
            {
                if (!_faucetCharging)
                {
                    HideChargeRing();
                    return;
                }
                _faucetCharging = false;
                _faucetChargeTimer?.Stop();
                HideChargeRing();
                AnimateFaucetScale(1.0);

                if (!silent)
                {
                    FaucetShiver();
                    FaucetDrip();
                    PlayFaucetSfx("faucet_charge_drop.wav", 0.15f);
                }

                UpdateFaucetPresentation();              // the wobble and the chip come back
            }
            catch (Exception ex) { App.Logger?.Debug("CancelFaucetCharge: {E}", ex.Message); }
        }

        /// <summary>
        /// THE RING CLOSED. Thud, pour clip, stream, 2.1s rise, the readout banking
        /// up WITH the liquid, and a sparkle burst inside 50ms — the whole beat lands
        /// on one frame (house book law X).
        /// </summary>
        private void CompleteFaucetCharge()
        {
            try
            {
                _faucetCharging = false;
                _faucetChargeTimer?.Stop();
                HideChargeRing();

                var glass = DiscordTab?.ProfileVatGlass;
                if (glass == null || !_vatArmed) { AnimateFaucetScale(1.0); return; }

                int poured = _faucetHold.HeldXp;
                if (poured <= 0) { AnimateFaucetScale(1.0); UpdateFaucetPresentation(); return; }

                bool wasOver = glass.IsPastBrim(glass.Fill);
                var step = _faucetHold.PourAll();
                bool crossesLip = glass.IsPastBrim(step.Fill) && !wasOver;

                AnimateFaucetScale(1.0, 120);
                FaucetThud();
                PlayFaucetSfx("faucet_pour.wav", 0.35f);   // SAME FRAME as the thud

                AnimateFaucetTilt(14);
                // userGesture: the one press this whole feature exists for outranks
                // the ambient cap — see VatGlassCanvas.PourTo.
                glass.PourTo(step.Fill, userGesture: true);
                FireFaucetPourHaptic();

                if (crossesLip)
                {
                    // THE MINOR JACKPOT RUNG: running over the lip gets a brighter
                    // spill and one extra chime layer, and nothing else. It is a
                    // flourish on a pour, not a second event.
                    glass.PulseOverflow();
                    PlayFaucetSfx("faucet_brim.wav", 0.30f);
                }

                if (!_faucetChargeCompact) SpawnFaucetSparkles();

                NoteFaucetPourToday();
                StartFaucetSettleWatch();
                UpdateFaucetPresentation();

                App.Logger?.Information(
                    "[Descent] faucet poured +{Xp} held XP -> {Pct:F0}% (hold {Ms:F0}ms{Cut})",
                    poured, step.Fill * 100, _faucetChargeBudgetMs,
                    _faucetChargeCompact ? ", compact" : string.Empty);
            }
            catch (Exception ex) { App.Logger?.Debug("CompleteFaucetCharge: {E}", ex.Message); }
        }

        // ---- the charge ring ---------------------------------------------------

        private void ShowChargeRing()
        {
            try
            {
                var ring = DiscordTab?.ProfileVatChargeRing;
                var arc = DiscordTab?.ProfileVatChargeArc;
                if (ring == null) return;

                bool sweep = MotionFx.Level == MotionLevel.Full;

                // REDUCED MOTION collapses the sweep to a 120ms opacity change on the
                // whole circle (house book law VI). The tone ladder is untouched, so
                // the beat still arrives on time and the gesture still has a length.
                if (arc != null) arc.Data = sweep ? Geometry.Empty : BuildChargeArc(1.0);

                ring.Visibility = Visibility.Visible;
                ring.BeginAnimation(OpacityProperty, null);
                if (MotionFx.AllowTransitions)
                    ring.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
                else
                    ring.Opacity = 1;
            }
            catch (Exception ex) { App.Logger?.Debug("ShowChargeRing: {E}", ex.Message); }
        }

        private void UpdateChargeRing(double t)
        {
            if (MotionFx.Level != MotionLevel.Full) return;      // reduced: the circle is already whole
            var arc = DiscordTab?.ProfileVatChargeArc;
            if (arc == null) return;
            arc.Data = BuildChargeArc(t);
        }

        private void HideChargeRing()
        {
            try
            {
                var ring = DiscordTab?.ProfileVatChargeRing;
                if (ring == null) return;
                ring.BeginAnimation(OpacityProperty, null);
                ring.Opacity = 0;
                ring.Visibility = Visibility.Collapsed;
                var arc = DiscordTab?.ProfileVatChargeArc;
                if (arc != null) arc.Data = Geometry.Empty;
            }
            catch (Exception ex) { App.Logger?.Debug("HideChargeRing: {E}", ex.Message); }
        }

        /// <summary>
        /// The conic-fill ring as a plain arc: WPF has no conic gradient, and it does
        /// not need one — a stroked arc from 12 o'clock clockwise IS the fill, and it
        /// costs one frozen geometry per frame instead of a shader.
        /// </summary>
        private static Geometry BuildChargeArc(double t)
        {
            t = Math.Clamp(t, 0, 1);
            double c = FaucetRingBox / 2, r = FaucetRingRadius;
            if (t <= 0.002) return Geometry.Empty;
            if (t >= 0.999)
            {
                var full = new EllipseGeometry(new Point(c, c), r, r);
                full.Freeze();
                return full;
            }

            double a = t * 2 * Math.PI;
            var figure = new PathFigure
            {
                StartPoint = new Point(c, c - r),
                IsClosed = false,
                IsFilled = false,
            };
            figure.Segments.Add(new ArcSegment(
                new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)),
                new Size(r, r), 0, t > 0.5, SweepDirection.Clockwise, true));

            var geo = new PathGeometry();
            geo.Figures.Add(figure);
            geo.Freeze();
            return geo;
        }

        // ---- the moves ---------------------------------------------------------

        /// <summary>
        /// THE THUD (house book): the handle stamps 2.1 -> 0.86 -> 1.0 over 340ms.
        /// The reference curve is cubic-bezier(.2,1.5,.4,1), whose 1.5 control point
        /// is OUT OF RANGE for a WPF KeySpline (0..1 only), so the overshoot is
        /// written as the keyframes that curve produces rather than as a spline that
        /// would be rejected at parse time.
        /// </summary>
        private void FaucetThud()
        {
            try
            {
                var scale = DiscordTab?.ProfileVatHandleScale;
                if (scale == null) return;

                if (!MotionFx.AllowTransitions)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = 1;
                    scale.ScaleY = 1;
                    return;
                }

                var stamp = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(340),
                    FillBehavior = FillBehavior.Stop,
                };
                stamp.KeyFrames.Add(new DiscreteDoubleKeyFrame(2.1, KeyTime.FromPercent(0)));
                stamp.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromPercent(0.56),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                stamp.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0),
                    new CubicEase { EasingMode = EasingMode.EaseInOut }));

                scale.BeginAnimation(ScaleTransform.ScaleXProperty, stamp);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, stamp);
            }
            catch (Exception ex) { App.Logger?.Debug("FaucetThud: {E}", ex.Message); }
        }

        /// <summary>THE SHIVER: 420ms of decaying x-jitter. The house's "not this
        /// time" — it reads as the tap settling back, never as a scolding.</summary>
        private void FaucetShiver()
        {
            try
            {
                var shake = DiscordTab?.ProfileVatFaucetShake;
                if (shake == null) return;

                if (!MotionFx.AllowTransitions)
                {
                    shake.BeginAnimation(TranslateTransform.XProperty, null);
                    shake.X = 0;
                    return;
                }

                double[] amps = { 0, -2.6, 2.2, -1.6, 1.1, -0.6, 0.25, 0 };
                var jitter = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(420),
                    FillBehavior = FillBehavior.Stop,
                };
                for (int i = 0; i < amps.Length; i++)
                    jitter.KeyFrames.Add(new LinearDoubleKeyFrame(
                        amps[i], KeyTime.FromPercent(i / (double)(amps.Length - 1))));

                shake.BeginAnimation(TranslateTransform.XProperty, jitter);
            }
            catch (Exception ex) { App.Logger?.Debug("FaucetShiver: {E}", ex.Message); }
        }

        /// <summary>One drop off the spout on an early release — the tap says "still here".</summary>
        private void FaucetDrip()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                var drop = DiscordTab?.ProfileVatFaucetDrop;
                var fall = DiscordTab?.ProfileVatFaucetDropFall;
                if (drop == null || fall == null) return;

                var dur = TimeSpan.FromMilliseconds(520);

                var fade = new DoubleAnimationUsingKeyFrames { Duration = dur, FillBehavior = FillBehavior.Stop };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.2)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                drop.BeginAnimation(OpacityProperty, fade);

                var descend = new DoubleAnimation(0, 10, dur)
                {
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                };
                fall.BeginAnimation(TranslateTransform.YProperty, descend);
            }
            catch (Exception ex) { App.Logger?.Debug("FaucetDrip: {E}", ex.Message); }
        }

        /// <summary>
        /// THE SPARKLE BURST: 5-9 motes thrown from the spout, gone in ~600ms, and
        /// only on a pour that was not the fourth of the day. Gated on MotionLevel
        /// rather than on AllowParticles — the tier cap governs AMBIENT fields, and
        /// nine ellipses answering a deliberate press is not an ambient field.
        /// </summary>
        private void SpawnFaucetSparkles()
        {
            try
            {
                if (MotionFx.Level != MotionLevel.Full) return;
                var host = DiscordTab?.ProfileVatSparks;
                var faucet = DiscordTab?.ProfileVatFaucet;
                if (host == null || faucet == null) return;

                double ox = faucet.Margin.Left + FaucetSpoutXInBox;
                double oy = faucet.Margin.Top + FaucetSpoutYInBox;
                var brush = TryFindResource("PinkBrush") as Brush ?? Brushes.HotPink;

                int count = FaucetRng.Next(5, 10);
                for (int i = 0; i < count; i++)
                {
                    double angle = (Math.PI * 2 * i / count) + FaucetRng.NextDouble() * 0.5;
                    double dist = 12 + FaucetRng.NextDouble() * 16;
                    double size = 2.2 + FaucetRng.NextDouble() * 2.0;

                    var mote = new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = brush,
                        IsHitTestVisible = false,
                    };
                    var move = new TranslateTransform();
                    mote.RenderTransform = move;
                    Canvas.SetLeft(mote, ox - size / 2);
                    Canvas.SetTop(mote, oy - size / 2);
                    host.Children.Add(mote);

                    var dur = TimeSpan.FromMilliseconds(420 + FaucetRng.Next(200));
                    var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                    move.BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(0, Math.Cos(angle) * dist, dur) { EasingFunction = ease });
                    move.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(0, Math.Sin(angle) * dist + 6, dur) { EasingFunction = ease });

                    var captured = mote;
                    var fade = new DoubleAnimation(1, 0, dur);
                    fade.Completed += (_, _) =>
                    {
                        try { host.Children.Remove(captured); }
                        catch (Exception ex) { App.Logger?.Debug("Faucet spark reap: {E}", ex.Message); }
                    };
                    mote.BeginAnimation(OpacityProperty, fade);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("SpawnFaucetSparkles: {E}", ex.Message); }
        }

        private void AnimateFaucetTilt(double angle)
        {
            try
            {
                var tilt = DiscordTab?.ProfileVatFaucetTilt;
                if (tilt == null) return;

                if (!MotionFx.AllowTransitions)
                {
                    tilt.BeginAnimation(RotateTransform.AngleProperty, null);
                    tilt.Angle = 0;     // reduced motion: the tap never leans
                    return;
                }

                var lean = new DoubleAnimation(angle, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                tilt.BeginAnimation(RotateTransform.AngleProperty, lean);
            }
            catch (Exception ex) { App.Logger?.Debug("AnimateFaucetTilt: {E}", ex.Message); }
        }

        /// <summary>
        /// Watch the pour run out (it can be EXTENDED by a mid-pour delta, so no
        /// fixed-length timer) and right the tap when the stream stops.
        /// </summary>
        private void StartFaucetSettleWatch()
        {
            _faucetSettleTimer ??= CreateFaucetSettleTimer();
            _faucetSettleTimer.Start();
        }

        private void StopFaucetSettleWatch() => _faucetSettleTimer?.Stop();

        private DispatcherTimer CreateFaucetSettleTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (Application.Current?.Dispatcher == null) { _faucetSettleTimer?.Stop(); return; }
                    if (DiscordTab?.ProfileVatGlass?.IsPouring == true) return;
                    _faucetSettleTimer?.Stop();
                    AnimateFaucetTilt(0);
                    UpdateFaucetPresentation();   // wobble resumes only if new XP accrued
                }
                catch (Exception ex) { App.Logger?.Debug("Faucet settle: {E}", ex.Message); }
            };
            return timer;
        }

        // ---- the compact cut's counter ------------------------------------------

        private int FaucetPourCountToday()
        {
            string day = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.Equals(_faucetPourDayUtc, day, StringComparison.Ordinal))
            {
                _faucetPourDayUtc = day;
                _faucetPoursToday = 0;
            }
            return _faucetPoursToday;
        }

        private void NoteFaucetPourToday()
        {
            FaucetPourCountToday();
            _faucetPoursToday++;
        }

        // ============================== feedback ===============================

        /// <summary>One rung of the rising ladder (faucet_charge_1..6.wav, generated
        /// by tools/asset_gen/gen_faucet_charge_sfx.py).</summary>
        private static void PlayChargeRung(int index)
        {
            int rung = Math.Clamp(index, 0, ChargeRungs - 1) + 1;
            PlayFaucetSfx($"faucet_charge_{rung}.wav", 0.15f);
        }

        /// <summary>
        /// One-shot through AudioService (device lifetime, concurrency cap and
        /// disposal are its problem). Scaled off the master volume only — these are
        /// UI cues, deliberately faint, and a muted master mutes them.
        /// </summary>
        private static void PlayFaucetSfx(string file, float scale)
        {
            try
            {
                float master = (float)(App.Settings?.Current?.MasterVolume ?? 0) / 100f;
                float volume = Math.Clamp(master * scale, 0f, 1f);
                if (volume <= 0f) return;

                var path = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", file);
                App.Audio?.PlayOneShot(path, volume, "vat-faucet");
            }
            catch (Exception ex) { App.Logger?.Debug("PlayFaucetSfx: {E}", ex.Message); }
        }

        /// <summary>
        /// A LIGHT pulse riding the Level-Up routing row at reduced intensity — the
        /// row's Enabled switch and the user's slider both stay in charge (a
        /// disabled row or a zeroed slider means silence, not a floor buzz).
        /// </summary>
        private static void FireFaucetPourHaptic()
        {
            try
            {
                var haptics = App.Haptics;
                if (haptics == null) return;
                var rule = haptics.Settings?.V2?.Rule(HapticEventKind.LevelUp);
                if (rule == null || !rule.Enabled || rule.Intensity <= 0) return;
                _ = haptics.PostEvent(HapticEventKind.LevelUp, Math.Clamp(rule.Intensity * 0.35, 0, 1));
            }
            catch (Exception ex) { App.Logger?.Debug("FireFaucetPourHaptic: {E}", ex.Message); }
        }
    }
}
