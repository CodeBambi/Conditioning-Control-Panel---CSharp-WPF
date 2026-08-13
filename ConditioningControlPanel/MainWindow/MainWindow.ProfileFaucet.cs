using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE FAUCET on the Trainer Card's vat — the interactive half of the XP hold
    /// (owner survey 2026-08-13; the pour/hold DECISIONS live in
    /// <see cref="VatFaucetHold"/>, folded in by MainWindow.ProfileVat.cs).
    ///
    /// WHAT THIS FILE OWNS: the XAML faucet perched on the jar's top-left lip
    /// (position derived from the same locked ratios ArmVat uses), its wobble
    /// while XP waits, the hover grow, the click-to-pour tilt, and the three
    /// SLIGHT feedback channels — a sparse metallic tick + a pour clip through
    /// AudioService one-shots, a gentle haptic pulse riding the Level-Up routing
    /// row at reduced intensity, and the droplet sparkle at the spout.
    ///
    /// MOTION LAW: the wobble and the sparkle are AMBIENT loops — they ask
    /// <see cref="MotionFx.AllowAmbientLoops"/> (sparkle additionally
    /// <see cref="PerformanceProfile.AllowGlow"/>) before starting a clock, run at
    /// <see cref="AmbientFrameRate"/>, and degrade to a still faucet with a small
    /// badge dot when denied. The CLICK still pours under reduced motion (the
    /// glass snaps, sounds and haptics still fire) — a user who turned animation
    /// off did not ask to stop banking their XP.
    /// </summary>
    public partial class MainWindow
    {
        // ---- geometry ---------------------------------------------------------
        // The faucet art box is 40x42 (DiscordTabView.xaml); the spout mouth
        // centre sits at (27, 22) inside it. Everything else is derived from the
        // jar ratios in ArmVat so the tap cannot drift off the lip if the jar is
        // ever retuned.

        private const double FaucetSpoutXInBox = 27;
        private const double FaucetBoxHeight = 42;

        // ---- state ------------------------------------------------------------

        private readonly VatFaucetHold _faucetHold = new();
        private bool _faucetWired;
        private bool _faucetWobbling;
        private bool _faucetSparkling;
        private DispatcherTimer? _faucetTickTimer;
        private DispatcherTimer? _faucetSettleTimer;

        // ============================== arm / disarm ===========================

        /// <summary>
        /// Perch the faucet on the vat's top-left lip and hand the canvas the spout
        /// x so the pour stream falls from OUR tap instead of the built-in art.
        /// Called from ArmVat with the jar box it just sized.
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
                double left = Math.Round(x0 - 12);  // flange straddles the corner
                double top = Math.Round(yT - (FaucetBoxHeight - 6));  // base sits ON the lip

                faucet.Margin = new Thickness(left, top, 0, 0);
                faucet.Visibility = Visibility.Visible;

                glass.ExternalSpoutXFraction = (left + FaucetSpoutXInBox) / jarW;

                UpdateFaucetPresentation();
            }
            catch (Exception ex) { App.Logger?.Debug("ArmFaucet: {E}", ex.Message); }
        }

        /// <summary>Put everything away — vat disarmed (dial narrowed, logout).</summary>
        private void DisarmFaucet()
        {
            try
            {
                _faucetHold.Reset();
                StopFaucetWobble();
                StopFaucetSettleWatch();

                var faucet = DiscordTab?.ProfileVatFaucet;
                if (faucet != null) faucet.Visibility = Visibility.Collapsed;

                var glass = DiscordTab?.ProfileVatGlass;
                if (glass != null) glass.ExternalSpoutXFraction = null;
            }
            catch (Exception ex) { App.Logger?.Debug("DisarmFaucet: {E}", ex.Message); }
        }

        /// <summary>Tab navigated away: park every clock. The held state itself
        /// survives only until re-entry, which clears it (the escape valve).</summary>
        private void OnFaucetVatOffScreen()
        {
            try
            {
                StopFaucetWobble();
                StopFaucetSettleWatch();
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
        }

        // ============================== presentation ===========================

        /// <summary>
        /// Fold the hold's current state into the visual: wobble + sparkle while XP
        /// waits (motion allowing), the still badge dot under reduced motion, and
        /// the tooltip that says how much is waiting. Idempotent — called after
        /// every reading, click and visibility change.
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

                faucet.ToolTip = held > 0
                    ? Loc.GetF("profile_faucet_tip_held", held)
                    : Loc.Get("profile_faucet_tip_empty");

                if (held > 0 && !pouring && ambient && _vatOnScreen) StartFaucetWobble();
                else StopFaucetWobble();

                var badge = DiscordTab?.ProfileVatFaucetBadge;
                if (badge != null)
                    badge.Visibility = held > 0 && !ambient
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("UpdateFaucetPresentation: {E}", ex.Message); }
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
            tilt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rock);

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
                tilt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
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
            fall.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, descend);
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
                fall.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                fall.Y = 0;
            }
        }

        // ================================ hover ================================

        private void OnFaucetMouseEnter(object sender, MouseEventArgs e)
        {
            AnimateFaucetScale(1.16);
        }

        private void OnFaucetMouseLeave(object sender, MouseEventArgs e)
        {
            AnimateFaucetScale(1.0);
        }

        private void AnimateFaucetScale(double to)
        {
            try
            {
                var scale = DiscordTab?.ProfileVatFaucetScale;
                if (scale == null) return;

                if (!MotionFx.AllowTransitions)
                {
                    scale.ScaleX = to;
                    scale.ScaleY = to;
                    return;
                }

                var grow = new DoubleAnimation(to, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
            }
            catch (Exception ex) { App.Logger?.Debug("AnimateFaucetScale: {E}", ex.Message); }
        }

        // ================================ the pour =============================

        private void OnFaucetMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                var glass = DiscordTab?.ProfileVatGlass;
                if (glass == null || !_vatArmed) return;
                if (_faucetHold.HeldXp <= 0) return;   // nothing waiting — the tooltip already says so

                int poured = _faucetHold.HeldXp;
                var step = _faucetHold.PourAll();

                // The theater: wobble off, tap tilts over the jar, the canvas runs
                // the stream from our spout. Under reduced motion PourTo snaps the
                // level and IsPouring never turns on — the settle watch below then
                // rights the tap on its first look.
                StopFaucetWobble();
                AnimateFaucetTilt(14);
                glass.PourTo(step.Fill);

                PlayFaucetSfx("faucet_pour.wav", 0.35f);
                FireFaucetPourHaptic();
                StartFaucetSettleWatch();
                UpdateFaucetPresentation();

                App.Logger?.Information("[Descent] faucet poured +{Xp} held XP -> {Pct:F0}%", poured, step.Fill * 100);
            }
            catch (Exception ex) { App.Logger?.Debug("OnFaucetMouseDown: {E}", ex.Message); }
        }

        private void AnimateFaucetTilt(double angle)
        {
            try
            {
                var tilt = DiscordTab?.ProfileVatFaucetTilt;
                if (tilt == null) return;

                if (!MotionFx.AllowTransitions)
                {
                    tilt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                    tilt.Angle = 0;     // reduced motion: the tap never leans
                    return;
                }

                var lean = new DoubleAnimation(angle, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                tilt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, lean);
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

        // ============================== feedback ===============================

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
