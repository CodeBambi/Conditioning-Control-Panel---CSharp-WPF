using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>BubbleService</c>'s ambient field (Services/BubbleService.cs):
    /// Core <see cref="AmbientBubbleField"/> moves and scores, this draws. WPF's default render is
    /// the shared host (one click-through surface, pops from a hook); here that is one override-
    /// redirect window per targeted screen whose X11 input region is exactly the live bubbles'
    /// hit squares, so the desktop stays clickable everywhere else. Reached through
    /// <see cref="CoreBubbles"/>, which App seeds.
    ///
    /// <para>Hot path is allocation-free: one cached frame delegate, one Render pass over the
    /// field with struct transforms, a reused rect buffer for the input region.</para>
    ///
    /// <para>ponytail: plain FloatUp bubbles only. Not here yet: trigger/effect bubbles
    /// (ChaosBubbleVariants and payloads are head-side), Bubbles v2 motions (Rain/Spiral In) and
    /// Brain Drain/Magnet, Natasha's red bubble, the avatar egg, gaze pops, the lucky gold glow and
    /// sparkles, pop haptics, mod pop-sound overrides, Discord presence, and the achievement
    /// (no AchievementService on this head; quests are credited).</para>
    /// </summary>
    internal static class BubbleOverlay
    {
        internal static readonly AmbientBubbleField Field = new();
        private static readonly List<BubbleOverlayWindow> Windows = new();
        private static readonly List<Screen> Targets = new();
        private static DispatcherTimer? _spawnTimer;
        private static bool _running;
        private static TimeSpan? _lastStep;
        private static bool _pending;   // a frame is requested; the loop idles while the field is empty
        internal static Bitmap? Image;

        /// <summary>The "N/300 today" line listens to this (WPF AmbientXpBudgetChanged).</summary>
        internal static event Action? XpBudgetChanged;

        // CCP_FRAME_STATS=1: one stdout line per 10 s - step+region cost per frame with N bubbles.
        private static readonly bool Stats = Environment.GetEnvironmentVariable("CCP_FRAME_STATS") == "1";
        private static readonly Stopwatch StatClock = new();
        private static double _statMax, _statSum;
        private static int _statN, _statBubbles;

        public static bool IsRunning => _running;
        internal static bool IsFrameOwner(BubbleOverlayWindow w) => Windows.Count > 0 && Windows[0] == w;

        public static void Start(Visual host)
        {
            if (_running) return;
            if (!X11Overlay.IsAvailable) { Log.Warning("Bubbles: this platform cannot show click-through overlays; skipped"); return; }
            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return;
            var s = CoreSettings.Current;
            var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
            Image ??= Helpers.ModArt.TryLoad("bubble.png");
            foreach (var i in PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary))
            {
                var sc = screens[i];
                var w = new BubbleOverlayWindow(Targets.Count, sc.Bounds, sc.Scaling > 0 ? sc.Scaling : 1);
                if (!X11Overlay.SetInputRects(w, w.Rects, 0) || !X11Overlay.SetOverrideRedirect(w, sc.Bounds))
                {
                    Log.Warning("Bubbles: the platform refused a click-through topmost overlay window; skipped");
                    w.Close();
                    Stop();
                    return;
                }
                w.Show();
                Windows.Add(w);
                Targets.Add(sc);
            }
            _running = true;
            _lastStep = null;
            _spawnTimer = new DispatcherTimer { Interval = AmbientBubbleField.SpawnInterval(s.BubblesFrequency) };
            _spawnTimer.Tick += (_, _) => Spawn();
            _spawnTimer.Start();
            Spawn();   // WPF: first bubble immediately (and wakes the frame loop)
            if (Stats) StatClock.Restart();
            Log.Information("BubbleService started - {Freq} bubbles/min", s.BubblesFrequency);
        }

        public static void Stop()
        {
            _running = false;
            _pending = false;
            _spawnTimer?.Stop();
            _spawnTimer = null;
            Field.Bubbles.Clear();   // WPF PopAllBubbles: force-destroy, no animation
            foreach (var w in Windows) w.Close();
            Windows.Clear();
            Targets.Clear();
        }

        public static void RefreshFrequency()
        {
            if (_spawnTimer == null) return;
            _spawnTimer.Interval = AmbientBubbleField.SpawnInterval(CoreSettings.Current.BubblesFrequency);
        }

        /// <summary>WPF SpawnBubble: under the cap, not during a display change, on a random target.</summary>
        internal static void Spawn()
        {
            var s = CoreSettings.Current;
            if (!_running || Targets.Count == 0 || DisplayChangeCoordinator.SpawnsSuppressed || !Field.CanSpawn(s)) return;
            var i = Field.Random.Next(Targets.Count);
            var a = Targets[i].WorkingArea;
            // Outside sessions, bubbles are always clickable (no UI toggle exists for the setting).
            var clickable = !CoreSession.IsSessionRunning || s.BubblesClickable;
            var mod = (CoreMods.ActiveModTokenProvider?.Invoke() as ModManifest)?.BubbleScale;
            Field.Bubbles.Add(AmbientBubble.Spawn(Field.Random, i, a.X, a.Y, a.Width, a.Height, Windows[i].Scaling, s, mod, clickable));
            RequestFrame();
        }

        /// <summary>WPF StartAnimationDriver/StopAnimationTimerIfIdle: one chain, parked while empty.</summary>
        private static void RequestFrame()
        {
            if (_pending || !_running || Windows.Count == 0) return;
            _pending = true;
            Windows[0].RequestAnimationFrame(Windows[0].FrameCallback);
        }

        /// <summary>WPF AwardAmbientPop, head half: sound, XP, quest credit.</summary>
        internal static void Pop(AmbientBubble b)
        {
            var s = CoreSettings.Current;
            var paid = Field.Pop(b, s);
            if (paid > 0) CoreProgression.AddXP(paid, "Bubble");
            XpBudgetChanged?.Invoke();
            var master = s.MasterVolume / 100f;
            var vol = (float)Math.Pow(master * (s.BubblesVolume / 100f), 1.5);
            var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds");
            var path = b.Lucky
                ? Path.Combine(dir, $"chime{Field.Random.Next(1, 4)}.mp3")   // lucky: a chime replaces the pop, at 0.35x
                : Path.Combine(dir, "bubbles", new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" }[Field.Random.Next(3)]);
            if (b.Lucky) vol *= 0.35f;
            CoreAudio.PlayOneShot(path, Math.Min(vol, 1f), "bubble-pop");
            try { App.Quests?.TrackBubblePopped(); } catch (Exception ex) { Log.Debug("bubble quest credit: {E}", ex.Message); }
            _ = CoreHaptics.Service?.BubblePopAsync();   // WPF BubbleService.cs:1089
        }

        internal static void OnFrame(TimeSpan now)
        {
            _pending = false;
            if (!_running || Windows.Count == 0) return;
            // WPF OnAnimationRenderTick: one logical step per >= 30 ms, re-based to the real frame
            // time so a late frame drops rather than bursting catch-up steps.
            if (_lastStep is not { } last || (now - last).TotalMilliseconds >= AmbientBubbleField.StepMs)
            {
                _lastStep = now;
                var t0 = Stats ? Stopwatch.GetTimestamp() : 0;
                Field.Step();
                foreach (var w in Windows) w.Sync();
                if (Stats) Sample(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                if (Field.Bubbles.Count == 0) { _lastStep = null; return; }   // idle until the next spawn
            }
            RequestFrame();
        }

        private static void Sample(double ms)
        {
            _statSum += ms; _statN++; _statBubbles += Field.Bubbles.Count;
            if (ms > _statMax) _statMax = ms;
            if (StatClock.Elapsed.TotalSeconds < 10) return;
            Console.WriteLine(FormattableString.Invariant(
                $"[Bubbles] {_statN} steps, avg {(double)_statBubbles / _statN:F1} bubbles, step+sync mean {_statSum / _statN:F3} ms, max {_statMax:F3} ms, gen0 GCs {GC.CollectionCount(0)}"));
            _statSum = _statMax = 0; _statN = _statBubbles = 0;
            StatClock.Restart();
        }
    }

    /// <summary>One screen of the field: draws its bubbles (WPF Bubble visuals: bubble.png, or the
    /// gradient ellipse fallback, scaled and rotated about the centre at the pop fade) and takes
    /// presses only inside their hit squares.</summary>
    internal sealed class BubbleOverlayWindow : Window
    {
        private static readonly IBrush FallbackFill = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(180, 200, 220, 255), 0), new GradientStop(Color.FromArgb(80, 255, 255, 255), 1) },
        };
        private static readonly IPen FallbackPen = new Pen(Brushes.White, 2);

        internal readonly int Index;
        internal readonly PixelRect Bounds;
        internal readonly double Scaling;
        internal readonly PixelRect[] Rects = new PixelRect[64];
        private int _lastCount = -1;
        /// <summary>Cached (no per-frame closure); a closed window's late frame is ignored.</summary>
        internal readonly Action<TimeSpan> FrameCallback;

        /// <summary>--render-all only: two bubbles on a grey backdrop (the proof PNG has no desktop).</summary>
        internal BubbleOverlayWindow() : this(0, new PixelRect(0, 0, 1280, 720), 1)
        {
            Width = 1280;
            Height = 720;
            Background = Brushes.DimGray;
            BubbleOverlay.Image ??= Helpers.ModArt.TryLoad("bubble.png");
            if (!BubbleOverlay.IsRunning && BubbleOverlay.Field.Bubbles.Count == 0)
            {
                BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { X = 300, Y = 250, Size = 200, Angle = 20 });
                BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { X = 750, Y = 300, Size = 240, Popping = true, Scale = 1.4, Fade = 0.5 });
            }
        }

        public BubbleOverlayWindow(int index, PixelRect bounds, double scaling)
        {
            Index = index;
            Bounds = bounds;
            Scaling = scaling;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Content = new Layer(this);
            FrameCallback = now => { if (BubbleOverlay.IsFrameOwner(this)) BubbleOverlay.OnFrame(now); };
            PointerPressed += OnPressed;
        }

        /// <summary>Global bubble DIPs (screen px / scaling) to this window's DIPs.</summary>
        private double K => Scaling / RenderScaling;

        /// <summary>WPF: left or right press on a bubble pops it (topmost first); a miss passes through.</summary>
        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            var p = e.GetPosition(this);
            var b = BubbleOverlay.Field.HitTest(Index, (Bounds.X + p.X * RenderScaling) / Scaling, (Bounds.Y + p.Y * RenderScaling) / Scaling);
            if (b == null) return;
            BubbleOverlay.Pop(b);
            e.Handled = true;
        }

        /// <summary>After a step: input region = live clickable bubbles here; repaint if any were drawn.</summary>
        internal void Sync()
        {
            var n = 0;
            var bubbles = BubbleOverlay.Field.Bubbles;
            for (var i = 0; i < bubbles.Count && n < Rects.Length; i++)
            {
                var b = bubbles[i];
                if (b.Screen != Index || !b.Clickable || b.Popping) continue;
                Rects[n++] = new PixelRect((int)(b.X * Scaling) - Bounds.X, (int)(b.Y * Scaling) - Bounds.Y,
                    (int)(b.Size * Scaling), (int)(b.Size * Scaling));
            }
            var drawn = HasBubbles();
            if (!drawn && _lastCount == 0) return;   // stayed empty: region already empty, nothing to repaint
            X11Overlay.SetInputRects(this, Rects, n);
            (Content as Control)?.InvalidateVisual();
            _lastCount = drawn ? 1 : 0;
        }

        private bool HasBubbles()
        {
            foreach (var b in BubbleOverlay.Field.Bubbles) if (b.Screen == Index) return true;
            return false;
        }

        private sealed class Layer : Control
        {
            private readonly BubbleOverlayWindow _w;
            public Layer(BubbleOverlayWindow w) { _w = w; IsHitTestVisible = false; }

            public override void Render(DrawingContext dc)
            {
                var k = _w.K;
                double ox = _w.Bounds.X / _w.Scaling, oy = _w.Bounds.Y / _w.Scaling;
                var img = BubbleOverlay.Image;
                var bubbles = BubbleOverlay.Field.Bubbles;
                for (var i = 0; i < bubbles.Count; i++)
                {
                    var b = bubbles[i];
                    if (b.Screen != _w.Index) continue;
                    double cx = (b.CenterX - ox) * k, cy = (b.CenterY - oy) * k, size = b.Size * k, s = b.DrawScale;
                    var m = Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateRotation(b.Angle * Math.PI / 180)
                          * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(cx, cy);
                    using (dc.PushTransform(m))
                    using (dc.PushOpacity(Math.Clamp(b.Fade, 0, 1)))
                    {
                        if (img != null) dc.DrawImage(img, new Rect(cx - size / 2, cy - size / 2, size, size));
                        else dc.DrawEllipse(FallbackFill, FallbackPen, new Point(cx, cy), size / 2 - 5, size / 2 - 5);
                    }
                }
            }
        }
    }
}
