// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileFaucet.cs (1146 lines).
//
// THE FAUCET IS THE TAP ON THE VAT. It holds the XP the Descent earned (Core VatFaucetHold, the one
// copy of that number) and pours it into the glass jar when the user presses and HOLDS it: a charge
// with six rungs, a ring that fills, a thud and a lean on release, a shiver and a drip on a short
// press. Every number (budget, rungs, amplitudes, durations, volumes) is WPF's.
//
// THIS HEAD'S FX RULES: the transforms are built in code (a Transform cannot be x:Named in axaml);
// one-shot moves go through Helpers/TransformTween (never Animation.RunAsync on a Transform); the
// three ambient loops (wobble, spout sparkle, chip breath) are Helpers/BeatLoop on the 30 fps frame
// clock, gated by AmbientFxCanvas.Env.AllowAmbientLoops and parked whenever the card leaves the
// screen; opacity tweens stay inside 0..1.
//
// Callers: ArmVat / DisarmVat / ApplyDescentToVat / OnProfileVatVisibilityChanged in
// MainShellWindow.ProfileVat.cs.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ---- geometry (the faucet's own box; see the XAML) ---------------------------
        private const double FaucetSpoutXInBox = 27;
        private const double FaucetSpoutYInBox = 22;
        private const double FaucetBoxHeight = 42;
        private const double FaucetRingBox = 34;
        private const double FaucetRingRadius = 15;

        // ---- the charge ----------------------------------------------------------------
        private const double ChargeFloorMs = 700;
        private const double ChargeCapMs = 1400;
        private const double ChargeXpPerMs = 4.0;
        private const int ChargeRungs = 6;
        private const int ChargeCompactAfter = 3;

        private readonly VatFaucetHold _faucetHold = new(new AppSettingsVatPourLedger());
        private bool _faucetWired;
        private DispatcherTimer? _faucetTickTimer;
        private DispatcherTimer? _faucetSettleTimer;
        private DispatcherTimer? _faucetChargeTimer;
        private DateTime _faucetChargeStart;
        private double _faucetChargeBudgetMs;
        private int _faucetChargeRungsPlayed;
        private bool _faucetCharging;
        private bool _faucetChargeCompact;
        private bool _faucetKeyHeld;
        private int _faucetPoursToday;
        private string _faucetPourDayUtc = string.Empty;
        private static readonly Random FaucetRng = new();

        // The transforms WPF named in XAML, attached from code on first arm.
        private readonly TranslateTransform _faucetShake = new();
        private readonly ScaleTransform _faucetScale = new(1, 1);
        private readonly RotateTransform _faucetTilt = new();
        private readonly ScaleTransform _faucetHandleScale = new(1, 1);
        private readonly TranslateTransform _faucetDropFall = new();
        private readonly ScaleTransform _faucetChipScale = new(1, 1);
        private BeatLoop? _faucetWobble;
        private BeatLoop? _faucetSparkle;
        private BeatLoop? _faucetChipBreath;
        private readonly Dictionary<AvaloniaObject, DispatcherTimer> _faucetTweens = new();

        private T? Vat<T>(string name) where T : Control => ProfilePage?.FindControl<T>(name);
        private Grid? FaucetHost => Vat<Grid>("ProfileVatFaucet");

        /// <summary>For tests: the XP the tap is holding, and whether a charge is running.</summary>
        internal int FaucetHeldXp => _faucetHold.HeldXp;
        internal bool FaucetCharging => _faucetCharging;

        /// <summary>One tween per target: a new move replaces the one still running (WPF BeginAnimation).</summary>
        private void FaucetTween(AvaloniaObject target, double ms, Easing? easing,
            params (double cue, AvaloniaProperty prop, double value)[] keys)
        {
            if (_faucetTweens.Remove(target, out var running)) running.Stop();
            _faucetTweens[target] = TransformTween.Run(target, TimeSpan.FromMilliseconds(ms), keys, easing);
        }

        private void FaucetStopTween(AvaloniaObject target)
        {
            if (_faucetTweens.Remove(target, out var running)) running.Stop();
        }

        private void ArmFaucet(VatGlassCanvas glass, double jarW, double jarH)
        {
            try
            {
                var faucet = FaucetHost;
                if (faucet == null) return;
                WireFaucet(faucet);

                double x0 = jarW * 0.10;            // the jar's left wall (VatGlassCanvas.JarX0)
                double yT = jarH * 0.175;           // the lip line (VatGlassCanvas.JarYTop)
                double left = Math.Round(x0 - 8);   // flange straddles the corner
                double top = Math.Round(yT - (FaucetBoxHeight - 6));  // base sits ON the lip
                faucet.Margin = new Thickness(left, top, 0, 0);
                faucet.IsVisible = true;

                if (Vat<Grid>("ProfileVatChargeRing") is { } ring)
                    ring.Margin = new Thickness(left + 15 - FaucetRingBox / 2, top + 18 - FaucetRingBox / 2, 0, 0);
                if (Vat<Canvas>("ProfileVatSparks") is { } sparks)
                {
                    sparks.Width = jarW;
                    sparks.Height = jarH;
                }
                if (Vat<Border>("ProfileVatChip") is { } chip) chip.Margin = new Thickness(0, Math.Round(yT) + 8, 0, 0);

                glass.ExternalSpoutXFraction = (left + FaucetSpoutXInBox) / jarW;
                PositionVatTickGlyphs();
                UpdateFaucetPresentation();
            }
            catch (Exception ex) { Log.Debug("ArmFaucet: {E}", ex.Message); }
        }

        /// <summary>The three reference glyphs beside the jar (drain, cap, max), from the glass's own marks.</summary>
        private void PositionVatTickGlyphs()
        {
            try
            {
                var glass = VatGlass;
                if (glass == null) return;
                double innerX = glass.TickInnerX;
                Place(Vat<Path>("ProfileVatTickDrain"), VatGlassCanvas.VatTickMark.Drain);
                Place(Vat<Path>("ProfileVatTickCap"), VatGlassCanvas.VatTickMark.Cap);
                Place(Vat<Path>("ProfileVatTickMax"), VatGlassCanvas.VatTickMark.Max);

                void Place(Path? glyph, VatGlassCanvas.VatTickMark mark)
                {
                    if (glyph == null) return;
                    double? y = glass.TickCenterY(mark);
                    if (y == null || innerX <= 0) { glyph.IsVisible = false; return; }
                    glyph.Margin = new Thickness(Math.Round(innerX - 3 - glyph.Width), Math.Round(y.Value - glyph.Height / 2), 0, 0);
                    glyph.IsVisible = true;
                }
            }
            catch (Exception ex) { Log.Debug("PositionVatTickGlyphs: {E}", ex.Message); }
        }

        private void DisarmFaucet()
        {
            try
            {
                CancelFaucetCharge(silent: true);
                _faucetHold.Reset();
                StopFaucetWobble();
                StopFaucetSettleWatch();
                StopChipBreath();

                if (FaucetHost is { } faucet) faucet.IsVisible = false;
                Hide(Vat<Border>("ProfileVatChip"));
                Hide(Vat<Grid>("ProfileVatChargeRing"));
                Hide(Vat<Path>("ProfileVatTickDrain"));
                Hide(Vat<Path>("ProfileVatTickCap"));
                Hide(Vat<Path>("ProfileVatTickMax"));
                Vat<Canvas>("ProfileVatSparks")?.Children.Clear();
                if (VatGlass is { } glass) glass.ExternalSpoutXFraction = null;

                static void Hide(Control? e) { if (e != null) e.IsVisible = false; }
            }
            catch (Exception ex) { Log.Debug("DisarmFaucet: {E}", ex.Message); }
        }

        /// <summary>The card left the screen: no charge survives it and no loop runs behind it.</summary>
        private void OnFaucetVatOffScreen()
        {
            try
            {
                CancelFaucetCharge(silent: true);
                StopFaucetWobble();
                StopFaucetSettleWatch();
                StopChipBreath();
            }
            catch (Exception ex) { Log.Debug("OnFaucetVatOffScreen: {E}", ex.Message); }
        }

        private void WireFaucet(Grid faucet)
        {
            if (_faucetWired) return;
            _faucetWired = true;

            faucet.RenderTransform = new TransformGroup { Children = { _faucetShake, _faucetScale, _faucetTilt } };
            if (Vat<Grid>("ProfileVatHandle") is { } handle) handle.RenderTransform = _faucetHandleScale;
            if (Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.RenderTransform = _faucetDropFall;
            if (Vat<Border>("ProfileVatChip") is { } chip) chip.RenderTransform = _faucetChipScale;

            faucet.PointerEntered += (_, _) => { if (!_faucetCharging) AnimateFaucetScale(1.16); };
            faucet.PointerExited += (_, _) =>
            {
                if (_faucetCharging) { CancelFaucetCharge(silent: false); return; }
                AnimateFaucetScale(1.0);
            };
            faucet.PointerPressed += OnFaucetPointerPressed;
            faucet.PointerReleased += OnFaucetPointerReleased;
            faucet.PointerCaptureLost += (_, _) => { if (_faucetCharging) CancelFaucetCharge(silent: true); };
            faucet.KeyDown += OnFaucetKeyDown;
            faucet.KeyUp += OnFaucetKeyUp;
            faucet.LostFocus += (_, _) => { _faucetKeyHeld = false; if (_faucetCharging) CancelFaucetCharge(silent: true); };

            // A motion-level change re-reads the gate for the loops that are running or owed.
            AmbientFxCanvas.Env.MotionGateChanged += UpdateFaucetPresentation;
            Closed += (_, _) => AmbientFxCanvas.Env.MotionGateChanged -= UpdateFaucetPresentation;
        }

        /// <summary>
        /// The one place the tap's look is decided: tooltip, wobble, the "XP waiting" chip and the
        /// reduced-motion badge. Idempotent; called after every fold, pour, cancel and gate change.
        /// </summary>
        private void UpdateFaucetPresentation()
        {
            try
            {
                var faucet = FaucetHost;
                if (faucet == null || !faucet.IsVisible) return;

                int held = _faucetHold.HeldXp;
                bool pouring = VatGlass?.IsPouring == true;
                bool ambient = AmbientFxCanvas.Env.AllowAmbientLoops;
                bool waiting = held > 0 && !pouring && !_faucetCharging;

                ToolTip.SetTip(faucet, BuildFaucetTooltip(held));

                if (waiting && ambient && _vatOnScreen) StartFaucetWobble();
                else StopFaucetWobble();

                if (Vat<Border>("ProfileVatChip") is { } chip)
                {
                    if (waiting)
                    {
                        if (Vat<TextBlock>("ProfileVatChipText") is { } chipText) chipText.Text = Loc.GetF("profile_vat_chip", held);
                        chip.IsVisible = true;
                        if (ambient && _vatOnScreen) StartChipBreath(); else StopChipBreath();
                    }
                    else
                    {
                        chip.IsVisible = false;
                        StopChipBreath();
                    }
                }

                if (Vat<Ellipse>("ProfileVatFaucetBadge") is { } badge) badge.IsVisible = held > 0 && !ambient;
            }
            catch (Exception ex) { Log.Debug("UpdateFaucetPresentation: {E}", ex.Message); }
        }

        private static Control BuildFaucetTooltip(int held)
        {
            var stack = new StackPanel { MaxWidth = 320 };
            stack.Children.Add(new TextBlock
            {
                Text = held > 0 ? Loc.GetF("profile_faucet_tip_held", held) : Loc.Get("profile_faucet_tip_empty"),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.SemiBold,
            });
            stack.Children.Add(new TextBlock
            {
                Text = Loc.Get("profile_vat_help_tip"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.72,
                Margin = new Thickness(0, 5, 0, 0),
            });
            return stack;
        }

        // ---- ambient loops ---------------------------------------------------------------

        private void StartFaucetWobble()
        {
            var faucet = FaucetHost;
            if (faucet == null || _faucetWobble?.IsRunning == true) return;
            // WPF: -2.2..2.2 degrees, 0.55 s each way, sine in and out.
            _faucetWobble ??= new BeatLoop(faucet, t => _faucetTilt.Angle = -2.2 + 4.4 * BeatLoop.Breath(t, 0.55));
            FaucetStopTween(_faucetTilt);
            _faucetWobble.Start();

            if (AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier)) StartFaucetSparkle();

            _faucetTickTimer ??= CreateFaucetTickTimer();
            _faucetTickTimer.Start();
        }

        private void StopFaucetWobble()
        {
            _faucetTickTimer?.Stop();
            StopFaucetSparkle();
            if (_faucetWobble?.IsRunning != true) return;
            _faucetWobble.Stop();
            _faucetTilt.Angle = 0;
        }

        /// <summary>The quiet tick while XP waits: every 3.8 s, only while the window presents.</summary>
        private DispatcherTimer CreateFaucetTickTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3.8) };
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (_faucetWobble?.IsRunning != true || !VatGlassCanvas.WindowIsPresenting(this)) return;
                    PlayFaucetSfx("faucet_tick.wav", 0.15f);
                }
                catch (Exception ex) { Log.Debug("Faucet tick: {E}", ex.Message); }
            };
            return timer;
        }

        private void StartFaucetSparkle()
        {
            if (_faucetSparkle?.IsRunning == true) return;
            if (Vat<Ellipse>("ProfileVatFaucetDrop") is not { } drop) return;
            // WPF: a 1.8 s cycle; opacity 0 -> 0.85 (25%) -> 0.55 (70%) -> 0, falling 7 px.
            _faucetSparkle ??= new BeatLoop(drop, t =>
            {
                double p = t % 1.8 / 1.8;
                double o = p < 0.25 ? 0.85 * p / 0.25
                    : p < 0.7 ? 0.85 - 0.30 * (p - 0.25) / 0.45
                    : 0.55 * (1 - (p - 0.7) / 0.3);
                drop.Opacity = Math.Clamp(o, 0, 1);
                _faucetDropFall.Y = 7 * p;
            });
            FaucetStopTween(drop);
            FaucetStopTween(_faucetDropFall);
            _faucetSparkle.Start();
        }

        private void StopFaucetSparkle()
        {
            if (_faucetSparkle?.IsRunning != true) return;
            _faucetSparkle.Stop();
            if (Vat<Ellipse>("ProfileVatFaucetDrop") is { } drop) drop.Opacity = 0;
            _faucetDropFall.Y = 0;
        }

        private void StartChipBreath()
        {
            if (_faucetChipBreath?.IsRunning == true) return;
            if (Vat<Border>("ProfileVatChip") is not { } chip) return;
            // WPF: 1.0 -> 1.05 over 2.8 s, there and back.
            _faucetChipBreath ??= new BeatLoop(chip, t =>
                _faucetChipScale.ScaleX = _faucetChipScale.ScaleY = 1 + 0.05 * BeatLoop.Breath(t, 2.8));
            _faucetChipBreath.Start();
        }

        private void StopChipBreath()
        {
            if (_faucetChipBreath?.IsRunning != true) return;
            _faucetChipBreath.Stop();
            _faucetChipScale.ScaleX = _faucetChipScale.ScaleY = 1;
        }

        // ---- hover, press, keys ----------------------------------------------------------

        private void AnimateFaucetScale(double to, double ms = 150)
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions)
                {
                    FaucetStopTween(_faucetScale);
                    _faucetScale.ScaleX = _faucetScale.ScaleY = to;
                    return;
                }
                double from = _faucetScale.ScaleX;
                FaucetTween(_faucetScale, ms, new QuadraticEaseOut(),
                    (0, ScaleTransform.ScaleXProperty, from), (1, ScaleTransform.ScaleXProperty, to),
                    (0, ScaleTransform.ScaleYProperty, from), (1, ScaleTransform.ScaleYProperty, to));
            }
            catch (Exception ex) { Log.Debug("AnimateFaucetScale: {E}", ex.Message); }
        }

        private void OnFaucetPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                if (sender is not Control faucet || !e.GetCurrentPoint(faucet).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                faucet.Focus();                        // so the keyboard can finish what the mouse started
                e.Pointer.Capture(faucet);
                BeginFaucetCharge();
            }
            catch (Exception ex) { Log.Debug("OnFaucetPointerPressed: {E}", ex.Message); }
        }

        private void OnFaucetPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                e.Handled = true;
                // A release before the ring closes is a short press: the tap shivers and drips.
                if (_faucetCharging) CancelFaucetCharge(silent: false);
                e.Pointer.Capture(null);
            }
            catch (Exception ex) { Log.Debug("OnFaucetPointerReleased: {E}", ex.Message); }
        }

        private void OnFaucetKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space && e.Key != Key.Enter) return;
            e.Handled = true;
            if (_faucetKeyHeld) return;               // auto-repeat is one long press, not many
            _faucetKeyHeld = true;
            BeginFaucetCharge();
        }

        private void OnFaucetKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space && e.Key != Key.Enter) return;
            e.Handled = true;
            _faucetKeyHeld = false;
            if (_faucetCharging) CancelFaucetCharge(silent: false);
        }

        // ---- the charge ------------------------------------------------------------------

        /// <summary>WPF BeginFaucetCharge: the hold is as long as the XP is worth (700 ms floor, 1400 cap,
        /// 4 XP a millisecond), 40% shorter from the fourth pour of the day.</summary>
        internal void BeginFaucetCharge()
        {
            try
            {
                if (_faucetCharging) return;
                var glass = VatGlass;
                if (glass == null || !_vatArmed) return;
                if (glass.IsPouring) return;             // a stream is already running
                int held = _faucetHold.HeldXp;
                if (held <= 0) return;                   // nothing waiting: the tooltip already says so

                _faucetCharging = true;
                _faucetChargeRungsPlayed = 0;
                _faucetChargeCompact = FaucetPourCountToday() >= ChargeCompactAfter;

                double budget = Math.Min(ChargeCapMs, ChargeFloorMs + held / ChargeXpPerMs);
                if (_faucetChargeCompact) budget *= 0.6;
                _faucetChargeBudgetMs = Math.Max(220, budget);
                _faucetChargeStart = DateTime.UtcNow;

                StopFaucetWobble();
                StopChipBreath();
                AnimateFaucetScale(0.94, 90);            // the bounce, on the way down
                ShowChargeRing();
                PlayChargeRung(_faucetChargeRungsPlayed++);   // the low tone, on the press frame

                _faucetChargeTimer ??= CreateFaucetChargeTimer();
                _faucetChargeTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Debug("BeginFaucetCharge: {E}", ex.Message);
                CancelFaucetCharge(silent: true);
            }
        }

        private DispatcherTimer CreateFaucetChargeTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (!_faucetCharging) { _faucetChargeTimer?.Stop(); return; }
                    AdvanceFaucetCharge((DateTime.UtcNow - _faucetChargeStart).TotalMilliseconds / _faucetChargeBudgetMs);
                }
                catch (Exception ex)
                {
                    Log.Debug("Faucet charge: {E}", ex.Message);
                    CancelFaucetCharge(silent: true);
                }
            };
            return timer;
        }

        /// <summary>One step of the charge at progress <paramref name="t"/> (the timer's body; tests drive it).</summary>
        internal void AdvanceFaucetCharge(double t)
        {
            if (!_faucetCharging) return;
            t = Math.Clamp(t, 0, 1);
            UpdateChargeRing(t);
            int want = Math.Min(ChargeRungs, (int)(t * ChargeRungs) + 1);
            while (_faucetChargeRungsPlayed < want) PlayChargeRung(_faucetChargeRungsPlayed++);
            if (t >= 1) CompleteFaucetCharge();
        }

        private void CancelFaucetCharge(bool silent)
        {
            try
            {
                if (!_faucetCharging) { HideChargeRing(); return; }
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
            catch (Exception ex) { Log.Debug("CancelFaucetCharge: {E}", ex.Message); }
        }

        private void CompleteFaucetCharge()
        {
            try
            {
                _faucetCharging = false;
                _faucetChargeTimer?.Stop();
                HideChargeRing();

                var glass = VatGlass;
                if (glass == null || !_vatArmed) { AnimateFaucetScale(1.0); return; }

                int poured = _faucetHold.HeldXp;
                if (poured <= 0) { AnimateFaucetScale(1.0); UpdateFaucetPresentation(); return; }

                bool wasOver = glass.IsPastBrim(glass.Fill);
                var step = _faucetHold.PourAll();
                bool crossesLip = glass.IsPastBrim(step.Fill) && !wasOver;

                AnimateFaucetScale(1.0, 120);
                FaucetThud();
                PlayFaucetSfx("faucet_pour.wav", 0.35f);   // the same frame as the thud
                AnimateFaucetTilt(14);
                glass.PourTo(step.Fill, userGesture: true);
                FireFaucetPourHaptic();

                if (crossesLip)
                {
                    glass.PulseOverflow();
                    PlayFaucetSfx("faucet_brim.wav", 0.30f);
                }
                if (!_faucetChargeCompact) SpawnFaucetSparkles();

                NoteFaucetPourToday();
                StartFaucetSettleWatch();
                UpdateFaucetPresentation();

                Log.Information("[Descent] faucet poured +{Xp} held XP -> {Pct:F0}% (hold {Ms:F0}ms{Cut})",
                    poured, step.Fill * 100, _faucetChargeBudgetMs, _faucetChargeCompact ? ", compact" : string.Empty);
            }
            catch (Exception ex) { Log.Debug("CompleteFaucetCharge: {E}", ex.Message); }
        }

        // ---- the ring --------------------------------------------------------------------

        private void ShowChargeRing()
        {
            try
            {
                var ring = Vat<Grid>("ProfileVatChargeRing");
                if (ring == null) return;
                bool sweep = AmbientFxCanvas.Env.Level == MotionLevel.Full;
                if (Vat<Path>("ProfileVatChargeArc") is { } arc) arc.Data = sweep ? null : BuildChargeArc(1.0);
                ring.IsVisible = true;
                if (AmbientFxCanvas.Env.AllowTransitions)
                    FaucetTween(ring, 120, null, (0, OpacityProperty, 0), (1, OpacityProperty, 1));
                else
                {
                    FaucetStopTween(ring);
                    ring.Opacity = 1;
                }
            }
            catch (Exception ex) { Log.Debug("ShowChargeRing: {E}", ex.Message); }
        }

        private void UpdateChargeRing(double t)
        {
            if (AmbientFxCanvas.Env.Level != MotionLevel.Full) return;      // reduced: the circle is already whole
            if (Vat<Path>("ProfileVatChargeArc") is { } arc) arc.Data = BuildChargeArc(t);
        }

        private void HideChargeRing()
        {
            try
            {
                var ring = Vat<Grid>("ProfileVatChargeRing");
                if (ring == null) return;
                FaucetStopTween(ring);
                ring.Opacity = 0;
                ring.IsVisible = false;
                if (Vat<Path>("ProfileVatChargeArc") is { } arc) arc.Data = null;
            }
            catch (Exception ex) { Log.Debug("HideChargeRing: {E}", ex.Message); }
        }

        /// <summary>The arc from 12 o'clock, clockwise, for progress 0..1 inside the 34 px ring box.</summary>
        internal static Geometry? BuildChargeArc(double t)
        {
            t = Math.Clamp(t, 0, 1);
            double c = FaucetRingBox / 2, r = FaucetRingRadius;
            if (t <= 0.002) return null;
            if (t >= 0.999) return new EllipseGeometry(new Rect(c - r, c - r, r * 2, r * 2));

            double a = t * 2 * Math.PI;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(c, c - r), isFilled: false);
                ctx.ArcTo(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, t > 0.5, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            return geo;
        }

        // ---- one-shot moves --------------------------------------------------------------

        /// <summary>The stamp on the handle as the pour lands: 2.1 -> 0.86 -> 1.0 in 340 ms.</summary>
        private void FaucetThud()
        {
            try
            {
                FaucetStopTween(_faucetHandleScale);
                _faucetHandleScale.ScaleX = _faucetHandleScale.ScaleY = 1;
                if (!AmbientFxCanvas.Env.AllowTransitions) return;
                FaucetTween(_faucetHandleScale, 340, new CubicEaseOut(),
                    (0, ScaleTransform.ScaleXProperty, 2.1), (0.56, ScaleTransform.ScaleXProperty, 0.86), (1, ScaleTransform.ScaleXProperty, 1.0),
                    (0, ScaleTransform.ScaleYProperty, 2.1), (0.56, ScaleTransform.ScaleYProperty, 0.86), (1, ScaleTransform.ScaleYProperty, 1.0));
            }
            catch (Exception ex) { Log.Debug("FaucetThud: {E}", ex.Message); }
        }

        /// <summary>The refusal shiver on a short press: a decaying side-to-side over 420 ms.</summary>
        private void FaucetShiver()
        {
            try
            {
                FaucetStopTween(_faucetShake);
                _faucetShake.X = 0;
                if (!AmbientFxCanvas.Env.AllowTransitions) return;
                double[] amps = { 0, -2.6, 2.2, -1.6, 1.1, -0.6, 0.25, 0 };
                var keys = new (double, AvaloniaProperty, double)[amps.Length];
                for (int i = 0; i < amps.Length; i++)
                    keys[i] = (i / (double)(amps.Length - 1), TranslateTransform.XProperty, amps[i]);
                FaucetTween(_faucetShake, 420, null, keys);
            }
            catch (Exception ex) { Log.Debug("FaucetShiver: {E}", ex.Message); }
        }

        /// <summary>One drop falls from the spout (520 ms) and is gone.</summary>
        private void FaucetDrip()
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions) return;
                if (Vat<Ellipse>("ProfileVatFaucetDrop") is not { } drop) return;
                FaucetTween(drop, 520, null, (0, OpacityProperty, 0), (0.2, OpacityProperty, 0.95), (1, OpacityProperty, 0));
                FaucetTween(_faucetDropFall, 520, new QuadraticEaseIn(),
                    (0, TranslateTransform.YProperty, 0), (0.999, TranslateTransform.YProperty, 10), (1, TranslateTransform.YProperty, 0));
            }
            catch (Exception ex) { Log.Debug("FaucetDrip: {E}", ex.Message); }
        }

        /// <summary>Five to nine motes burst from the spout on a full pour (Full motion only).</summary>
        private void SpawnFaucetSparkles()
        {
            try
            {
                if (AmbientFxCanvas.Env.Level != MotionLevel.Full) return;
                var host = Vat<Canvas>("ProfileVatSparks");
                var faucet = FaucetHost;
                if (host == null || faucet == null) return;

                double ox = faucet.Margin.Left + FaucetSpoutXInBox;
                double oy = faucet.Margin.Top + FaucetSpoutYInBox;
                IBrush brush = this.TryFindResource("PinkBrush", ActualThemeVariant, out var found) && found is IBrush b ? b : Brushes.HotPink;

                int count = FaucetRng.Next(5, 10);
                for (int i = 0; i < count; i++)
                {
                    double angle = (Math.PI * 2 * i / count) + FaucetRng.NextDouble() * 0.5;
                    double dist = 12 + FaucetRng.NextDouble() * 16;
                    double size = 2.2 + FaucetRng.NextDouble() * 2.0;

                    var move = new TranslateTransform();
                    var mote = new Ellipse { Width = size, Height = size, Fill = brush, IsHitTestVisible = false, RenderTransform = move };
                    Canvas.SetLeft(mote, ox - size / 2);
                    Canvas.SetTop(mote, oy - size / 2);
                    host.Children.Add(mote);

                    double ms = 420 + FaucetRng.Next(200);
                    TransformTween.Run(move, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[]
                    {
                        (0, TranslateTransform.XProperty, 0), (1, TranslateTransform.XProperty, Math.Cos(angle) * dist),
                        (0, TranslateTransform.YProperty, 0), (1, TranslateTransform.YProperty, Math.Sin(angle) * dist + 6),
                    }, new QuadraticEaseOut());
                    TransformTween.Run(mote, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[]
                    {
                        (0, OpacityProperty, 1), (1, OpacityProperty, 0),
                    });
                    var captured = mote;
                    DispatcherTimer.RunOnce(() =>
                    {
                        try { host.Children.Remove(captured); }
                        catch (Exception ex) { Log.Debug("Faucet spark reap: {E}", ex.Message); }
                    }, TimeSpan.FromMilliseconds(ms + 40));
                }
            }
            catch (Exception ex) { Log.Debug("SpawnFaucetSparkles: {E}", ex.Message); }
        }

        /// <summary>The tap leans while it pours (14 degrees) and stands back up after; never under reduced motion.</summary>
        private void AnimateFaucetTilt(double angle)
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions)
                {
                    FaucetStopTween(_faucetTilt);
                    _faucetTilt.Angle = 0;
                    return;
                }
                FaucetTween(_faucetTilt, 240, new QuadraticEaseOut(),
                    (0, RotateTransform.AngleProperty, _faucetTilt.Angle), (1, RotateTransform.AngleProperty, angle));
            }
            catch (Exception ex) { Log.Debug("AnimateFaucetTilt: {E}", ex.Message); }
        }

        // ---- after the pour --------------------------------------------------------------

        private void StartFaucetSettleWatch()
        {
            _faucetSettleTimer ??= CreateFaucetSettleTimer();
            _faucetSettleTimer.Start();
        }

        private void StopFaucetSettleWatch() => _faucetSettleTimer?.Stop();

        private DispatcherTimer CreateFaucetSettleTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) =>
            {
                try
                {
                    if (VatGlass?.IsPouring == true) return;
                    _faucetSettleTimer?.Stop();
                    AnimateFaucetTilt(0);
                    UpdateFaucetPresentation();   // the wobble resumes only if new XP accrued
                }
                catch (Exception ex) { Log.Debug("Faucet settle: {E}", ex.Message); }
            };
            return timer;
        }

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

        // ---- sound and touch -------------------------------------------------------------

        private static void PlayChargeRung(int index)
        {
            int rung = Math.Clamp(index, 0, ChargeRungs - 1) + 1;
            PlayFaucetSfx($"faucet_charge_{rung}.wav", 0.15f);
        }

        /// <summary>Recorded clips only, scaled by the master volume; silent at master 0.</summary>
        private static void PlayFaucetSfx(string file, float scale)
        {
            try
            {
                float master = (float)CoreSettings.Current.MasterVolume / 100f;
                float volume = Math.Clamp(master * scale, 0f, 1f);
                if (volume <= 0f) return;
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", file);
                CoreAudio.PlayOneShot(path, volume, "vat-faucet");
            }
            catch (Exception ex) { Log.Debug("PlayFaucetSfx: {E}", ex.Message); }
        }

        /// <summary>A soft pulse on the pour: the level-up rule at 35%, and only if that rule is on.</summary>
        private static void FireFaucetPourHaptic()
        {
            try
            {
                var haptics = CoreHaptics.Service;
                if (haptics == null) return;
                var rule = CoreSettings.Current.Haptics?.V2?.Rule(HapticEventKind.LevelUp);
                if (rule == null || !rule.Enabled || rule.Intensity <= 0) return;
                _ = haptics.PostEvent(HapticEventKind.LevelUp, Math.Clamp(rule.Intensity * 0.35, 0, 1));
            }
            catch (Exception ex) { Log.Debug("FireFaucetPourHaptic: {E}", ex.Message); }
        }
    }
}
