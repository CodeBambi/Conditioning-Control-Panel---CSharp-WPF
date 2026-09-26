using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Compositor;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The big "+3:00" that pops where the time was earned: on the popped bubble, on the flash
    /// that showed, else at the cursor.
    ///
    /// <para><b>Its own window, on purpose.</b> The rail adorner lived inside the panel, so any
    /// overlay (Brain Drain's blur and melt, flash windows, the bubble host) sat over it, and a
    /// panel hidden to the tray had nowhere to draw it. This is a small transparent window,
    /// topmost, click-through, never activated and kept off Alt+Tab
    /// (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW), re-asserted HWND_TOPMOST as it
    /// shows so it lands over whatever went topmost before it.</para>
    ///
    /// <para><b>Never outlives its life.</b> Each pop closes itself when its plan ends, and
    /// <see cref="CloseAll"/> runs from the main window's Closed so an open pop can never hold
    /// <c>OnLastWindowClose</c> (house rule on unowned windows).</para>
    ///
    /// <para>Layout and timing come from <see cref="BookedPopLayout"/>; colour and text from
    /// <see cref="BookedFlashPlan"/>.</para>
    /// </summary>
    internal sealed class ChasterBookedPop : Window
    {
        private static readonly List<ChasterBookedPop> Live = new();
        private static readonly FontFamily Display = new(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka, Segoe UI");
        private static readonly Random Rng = new();

        private readonly BookedPopLayout.PopPlan _plan;
        private readonly Point _originPx;
        private readonly int _slot;
        private readonly long _bornMs;
        private readonly Canvas _stage = new() { IsHitTestVisible = false };
        private readonly Path _figure = new() { IsHitTestVisible = false };
        // The outline is its own path UNDER the fill, so the thick dark stroke frames the glyphs
        // instead of eating half its width out of them.
        private readonly Path _outline = new() { IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly TranslateTransform _lift = new();
        private readonly DropShadowEffect _glow = new() { ShadowDepth = 0, BlurRadius = 22, Opacity = 0.9 };
        // The faded name of what cost (or earned) the time, under the figure. Rides the same
        // rise as the figure but not its scale punch, so it reads as a caption.
        private readonly TextBlock _sourceText = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false };
        private readonly Border _source = new()
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 2, 9, 3),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x1A, 0x0C, 0x1E)),
            Opacity = BookedFlashPlan.SourceOpacity,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        private Color _colour;
        private bool _gone;

        private ChasterBookedPop(Point originPx, int slot, BookedFlashPlan.Plan look, BookedPopLayout.PopPlan plan)
        {
            _plan = plan;
            _originPx = originPx;
            _slot = slot;
            _bornMs = Environment.TickCount64;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = BookedPopLayout.WindowWidthDip;
            Height = BookedPopLayout.WindowHeightDip;
            // Start far off screen; SourceInitialized puts it on the origin in physical px.
            Left = -32000;
            Top = -32000;
            Title = "";

            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_lift);
            _figure.RenderTransform = group;
            _outline.RenderTransform = group;
            _outline.Effect = _glow;
            _stage.Children.Add(_outline);
            _stage.Children.Add(_figure);
            _sourceText.FontFamily = Display;
            _source.Child = _sourceText;
            _source.RenderTransform = _lift;
            _stage.Children.Add(_source);
            Content = _stage;

            Paint(look.Text, look.Colour);
            Label(look.Source);
            SourceInitialized += (_, _) => PlaceNative();
        }

        /// <summary>
        /// Pop a figure at <paramref name="originPx"/> (physical desktop px). Returns the live pop
        /// so a booking inside the coalescing window can re-label it, or null when it could not
        /// be shown (decoration fails quietly).
        /// </summary>
        internal static ChasterBookedPop? Show(Point originPx, BookedFlashPlan.Plan look, BookedPopLayout.PopPlan plan)
        {
            try
            {
                Prune();
                var now = Environment.TickCount64;
                var live = new List<BookedPopLayout.LivePop>(Live.Count);
                foreach (var p in Live) live.Add(new BookedPopLayout.LivePop(p._originPx, p._bornMs, p._plan.TotalMs, p._slot));
                var slot = BookedPopLayout.PickSlot(live, originPx, now);

                var pop = new ChasterBookedPop(originPx, slot, look, plan);
                Live.Add(pop);
                pop.Show();
                pop.Run();
                return pop;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Chaster] booked pop: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>Close every pop now (the main window is going away).</summary>
        internal static void CloseAll()
        {
            foreach (var p in Live.ToArray()) p.Dismiss();
            Live.Clear();
        }

        /// <summary>Same figure, bigger number (a booking inside the coalescing window).</summary>
        internal void Retitle(string text, Color colour, string? source = null)
        {
            if (_gone) return;
            Paint(text, colour);
            if (source != null) Label(source);
            // A small re-punch so the change of number is seen, not just the number.
            if (_plan.ScaleInMs > 0)
            {
                var punch = new DoubleAnimation(1.18, 1.0, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, punch);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, punch);
            }
        }

        internal void Dismiss()
        {
            if (_gone) return;
            _gone = true;
            Live.Remove(this);
            try { Close(); }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] booked pop close: {E}", ex.Message); }
        }

        private static void Prune()
        {
            for (var i = Live.Count - 1; i >= 0; i--)
                if (Live[i]._gone) Live.RemoveAt(i);
        }

        // ---- drawing -------------------------------------------------------

        /// <summary>The figure as outlined geometry: a thick dark stroke under the colour, so the
        /// number reads over a white flash, a pink wash and a blurred desktop alike.</summary>
        private void Paint(string text, Color colour)
        {
            _colour = colour;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Display, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                _plan.FontDip, Brushes.White, dpi > 0 ? dpi : 1.0);
            var geometry = formatted.BuildGeometry(new Point(0, 0));
            if (geometry.CanFreeze) geometry.Freeze();

            _figure.Data = geometry;
            _figure.Fill = new LinearGradientBrush(Lighten(colour, 0.35), colour, 90);
            _outline.Data = geometry;
            _outline.Fill = Brushes.Transparent;
            _outline.Stroke = new SolidColorBrush(Color.FromArgb(0xF0, 0x1A, 0x0C, 0x1E));
            _outline.StrokeThickness = Math.Max(8, _plan.FontDip * 0.2);
            _glow.Color = colour;

            // Centre the glyphs on the anchor point; the transforms scale around it.
            var w = formatted.WidthIncludingTrailingWhitespace;
            var h = formatted.Height;
            var cx = BookedPopLayout.WindowWidthDip / 2;
            var cy = BookedPopLayout.WindowHeightDip * BookedPopLayout.FigureAnchorY;
            Canvas.SetLeft(_figure, cx - w / 2);
            Canvas.SetTop(_figure, cy - h / 2);
            Canvas.SetLeft(_outline, cx - w / 2);
            Canvas.SetTop(_outline, cy - h / 2);
            _scale.CenterX = w / 2;
            _scale.CenterY = h / 2;
        }

        /// <summary>The source badge: the row's short name, or nothing when the row has no copy
        /// (a raw id is never shown to a player).</summary>
        private void Label(string? eventId)
        {
            var name = SourceName(eventId);
            if (name == null) { _source.Visibility = Visibility.Collapsed; return; }
            _sourceText.Text = name;
            _sourceText.Foreground = new SolidColorBrush(Lighten(_colour, 0.55));
            _source.Visibility = Visibility.Visible;
            _source.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _source.DesiredSize;
            var cx = BookedPopLayout.WindowWidthDip / 2;
            var below = Canvas.GetTop(_figure) + (_figure.Data?.Bounds.Bottom ?? 0) + 10;
            Canvas.SetLeft(_source, cx - size.Width / 2);
            Canvas.SetTop(_source, Math.Min(below, BookedPopLayout.WindowHeightDip - size.Height - 4));
        }

        internal static string? SourceName(string? eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            var key = TabMenuCopy.ShortKey(eventId);
            var name = Loc.Get(key);
            return string.IsNullOrWhiteSpace(name) || name == key ? null : name;
        }

        private static Color Lighten(Color c, double t) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

        // ---- motion --------------------------------------------------------

        private void Run()
        {
            var life = TimeSpan.FromMilliseconds(Math.Max(1, _plan.TotalMs));

            if (_plan.ScaleInMs > 0)
            {
                var pop = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(_plan.ScaleInMs))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = _plan.Overshoot } };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

                // The glow flares at birth and settles.
                var flare = new DoubleAnimation(60, 22, TimeSpan.FromMilliseconds(_plan.ScaleInMs * 2));
                _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, flare);
            }

            if (_plan.RiseDip > 0)
            {
                var rise = new DoubleAnimation(0, -_plan.RiseDip, life)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                _lift.BeginAnimation(TranslateTransform.YProperty, rise);
            }

            if (_plan.FadeMs > 0)
            {
                var fade = new DoubleAnimationUsingKeyFrames { Duration = life, FillBehavior = FillBehavior.HoldEnd };
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(life - TimeSpan.FromMilliseconds(Math.Min(_plan.FadeMs, _plan.TotalMs)))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(life)));
                _stage.BeginAnimation(OpacityProperty, fade);
            }

            if (_plan.Particles > 0) Sparks(_plan.Particles);

            // One timer for the end of life, whatever the motion level.
            var end = new System.Windows.Threading.DispatcherTimer { Interval = life };
            end.Tick += (_, _) => { end.Stop(); Dismiss(); };
            end.Start();
        }

        /// <summary>A small ring of sparks thrown out from the figure and fading.</summary>
        private void Sparks(int count)
        {
            var cx = BookedPopLayout.WindowWidthDip / 2;
            var cy = BookedPopLayout.WindowHeightDip * BookedPopLayout.FigureAnchorY;
            for (var i = 0; i < count; i++)
            {
                var angle = (i + Rng.NextDouble() * 0.6) / count * Math.PI * 2;
                var dist = 70 + Rng.NextDouble() * 60;
                var size = 5 + Rng.NextDouble() * 5;
                var ms = 450 + Rng.Next(300);
                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(i % 3 == 0 ? Colors.White : _colour),
                    IsHitTestVisible = false,
                };
                var move = new TranslateTransform();
                dot.RenderTransform = move;
                Canvas.SetLeft(dot, cx - size / 2);
                Canvas.SetTop(dot, cy - size / 2);
                _stage.Children.Insert(0, dot);

                var span = TimeSpan.FromMilliseconds(ms);
                var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(angle) * dist, span) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(angle) * dist * 0.8, span) { EasingFunction = ease });
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, span));
            }
        }

        // ---- native placement ----------------------------------------------

        private void PlaceNative()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED);

                // The monitor under the origin decides the scale. Top-left is kept by a DPI
                // change, and the window's physical size becomes WindowDip * that scale, which is
                // what TopLeftPx centres against.
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)_originPx.X, (int)_originPx.Y));
                var scale = CompositorHostWindow.GetDpiScaleForScreen(screen);
                var b = screen.Bounds;
                var topLeft = BookedPopLayout.TopLeftPx(_originPx, _slot, scale, new Rect(b.Left, b.Top, b.Width, b.Height));
                SetWindowPos(hwnd, HWND_TOPMOST, (int)topLeft.X, (int)topLeft.Y, 0, 0,
                    SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] booked pop place: {E}", ex.Message); }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            // Re-assert on the first frame: an overlay that went topmost between our create and
            // our show must not end up over the number.
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] booked pop topmost: {E}", ex.Message); }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
