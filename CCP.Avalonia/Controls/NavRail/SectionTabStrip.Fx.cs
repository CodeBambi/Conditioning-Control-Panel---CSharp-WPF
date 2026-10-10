// PORTED from WPF 7.1.5 Controls/NavRail/SectionTabStrip.Fx.cs (nav polish wave 7, owner
// 2026-10-06: "add effects and personality to those buttons, they should POP"). Numbers are
// Core's NavStripFxRules, unchanged from WPF.
//
// Hover lifts and grows the pill a touch and the glyph gives a little wiggle; choosing a pill
// throws a small spark burst in the section hue (the ring is NavGlow, already in Choose); the
// active pill catches a sheen every 9 s. Full = all of it, Reduced = the lift only, Off = nothing.
//
// Avalonia differences, on purpose (the head's FX law):
//  - transforms are written by Helpers/TransformTween, never Animation.RunAsync(aTransform);
//  - the 9 s sheen clock is a plain 9 s DispatcherTimer (one tick every nine seconds, not a
//    per-frame loop) and each sweep is one 650 ms tween that ends, so nothing here keeps the
//    window composing;
//  - a motion-level change (AmbientFxCanvas.Env.MotionGateChanged) re-reads every gate and
//    settles a lift the old level left behind.

using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Nav;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls.NavRail
{
    public sealed partial class SectionTabStrip
    {
        private sealed class HoverParts
        {
            public ScaleTransform Scale = null!;
            public TranslateTransform Lift = null!;
            public DispatcherTimer? Tween, ScaleTween;
        }

        private static readonly ConditionalWeakTable<Control, HoverParts> FxHoverParts = new();

        private Panel? _fxLayer;
        private Canvas? _fxSheenLayer;
        private Border? _fxSheenHost;
        private Border? _fxSheenBand;
        private TranslateTransform? _fxSheenShift;
        private AmbientFxCanvas? _fxSparks;
        private DispatcherTimer? _fxSheenClock;
        private DispatcherTimer? _fxSheenTween;
        private DispatcherTimer? _fxWiggleTween;
        private bool _fxWired;

        /// <summary>Test seam: spark bursts the strip asked its overlay for (the overlay itself
        /// still refuses unless particles are allowed).</summary>
        internal int BurstsFired { get; private set; }

        /// <summary>Test seam: sheen sweeps run.</summary>
        internal int SheensRun { get; private set; }

        /// <summary>Test seam: forces the tier's ambient verdict instead of asking the head.</summary>
        internal bool? AmbientTierOverride { get; set; }

        /// <summary>Test seam: forces the on-screen verdict (an offscreen strip is never visible).</summary>
        internal bool? VisibleOverride { get; set; }

        internal Control? FxLayer => _fxLayer;
        internal AmbientFxCanvas? FxSparks => _fxSparks;
        internal bool SheenClockRunning => _fxSheenClock?.IsEnabled == true;
        internal bool SheenShown => _fxSheenHost?.IsVisible == true;
        internal double SheenBandOpacity => _fxSheenBand?.Opacity ?? 0;

        /// <summary>Test seam: the ring Border a pill wears (the element hover moves).</summary>
        internal Control? RingOf(string key) => Part(key)?.Ring;

        /// <summary>Test seam: the hover lift (Y, negative = up) and scale a pill's ring wears now.</summary>
        internal (double Lift, double Scale) HoverStateOf(string key) =>
            Part(key)?.Ring is { } ring && FxHoverParts.TryGetValue(ring, out var p) ? (p.Lift.Y, p.Scale.ScaleX) : (0, 1);

        // ----------------------------------------------------------------- wiring

        /// <summary>Called once from the constructor (WPF OnInitialized).</summary>
        private void FxInit()
        {
            try
            {
                if (_fxWired) return;
                _fxWired = true;
                PillCreated += FxWirePill;
                TabRequested += FxOnChosen;
                FxBuildLayer();
                AttachedToVisualTree += (_, _) =>
                {
                    AmbientFxCanvas.Env.MotionGateChanged += FxOnMotionGate;
                    UpdateSheenClock();
                };
                DetachedFromVisualTree += (_, _) =>
                {
                    AmbientFxCanvas.Env.MotionGateChanged -= FxOnMotionGate;
                    FxPark();
                };
                PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateSheenClock(); };
            }
            catch (Exception ex) { Log.Debug("SectionTabStrip.Fx init: {E}", ex.Message); }
        }

        /// <summary>A motion-level change: re-read the sheen gate, settle what the old level left.</summary>
        private void FxOnMotionGate()
        {
            try
            {
                UpdateSheenClock();
                var level = Level;
                foreach (var p in _pills)
                {
                    if (!FxHoverParts.TryGetValue(p.Ring, out var parts)) continue;
                    if (level == MotionLevel.Off) FxSettle(parts, 0, 1, 0, animate: false);
                    else if (level == MotionLevel.Reduced) FxSettle(parts, parts.Lift.Y, 1, 0, animate: false);
                }
                if (level != MotionLevel.Full) FxHideSheen();
            }
            catch (Exception ex) { Log.Debug("SectionTabStrip.FxOnMotionGate: {E}", ex.Message); }
        }

        /// <summary>Everything stops and rests (the strip left the tree).</summary>
        private void FxPark()
        {
            _fxSheenClock?.Stop();
            _fxWiggleTween?.Stop();
            FxHideSheen();
            foreach (var p in _pills)
                if (FxHoverParts.TryGetValue(p.Ring, out var parts)) FxSettle(parts, 0, 1, 0, animate: false);
        }

        private void FxHideSheen()
        {
            _fxSheenTween?.Stop();
            _fxSheenTween = null;
            if (_fxSheenHost != null) _fxSheenHost.IsVisible = false;
        }

        // ----------------------------------------------------------------- the overlay

        private void FxBuildLayer()
        {
            if (PillTrack.Child is not Panel track) return;
            double bleed = NavStripFxRules.SparkBleed;
            _fxSheenShift = new TranslateTransform();
            _fxSheenBand = new Border
            {
                Width = NavStripFxRules.SheenWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Opacity = NavStripFxRules.SheenOpacity,
                RenderTransform = _fxSheenShift,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                        new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.5),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                    },
                }.ToImmutable(),
            };
            _fxSheenHost = new Border
            {
                IsVisible = false,
                IsHitTestVisible = false,
                ClipToBounds = true,   // with the CornerRadius set per sweep: the pill's own round ends
                Child = _fxSheenBand,
            };
            // The canvas sits back inside the bleed, so its origin is the track panel's origin.
            _fxSheenLayer = new Canvas { IsHitTestVisible = false, Margin = new Thickness(bleed), Children = { _fxSheenHost } };
            _fxSparks = new AmbientFxCanvas { IsHitTestVisible = false };
            _fxLayer = new Panel
            {
                IsHitTestVisible = false,
                Margin = new Thickness(-bleed),
                Children = { _fxSheenLayer, _fxSparks },
            };
            track.Children.Add(_fxLayer);
        }

        // ----------------------------------------------------------------- hover

        private void FxWirePill(NavTab tab, Control element)
        {
            if (element is not Button pill) return;
            pill.PointerEntered += (_, _) => FxHover(tab.Key, true, animate: true);
            pill.PointerExited += (_, _) => FxHover(tab.Key, false, animate: true);
        }

        /// <summary>Test seam: hover a pill (or leave it) without a pointer and without animation.</summary>
        internal void FxHoverForTests(string key, bool on) => FxHover(key, on, animate: false);

        private void FxHover(string key, bool on, bool animate)
        {
            try
            {
                var p = Part(key);
                if (p == null) return;
                var ring = p.Ring;
                var level = Level;
                if (level == MotionLevel.Off)
                {
                    // Off: never add a transform; settle one a level change left behind.
                    if (FxHoverParts.TryGetValue(ring, out var stale)) FxSettle(stale, 0, 1, 0, false);
                    return;
                }
                // The active pill rides the sliding fill; lifting its face off the fill reads as
                // a misprint, so it keeps still (its glyph may still wiggle).
                bool lift = on && !IsActive(key);
                var parts = FxEnsureHoverParts(ring);
                FxSettle(parts,
                    lift ? -NavStripFxRules.LiftFor(level) : 0,
                    lift ? NavStripFxRules.ScaleFor(level) : 1.0,
                    on ? NavStripFxRules.HoverInMs : NavStripFxRules.HoverOutMs,
                    animate);
                if (on && animate && NavStripFxRules.Wiggles(level)) FxWiggle(p.Glyph);
            }
            catch (Exception ex) { Log.Debug("SectionTabStrip.FxHover: {E}", ex.Message); }
        }

        private static void FxSettle(HoverParts parts, double y, double scale, int ms, bool animate)
        {
            parts.Tween?.Stop();
            parts.ScaleTween?.Stop();
            parts.Tween = parts.ScaleTween = null;
            if (!animate || ms <= 0)
            {
                parts.Lift.Y = y;
                parts.Scale.ScaleX = parts.Scale.ScaleY = scale;
                return;
            }
            double y0 = parts.Lift.Y, s0 = parts.Scale.ScaleX;
            parts.Tween = TransformTween.Run(parts.Lift, TimeSpan.FromMilliseconds(ms),
                new (double, AvaloniaProperty, double)[]
                {
                    (0, TranslateTransform.YProperty, y0), (1, TranslateTransform.YProperty, y),
                }, new CubicEaseOut());
            // Two objects, one clock each: the scale rides its own tween of the same length.
            parts.ScaleTween = TransformTween.Run(parts.Scale, TimeSpan.FromMilliseconds(ms),
                new (double, AvaloniaProperty, double)[]
                {
                    (0, ScaleTransform.ScaleXProperty, s0), (1, ScaleTransform.ScaleXProperty, scale),
                    (0, ScaleTransform.ScaleYProperty, s0), (1, ScaleTransform.ScaleYProperty, scale),
                }, new CubicEaseOut());
        }

        /// <summary>The ring's hover transforms, made once: the hover scale, then the lift. The
        /// ring wears no other transform on this head (depth travel is the FACE's).</summary>
        private static HoverParts FxEnsureHoverParts(Control ring)
        {
            if (FxHoverParts.TryGetValue(ring, out var known)) return known;
            var parts = new HoverParts { Scale = new ScaleTransform(1, 1), Lift = new TranslateTransform() };
            var group = new TransformGroup();
            group.Children.Add(parts.Scale);
            group.Children.Add(parts.Lift);
            ring.RenderTransformOrigin = RelativePoint.Center;
            ring.RenderTransform = group;
            FxHoverParts.Add(ring, parts);
            return parts;
        }

        /// <summary>WPF FxWiggle: -8 at 25%, +8 at 65%, 0 at the end, 280 ms.</summary>
        private void FxWiggle(TextBlock? glyph)
        {
            if (glyph == null) return;
            if (glyph.RenderTransform is not RotateTransform spin)
            {
                if (glyph.RenderTransform != null) return;   // somebody else's transform: leave the glyph alone
                spin = new RotateTransform();
                glyph.RenderTransformOrigin = RelativePoint.Center;
                glyph.RenderTransform = spin;
            }
            double a = NavStripFxRules.WiggleDegrees;
            _fxWiggleTween?.Stop();
            foreach (var p in _pills)
                if (p.Glyph?.RenderTransform is RotateTransform other && !ReferenceEquals(other, spin)) other.Angle = 0;
            _fxWiggleTween = TransformTween.Run(spin, TimeSpan.FromMilliseconds(NavStripFxRules.WiggleMs),
                new (double, AvaloniaProperty, double)[]
                {
                    (0, RotateTransform.AngleProperty, spin.Angle),
                    (0.25, RotateTransform.AngleProperty, -a),
                    (0.65, RotateTransform.AngleProperty, a),
                    (1, RotateTransform.AngleProperty, 0),
                }, new SineEaseOut());
        }

        // ----------------------------------------------------------------- choose

        private void FxOnChosen(NavTab tab)
        {
            try
            {
                var level = Level;
                if (level == MotionLevel.Off) return;
                var part = Part(tab.Key);
                if (part == null) return;
                // The chosen pill now rides the fill: settle any hover lift it carried.
                if (FxHoverParts.TryGetValue(part.Ring, out var parts) && IsActive(tab.Key))
                    FxSettle(parts, 0, 1, NavStripFxRules.HoverOutMs, animate: level == MotionLevel.Full);

                var pill = part.Pill;
                if (_fxSparks != null && pill.Bounds.Width > 0
                    && pill.TranslatePoint(new Point(pill.Bounds.Width / 2, pill.Bounds.Height / 2), _fxSparks) is { } centre)
                {
                    _fxSparks.Burst(centre.X, centre.Y, NavPaint.C(NavStripRules.Accent(_section)), NavStripFxRules.BurstSparks);
                    BurstsFired++;
                }
            }
            catch (Exception ex) { Log.Debug("SectionTabStrip.FxOnChosen: {E}", ex.Message); }
        }

        // ----------------------------------------------------------------- idle sheen

        private bool FxTierAllowsAmbient() =>
            AmbientTierOverride ?? AmbientFxCanvas.Env.AllowAmbientMotion(AmbientFxCanvas.Env.CurrentTier);

        /// <summary>The window gate WPF read per tick: active and not minimised. No window = no gate.</summary>
        private bool FxChromeAllows()
        {
            try
            {
                return TopLevel.GetTopLevel(this) is not Window w || (w.IsActive && w.WindowState != WindowState.Minimized);
            }
            catch { return true; }
        }

        /// <summary>Start or park the 9 s sheen clock to match the gates.</summary>
        internal void UpdateSheenClock()
        {
            try
            {
                bool visible = VisibleOverride ?? IsEffectivelyVisible;
                bool runs = NavStripFxRules.SheenRuns(Level, FxTierAllowsAmbient(), visible, chromeAllows: true);
                if (!runs)
                {
                    _fxSheenClock?.Stop();
                    return;
                }
                if (_fxSheenClock == null)
                {
                    _fxSheenClock = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(NavStripFxRules.SheenEveryMs),
                    };
                    _fxSheenClock.Tick += (_, _) => FxSheenTick();
                }
                if (!_fxSheenClock.IsEnabled) _fxSheenClock.Start();
            }
            catch (Exception ex) { Log.Debug("SectionTabStrip.UpdateSheenClock: {E}", ex.Message); }
        }

        /// <summary>One beat of the 9 s clock (test seam). The window gate is read per tick: a
        /// deactivated or minimised window skips the sweep.</summary>
        internal void FxSheenTick()
        {
            bool visible = VisibleOverride ?? IsEffectivelyVisible;
            bool live = NavStripFxRules.SheenRuns(Level, FxTierAllowsAmbient(), visible, FxChromeAllows());
            if (!live)
            {
                if (!visible || Level != MotionLevel.Full) _fxSheenClock?.Stop();
                return;
            }
            SweepSheen();
        }

        /// <summary>One sheen across the active pill. False when there is no laid-out active pill.</summary>
        internal bool SweepSheen()
        {
            try
            {
                if (_fxSheenHost == null || _fxSheenShift == null || _fxSheenBand == null || PillTrack.Child is not Panel track) return false;
                var pill = _activePill == null ? null : PillFor(_activePill);
                if (pill == null || pill.Bounds.Width <= 0 || pill.Bounds.Height <= 0) return false;
                if (pill.TranslatePoint(new Point(0, 0), track) is not { } at) return false;
                double w = pill.Bounds.Width, h = pill.Bounds.Height;
                Canvas.SetLeft(_fxSheenHost, at.X);
                Canvas.SetTop(_fxSheenHost, at.Y);
                _fxSheenHost.Width = w;
                _fxSheenHost.Height = h;
                _fxSheenHost.CornerRadius = new CornerRadius(h / 2);
                _fxSheenBand.Height = h;
                _fxSheenShift.X = -NavStripFxRules.SheenWidth;
                _fxSheenHost.IsVisible = true;

                _fxSheenTween?.Stop();
                var tween = TransformTween.Run(_fxSheenShift, TimeSpan.FromMilliseconds(NavStripFxRules.SheenMs),
                    new (double, AvaloniaProperty, double)[]
                    {
                        (0, TranslateTransform.XProperty, -NavStripFxRules.SheenWidth),
                        (1, TranslateTransform.XProperty, w),
                    }, new SineEaseInOut());
                _fxSheenTween = tween;
                // The band leaves through the clip; the host drops itself when the sweep ends.
                tween.Tick += (_, _) =>
                {
                    if (tween.IsEnabled || !ReferenceEquals(_fxSheenTween, tween)) return;
                    _fxSheenTween = null;
                    if (_fxSheenHost != null) _fxSheenHost.IsVisible = false;
                };
                SheensRun++;
                return true;
            }
            catch (Exception ex) { Log.Debug("SectionTabStrip.SweepSheen: {E}", ex.Message); return false; }
        }
    }
}
