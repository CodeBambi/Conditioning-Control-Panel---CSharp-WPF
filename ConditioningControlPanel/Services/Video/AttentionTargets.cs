using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.Video.Browser;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// One live attention-check target, whatever it happens to be made of. VideoService's
    /// <c>_targets</c> list is the authority on which checks are outstanding - it drives the
    /// pass/fail tally AND <c>EvaluateGraceRequest(attentionTargetLive:)</c>, which refuses a grace
    /// pause while a target is up - so every representation must add/remove itself through exactly
    /// the same bookkeeping. A target counts as live from spawn until click, timeout or teardown.
    ///
    /// Three implementations, chosen per SCREEN at spawn time (see VideoService.CreateAttentionTarget):
    ///   * <see cref="BrowserAttentionTarget"/> - a DOM element inside the browser-engine player page.
    ///   * <see cref="InWindowAttentionTarget"/> - a WPF element inside a LibVLC video window, which
    ///     only works on the vmem/blurred-background path (a VideoView is an HwndHost and airspace
    ///     would hide anything WPF puts over it).
    ///   * <see cref="FloatingText"/> - the original separate topmost window, still the only option
    ///     over a VideoView surface, over the MediaElement fallback, and on a monitor that has no
    ///     video window at all (3+ monitors without FillAllMonitorsWithVideo).
    /// </summary>
    internal interface IAttentionTarget
    {
        /// <summary>Idempotent "the user got this one": pop sound, onHit callback, fade. Shared by the
        /// mouse click, the gaze dwell and the toy-button path.</summary>
        void Hit();

        /// <summary>Remove immediately, no fade, no callback. Idempotent.</summary>
        void Destroy();

        /// <summary>Freeze / unfreeze for a grace pause (#735): motion stops and clicks stop
        /// registering, but the target stays on screen and keeps its place in <c>_targets</c>. The
        /// expiry countdown is frozen by VideoService's shared clock, not here.</summary>
        void SetPaused(bool paused);

        /// <summary>Re-assert z-order after something raised itself over the target. Only the
        /// separate-window representation has anything to do here.</summary>
        void BringToFront();

        /// <summary>Current bounds in WPF DIPs for gaze hit-testing, or <see cref="Rect.Empty"/> when
        /// the target is dead or its position is unknown. Matches the coordinate space
        /// WebcamTrackingService.OnGazeMove emits.</summary>
        Rect GetGazeBounds();
    }

    /// <summary>
    /// One spawn's pending timeout. Due at an <c>_startTime</c>-relative second rather than a
    /// wall-clock deadline, which is what lets a grace pause hold it: ResumeFromGrace slides
    /// <c>_startTime</c> forward by the paused duration, so the target gets back exactly the
    /// lifespan it had left instead of expiring behind the Resume card.
    /// </summary>
    internal sealed class AttentionSpawnExpiry
    {
        public double DueElapsed { get; init; }
        /// <summary>Every monitor's copy of this spawn - they live and die together.</summary>
        public List<IAttentionTarget> Targets { get; init; } = new();
        /// <summary>Drops the spawn's toy-button subscription. Idempotent.</summary>
        public Action? Unhook { get; init; }
    }

    /// <summary>Attention-target styling read from settings, resolved once per spawn so the three
    /// representations cannot drift apart.</summary>
    internal readonly struct AttentionStyle
    {
        public Color Color1 { get; init; }
        public Color Color2 { get; init; }
        public Color TextColor { get; init; }
        public Color BorderColor { get; init; }
        /// <summary>No background, no border, no padding - just the outlined glyphs.</summary>
        public bool Floating { get; init; }
        public bool ShowBorder { get; init; }
        public string Font { get; init; }
    }

    /// <summary>
    /// The look of an attention target, in one place. The WPF builders below and the CSS in
    /// <c>Resources/web/player/player.css</c> are the two renderings of it; the numbers here
    /// (7.5px outline, 20/10 padding, 20px corner radius, 3px border, 150x60 minimum) are mirrored
    /// there deliberately.
    /// </summary>
    internal static class AttentionTargetVisual
    {
        /// <summary>2mm at 96 DPI.</summary>
        internal const double OutlineThickness = 7.5;
        internal const double MinWidth = 150;
        internal const double MinHeight = 60;

        /// <summary>DIPs per second. The old per-window bounce moved 3.0 DIP per 16 ms tick; keeping
        /// the speed identical is what makes the in-window and DOM targets feel like the same game.</summary>
        internal const double SpeedPerSecond = 187.5;

        /// <summary>Fraction of the play area kept clear on each edge, capped in DIPs.</summary>
        internal static double MarginFor(double extent, double cap) => Math.Min(cap, extent * 0.08);

        internal static AttentionStyle ReadStyle()
        {
            var settings = App.Settings?.Current;
            Color c1, c2, text, border;
            try
            {
                c1 = (Color)ColorConverter.ConvertFromString(settings!.AttentionColor1);
                c2 = (Color)ColorConverter.ConvertFromString(settings.AttentionColor2);
                text = (Color)ColorConverter.ConvertFromString(settings.AttentionTextColor);
                border = (Color)ColorConverter.ConvertFromString(settings.AttentionBorderColor);
            }
            catch
            {
                // Fallback to bright fluo pink if colors invalid
                c1 = Color.FromRgb(255, 20, 147);   // DeepPink
                c2 = Color.FromRgb(255, 105, 180);  // HotPink
                text = Color.FromRgb(255, 20, 147);
                border = Color.FromRgb(255, 20, 147);
            }
            return new AttentionStyle
            {
                Color1 = c1,
                Color2 = c2,
                TextColor = text,
                BorderColor = border,
                Floating = settings?.AttentionFloatingText == true,
                ShowBorder = settings?.AttentionShowBorder == true,
                Font = string.IsNullOrWhiteSpace(settings?.AttentionFont) ? "Segoe UI" : settings!.AttentionFont,
            };
        }

        /// <summary>"#RRGGBB" for the page protocol.</summary>
        internal static string ToCss(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        /// <summary>
        /// The target's WPF content: an invisible-but-hit-testable zone under a (optionally
        /// gradient-filled) border holding black-outlined text. <paramref name="width"/> /
        /// <paramref name="height"/> come back as the size the caller must give it - the text is
        /// measured as geometry, so the caller cannot work it out on its own.
        /// </summary>
        internal static FrameworkElement Build(string text, int size, in AttentionStyle style,
            out double width, out double height)
        {
            var border = new Border
            {
                Background = style.Floating
                    ? Brushes.Transparent
                    : new LinearGradientBrush(style.Color1, style.Color2, 90),
                CornerRadius = style.Floating ? new CornerRadius(0) : new CornerRadius(20),
                BorderBrush = (style.ShowBorder && !style.Floating)
                    ? new SolidColorBrush(style.BorderColor)
                    : Brushes.Transparent,
                BorderThickness = (style.ShowBorder && !style.Floating)
                    ? new Thickness(3)
                    : new Thickness(0),
                Padding = style.Floating ? new Thickness(0) : new Thickness(20, 10, 20, 10),
                Effect = style.Floating ? null : new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 15,
                    ShadowDepth = 5,
                    Opacity = 0.6
                },
                Cursor = Cursors.Hand
            };

            // Outlined text via geometry - a crisp black stroke behind the fill, which no WPF text
            // decoration gives you.
            var fontFamily = new FontFamily($"{style.Font}, Segoe UI, Arial");
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                size,
                Brushes.White, // Placeholder, the geometry below carries the real paint
                PixelsPerDip());
            formattedText.TextAlignment = TextAlignment.Center;
            formattedText.LineHeight = size * 0.95;

            var textGeometry = formattedText.BuildGeometry(new System.Windows.Point(0, 0));

            // Offset the geometry so the stroke cannot be clipped by the container.
            var bounds = textGeometry.Bounds;
            var transformedGeometry = textGeometry.Clone();
            transformedGeometry.Transform = new TranslateTransform(
                -bounds.X + OutlineThickness, -bounds.Y + OutlineThickness);

            var outlinePath = new System.Windows.Shapes.Path
            {
                Data = transformedGeometry,
                Stroke = Brushes.Black,
                StrokeThickness = OutlineThickness,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent
            };
            var fillPath = new System.Windows.Shapes.Path
            {
                Data = transformedGeometry,
                Fill = new SolidColorBrush(style.TextColor),
                Stroke = Brushes.Transparent
            };

            var textContainer = new Grid
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            textContainer.Children.Add(outlinePath);
            textContainer.Children.Add(fillPath);
            border.Child = textContainer;

            width = Math.Max(bounds.Width + OutlineThickness * 2 + 60, MinWidth);
            height = Math.Max(bounds.Height + OutlineThickness * 2 + 40, MinHeight);

            // The hit zone makes the transparent pixels (the inside of an "O", the gap between two
            // words) count as clicks, which is the whole target's worth of hit area the user aims at.
            var container = new Grid();
            container.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                IsHitTestVisible = true
            });
            container.Children.Add(border);
            return container;
        }

        private static double PixelsPerDip()
        {
            try
            {
                if (Application.Current?.MainWindow is Visual v) return VisualTreeHelper.GetDpi(v).PixelsPerDip;
            }
            catch { /* no main window yet - 1.0 is the right guess */ }
            return 1.0;
        }

        /// <summary>
        /// Formats trigger text for display:
        /// - 2 words: stack vertically (one per line)
        /// - 4+ words: 2 lines with words split evenly
        /// - 1 word or 3 words: keep as-is
        /// </summary>
        internal static string FormatTriggerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 2)
            {
                // 2 words: stack vertically
                return $"{words[0]}\n{words[1]}";
            }
            else if (words.Length >= 4)
            {
                // 4+ words: split into 2 lines
                int mid = words.Length / 2;
                var line1 = string.Join(" ", words.Take(mid));
                var line2 = string.Join(" ", words.Skip(mid));
                return $"{line1}\n{line2}";
            }

            // 1 or 3 words: keep as-is
            return text;
        }

        internal static void PlayPopSound()
        {
            try
            {
                var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var chosenPop = popFiles[Random.Shared.Next(popFiles.Length)];
                var popPath = Path.Combine(soundsPath, chosenPop);

                if (File.Exists(popPath))
                {
                    // Apply master volume to attention target pop sound
                    var masterVolume = App.Settings?.Current?.MasterVolume ?? 100;
                    App.Audio?.PlayOneShot(popPath, 0.6f * (masterVolume / 100f), "target-pop");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to start pop sound: {Error}", ex.Message);
            }
        }
    }

    /// <summary>
    /// The in-window attention plane for ONE LibVLC video window: a transparent Canvas sitting above
    /// the video surface and the click overlay in the window's root Grid.
    ///
    /// Only ever created on the vmem/blurred-background render path. The VideoView path is an
    /// HwndHost, and airspace means a native child window paints OVER every WPF element in the same
    /// window - a target added there would be invisible and unclickable.
    ///
    /// Hit-testing is done by hand from the window's PreviewMouseDown rather than by the routed
    /// event system: that handler tunnels from the window and swallows the click (it exists to stop
    /// the video raising itself), so it has to offer the press to this layer first or nothing inside
    /// the window could ever be clicked.
    /// </summary>
    internal sealed class InWindowAttentionLayer : Canvas
    {
        private readonly List<InWindowAttentionTarget> _live = new();

        public InWindowAttentionLayer(Window window, Screen screen)
        {
            Window = window;
            Screen = screen;
            Background = null;
            ClipToBounds = true;
            IsHitTestVisible = false;   // hit-testing is manual - see TryHit
        }

        public Window Window { get; }
        public Screen Screen { get; }

        public bool IsOn(Screen screen)
            => screen != null && string.Equals(Screen.DeviceName, screen.DeviceName, StringComparison.Ordinal);

        internal void Attach(InWindowAttentionTarget target, UIElement root)
        {
            Children.Add(root);
            _live.Add(target);
        }

        internal void Detach(InWindowAttentionTarget target, UIElement root)
        {
            Children.Remove(root);
            _live.Remove(target);
        }

        /// <summary>Topmost-first: the newest target wins an overlap, same as the old stack of
        /// topmost windows did.</summary>
        public bool TryHit(System.Windows.Point p)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var t = _live[i];
                if (!t.Contains(p)) continue;
                t.Hit();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// An attention target rendered INSIDE a LibVLC video window (vmem path only).
    ///
    /// The separate-window predecessor moved itself with a 16 ms DispatcherTimer driving
    /// <c>Window.Left/Top</c> - one SetWindowPos per frame per target per monitor, which is what made
    /// the bounce visibly stutter. This one only writes a <see cref="TranslateTransform"/> from
    /// CompositionTarget.Rendering, so the motion is frame-synced and costs no layout pass at all.
    /// </summary>
    internal sealed class InWindowAttentionTarget : IAttentionTarget
    {
        private readonly InWindowAttentionLayer _layer;
        private readonly FrameworkElement _root;
        private readonly TranslateTransform _move = new();
        private readonly Action _onHit;

        private readonly double _w, _h;
        private double _x, _y, _vx, _vy;
        private double _minX, _minY, _maxX, _maxY;
        private TimeSpan _lastRender = TimeSpan.MinValue;
        private bool _rendering;
        private bool _dead;
        private bool _clicked;
        private bool _paused;

        public InWindowAttentionTarget(InWindowAttentionLayer layer, string text, int size,
            in AttentionStyle style, Action onHit)
        {
            _layer = layer;
            _onHit = onHit;
            size = Math.Max(40, size);

            _root = AttentionTargetVisual.Build(AttentionTargetVisual.FormatTriggerText(text), size, style,
                out _w, out _h);
            _root.Width = _w;
            _root.Height = _h;
            _root.RenderTransform = _move;

            // The window is already shown at full screen bounds when a target spawns, so the layer
            // has a real size; the window fallback covers a spawn that races the first layout pass.
            double areaW = _layer.ActualWidth > 0 ? _layer.ActualWidth : _layer.Window.ActualWidth;
            double areaH = _layer.ActualHeight > 0 ? _layer.ActualHeight : _layer.Window.ActualHeight;
            if (areaW <= 0) areaW = _w * 3;
            if (areaH <= 0) areaH = _h * 3;

            var marginX = AttentionTargetVisual.MarginFor(areaW, 150);
            var marginY = AttentionTargetVisual.MarginFor(areaH, 100);
            _minX = marginX;
            _minY = marginY;
            _maxX = Math.Max(_minX + _w, areaW - marginX);
            _maxY = Math.Max(_minY + _h, areaH - marginY);

            _x = _minX + Random.Shared.NextDouble() * Math.Max(0, (_maxX - _w) - _minX);
            _y = _minY + Random.Shared.NextDouble() * Math.Max(0, (_maxY - _h) - _minY);
            _x = Math.Clamp(_x, _minX, Math.Max(_minX, _maxX - _w));
            _y = Math.Clamp(_y, _minY, Math.Max(_minY, _maxY - _h));
            _move.X = _x;
            _move.Y = _y;

            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            _vx = Math.Cos(angle) * AttentionTargetVisual.SpeedPerSecond;
            _vy = Math.Sin(angle) * AttentionTargetVisual.SpeedPerSecond;

            _layer.Attach(this, _root);
            CompositionTarget.Rendering += OnRendering;
            _rendering = true;
        }

        /// <summary>Layer-relative bounds, for the manual hit test in <see cref="InWindowAttentionLayer"/>.
        /// A paused target is not clickable - the Resume card owns the screen.</summary>
        public bool Contains(System.Windows.Point p)
            => !_dead && !_clicked && !_paused && p.X >= _x && p.X <= _x + _w && p.Y >= _y && p.Y <= _y + _h;

        public void SetPaused(bool paused)
        {
            if (_dead || _clicked || _paused == paused) return;
            _paused = paused;
            if (paused) { StopRendering(); return; }
            // MinValue rather than "now": the first frame after the pause must step by one frame's
            // worth, not by the whole 60s the pause could have lasted.
            _lastRender = TimeSpan.MinValue;
            if (!_rendering)
            {
                CompositionTarget.Rendering += OnRendering;
                _rendering = true;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (_dead) return;
            try
            {
                // Rendering can fire more than once for the same frame; RenderingTime is the only
                // reliable clock here (a stopwatch would double-step on those repeats).
                var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
                if (now == _lastRender) return;
                double dt = _lastRender == TimeSpan.MinValue ? 1.0 / 60.0 : (now - _lastRender).TotalSeconds;
                _lastRender = now;
                if (dt <= 0 || dt > 0.25) dt = 1.0 / 60.0;   // a dropped/stalled frame must not teleport it

                _x += _vx * dt;
                _y += _vy * dt;
                if (_x < _minX) { _x = _minX; _vx = Math.Abs(_vx); }
                if (_x + _w > _maxX) { _x = _maxX - _w; _vx = -Math.Abs(_vx); }
                if (_y < _minY) { _y = _minY; _vy = Math.Abs(_vy); }
                if (_y + _h > _maxY) { _y = _maxY - _h; _vy = -Math.Abs(_vy); }
                _move.X = _x;
                _move.Y = _y;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("InWindowAttentionTarget: motion tick failed: {Error}", ex.Message);
                StopRendering();
            }
        }

        private void StopRendering()
        {
            if (!_rendering) return;
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        public void Hit()
        {
            // _paused: a gaze dwell or a toy-button press must not score while the video is held
            // behind the Resume card either.
            if (_clicked || _dead || _paused) return;
            _clicked = true;
            App.Logger?.Information("ATTENTION: Target clicked");
            AttentionTargetVisual.PlayPopSound();
            try { _onHit?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("InWindowAttentionTarget.Hit: onHit callback threw: {Error}", ex.Message); }
            FadeOut();
        }

        private void FadeOut()
        {
            StopRendering();
            try
            {
                var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (_, _) => Destroy();
                _root.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("InWindowAttentionTarget: fade failed: {Error}", ex.Message);
                Destroy();
            }
        }

        public void Destroy()
        {
            if (_dead) return;
            _dead = true;
            StopRendering();
            try { _layer.Detach(this, _root); }
            catch (Exception ex) { App.Logger?.Debug("InWindowAttentionTarget: detach failed: {Error}", ex.Message); }
        }

        /// <summary>Nothing to do: the target IS the top layer of the video window it lives in.</summary>
        public void BringToFront() { }

        public Rect GetGazeBounds()
        {
            if (_dead) return Rect.Empty;
            try { return new Rect(_layer.Window.Left + _x, _layer.Window.Top + _y, _w, _h); }
            catch { return Rect.Empty; }
        }
    }

    /// <summary>
    /// An attention target rendered as a DOM element inside the browser engine's player page.
    ///
    /// C# still owns everything that matters - the text (localization and the mod trigger pool stay
    /// C#-side), the style, the spawn/expire schedule, the hit bookkeeping - and the page owns only
    /// the paint and the bounce, which it runs on transform/opacity so the compositor carries it.
    ///
    /// The target is live from the <c>attentionShow</c> post until click, timeout or teardown, and
    /// the owning VideoService adds/removes it from <c>_targets</c> on exactly the same beats as
    /// every other representation.
    /// </summary>
    internal sealed class BrowserAttentionTarget : IAttentionTarget
    {
        private static int _seq;

        private readonly BrowserVideoWindow _window;
        private readonly Action _onHit;
        private readonly bool _reportMotion;
        private Rect _bounds = Rect.Empty;
        private bool _dead;
        private bool _clicked;
        private bool _paused;

        public string Id { get; }

        public BrowserAttentionTarget(BrowserVideoWindow window, string text, int size,
            in AttentionStyle style, Action onHit)
        {
            _window = window;
            _onHit = onHit;
            Id = "at" + System.Threading.Interlocked.Increment(ref _seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            size = Math.Max(40, size);

            // Only worth the 10 Hz motion reports when something actually consumes the bounds.
            _reportMotion = App.Settings?.Current?.VideoGazeClickEnabled == true;

            var angle = Random.Shared.NextDouble() * Math.PI * 2;

            // Queued until the page's `ready` handshake, so posting before the CoreWebView2 exists
            // is safe - a target spawned during a cold browser start still lands.
            _window.Post(new
            {
                type = "attentionShow",
                id = Id,
                text = AttentionTargetVisual.FormatTriggerText(text),
                size,
                font = style.Font,
                color1 = AttentionTargetVisual.ToCss(style.Color1),
                color2 = AttentionTargetVisual.ToCss(style.Color2),
                textColor = AttentionTargetVisual.ToCss(style.TextColor),
                borderColor = AttentionTargetVisual.ToCss(style.BorderColor),
                floating = style.Floating,
                showBorder = style.ShowBorder,
                // Fractions of the free spawn range, not of the viewport: the page is the only side
                // that knows how big the element measured, so it resolves them against its own bounds.
                xPct = Random.Shared.NextDouble(),
                yPct = Random.Shared.NextDouble(),
                vx = Math.Cos(angle) * AttentionTargetVisual.SpeedPerSecond,
                vy = Math.Sin(angle) * AttentionTargetVisual.SpeedPerSecond,
                reportMotion = _reportMotion,
            });
        }

        /// <summary>Position report from the page, in viewport fractions. Gaze is the only consumer,
        /// so this is a plain field write - no event, nothing re-entrant.</summary>
        internal void UpdateBounds(double xPct, double yPct, double wPct, double hPct)
        {
            if (_dead) return;
            try
            {
                // The window covers the monitor exactly, so viewport fractions map straight onto its
                // DIP rectangle - the same space FloatingText reported and GazeFocusService expects.
                double w = _window.Width, h = _window.Height;
                if (w <= 0 || h <= 0) return;
                _bounds = new Rect(_window.Left + xPct * w, _window.Top + yPct * h, wPct * w, hPct * h);
            }
            catch { _bounds = Rect.Empty; }
        }

        /// <summary>
        /// No message of its own: the page freezes and unfreezes its targets from the SAME
        /// <c>pause</c>/<c>resume</c> the grace pause already sends to hold the video, so the two can
        /// never disagree. The flag here is only the second lock on a click that raced the pause.
        /// </summary>
        public void SetPaused(bool paused) => _paused = paused;

        public void Hit()
        {
            if (_clicked || _dead || _paused) return;
            _clicked = true;
            App.Logger?.Information("ATTENTION: Target clicked");
            AttentionTargetVisual.PlayPopSound();
            try { _onHit?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("BrowserAttentionTarget.Hit: onHit callback threw: {Error}", ex.Message); }
            // The page fades it out and drops it; from here on it is gone as far as C# is concerned.
            Post(new { type = "attentionHide", id = Id, fade = true });
            _dead = true;
        }

        public void Destroy()
        {
            if (_dead) return;
            _dead = true;
            Post(new { type = "attentionHide", id = Id, fade = false });
        }

        /// <summary>Nothing to do: the element is inside the video surface it would be raised over.</summary>
        public void BringToFront() { }

        public Rect GetGazeBounds() => _dead ? Rect.Empty : _bounds;

        private void Post(object msg)
        {
            try { _window.Post(msg); }
            catch (Exception ex) { App.Logger?.Debug("BrowserAttentionTarget: post failed: {Error}", ex.Message); }
        }
    }
}
