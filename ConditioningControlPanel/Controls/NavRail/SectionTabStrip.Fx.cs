using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// Nav polish wave 7 (owner, 2026-10-06: "add effects and personality to those buttons, they
    /// should POP"): the pills' motion. Hover lifts and grows the pill a touch and the glyph gives
    /// a little wiggle; choosing a pill rings it (NavGlow) and throws a small spark burst; the
    /// active pill catches a sheen every 9 s. Full = all of it, Reduced = the lift only and the
    /// ring held still, Off = nothing. The static look (plates, tints, badges) lives in the
    /// strip's own files; this partial only listens to the strip's hooks.
    /// </summary>
    internal static class NavStripFxRules
    {
        public const double HoverLiftPx = 1.5;
        public const double HoverScale = 1.035;
        public const int HoverInMs = 120;
        public const int HoverOutMs = 160;
        public const double WiggleDegrees = 8;
        public const int WiggleMs = 280;
        public const int SheenEveryMs = 9000;
        public const int SheenMs = 650;
        public const double SheenWidth = 40;
        public const double SheenOpacity = 0.22;
        public const int BurstSparks = 18;
        /// <summary>How far the spark layer spills past the track on every side.</summary>
        public const double SparkBleed = 12;

        /// <summary>The hover lift at a motion level (0 = none).</summary>
        public static double LiftFor(MotionLevel level) => level == MotionLevel.Off ? 0 : HoverLiftPx;

        /// <summary>The hover scale at a motion level (Reduced lifts only).</summary>
        public static double ScaleFor(MotionLevel level) => level == MotionLevel.Full ? HoverScale : 1.0;

        /// <summary>The glyph wiggles on Full only.</summary>
        public static bool Wiggles(MotionLevel level) => level == MotionLevel.Full;

        /// <summary>The idle sheen clock runs only on Full, on a tier that allows ambient motion,
        /// while the strip is on screen and the main window's chrome loops are allowed.</summary>
        public static bool SheenRuns(MotionLevel level, bool tierAllowsAmbient, bool visible, bool chromeAllows) =>
            level == MotionLevel.Full && tierAllowsAmbient && visible && chromeAllows;
    }

    public partial class SectionTabStrip
    {
        private sealed class HoverParts
        {
            public ScaleTransform Scale = null!;
            public TranslateTransform Lift = null!;
        }

        private static readonly ConditionalWeakTable<FrameworkElement, HoverParts> FxHoverParts = new();
        private static readonly Duration WiggleDuration = new(TimeSpan.FromMilliseconds(NavStripFxRules.WiggleMs));

        private Grid? _fxLayer;
        private Canvas? _fxSheenLayer;
        private Border? _fxSheenHost;
        private TranslateTransform? _fxSheenShift;
        private AmbientFxCanvas? _fxSparks;
        private DispatcherTimer? _fxSheenClock;

        /// <summary>Test seam: spark bursts the strip asked its overlay for (the overlay itself
        /// still refuses unless particles are allowed).</summary>
        internal int BurstsFired { get; private set; }

        /// <summary>Test seam: choose rings that NavGlow drew.</summary>
        internal int GlowsFired { get; private set; }

        /// <summary>Test seam: sheen sweeps run.</summary>
        internal int SheensRun { get; private set; }

        /// <summary>Test seam: forces the tier's ambient verdict instead of asking PerformanceProfile.</summary>
        internal bool? AmbientTierOverride { get; set; }

        /// <summary>Test seam: forces the on-screen verdict (an offscreen strip is never IsVisible).</summary>
        internal bool? VisibleOverride { get; set; }

        internal FrameworkElement? FxLayer => _fxLayer;
        internal AmbientFxCanvas? FxSparks => _fxSparks;
        internal bool SheenClockRunning => _fxSheenClock?.IsEnabled == true;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            try
            {
                PillCreated += FxWirePill;
                TabRequested += FxOnChosen;
                IsVisibleChanged += (_, _) => UpdateSheenClock();
                Loaded += (_, _) => UpdateSheenClock();
                Unloaded += (_, _) => _fxSheenClock?.Stop();
                FxBuildLayer();
            }
            catch (Exception ex) { App.Logger?.Debug("SectionTabStrip.Fx init: {E}", ex.Message); }
        }

        // ----------------------------------------------------------------- the overlay

        private void FxBuildLayer()
        {
            if (PillTrack?.Child is not Grid track) return;
            double bleed = NavStripFxRules.SparkBleed;
            _fxSheenShift = new TranslateTransform();
            _fxSheenHost = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                ClipToBounds = true,
                Child = new Border
                {
                    Width = NavStripFxRules.SheenWidth,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Opacity = NavStripFxRules.SheenOpacity,
                    RenderTransform = _fxSheenShift,
                    Background = Freeze(new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0.5),
                        EndPoint = new Point(1, 0.5),
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                            new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.5),
                            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
                        },
                    }),
                },
            };
            // The canvas sits back inside the bleed, so its origin is the track grid's origin.
            _fxSheenLayer = new Canvas { IsHitTestVisible = false, Margin = new Thickness(bleed), Children = { _fxSheenHost } };
            _fxSparks = new AmbientFxCanvas();
            _fxLayer = new Grid
            {
                IsHitTestVisible = false,
                Margin = new Thickness(-bleed),
                Children = { _fxSheenLayer, _fxSparks },
            };
            track.Children.Add(_fxLayer);
        }

        // ----------------------------------------------------------------- hover

        private void FxWirePill(NavTab tab, FrameworkElement element)
        {
            if (element is not Button pill) return;
            pill.MouseEnter += (_, _) => FxHover(pill, true, animate: true);
            pill.MouseLeave += (_, _) => FxHover(pill, false, animate: true);
        }

        /// <summary>Test seam: hover a pill (or leave it) without a mouse and without animation.</summary>
        internal void FxHoverForTests(string key, bool on)
        {
            if (PillFor(key) is { } pill) FxHover(pill, on, animate: false);
        }

        /// <summary>Test seam: the ring Border a pill wears (the element hover moves).</summary>
        internal static FrameworkElement? RingOf(Button? pill) => pill?.Content as FrameworkElement;

        private void FxHover(Button pill, bool on, bool animate)
        {
            try
            {
                if (pill.Content is not FrameworkElement ring) return;
                var level = Level;
                if (level == MotionLevel.Off)
                {
                    // Off: never add a transform; settle one a level change left behind.
                    if (FxHoverParts.TryGetValue(ring, out var stale)) FxSettle(stale, 0, 1, 0, false);
                    return;
                }
                // The active pill rides the sliding fill; lifting its face off the fill reads as
                // a misprint, so it keeps still (its glyph may still wiggle).
                bool lift = on && !IsActive(pill.Tag as string ?? string.Empty);
                var parts = FxEnsureHoverParts(ring);
                if (parts != null)
                {
                    FxSettle(parts,
                        lift ? -NavStripFxRules.LiftFor(level) : 0,
                        lift ? NavStripFxRules.ScaleFor(level) : 1.0,
                        on ? NavStripFxRules.HoverInMs : NavStripFxRules.HoverOutMs,
                        animate);
                }
                if (on && animate && NavStripFxRules.Wiggles(level)) FxWiggle(FxGlyphOf(ring));
            }
            catch (Exception ex) { App.Logger?.Debug("SectionTabStrip.FxHover: {E}", ex.Message); }
        }

        private static void FxSettle(HoverParts parts, double y, double scale, int ms, bool animate)
        {
            if (!animate || ms <= 0)
            {
                parts.Lift.BeginAnimation(TranslateTransform.YProperty, null);
                parts.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                parts.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                parts.Lift.Y = y;
                parts.Scale.ScaleX = parts.Scale.ScaleY = scale;
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = new Duration(TimeSpan.FromMilliseconds(ms));
            parts.Lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, d) { EasingFunction = ease });
            parts.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
            parts.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
        }

        /// <summary>
        /// The ring's hover transforms, made once. The strip's PressSquish finds the FIRST
        /// ScaleTransform in the ring's group, so that slot stays the squish's: whatever transform
        /// the ring already wears goes first (or a fresh identity scale), then the hover scale,
        /// then the lift.
        /// </summary>
        private static HoverParts? FxEnsureHoverParts(FrameworkElement ring)
        {
            if (FxHoverParts.TryGetValue(ring, out var known)) return known;
            var current = ring.RenderTransform;
            TransformGroup group;
            if (current is TransformGroup existing && !existing.IsFrozen) group = existing;
            else
            {
                group = new TransformGroup();
                if (current != null && current != Transform.Identity && !current.IsFrozen) group.Children.Add(current);
                else group.Children.Add(new ScaleTransform(1, 1));
            }
            var parts = new HoverParts { Scale = new ScaleTransform(1, 1), Lift = new TranslateTransform() };
            group.Children.Add(parts.Scale);
            group.Children.Add(parts.Lift);
            ring.RenderTransformOrigin = new Point(0.5, 0.5);
            if (!ReferenceEquals(ring.RenderTransform, group)) ring.RenderTransform = group;
            FxHoverParts.Add(ring, parts);
            return parts;
        }

        /// <summary>The pill's glyph: the TextBlock drawn in the glyph font, if any.</summary>
        private static TextBlock? FxGlyphOf(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb && tb.FontFamily?.Source == NavStripRules.GlyphFont) return tb;
                if (FxGlyphOf(child) is { } hit) return hit;
            }
            // Before the first layout the visual tree is empty: walk the logical children too.
            if (root is Decorator { Child: { } dc }) return dc is TextBlock t && t.FontFamily?.Source == NavStripRules.GlyphFont ? t : FxGlyphOf(dc);
            if (root is ContentControl { Content: DependencyObject cc }) return FxGlyphOf(cc);
            if (root is Panel panel)
                foreach (UIElement c in panel.Children)
                {
                    if (c is TextBlock t2 && t2.FontFamily?.Source == NavStripRules.GlyphFont) return t2;
                    if (c is not TextBlock && FxGlyphOf(c) is { } hit2) return hit2;
                }
            return null;
        }

        private static void FxWiggle(TextBlock? glyph)
        {
            if (glyph == null) return;
            RotateTransform? spin = glyph.RenderTransform as RotateTransform;
            if (spin == null || spin.IsFrozen)
            {
                var current = glyph.RenderTransform;
                if (current is TransformGroup g && !g.IsFrozen)
                {
                    foreach (var c in g.Children) if (c is RotateTransform r) spin = r;
                    if (spin == null) { spin = new RotateTransform(); g.Children.Add(spin); }
                }
                else if (current == null || current == Transform.Identity || current.Value.IsIdentity)
                {
                    spin = new RotateTransform();
                    glyph.RenderTransformOrigin = new Point(0.5, 0.5);
                    glyph.RenderTransform = spin;
                }
                else return;   // somebody else's transform: leave the glyph alone
            }
            double a = NavStripFxRules.WiggleDegrees;
            int ms = NavStripFxRules.WiggleMs;
            var wiggle = new DoubleAnimationUsingKeyFrames { Duration = WiggleDuration };
            wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(-a, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.25)), new SineEase { EasingMode = EasingMode.EaseOut }));
            wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(a, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.65)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase { EasingMode = EasingMode.EaseOut }));
            spin.BeginAnimation(RotateTransform.AngleProperty, wiggle);
        }

        // ----------------------------------------------------------------- choose

        /// <summary>The section hue the strip paints with (the active fill's colour).</summary>
        private Color FxHue() => ActiveFill?.Background is SolidColorBrush b ? b.Color : _hue;

        private void FxOnChosen(NavTab tab)
        {
            try
            {
                var level = Level;
                if (level == MotionLevel.Off) return;
                var pill = PillFor(tab.Key);
                if (pill == null) return;
                // The chosen pill now rides the fill: settle any hover lift it carried.
                if (RingOf(pill) is { } ring && FxHoverParts.TryGetValue(ring, out var parts) && IsActive(tab.Key))
                    FxSettle(parts, 0, 1, NavStripFxRules.HoverOutMs, animate: level == MotionLevel.Full);

                var hue = FxHue();
                if (NavGlow.Once(pill, hue, level, "pill-choose")) GlowsFired++;

                if (_fxSparks != null && pill.ActualWidth > 0)
                {
                    var centre = pill.TranslatePoint(new Point(pill.ActualWidth / 2, pill.ActualHeight / 2), _fxSparks);
                    _fxSparks.Burst(centre.X, centre.Y, hue, NavStripFxRules.BurstSparks);
                    BurstsFired++;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("SectionTabStrip.FxOnChosen: {E}", ex.Message); }
        }

        /// <summary>Test seam: what choosing a pill does, without the click.</summary>
        internal void FxChooseForTests(string key)
        {
            foreach (var t in NavStripRules.Pills(_section))
                if (string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) { FxOnChosen(t); return; }
        }

        // ----------------------------------------------------------------- idle sheen

        private bool FxTierAllowsAmbient() =>
            AmbientTierOverride ?? PerformanceProfile.AllowAmbientMotion(PerformanceProfile.CurrentTier);

        /// <summary>
        /// The main window's chrome gate. MainWindow.ChromeAmbientAllowed is private, so this reads
        /// the same two facts it does (window active, not minimised); motion and tier are checked
        /// beside it. No main window (tests, the launcher alone) = no window gate.
        /// </summary>
        private static bool FxChromeAllows()
        {
            try
            {
                var mw = App.MainWindowRef ?? Application.Current?.MainWindow as MainWindow;
                if (mw == null) return true;
                return mw.IsActive && mw.WindowState != WindowState.Minimized;
            }
            catch { return true; }
        }

        /// <summary>Start or park the 9 s sheen clock to match the gates.</summary>
        internal void UpdateSheenClock()
        {
            try
            {
                bool visible = VisibleOverride ?? IsVisible;
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
                    _fxSheenClock.Tick += (_, _) =>
                    {
                        // The window gate is read per tick: a deactivated or minimised window
                        // skips the sweep rather than hooking the window's events a second time.
                        bool live = NavStripFxRules.SheenRuns(Level, FxTierAllowsAmbient(), VisibleOverride ?? IsVisible, FxChromeAllows());
                        if (!live)
                        {
                            if (!(VisibleOverride ?? IsVisible) || Level != MotionLevel.Full) _fxSheenClock?.Stop();
                            return;
                        }
                        SweepSheen();
                    };
                }
                if (!_fxSheenClock.IsEnabled) _fxSheenClock.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("SectionTabStrip.UpdateSheenClock: {E}", ex.Message); }
        }

        /// <summary>One sheen across the active pill. False when there is no laid-out active pill.</summary>
        internal bool SweepSheen()
        {
            try
            {
                if (_fxSheenHost == null || _fxSheenShift == null || PillTrack?.Child is not Grid track) return false;
                var pill = _activePill == null ? null : PillFor(_activePill);
                if (pill == null || pill.ActualWidth <= 0 || pill.ActualHeight <= 0) return false;
                var at = pill.TranslatePoint(new Point(0, 0), track);
                double w = pill.ActualWidth, h = pill.ActualHeight;
                Canvas.SetLeft(_fxSheenHost, at.X);
                Canvas.SetTop(_fxSheenHost, at.Y);
                _fxSheenHost.Width = w;
                _fxSheenHost.Height = h;
                _fxSheenHost.Clip = new RectangleGeometry(new Rect(0, 0, w, h), h / 2, h / 2);
                if (_fxSheenHost.Child is FrameworkElement band) band.Height = h;
                _fxSheenHost.Visibility = Visibility.Visible;

                var sweep = new DoubleAnimation(-NavStripFxRules.SheenWidth, w,
                    new Duration(TimeSpan.FromMilliseconds(NavStripFxRules.SheenMs)))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                sweep.Completed += (_, _) => { if (_fxSheenHost != null) _fxSheenHost.Visibility = Visibility.Collapsed; };
                _fxSheenShift.BeginAnimation(TranslateTransform.XProperty, sweep);
                SheensRun++;
                return true;
            }
            catch (Exception ex) { App.Logger?.Debug("SectionTabStrip.SweepSheen: {E}", ex.Message); return false; }
        }
    }
}
