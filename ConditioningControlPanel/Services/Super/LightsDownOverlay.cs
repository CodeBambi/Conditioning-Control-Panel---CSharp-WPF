using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super Lights Down: one click-through, non-activating window OWNED by a mandatory-video
    /// window, so it always sits just above that video and closes with it (panic, emergency exit,
    /// skip and natural end all close the owner). It cannot be a compositor layer: the z-order
    /// reconciler pins every compositor host BELOW a playing video (#497), and the video surface
    /// is an airspace HWND (WebView2 or VideoView) nothing in WPF can draw over.
    ///
    /// Retained WPF visuals built once; a frame only copies <see cref="LightsDownFrame"/> values
    /// onto transforms, opacities and gradient stops. No geometry is rebuilt per frame except the
    /// cut-outs around live attention targets, and only when they move.
    /// </summary>
    internal sealed class LightsDownOverlay : Window
    {
        private const int MaxMotes = 48;

        private readonly IntPtr _ownerHwnd;
        private readonly System.Drawing.Rectangle _bounds;
        private readonly double _dpi;
        private readonly double _w, _h;
        private readonly bool _swellVideo;
        private readonly FrameworkElement? _videoContent;
        private readonly Transform? _savedTransform;
        private readonly Point _savedOrigin;
        private ScaleTransform? _videoScale;

        private Rect _pic;
        private readonly Canvas _root = new();
        private readonly Canvas _shade = new();
        private readonly RectangleGeometry _picHole = new();
        private readonly ScaleTransform _holeSwell = new();
        private readonly CombinedGeometry _outside;
        private readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromRgb(3, 1, 9)) };
        private readonly Path[] _aperture = new Path[LightsDownMath.ApertureLayers];
        private readonly ScaleTransform[] _apScale = new ScaleTransform[LightsDownMath.ApertureLayers];
        private readonly RotateTransform _apRotate = new();
        private readonly TranslateTransform _apMove = new();
        private readonly Canvas _rays = new();
        private readonly RotateTransform _raysRotate = new();
        private readonly GradientStop[] _rayStops = new GradientStop[LightsDownMath.Rays];
        private readonly Canvas _frame = new();
        private readonly ScaleTransform _frameSwell = new();
        private readonly Rectangle _glow = new() { RadiusX = 10, RadiusY = 10, StrokeThickness = 3 };
        private readonly SolidColorBrush _glowBrush = new();
        private readonly DropShadowEffect? _glowFx;
        private readonly Rectangle _run = new() { RadiusX = 10, RadiusY = 10, StrokeThickness = 3, Stroke = new SolidColorBrush(Color.FromRgb(255, 235, 250)) };
        private readonly Rectangle _splitA = new() { Fill = new SolidColorBrush(Color.FromRgb(255, 0, 90)), RadiusX = 10, RadiusY = 10 };
        private readonly Rectangle _splitB = new() { Fill = new SolidColorBrush(Color.FromRgb(0, 210, 255)), RadiusX = 10, RadiusY = 10 };
        private readonly Rectangle _streak = new() { Height = 4 };
        private readonly Ellipse[] _motes = new Ellipse[MaxMotes];
        private readonly TranslateTransform[] _moteAt = new TranslateTransform[MaxMotes];
        private readonly SolidColorBrush[] _moteBrush = new SolidColorBrush[MaxMotes];
        private readonly double[] _mx = new double[MaxMotes], _my = new double[MaxMotes], _mvx = new double[MaxMotes], _mvy = new double[MaxMotes], _mAge = new double[MaxMotes], _mLife = new double[MaxMotes];
        private readonly bool[] _mBurst = new bool[MaxMotes];
        private readonly Random _rng = new();
        private readonly List<Rect> _cutRects = new();
        private bool _cutDirty;

        public IntPtr OwnerHwnd => _ownerHwnd;

        /// <summary>An unowned overlay for a screen at the origin, for the offscreen render test.</summary>
        internal static LightsDownOverlay ForTest(int widthPx, int heightPx, double aspect)
            => new(IntPtr.Zero, new System.Drawing.Rectangle(0, 0, widthPx, heightPx), 1, aspect, null, true, 116);

        /// <summary>The drawing root, for the offscreen render test.</summary>
        internal FrameworkElement RootForTest => _root;

        public LightsDownOverlay(IntPtr ownerHwnd, System.Drawing.Rectangle screenBoundsPx, double dpi, double aspect, Window? videoWindow, bool allowGlow, double glowCap)
        {
            _ownerHwnd = ownerHwnd;
            _bounds = screenBoundsPx;
            _dpi = dpi <= 0 ? 1 : dpi;
            _w = screenBoundsPx.Width / _dpi;
            _h = screenBoundsPx.Height / _dpi;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Same placement formula as the video window it rides (VideoService.CreateLibVLCVideoWindow).
            Left = screenBoundsPx.X / _dpi;
            Top = screenBoundsPx.Y / _dpi;
            Width = _w;
            Height = _h;
            Title = "CCP Lights Down";

            // The swell only works where the video is WPF content (the blurred-background vmem path):
            // a WebView2 or VideoView surface is an HwndHost and ignores render transforms.
            if (videoWindow?.Content is FrameworkElement fe && !HasHwndHost(fe))
            {
                _videoContent = fe;
                _savedTransform = fe.RenderTransform;
                _savedOrigin = fe.RenderTransformOrigin;
                _swellVideo = true;
            }

            _outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, _w, _h)), _picHole);
            _picHole.Transform = _holeSwell;

            _dim.Data = _outside;
            _shade.Children.Add(_dim);

            // Rays: a fixed fan from the picture centre, clipped to the room so the picture stays clean.
            double L = Math.Max(_w, _h);
            for (int i = 0; i < LightsDownMath.Rays; i++)
            {
                double an = i * Math.PI * 2 / LightsDownMath.Rays, wd = LightsDownMath.RayHalfWidth;
                var stop = new GradientStop(Colors.Transparent, 0);
                _rayStops[i] = stop;
                var brush = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(Math.Cos(an) * L, Math.Sin(an) * L),
                    GradientStops = { stop, new GradientStop(Colors.Transparent, 1) },
                };
                var fig = new PathFigure(new Point(0, 0), new[]
                {
                    new LineSegment(new Point(Math.Cos(an - wd) * L, Math.Sin(an - wd) * L), false),
                    new LineSegment(new Point(Math.Cos(an + wd) * L, Math.Sin(an + wd) * L), false),
                }, true);
                _rays.Children.Add(new Path { Data = new PathGeometry(new[] { fig }), Fill = brush });
            }
            var raysHost = new Canvas { Clip = _outside };
            _rays.RenderTransform = new TransformGroup { Children = { _raysRotate, new TranslateTransform() } };
            raysHost.Children.Add(_rays);
            _shade.Children.Add(raysHost);

            // Aperture: four soft hexagonal irises. Unit hexagon inside a huge rectangle, even-odd,
            // scaled per layer; one rotation and one centre shared by all four.
            var hex = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var g = hex.Open())
            {
                g.BeginFigure(new Point(-40, -40), true, true);
                g.PolyLineTo(new[] { new Point(40, -40), new Point(40, 40), new Point(-40, 40) }, false, false);
                var v0 = LightsDownMath.HexVertex(0);
                g.BeginFigure(new Point(v0.X, v0.Y), true, true);
                var pts = new Point[5];
                for (int i = 1; i < 6; i++) { var v = LightsDownMath.HexVertex(i); pts[i - 1] = new Point(v.X, v.Y); }
                g.PolyLineTo(pts, false, false);
            }
            hex.Freeze();
            var apBrush = new SolidColorBrush(Color.FromRgb(2, 0, 8));
            apBrush.Freeze();
            for (int i = 0; i < _aperture.Length; i++)
            {
                _apScale[i] = new ScaleTransform();
                _aperture[i] = new Path
                {
                    Data = hex,
                    Fill = apBrush,
                    RenderTransform = new TransformGroup { Children = { _apScale[i], _apRotate, _apMove } },
                };
                _shade.Children.Add(_aperture[i]);
            }

            // Dust motes catching the light.
            for (int i = 0; i < MaxMotes; i++)
            {
                _moteAt[i] = new TranslateTransform();
                _moteBrush[i] = new SolidColorBrush(Color.FromArgb(0, 255, 215, 240));
                _motes[i] = new Ellipse { Width = 2.8, Height = 2.8, Fill = _moteBrush[i], RenderTransform = _moteAt[i], Visibility = Visibility.Collapsed };
                _shade.Children.Add(_motes[i]);
            }
            _root.Children.Add(_shade);

            // Glow and the lock-in run hug the frame; they swell with it.
            // The mockup's glow is the shadow of the whole frame, so with glow allowed the rect is
            // FILLED and the room clip below cuts the fill away, leaving only the halo outside.
            // Without glow (Performance tier) a soft stroke stands in.
            if (allowGlow) _glow.Fill = _glowBrush; else _glow.Stroke = _glowBrush;
            if (allowGlow)
            {
                _glowFx = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 26, Opacity = 1 };
                _glow.Effect = _glowFx;
            }
            _glowCap = glowCap;
            _run.Effect = allowGlow ? new DropShadowEffect { ShadowDepth = 0, BlurRadius = 14, Color = Colors.White, Opacity = 1 } : null;
            var frameHost = new Canvas { Clip = _outside };
            frameHost.Children.Add(_glow);
            _frame.Children.Add(frameHost);
            _frame.Children.Add(_run);
            _frame.RenderTransform = _frameSwell;
            _root.Children.Add(_frame);

            _root.Children.Add(_splitA);
            _root.Children.Add(_splitB);
            var streakBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            streakBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 200, 240), 0));
            streakBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 220, 245), 0.5));
            streakBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 200, 240), 1));
            streakBrush.Freeze();
            _streak.Fill = streakBrush;
            _streak.Width = _w;
            _root.Children.Add(_streak);

            _root.Width = _w;
            _root.Height = _h;
            Content = _root;
            SetAspect(aspect);
            Apply(default, 0);

            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(h, GWL_EXSTYLE);
                SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
            };
            new WindowInteropHelper(this).Owner = ownerHwnd;
            Closed += (_, _) => RestoreVideo();
        }

        private readonly double _glowCap;

        /// <summary>Re-fit the picture once the video's aspect is known.</summary>
        public void SetAspect(double aspect)
        {
            var (x, y, w, h) = LightsDownMath.FitPicture(_w, _h, aspect);
            _pic = new Rect(x, y, w, h);
            _picHole.Rect = _pic;
            _picHole.RadiusX = _picHole.RadiusY = 10;
            double cx = x + w / 2, cy = y + h / 2;
            _holeSwell.CenterX = cx; _holeSwell.CenterY = cy;
            _frameSwell.CenterX = cx; _frameSwell.CenterY = cy;
            ((TranslateTransform)((TransformGroup)_rays.RenderTransform).Children[1]).X = cx;
            ((TranslateTransform)((TransformGroup)_rays.RenderTransform).Children[1]).Y = cy;
            _apMove.X = cx; _apMove.Y = cy;
            foreach (var r in new[] { _glow, _run, _splitA, _splitB })
            {
                r.Width = w; r.Height = h;
                Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
            }
            Canvas.SetLeft(_splitA, x - 4);
            Canvas.SetLeft(_splitB, x + 4);
            Canvas.SetTop(_streak, cy - 2);
            double per = 2 * (w + h);
            // Dash lengths are in stroke widths: a bright run 18% of the perimeter, then a gap.
            _run.StrokeDashArray = new DoubleCollection { per * 0.18 / _run.StrokeThickness, per / _run.StrokeThickness };
        }

        /// <summary>Is this physical-pixel point on the picture (unswollen)?</summary>
        public bool PictureContainsPx(double px, double py)
            => LightsDownMath.Inside((px - _bounds.X) / _dpi, (py - _bounds.Y) / _dpi, _pic.X, _pic.Y, _pic.Width, _pic.Height);

        /// <summary>Cut the live attention targets (desktop DIP rects) out of the dark. Rebuilt only on change.</summary>
        public void SetCutouts(List<Rect> desktopDips)
        {
            bool same = desktopDips.Count == _cutRects.Count;
            for (int i = 0; same && i < desktopDips.Count; i++) same = desktopDips[i] == _cutRects[i];
            if (same && !_cutDirty) return;
            _cutRects.Clear();
            _cutRects.AddRange(desktopDips);
            _cutDirty = false;
            if (_cutRects.Count == 0) { _shade.Clip = null; return; }
            var holes = new GeometryGroup();
            foreach (var r in _cutRects)
            {
                var local = new Rect(r.X - Left, r.Y - Top, r.Width, r.Height);
                local.Inflate(24, 24);
                holes.Children.Add(new RectangleGeometry(local, 12, 12));
            }
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, _w, _h)), holes);
            clip.Freeze();
            _shade.Clip = clip;
        }

        /// <summary>Copy one frame onto the visuals. <paramref name="time"/> drives the ray shimmer.</summary>
        public void Apply(in LightsDownFrame f, double time, double dt = 0, bool particles = false, double moteSpeed = 1)
        {
            double d = f.Depth;
            _dim.Opacity = Math.Clamp(f.DimAlpha, 0, 1);

            for (int i = 0; i < _aperture.Length; i++)
            {
                double r = LightsDownMath.ApertureRadius(_w, _h, Math.Max(_pic.Width, _pic.Height), f.ApertureClose, i);
                _apScale[i].ScaleX = _apScale[i].ScaleY = r;
                _aperture[i].Opacity = f.ApertureAlpha;
            }
            _apRotate.Angle = f.ApertureRotation * 180 / Math.PI;

            var (lr, lg, lb) = LightsDownMath.Hsl(f.Hue, 0.9, 0.7);
            _raysRotate.Angle = f.RayAngle * 180 / Math.PI;
            for (int i = 0; i < _rayStops.Length; i++)
            {
                double a = LightsDownMath.RayAlpha(f.RayAlpha, time, i);
                _rayStops[i].Color = Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), lr, lg, lb);
            }

            double swell = _swellVideo ? f.Swell : 1;
            _frameSwell.ScaleX = _frameSwell.ScaleY = swell;
            _holeSwell.ScaleX = _holeSwell.ScaleY = swell;
            if (_videoContent != null && _swellVideo)
            {
                if (_videoScale == null)
                {
                    _videoScale = new ScaleTransform();
                    _videoContent.RenderTransformOrigin = new Point(0.5, 0.5);
                    _videoContent.RenderTransform = _videoScale;
                }
                if (_videoScale.ScaleX != swell) _videoScale.ScaleX = _videoScale.ScaleY = swell;
            }

            var (gr, gg, gb) = LightsDownMath.Hsl(f.Hue, 0.95, 0.62);
            _glowBrush.Color = Color.FromRgb(gr, gg, gb);
            _glow.Opacity = Math.Clamp(f.GlowAlpha, 0, 1);
            if (_glowFx != null)
            {
                _glowFx.Color = _glowBrush.Color;
                _glowFx.BlurRadius = Math.Min(f.GlowBlur, _glowCap);
            }
            else _glow.StrokeThickness = 3 + 8 * d;

            if (f.LockRun >= 0)
            {
                double per = 2 * (_pic.Width + _pic.Height) / _run.StrokeThickness;
                _run.StrokeDashOffset = -f.LockRun * per;
                _run.Opacity = 1 - f.LockRun * 0.6;
            }
            else _run.Opacity = 0;

            _splitA.Opacity = _splitB.Opacity = f.SplitAlpha * 0.8;
            _streak.Opacity = f.StreakAlpha;

            StepMotes(f, dt, particles, moteSpeed);
        }

        private void StepMotes(in LightsDownFrame f, double dt, bool particles, double speed)
        {
            if (!particles)
            {
                for (int i = 0; i < MaxMotes; i++) if (_mLife[i] > 0) { _mLife[i] = 0; _motes[i].Visibility = Visibility.Collapsed; }
                return;
            }
            int spawn = LightsDownMath.MotesToSpawn(dt, f.Depth, _rng.NextDouble());
            for (int k = 0; k < spawn; k++)
                Spawn(_pic.X - _pic.Width * 0.3 + _rng.NextDouble() * _pic.Width * 1.6,
                      _pic.Y - _pic.Height * 0.3 + _rng.NextDouble() * _pic.Height * 1.6,
                      0, -(4 + _rng.NextDouble() * 6), LightsDownMath.MoteLife, false);
            if (f.LockInFired)
            {
                double cx = _pic.X + _pic.Width / 2;
                for (int k = 0; k < 14; k++) Burst(cx, _pic.Y);
                for (int k = 0; k < 14; k++) Burst(cx, _pic.Y + _pic.Height);
            }
            for (int i = 0; i < MaxMotes; i++)
            {
                if (_mLife[i] <= 0) continue;
                _mAge[i] += dt;
                if (_mAge[i] >= _mLife[i]) { _mLife[i] = 0; _motes[i].Visibility = Visibility.Collapsed; continue; }
                _mx[i] += _mvx[i] * dt * speed;
                _my[i] += _mvy[i] * dt * speed;
                if (_mBurst[i]) { _mvx[i] *= 1 - Math.Min(1, dt * 2.5); _mvy[i] *= 1 - Math.Min(1, dt * 2.5); }
                double a = _mBurst[i] ? 1 - _mAge[i] / _mLife[i] : LightsDownMath.MoteAlpha(_mAge[i], f.Depth);
                var c = _moteBrush[i].Color;
                _moteBrush[i].Color = Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);
                _moteAt[i].X = _mx[i];
                _moteAt[i].Y = _my[i];
            }
        }

        private void Burst(double x, double y)
        {
            double an = _rng.NextDouble() * Math.PI * 2, sp = 40 + _rng.NextDouble() * 80;
            Spawn(x, y, Math.Cos(an) * sp, Math.Sin(an) * sp, 0.8, true);
        }

        private void Spawn(double x, double y, double vx, double vy, double life, bool burst)
        {
            for (int i = 0; i < MaxMotes; i++)
            {
                if (_mLife[i] > 0) continue;
                _mx[i] = x; _my[i] = y; _mvx[i] = vx; _mvy[i] = vy; _mAge[i] = 0; _mLife[i] = life; _mBurst[i] = burst;
                _moteBrush[i].Color = burst ? (y <= _pic.Y + 1 ? Color.FromArgb(255, 255, 208, 240) : Color.FromArgb(255, 208, 240, 255)) : Color.FromArgb(0, 255, 215, 240);
                _motes[i].Visibility = Visibility.Visible;
                return;
            }
        }

        /// <summary>Hand the video its own transform back. Safe to call more than once.</summary>
        public void RestoreVideo()
        {
            if (_videoContent == null || _videoScale == null) return;
            _videoScale = null;
            try
            {
                _videoContent.RenderTransform = _savedTransform ?? Transform.Identity;
                _videoContent.RenderTransformOrigin = _savedOrigin;
            }
            catch { }
        }

        private static bool HasHwndHost(DependencyObject root)
        {
            if (root is HwndHost) return true;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
                if (HasHwndHost(VisualTreeHelper.GetChild(root, i))) return true;
            return false;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
