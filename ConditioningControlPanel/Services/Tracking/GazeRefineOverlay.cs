using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services;

/// <summary>
/// What a refine chip is standing in for. Only drives the chip's look — the
/// service owns what actually happens when the chip is selected.
/// </summary>
internal enum GazeRefineKind
{
    Bubble,
    Flash,
    Floating,
}

/// <summary>
/// One entry in a two-stage zoom-refine. <see cref="SourceBounds"/> is the
/// candidate's real on-screen rect in the OnGazeMove DIP space; the overlay
/// magnifies the constellation of those rects about their centroid so the
/// chips end up far enough apart to be picked apart by eye. Activate/IsAlive
/// belong to GazeFocusService — the overlay never calls them.
/// </summary>
internal sealed class GazeRefineCandidate
{
    public Rect SourceBounds { get; init; }
    public GazeRefineKind Kind { get; init; }
    /// <summary>Optional real preview (flash frames). Null = draw a stylized token.</summary>
    public BitmapSource? Preview { get; init; }
    public Action? Activate { get; init; }
    public Func<bool>? IsAlive { get; init; }
}

/// <summary>
/// Stage two of gaze selection: a magnified inset of an ambiguous
/// neighbourhood.
///
/// Why this exists: the fovea is about 1 degree wide and the eye micro-moves
/// continuously during fixation, so a raw gaze point can never be precise —
/// a webcam realistically lands inside a 90-200px blob. The fix every
/// shipping eye-control product converges on is to make the SELECTION
/// precise rather than the cursor: dwell activation, magnetic targets sized
/// >= 3 degrees, and a zoom-refine stage for anything smaller. GazeFocusService
/// already had the first two. This is the third.
///
/// When a dwell completes over a spot where two or more targets sit inside
/// one gaze error blob with near-identical scores, picking the top score is a
/// coin flip — that coin flip is exactly what users feel as "it selected the
/// wrong thing". Instead the service raises this overlay: the candidates are
/// re-drawn, magnified, pushed apart to a comfortable >= 3 degree pitch, and a
/// second dwell picks one for real.
///
/// Rendering follows the GazeDebugCursorService overlay idiom — one unowned
/// transparent topmost window spanning the virtual screen, click-through via
/// WS_EX_TRANSPARENT so it can never trap input, never activated so it can
/// never steal focus. Plain WPF shapes rather than Skia: this is a static
/// panel with one animated ring, not a per-frame comet trail, and it must not
/// be able to fail the way a missing libSkiaSharp fails (#912).
///
/// Coordinates: everything public is in screen DIPs (the OnGazeMove space);
/// the canvas is positioned at the virtual-screen origin so chip layout is
/// just a translate.
/// </summary>
internal sealed class GazeRefineOverlay : IDisposable
{
    // A chip has to be comfortably bigger than the gaze error blob. 150 DIPs
    // is ~3.3 degrees at 60cm on a 27" 1440p panel, which is the size the
    // literature (and Tobii/Windows Eye Control's own target guidance) calls
    // the floor for reliable dwell selection.
    private const double MinChipDips = 150;
    private const double MaxChipDips = 220;
    // Absolute floor on centre-to-centre pitch. The pitch that actually gets
    // enforced is radius-dependent (see RequiredSeparation) because chips are
    // not all one size: this constant alone is smaller than two MaxChipDips
    // radii (110 + 110 = 220), so on its own it would let the drawn bodies
    // overlap. It survives only as a floor and as the zoom target below.
    private const double MinChipSeparation = 210;
    // HitTest is forgiving by this much past the drawn body, so a chip's
    // catchment radius is Radius + ChipHitSlack — up to 110 + 34 = 144.
    private const double ChipHitSlack = 34;
    private const double PanelPadding = 34;
    private const double CaptionHeight = 30;
    private const double ZoomCap = 20.0;

    // Project palette.
    private static readonly Color Accent = Color.FromRgb(0xFF, 0x69, 0xB4);      // #FF69B4
    private static readonly Color PanelFill = Color.FromRgb(0x1A, 0x1A, 0x2E);   // #1A1A2E
    private static readonly Color ChipFill = Color.FromRgb(0x25, 0x25, 0x42);    // #252542

    private Window? _window;
    private Canvas? _canvas;
    private IntPtr _hwnd;
    private DateTime _lastTopmostAssert = DateTime.MinValue;

    private readonly List<Chip> _chips = new();
    private Rect _panelBounds;   // screen DIPs, panel only
    private double _originX, _originY;

    private sealed class Chip
    {
        public Point Center;     // screen DIPs
        public double Radius;    // screen DIPs
        public Path? Ring;
        public FrameworkElement? Body;
        public double Progress;
    }

    /// <summary>True once <see cref="Show"/> has put a panel on screen.</summary>
    public bool IsShowing => _window != null;

    /// <summary>Number of chips currently laid out.</summary>
    public int ChipCount => _chips.Count;

    /// <summary>
    /// The refine panel's rect in screen DIPs, grown by an escape margin. Gaze
    /// outside this for long enough is the "look away to cancel" gesture, so
    /// the margin is deliberately generous — a user who is still reading the
    /// panel must not be treated as having left it.
    /// </summary>
    public Rect EscapeBounds
    {
        get
        {
            if (_panelBounds.IsEmpty) return Rect.Empty;
            var r = _panelBounds;
            r.Inflate(140, 140);
            return r;
        }
    }

    /// <summary>Bounds of one chip in screen DIPs, for the soft gaze attractor.</summary>
    public Rect GetChipBounds(int index)
    {
        if (index < 0 || index >= _chips.Count) return Rect.Empty;
        var c = _chips[index];
        return new Rect(c.Center.X - c.Radius, c.Center.Y - c.Radius, c.Radius * 2, c.Radius * 2);
    }

    /// <summary>
    /// Which chip the gaze point is on, or -1.
    ///
    /// LAYOUT INVARIANT (enforced in <see cref="Layout"/>, checkable by hand):
    /// for every pair of chips i != j,
    ///     dist(Center_i, Center_j) >= (Radius_i + ChipHitSlack) + (Radius_j + ChipHitSlack)
    /// i.e. the two catchment discs are disjoint. Worst case is two chips at
    /// MaxChipDips: (110 + 34) + (110 + 34) = 288 DIPs of required pitch.
    ///
    /// So at most one chip can ever contain a gaze point, and — because a
    /// catchment strictly contains its drawn body — every point drawn as part
    /// of a chip resolves to THAT chip. Losing the chip requires the gaze to
    /// leave the body by more than a full slack ring, which is bare panel
    /// background. (Before this held, adjacent max-size chips overlapped by 10
    /// DIPs of body and 78 DIPs of catchment: the boundary between them sat
    /// 105 DIPs from each centre, i.e. *inside* both drawn bodies, so a few
    /// DIPs of drift over a chip you were plainly staring at flipped the answer
    /// and restarted the 550 ms dwell.)
    ///
    /// The nearest-centre resolution below is therefore now a belt-and-braces
    /// tiebreak that the invariant makes unreachable; it is kept because it is
    /// the safe way to lose the invariant if the size constants are ever
    /// retuned (first-match-wins would be order-dependent and arbitrary).
    /// </summary>
    public int HitTest(Point p)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < _chips.Count; i++)
        {
            var c = _chips[i];
            var dx = p.X - c.Center.X;
            var dy = p.Y - c.Center.Y;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d > c.Radius + ChipHitSlack) continue;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Builds and shows the magnified panel. Returns false (and shows nothing)
    /// if the candidates can't be laid out — the caller must then fall back to
    /// firing the original selection rather than swallowing it.
    /// </summary>
    public bool Show(IReadOnlyList<GazeRefineCandidate> candidates)
    {
        try
        {
            if (candidates == null || candidates.Count < 2) return false;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return false;

            Close();

            var centers = Layout(candidates);
            if (centers == null) return false;

            _originX = SystemParameters.VirtualScreenLeft;
            _originY = SystemParameters.VirtualScreenTop;

            _canvas = new Canvas { IsHitTestVisible = false, Opacity = 0 };

            // Panel backdrop sized to the laid-out chips.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < centers.Count; i++)
            {
                var (c, r) = centers[i];
                minX = Math.Min(minX, c.X - r);
                minY = Math.Min(minY, c.Y - r);
                maxX = Math.Max(maxX, c.X + r);
                maxY = Math.Max(maxY, c.Y + r);
            }
            _panelBounds = new Rect(
                minX - PanelPadding,
                minY - PanelPadding - CaptionHeight,
                (maxX - minX) + PanelPadding * 2,
                (maxY - minY) + PanelPadding * 2 + CaptionHeight);

            var panel = new Border
            {
                Width = _panelBounds.Width,
                Height = _panelBounds.Height,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromArgb(0xEE, PanelFill.R, PanelFill.G, PanelFill.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, Accent.R, Accent.G, Accent.B)),
                BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(panel, _panelBounds.X - _originX);
            Canvas.SetTop(panel, _panelBounds.Y - _originY);
            _canvas.Children.Add(panel);

            var caption = new TextBlock
            {
                Text = Loc.Get("gaze_refine_prompt"),
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Width = _panelBounds.Width,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(caption, _panelBounds.X - _originX);
            Canvas.SetTop(caption, _panelBounds.Y - _originY + 12);
            _canvas.Children.Add(caption);

            for (int i = 0; i < centers.Count; i++)
            {
                var (c, r) = centers[i];
                var chip = BuildChip(candidates[i], c, r);
                _chips.Add(chip);
            }

            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = _originX,
                Top = _originY,
                Width = Math.Max(1, SystemParameters.VirtualScreenWidth),
                Height = Math.Max(1, SystemParameters.VirtualScreenHeight),
                Content = _canvas,
            };
            _window.SourceInitialized += (_, _) =>
            {
                try
                {
                    _hwnd = new WindowInteropHelper(_window).Handle;
                    MakeClickThrough(_hwnd);
                }
                catch { }
            };
            _window.Show();

            // Quick fade-in. Animating the canvas (not the AllowsTransparency
            // window) keeps this off the layered-window redraw path.
            try
            {
                _canvas.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)) { FillBehavior = FillBehavior.HoldEnd });
            }
            catch { _canvas.Opacity = 1; }

            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeRefineOverlay.Show failed: {Error}", ex.Message);
            Close();
            return false;
        }
    }

    /// <summary>
    /// Lays the candidates out: magnify the constellation about its centroid,
    /// relax any pair that is still closer than <see cref="RequiredSeparation"/>,
    /// then slide the whole cluster back inside the work area. Returns null when
    /// the candidates degenerate.
    ///
    /// Postcondition (what HitTest's invariant rests on): every pair of chips
    /// ends up at least (r_i + ChipHitSlack) + (r_j + ChipHitSlack) apart —
    /// enforced by the relaxation and, if that has not converged, by the ring
    /// fallback, which satisfies it by construction.
    /// </summary>
    private static List<(Point Center, double Radius)>? Layout(IReadOnlyList<GazeRefineCandidate> cands)
    {
        int n = cands.Count;
        if (n < 2) return null;

        var src = new Point[n];
        double cx = 0, cy = 0;
        for (int i = 0; i < n; i++)
        {
            var b = cands[i].SourceBounds;
            if (b.IsEmpty) return null;
            src[i] = new Point(b.X + b.Width / 2.0, b.Y + b.Height / 2.0);
            cx += src[i].X; cy += src[i].Y;
        }
        cx /= n; cy /= n;

        // Smallest gap in the real constellation decides the magnification.
        double dMin = double.MaxValue;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                dMin = Math.Min(dMin, Dist(src[i], src[j]));

        double zoom = dMin < 4 ? ZoomCap : Math.Clamp(MinChipSeparation / dMin, 1.6, ZoomCap);

        // Radii first: the pitch the relaxation has to enforce depends on them,
        // and they depend only on the source rects and the zoom.
        var radii = new double[n];
        for (int i = 0; i < n; i++)
        {
            var b = cands[i].SourceBounds;
            double srcMax = Math.Max(b.Width, b.Height);
            radii[i] = Math.Clamp(srcMax * zoom, MinChipDips, MaxChipDips) / 2.0;
        }

        var pts = new Point[n];
        for (int i = 0; i < n; i++)
            pts[i] = new Point(cx + (src[i].X - cx) * zoom, cy + (src[i].Y - cy) * zoom);

        // Widest pitch any pair here will need. A regular n-gon on a ring of
        // this radius satisfies every pair at once (adjacent vertices sit
        // exactly `widest` apart, the rest further), so it is both the seed for
        // degenerate input and the guaranteed fallback after relaxation.
        double widest = MinChipSeparation;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                widest = Math.Max(widest, RequiredSeparation(radii[i], radii[j]));
        double ringR = widest / (2.0 * Math.Sin(Math.PI / Math.Max(2, n)));

        // Degenerate constellations (two targets stacked on the same spot) can
        // survive the zoom on top of each other. Fan them onto the ring so the
        // relaxation below has something to work with.
        if (dMin < 4) FanOntoRing(pts, n, cx, cy, ringR);

        // Relaxation: push apart anything still under the pitch that pair
        // needs. A handful of passes is plenty for the 2-4 chips we ever show;
        // each pass strictly reduces the shortfall of every violating pair, and
        // the +0.5 overshoot keeps it off the fixed point.
        for (int pass = 0; pass < 24; pass++)
        {
            bool moved = false;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double need = RequiredSeparation(radii[i], radii[j]);
                    double dx = pts[j].X - pts[i].X, dy = pts[j].Y - pts[i].Y;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d >= need) continue;
                    if (d < 0.001) { dx = 1; dy = 0; d = 1; }
                    double push = (need - d) / 2.0 + 0.5;
                    dx /= d; dy /= d;
                    pts[i] = new Point(pts[i].X - dx * push, pts[i].Y - dy * push);
                    pts[j] = new Point(pts[j].X + dx * push, pts[j].Y + dy * push);
                    moved = true;
                }
            }
            if (!moved) break;
        }

        // The relaxation is iterative, so assert rather than assume: if any
        // pair is still short (pathological input, or a future retune of the
        // size constants outrunning the pass count), drop to the ring, which
        // satisfies the invariant by construction. Losing the spatial
        // correspondence with the real constellation is a far smaller cost than
        // shipping two chips that fight over the same gaze point.
        if (!SeparationHolds(pts, radii, n)) FanOntoRing(pts, n, cx, cy, ringR);

        // Keep the whole cluster inside the work area. Translate only — never
        // squash the layout, which would undo the separation we just bought.
        // SystemParameters.WorkArea is the PRIMARY display only, but the
        // candidate rects are in virtual-desktop space. On a multi-monitor rig
        // whose tracking monitor is not the primary, clamping to it drags the
        // whole chip cluster onto the wrong screen - the user stares at a panel
        // that is not where they are looking and cannot reach any chip.
        //
        // UNITS - the trap this block already fell into once. pts/radii are
        // virtual-desktop DIPs. SystemParameters.WorkArea is DIPs. But the
        // calibrated monitor's rect comes from System.Windows.Forms.Screen,
        // which reports PHYSICAL DEVICE PIXELS in a PerMonitorV2 process
        // (app.manifest) - and it arrives packed in a System.Windows.Rect,
        // whose type name reads as DIPs at every call site. Assigning it
        // straight into `wa` is silently correct at 100% scale and 1.5x too
        // large at 150%, where the shift then never fires and chips get laid
        // out past the edge of the monitor - unreachable, on a click-through
        // panel, until the 6 s hard timeout. So: divide by the calibration's
        // own DpiScale, exactly as GazeFocusService.CalOriginDips does. Do not
        // "simplify" that division away.
        //
        // WorkingArea, not Bounds: Bounds would let the panel be clamped under
        // the taskbar.
        var wa = SystemParameters.WorkArea;                  // DIPs (primary only)
        try
        {
            // DeviceName == null is a pre-hotfix calibration whose monitor
            // identity is meaningless - same guard the house pattern uses.
            var mb = App.Webcam?.Calibration?.MonitorBounds;
            var calScreen = mb?.DeviceName != null ? App.Webcam?.GetCalibratedScreen() : null;
            if (calScreen != null)
            {
                var px = calScreen.WorkingArea;              // PHYSICAL device px
                double dpi = mb!.DpiScale is > 0.25 and < 8.0 ? mb.DpiScale : 1.0;
                if (px.Width > 0 && px.Height > 0)
                    wa = new Rect(px.X / dpi, px.Y / dpi,    // -> DIPs
                                  px.Width / dpi, px.Height / dpi);
            }
        }
        catch { }

        // Translate-only can only save a cluster that FITS. Worst case here is
        // 4 max-size chips the user's real targets happened to stack in a line:
        // 3 gaps x 288 + 220 of chip + 98 of chrome = 1182 DIPs on that axis,
        // which overflows a 768-DIP-tall work area. Overflow on a click-through
        // panel means a chip is off-screen and unreachable until the 6 s
        // timeout. The ring is the most compact arrangement that still
        // satisfies the separation invariant (4 chips: 695 x 725 including
        // chrome, which does fit 768), so re-lay onto it rather than shipping
        // an unreachable chip. Costs the spatial correspondence with the real
        // constellation, which is worth far less than reachability.
        //
        // No-op on any display with room for the relaxed layout - i.e. every
        // ordinary case, including the whole of a 1080p/1440p work area.
        if (wa.Width > 0 && wa.Height > 0 && !FitsWorkArea(pts, radii, n, wa))
            FanOntoRing(pts, n, cx, cy, ringR);

        if (wa.Width > 0 && wa.Height > 0)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, pts[i].X - radii[i]);
                minY = Math.Min(minY, pts[i].Y - radii[i]);
                maxX = Math.Max(maxX, pts[i].X + radii[i]);
                maxY = Math.Max(maxY, pts[i].Y + radii[i]);
            }
            double padL = PanelPadding, padT = PanelPadding + CaptionHeight, padR = PanelPadding, padB = PanelPadding;
            double dxShift = 0, dyShift = 0;
            if (minX - padL < wa.Left) dxShift = wa.Left - (minX - padL);
            else if (maxX + padR > wa.Right) dxShift = wa.Right - (maxX + padR);
            if (minY - padT < wa.Top) dyShift = wa.Top - (minY - padT);
            else if (maxY + padB > wa.Bottom) dyShift = wa.Bottom - (maxY + padB);
            if (dxShift != 0 || dyShift != 0)
                for (int i = 0; i < n; i++) pts[i] = new Point(pts[i].X + dxShift, pts[i].Y + dyShift);
        }

        var result = new List<(Point, double)>(n);
        for (int i = 0; i < n; i++) result.Add((pts[i], radii[i]));
        return result;
    }

    private static double Dist(Point a, Point b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Centre-to-centre distance two chips must keep for their HitTest
    /// catchment discs (radius + ChipHitSlack each) to stay disjoint. Both
    /// chips at MinChipDips: (75+34)+(75+34) = 218; both at MaxChipDips:
    /// (110+34)+(110+34) = 288. MinChipSeparation is only a floor.
    /// </summary>
    private static double RequiredSeparation(double ri, double rj)
        => Math.Max(MinChipSeparation, (ri + ChipHitSlack) + (rj + ChipHitSlack));

    /// <summary>
    /// Does the laid-out cluster, plus panel chrome, fit inside
    /// <paramref name="wa"/> (both in desktop DIPs)?
    /// </summary>
    private static bool FitsWorkArea(Point[] pts, double[] radii, int n, Rect wa)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            minX = Math.Min(minX, pts[i].X - radii[i]);
            minY = Math.Min(minY, pts[i].Y - radii[i]);
            maxX = Math.Max(maxX, pts[i].X + radii[i]);
            maxY = Math.Max(maxY, pts[i].Y + radii[i]);
        }
        return (maxX - minX) + PanelPadding * 2 <= wa.Width
            && (maxY - minY) + PanelPadding * 2 + CaptionHeight <= wa.Height;
    }

    /// <summary>Does every pair satisfy <see cref="RequiredSeparation"/>?</summary>
    private static bool SeparationHolds(Point[] pts, double[] radii, int n)
    {
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Dist(pts[i], pts[j]) < RequiredSeparation(radii[i], radii[j]) - 0.001)
                    return false;
        return true;
    }

    /// <summary>
    /// Places the chips on a regular n-gon of radius <paramref name="ringR"/>
    /// about (cx, cy), first vertex at 12 o'clock.
    /// </summary>
    private static void FanOntoRing(Point[] pts, int n, double cx, double cy, double ringR)
    {
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / n;
            pts[i] = new Point(cx + Math.Cos(a) * ringR, cy + Math.Sin(a) * ringR);
        }
    }

    private Chip BuildChip(GazeRefineCandidate cand, Point center, double radius)
    {
        var chip = new Chip { Center = center, Radius = radius };
        double size = radius * 2;
        double lx = center.X - radius - _originX;
        double ly = center.Y - radius - _originY;

        FrameworkElement body;
        if (cand.Kind == GazeRefineKind.Bubble)
        {
            body = new Ellipse
            {
                Width = size,
                Height = size,
                IsHitTestVisible = false,
                Fill = new RadialGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb(0xAA, Accent.R, Accent.G, Accent.B), 0.62),
                        new GradientStop(Color.FromArgb(0x66, 0xB4, 0x69, 0xFF), 1.0),
                    })
                { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.42) },
                Stroke = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1.5,
            };
        }
        else if (cand.Kind == GazeRefineKind.Flash && cand.Preview != null)
        {
            body = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(14),
                IsHitTestVisible = false,
                Background = new ImageBrush(cand.Preview) { Stretch = Stretch.UniformToFill },
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, Accent.R, Accent.G, Accent.B)),
                BorderThickness = new Thickness(2),
            };
        }
        else
        {
            var b = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(14),
                IsHitTestVisible = false,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0xFF, ChipFill.R, ChipFill.G, ChipFill.B),
                    Color.FromArgb(0xFF, 0x3A, 0x2A, 0x55),
                    new Point(0, 0), new Point(1, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, Accent.R, Accent.G, Accent.B)),
                BorderThickness = new Thickness(2),
            };
            b.Child = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(size * 0.5, size * 0.28),
                    new Point(size * 0.72, size * 0.5),
                    new Point(size * 0.5, size * 0.72),
                    new Point(size * 0.28, size * 0.5),
                },
                Fill = new SolidColorBrush(Color.FromArgb(0xCC, Accent.R, Accent.G, Accent.B)),
                IsHitTestVisible = false,
            };
            body = b;
        }

        Canvas.SetLeft(body, lx);
        Canvas.SetTop(body, ly);
        _canvas!.Children.Add(body);
        chip.Body = body;

        // Dwell progress ring, drawn just outside the chip body.
        var ring = new Path
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, Accent.R, Accent.G, Accent.B)),
            StrokeThickness = 6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Canvas.SetLeft(ring, 0);
        Canvas.SetTop(ring, 0);
        _canvas.Children.Add(ring);
        chip.Ring = ring;

        return chip;
    }

    /// <summary>
    /// Draws the dwell ring for one chip; pass -1 to clear every ring. t01 is
    /// dwell progress in [0,1].
    /// </summary>
    public void SetProgress(int index, double t01)
    {
        try
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                var c = _chips[i];
                if (c.Ring == null) continue;
                if (i != index)
                {
                    if (c.Ring.Visibility != Visibility.Collapsed) c.Ring.Visibility = Visibility.Collapsed;
                    c.Progress = 0;
                    if (c.Body != null && c.Body.RenderTransform is ScaleTransform) c.Body.RenderTransform = Transform.Identity;
                    continue;
                }

                var t = Math.Clamp(t01, 0, 0.999);
                c.Progress = t;
                c.Ring.Visibility = t <= 0.001 ? Visibility.Collapsed : Visibility.Visible;
                if (t > 0.001) c.Ring.Data = BuildArc(c, t);

                // Same "it's filling up" language the bubbles use for dwell.
                if (c.Body != null)
                {
                    double s = 1.0 + t * 0.08;
                    c.Body.RenderTransformOrigin = new Point(0.5, 0.5);
                    c.Body.RenderTransform = new ScaleTransform(s, s);
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeRefineOverlay.SetProgress failed: {Error}", ex.Message);
        }
    }

    private Geometry BuildArc(Chip c, double t)
    {
        double r = c.Radius + 12;
        double ox = c.Center.X - _originX;
        double oy = c.Center.Y - _originY;
        double sweep = t * 2 * Math.PI;
        var start = new Point(ox, oy - r);                       // 12 o'clock
        var end = new Point(ox + Math.Sin(sweep) * r, oy - Math.Cos(sweep) * r);

        var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            IsLargeArc = t > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    /// <summary>
    /// Re-asserts topmost. Flash windows and the video attention targets call
    /// SetWindowPos(HWND_TOPMOST) continuously and would otherwise bury the
    /// panel inside the topmost band. Throttled internally, so the caller can
    /// hand this every dwell tick. SWP_NOACTIVATE — never steals focus.
    /// </summary>
    public void KeepOnTop()
    {
        if (_hwnd == IntPtr.Zero) return;
        var now = DateTime.UtcNow;
        if ((now - _lastTopmostAssert).TotalMilliseconds < 200) return;
        _lastTopmostAssert = now;
        try { SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); } catch { }
    }

    public void Close()
    {
        _chips.Clear();
        _panelBounds = Rect.Empty;
        _lastTopmostAssert = DateTime.MinValue;
        _hwnd = IntPtr.Zero;
        if (_window != null)
        {
            var w = _window;
            _window = null;
            _canvas = null;
            try { w.Close(); } catch { }
        }
        _canvas = null;
    }

    public void Dispose() => Close();

    private static void MakeClickThrough(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
