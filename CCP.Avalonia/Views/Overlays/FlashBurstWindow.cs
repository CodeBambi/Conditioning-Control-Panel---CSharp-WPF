using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Flash;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The part of a popped flash that leaves its own window: WPF FlashLayer.DrawShards (a
    /// shattered flash, the pieces falling across the monitor) and DrawSparks (Pop's spray of pink
    /// sparks around the box). A click-through, override-redirect overlay drawn in WORLD px (the
    /// shared host's space, where FlashShatter / FlashExit do their maths); it steps itself at
    /// ~60 fps, closes the moment the effect is done, and never outlives a CloseAll (panic).
    /// </summary>
    internal sealed class FlashBurstWindow : Window
    {
        /// <summary>Every burst window up right now; <see cref="FlashOverlay.CloseAll"/> closes them.</summary>
        internal static readonly List<FlashBurstWindow> Open = new();

        /// <summary>A shard break or a spark spray never runs longer than this, whatever its state says.</summary>
        private static readonly TimeSpan SafetyLife = TimeSpan.FromSeconds(2);

        private readonly BurstCanvas _canvas;
        private readonly PixelRect _bounds;
        private readonly Func<double, bool> _step;
        private Action<TimeSpan>? _frame;
        private Action? _firstFrame;
        private bool _closed;

        internal PixelRect WorldBounds => _bounds;

        /// <param name="bounds">Where the window sits, world (physical) px.</param>
        /// <param name="step">Advance by dt seconds; true while there is still something to draw.</param>
        /// <param name="draw">Paint one frame in world px (the canvas maps world to its own DIPs).</param>
        private FlashBurstWindow(PixelRect bounds, Func<double, bool> step, Action<DrawingContext> draw, IDisposable? owned)
        {
            _bounds = bounds;
            _step = step;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            _canvas = new BurstCanvas(this, draw);
            Content = _canvas;
            Closed += (_, _) =>
            {
                _closed = true;
                StopFrames();
                Open.Remove(this);
                owned?.Dispose();
                FireFirstFrame();   // a burst that dies before it paints must not strand the flash it replaces
            };
        }

        /// <summary>One frame of the effect (tests drive it; the timer calls it).</summary>
        internal bool Tick(double dt)
        {
            if (_closed) return false;
            var alive = _step(dt);
            _canvas.InvalidateVisual();
            if (!alive) Close();
            return alive;
        }

        /// <summary>
        /// Place, show and start. False (window closed) when the platform refuses an overlay.
        ///
        /// <para>Stepped off the window's own animation frame (one tick per composed frame), never
        /// a 16 ms DispatcherTimer: on Windows that timer fires on the 15.6 ms system tick, so it
        /// lands 15.6 or 31.2 ms apart and the pieces visibly skip frames.</para>
        ///
        /// <para>The hand-off given at creation runs once the burst has been composed at least once
        /// (the second animation frame after Show), or when the burst closes first, or after
        /// <see cref="HandOffLimit"/> at the latest. The flash it replaces stays on screen until
        /// then: closing it at once left a blank gap on Windows while the new overlay window
        /// painted its first frame (owner: "it flickers, then reappears and breaks").</para>
        /// </summary>
        private bool Launch()
        {
            if (!X11Overlay.SetClickThrough(this, true) || !X11Overlay.SetOverrideRedirect(this, _bounds))
            {
                _firstFrame = null;   // refused: the caller keeps its own exit
                Close();
                return false;
            }
            Open.Add(this);
            Show();
            TimeSpan? started = null, last = null;
            var frames = 0;
            _frame = now =>
            {
                if (_closed) return;
                started ??= now;
                var dt = last is { } l ? Math.Clamp((now - l).TotalSeconds, 0, 0.1) : 0;
                last = now;
                if (++frames >= 2) FireFirstFrame();
                if (now - started.Value > SafetyLife) { Close(); return; }
                if (dt > 0) Tick(dt);
            };
            global::ConditioningControlPanel.Avalonia.Controls.Fx.TopLevelFrameSource.For(this).Subscribe(_frame);
            if (_firstFrame != null) DispatcherTimer.RunOnce(FireFirstFrame, HandOffLimit);
            return true;
        }

        /// <summary>The longest a flash waits for its burst to paint before it goes anyway.</summary>
        internal static readonly TimeSpan HandOffLimit = TimeSpan.FromMilliseconds(250);

        /// <summary>The hand-off (tests call it the way the second frame does).</summary>
        internal void FireFirstFrame()
        {
            var cb = _firstFrame;
            _firstFrame = null;
            try { cb?.Invoke(); }
            catch (Exception ex) { Log.Debug("Flash burst hand-off failed: {E}", ex.Message); }
        }

        private void StopFrames()
        {
            if (_frame == null) return;
            global::ConditioningControlPanel.Avalonia.Controls.Fx.TopLevelFrameSource.For(this).Unsubscribe(_frame);
            _frame = null;
        }

        /// <summary>Close every burst window (panic, stop, shell close).</summary>
        internal static void CloseAll()
        {
            foreach (var w in Open.ToArray()) w.Close();
        }

        // ---- shatter ----

        /// <summary>
        /// WPF BeginShatter + DrawShards: the break of <paramref name="snapshot"/> (owned, freed on
        /// close) on the monitor <paramref name="monitor"/>. The pieces are sub-rects of the
        /// snapshot blitted at their offsets, each turned about its own centre and faded together;
        /// a pendulum break rotates about the pivot by the angle frozen at the dismiss.
        /// </summary>
        internal static FlashBurstWindow? CreateShatter(FlashShatterState state, Bitmap snapshot, PixelRect monitor, Action? shown = null)
        {
            var w = new FlashBurstWindow(monitor,
                dt => { FlashShatter.Step(state, dt); return !state.Done; },
                dc => DrawShards(dc, state, snapshot),
                snapshot) { _firstFrame = shown };
            return w;
        }

        /// <param name="shown">Runs once the pieces are on screen (see <see cref="Launch"/>); the
        /// caller closes the flash there, never before. Not called when this returns false.</param>
        internal static bool ShowShatter(FlashShatterState state, Bitmap snapshot, PixelRect monitor, Action? shown = null)
        {
            var w = CreateShatter(state, snapshot, ShatterBounds(state, monitor), shown);
            if (w != null && w.Launch()) return true;
            snapshot.Dispose();
            return false;
        }

        /// <summary>
        /// The part of the monitor a break can reach, not the whole monitor: a monitor-sized
        /// per-pixel-alpha window recomposed every frame was the cost behind laggy pops on
        /// Windows. The reach comes off the Core constants: the furthest outward kick sideways and
        /// up, kick + down-shove + gravity downwards over the duration, plus the half-diagonal of
        /// the biggest piece (a piece turns about its own centre). A pendulum break turns the whole
        /// rect about its pivot, so it keeps the monitor.
        /// </summary>
        internal static PixelRect ShatterBounds(FlashShatterState s, PixelRect monitor)
        {
            if (s.FrozenAngleRad != 0 || s.RectW <= 0 || s.RectH <= 0) return monitor;
            var t = s.DurationSec;
            var side = FlashShatter.OutwardSpeedMax * t;
            var up = FlashShatter.OutwardSpeedMax * t;   // ignores gravity: generous on purpose
            var down = (FlashShatter.OutwardSpeedMax + FlashShatter.DownKickMax) * t + 0.5 * s.GravityPxPerSec2 * t * t;
            var piece = 0.5 * Math.Sqrt(Math.Pow(s.RectW * 0.65, 2) + Math.Pow(s.RectH * 0.65, 2)) + 8;
            var left = Math.Max((int)Math.Floor(s.RectX - side - piece), monitor.X);
            var top = Math.Max((int)Math.Floor(s.RectY - up - piece), monitor.Y);
            var right = Math.Min((int)Math.Ceiling(s.RectX + s.RectW + side + piece), monitor.Right);
            var bottom = Math.Min((int)Math.Ceiling(s.RectY + s.RectH + down + piece), monitor.Bottom);
            return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : monitor;
        }

        internal static void DrawShards(DrawingContext dc, FlashShatterState s, IImage image)
        {
            var src = image.Size;
            if (src.Width <= 0 || src.Height <= 0) return;
            using var tilt = s.FrozenAngleRad != 0
                ? dc.PushTransform(Matrix.CreateTranslation(-s.PivotX, -s.PivotY)
                                   * Matrix.CreateRotation(s.FrozenAngleRad)
                                   * Matrix.CreateTranslation(s.PivotX, s.PivotY))
                : default(DrawingContext.PushedState?);
            foreach (var shard in s.Shards)
            {
                if (shard.Alpha <= 0) continue;
                var dest = new Rect(
                    s.RectX + shard.U0 * s.RectW + shard.Dx,
                    s.RectY + shard.V0 * s.RectH + shard.Dy,
                    (shard.U1 - shard.U0) * s.RectW,
                    (shard.V1 - shard.V0) * s.RectH);
                if (dest.Width <= 0 || dest.Height <= 0) continue;
                var from = new Rect(shard.U0 * src.Width, shard.V0 * src.Height,
                    (shard.U1 - shard.U0) * src.Width, (shard.V1 - shard.V0) * src.Height);
                var c = dest.Center;
                using (dc.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y)
                                        * Matrix.CreateRotation(shard.AngleRad)
                                        * Matrix.CreateTranslation(c.X, c.Y)))
                using (dc.PushOpacity(Math.Clamp(shard.Alpha, 0, 1)))
                    dc.DrawImage(image, from, dest);
            }
        }

        // ---- sparks ----

        private static readonly IBrush SparkPink = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));

        /// <summary>The box a spark spray needs around a <paramref name="w"/> x <paramref name="h"/>
        /// picture centred on (<paramref name="cx"/>, <paramref name="cy"/>): the furthest spark flies
        /// (0.55 + 0.75 x 1.15) half-sizes out, plus its radius.</summary>
        internal static PixelRect SparkBounds(double cx, double cy, double w, double h)
        {
            var half = Math.Max(w, h) / 2;
            var reach = (int)Math.Ceiling(half * 1.45 + 10);
            return new PixelRect((int)Math.Round(cx) - reach, (int)Math.Round(cy) - reach, reach * 2, reach * 2);
        }

        /// <summary>WPF DrawSparks: Pop's pink spray around the flash's box, read off the exit the
        /// flash window steps; it ends with that exit.</summary>
        internal static FlashBurstWindow? CreateSparks(FlashExitState exit, double cx, double cy, double w, double h)
        {
            if (FlashExit.SparkCount(exit) == 0) return null;
            var half = Math.Max(w, h) / 2;
            return new FlashBurstWindow(SparkBounds(cx, cy, w, h),
                _ => !exit.Done,
                dc => DrawSparks(dc, exit, cx, cy, half),
                null);
        }

        internal static bool ShowSparks(FlashExitState exit, double cx, double cy, double w, double h)
            => CreateSparks(exit, cx, cy, w, h) is { } win && win.Launch();

        internal static void DrawSparks(DrawingContext dc, FlashExitState exit, double cx, double cy, double half)
        {
            var n = FlashExit.SparkCount(exit);
            for (var i = 0; i < n; i++)
            {
                var sp = FlashExit.Spark(exit, i);
                if (sp.Alpha <= 0 || sp.RadiusPx <= 0.2) continue;
                using (dc.PushOpacity(Math.Clamp(sp.Alpha, 0, 1)))
                    dc.DrawEllipse(SparkPink, null,
                        new Point(cx + sp.Dx * sp.Distance * half, cy + sp.Dy * sp.Distance * half),
                        sp.RadiusPx, sp.RadiusPx);
            }
        }

        /// <summary>World px in, the window's DIPs out: one transform for the whole frame.</summary>
        private sealed class BurstCanvas : Control
        {
            private readonly FlashBurstWindow _host;
            private readonly Action<DrawingContext> _draw;

            public BurstCanvas(FlashBurstWindow host, Action<DrawingContext> draw)
            {
                _host = host;
                _draw = draw;
                IsHitTestVisible = false;
            }

            public override void Render(DrawingContext context)
            {
                var k = _host.RenderScaling > 0 ? _host.RenderScaling : 1.0;
                try
                {
                    using (context.PushTransform(Matrix.CreateTranslation(-_host._bounds.X, -_host._bounds.Y)
                                                 * Matrix.CreateScale(1 / k, 1 / k)))
                        _draw(context);
                }
                catch (Exception ex) { Log.Debug("Flash burst draw failed: {E}", ex.Message); }
            }
        }
    }
}
