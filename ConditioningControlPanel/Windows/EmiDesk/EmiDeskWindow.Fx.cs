using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Serilog;

// NAMESPACE TRAP, and it is not a style preference. Every window under Windows/ lives in the
// FLAT ConditioningControlPanel namespace - not one of them declares ConditioningControlPanel.Windows.
// Declaring it here compiles for a moment and then breaks ScreenOcrService, whose
// `Windows.Graphics.Imaging.BitmapDecoder` is resolved relative to the enclosing
// ConditioningControlPanel namespace: the instant a ConditioningControlPanel.Windows exists, it
// shadows the WinRT `Windows` root and the OCR service stops finding its decoder. Keep it flat.
namespace ConditioningControlPanel;

/// <summary>
/// The summon and the dismiss: the pixel smoke bomb, the sparkle scatter and the CRT power-on.
/// This is chunk B1's own partial. It owns nothing chunk B2 or B3 needs to change; the only seam
/// it fires is <c>OnTearDownCore()</c>, so a dismiss takes the ring and the glass down with it.
///
/// Timings come from <c>docs/emi-desk/reference/pitch-demo.js</c> and the BRIEF: about a second
/// each way. Long enough to read as an arrival, short enough that nobody learns to dread it.
/// </summary>
public partial class EmiDeskWindow
{
    private const int SmokeLeadMs = 380;      // smoke starts, she appears this long after
    private const int CrtOnMs = 220;          // the power-on stutter
    private const int CrtOffMs = 230;         // the power-off collapse
    private const int FxLifeMs = 900;         // a burst's container is swept this long after it starts

    private readonly List<UIElement> _fxLayers = new();

    // ------------------------------------------------------------------ summon

    /// <summary>
    /// Bring her in: smoke bomb, CRT power-on, then the <c>wake</c> chain. Input is locked for the
    /// whole transition so a click cannot land mid-CRT and open a ring onto a 2 percent tall EMI.
    /// </summary>
    /// <summary>
    /// End the summon exactly once, however it ended: the wake chain ran out, or a pat cut it.
    /// Idempotent, because those two can arrive in either order and sometimes both.
    /// </summary>
    internal void FinishSummon()
    {
        if (!_summonChainLive) return;
        _summonChainLive = false;
        var done = _summonDone;
        _summonDone = null;
        try { RestartIdleBeats(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] idle beats failed to restart after the summon"); }
        try { done?.Invoke(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] summon continuation threw"); }
    }

    private Action? _summonDone;

    public void RunSummon(Action? done = null)
    {
        try
        {
            if (_closingForGood) return;
            _transiting = true;
            InputLocked = true;

            CancelChain();
            StopIdleBeats();
            OnBubbleTextCore(null);
            TearDownReactions();

            // The window goes up first (the smoke has to be somewhere), but she does not.
            BodyRoot.Visibility = Visibility.Hidden;
            CrtScale.ScaleX = 0.02;
            CrtScale.ScaleY = 0.02;
            Visibility = Visibility.Visible;
            Opacity = 1;

            Burst(FxKind.Smoke);

            After(SmokeLeadMs, () =>
            {
                if (_closingForGood) return;
                SetPose("idle");
                DrawFace("-_-");
                BodyRoot.Visibility = Visibility.Visible;
                CrtOn();

                After(CrtOnMs + 20, () =>
                {
                    if (_closingForGood) return;
                    _transiting = false;
                    InputLocked = false;

                    // Her entrance is CUTTABLE from here on. A pat that lands during it ends it
                    // through FinishSummon so the idle beats still start and the caller's
                    // continuation still runs: the chain is interruptible, the bookkeeping is not.
                    _summonDone = done;
                    _summonChainLive = true;
                    PlayChain("wake", FinishSummon);
                });
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] summon FX failed, showing her plain");
            try
            {
                _transiting = false;
                InputLocked = false;
                BodyRoot.Visibility = Visibility.Visible;
                CrtScale.ScaleX = 1;
                CrtScale.ScaleY = 1;
                Visibility = Visibility.Visible;
                RestartIdleBeats();
                done?.Invoke();
            }
            catch { /* nothing left to try */ }
        }
    }

    // ------------------------------------------------------------------ dismiss

    /// <summary>
    /// Send her away: a wink, the CRT collapse, a sparkle scatter, then hide. She always gets the
    /// wink first, so leaving never reads as a crash.
    /// </summary>
    public void RunDismiss(Action? done = null)
    {
        try
        {
            if (Visibility != Visibility.Visible)
            {
                done?.Invoke();
                return;
            }

            _transiting = true;
            InputLocked = true;
            StopIdleBeats();
            DisarmPet();
            CancelChain();
            TearDownReactions();

            try { OnTearDownCore(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tear-down seam threw"); }

            PlayChain("wink", () =>
            {
                if (_closingForGood) { FinishDismiss(done); return; }
                CrtOff();
                After(CrtOffMs, () =>
                {
                    BodyRoot.Visibility = Visibility.Hidden;
                    Burst(FxKind.Spark);
                    After(FxLifeMs, () => FinishDismiss(done));
                });
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] dismiss FX failed, hiding her plain");
            FinishDismiss(done);
        }
    }

    private void FinishDismiss(Action? done)
    {
        try
        {
            SweepFx(all: true);
            OnBubbleTextCore(null);
            Hide();
            Visibility = Visibility.Hidden;
            BodyRoot.Visibility = Visibility.Visible;
            CrtScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CrtScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            CrtScale.ScaleX = 1;
            CrtScale.ScaleY = 1;
            _transiting = false;
            InputLocked = false;
            SetPose("idle");
            DrawFace(Services.EmiDesk.EmiChains.RestFace);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dismiss finish failed");
        }
        finally
        {
            try { done?.Invoke(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] dismiss callback threw"); }
        }
    }

    // ------------------------------------------------------------------ CRT

    /// <summary>
    /// The power-on: a dot, a horizontal line, then the picture. Four DISCRETE steps, no easing,
    /// because a smooth interpolation reads as a modern zoom and this is meant to be a CRT.
    /// </summary>
    private void CrtOn()
    {
        var sx = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(CrtOnMs) };
        sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.02, KeyTime.FromPercent(0.0)));
        sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.00, KeyTime.FromPercent(0.30)));
        sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.00, KeyTime.FromPercent(0.65)));
        sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.00, KeyTime.FromPercent(1.0)));

        var sy = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(CrtOnMs) };
        sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.02, KeyTime.FromPercent(0.0)));
        sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.03, KeyTime.FromPercent(0.30)));
        sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.06, KeyTime.FromPercent(0.65)));
        sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.00, KeyTime.FromPercent(1.0)));

        BeginCrt(sx, sy);
    }

    /// <summary>The power-off: the picture collapses to a line, then to a dot.</summary>
    private void CrtOff()
    {
        var sx = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(CrtOffMs) };
        sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.00, KeyTime.FromPercent(0.0)));
        sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.00, KeyTime.FromPercent(0.45)));
        sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.35, KeyTime.FromPercent(0.80)));
        sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.02, KeyTime.FromPercent(1.0)));

        var sy = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(CrtOffMs) };
        sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.00, KeyTime.FromPercent(0.0)));
        sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.06, KeyTime.FromPercent(0.45)));
        sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.03, KeyTime.FromPercent(0.80)));
        sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.02, KeyTime.FromPercent(1.0)));

        BeginCrt(sx, sy);
    }

    private void BeginCrt(AnimationTimeline sx, AnimationTimeline sy)
    {
        try
        {
            sx.FillBehavior = FillBehavior.HoldEnd;
            sy.FillBehavior = FillBehavior.HoldEnd;
            CrtScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            CrtScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] CRT animation failed");
        }
    }

    // ------------------------------------------------------------------ particles

    private enum FxKind { Smoke, Spark }

    /// <summary>
    /// One pixel burst at her centre. Smoke on the way in (dark chips, a wider throw), sparks on the
    /// way out (pink chips, tighter and faster). Ported from <c>pitch-demo.js smoke()</c>: the
    /// count, the radius, the upward bias and the 380 + rand(320) ms lifetimes are its numbers.
    /// </summary>
    private void Burst(FxKind kind)
    {
        try
        {
            var host = OverlayCanvas;
            if (host == null) return;

            double cx = (double.IsNaN(Width) ? ActualWidth : Width) / 2.0;
            double cy = (double.IsNaN(Height) ? ActualHeight : Height) / 2.0;
            if (cx <= 0 || cy <= 0)
            {
                cx = OverlayPadX + _bodyWidth / 2.0;
                cy = OverlayPad + _bodyWidth * BodyAspect / 2.0;
            }

            bool spark = kind == FxKind.Spark;
            int n = spark ? 14 : 22;

            var layer = new Canvas { IsHitTestVisible = false };
            host.Children.Add(layer);
            _fxLayers.Add(layer);

            var pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            var ink = new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x46));
            var cream = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1));
            pink.Freeze();
            ink.Freeze();
            cream.Freeze();

            for (int i = 0; i < n; i++)
            {
                double ang = Rng.NextDouble() * Math.PI * 2;
                double r = (spark ? 30 : 22) + Rng.NextDouble() * 40;
                double dx = Math.Cos(ang) * r;
                double dy = Math.Sin(ang) * r - (spark ? 10 : 20);
                int life = 380 + Rng.Next(320);
                double size = spark ? 3 + Rng.Next(2) : 4 + Rng.Next(3);

                var chip = new Rectangle
                {
                    Width = size,
                    Height = size,
                    Fill = spark
                        ? (Rng.NextDouble() < 0.35 ? cream : pink)
                        : (Rng.NextDouble() < 0.30 ? pink : ink),
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true
                };
                var tt = new TranslateTransform();
                chip.RenderTransform = tt;
                Canvas.SetLeft(chip, cx - size / 2.0);
                Canvas.SetTop(chip, cy - size / 2.0);
                layer.Children.Add(chip);

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = TimeSpan.FromMilliseconds(life);
                tt.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(0, dx, dur) { EasingFunction = ease });
                tt.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, dy, dur) { EasingFunction = ease });
                chip.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, 0, dur) { EasingFunction = ease });
            }

            // One sweep timer for every burst: a per-chip Completed handler would be 22 closures
            // fighting the same collection.
            if (_fxSweepTimer == null)
            {
                _fxSweepTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(FxLifeMs)
                };
                _fxSweepTimer.Tick += (_, _) =>
                {
                    try
                    {
                        _fxSweepTimer?.Stop();
                        SweepFx(all: true);
                    }
                    catch (Exception ex) { Log.Debug(ex, "[EmiDesk] FX sweep failed"); }
                };
            }
            _fxSweepTimer.Stop();
            _fxSweepTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] particle burst failed");
        }
    }

    /// <summary>Drop spent FX layers. <paramref name="all"/> false is reserved for a partial sweep.</summary>
    private void SweepFx(bool all)
    {
        try
        {
            if (_fxLayers.Count == 0) return;
            foreach (var layer in _fxLayers)
            {
                try { OverlayCanvas?.Children.Remove(layer); }
                catch { /* already gone */ }
            }
            if (all) _fxLayers.Clear();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] SweepFx failed");
        }
    }

    // ------------------------------------------------------------------ helper

    /// <summary>
    /// A one-shot dispatcher delay that cannot outlive the app. Every FX step goes through here so
    /// there is exactly one place where the shutdown and dispatcher-null guards live.
    /// </summary>
    private void After(int ms, Action act)
    {
        try
        {
            var t = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms))
            };
            t.Tick += (_, _) =>
            {
                try
                {
                    t.Stop();
                    if (Application.Current?.Dispatcher == null) return;
                    if (Application.Current.Dispatcher.HasShutdownStarted) return;
                    act();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[EmiDesk] deferred FX step failed");
                }
            };
            t.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] After({Ms}) failed", ms);
        }
    }
}
