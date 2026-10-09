using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The standalone corner-GIF overlays (WPF Services/CornerGifService.cs): one click-through,
    /// topmost, no-taskbar window per enabled <see cref="AppSettings.CornerGifOverlays"/> slot (at most
    /// <see cref="CornerGifPlanner.MaxOverlays"/>) on the primary screen, placed by
    /// <see cref="CornerGifPlanner"/>. Seeded as <see cref="CoreCornerGif.RefreshHandler"/>.
    ///
    /// <para>Kept from WPF: per-slot rebuild (a slider edit leaves the other slot's animation alone), the
    /// <see cref="CornerGifPlanner.StaggerMs"/> realization gap, deferral while a display change is in
    /// flight (8 x 250 ms), a generation counter so a teardown cancels a queued realization, and the
    /// #709/#954/#958 restore sentinel. Frames are decoded once, off the UI thread, at the overlay's
    /// pixel size (WPF CornerGifMedia.Attach); a frame tick only swaps the Image source.</para>
    /// </summary>
    internal static class CornerGifOverlay
    {
        internal static bool SkipPlatformChecksForTests;
        /// <summary>Tests step frames with <see cref="Advance(int)"/>; the real-time frame timer would tick
        /// whenever the test dispatcher yields (P08).</summary>
        internal static bool ManualFramesForTests;
        internal static TimeProvider Clock = TimeProvider.System;

        private const int SpawnDeferMaxAttempts = 8;
        private const int SpawnRetryMs = 250;

        private sealed class Shown
        {
            public SpiralOverlayWindow Window = null!;
            public List<Bitmap> Frames = new();
            public DispatcherTimer? Timer;
            public int Index;
        }

        private sealed record Pending(CornerGifOverlaySetting Setting, long DueMs, int Defers);

        private static readonly Dictionary<int, Shown> Windows = new();
        private static readonly Dictionary<int, Pending> Queue = new();
        private static readonly Dictionary<int, int> Seq = new();
        private static readonly HashSet<int> Decoding = new();
        private static long _nextRealizeTick;
        private static bool _sentinelArmed;
        private static Visual? _host;
        private static DispatcherTimer? _pump;
        private static bool _refused;

        internal static Task? LastDecode { get; private set; }
        internal static IReadOnlyCollection<int> ShownSlots => Windows.Keys;
        internal static bool HasActiveOverlays => Windows.Count > 0 || Queue.Count > 0 || Decoding.Count > 0;
        internal static SpiralOverlayWindow? WindowFor(int slot) => Windows.TryGetValue(slot, out var s) ? s.Window : null;
        internal static int FrameIndexFor(int slot) => Windows.TryGetValue(slot, out var s) ? s.Index : -1;
        internal static string SentinelPath => Path.Combine(CorePaths.UserData, "logs", "cornergif_restore.active");

        private static long NowMs => (long)Clock.GetElapsedTime(0).TotalMilliseconds;

        /// <summary>WPF App.xaml.cs:480: the <see cref="CoreCornerGif"/> surface seam, on the UI thread.</summary>
        internal static void Seed(Func<Visual?> host) =>
            CoreCornerGif.RefreshHandler = index => Dispatcher.UIThread.Post(() =>
            {
                if (host() is { } h) Refresh(h, index);
            });

        /// <summary>WPF RefreshOverlays (index &lt; 0) / RefreshSlot (index &gt;= 0). UI thread.</summary>
        public static void Refresh(Visual host, int index = -1)
        {
            _host = host;
            var overlays = CoreSettings.Current.CornerGifOverlays;
            if (index < 0)
            {
                StopAll();
                if (overlays == null) return;
                int queued = 0;
                for (int i = 0; i < overlays.Count; i++)
                {
                    var o = overlays[i];
                    if (o is not { Enabled: true }) continue;
                    if (queued >= CornerGifPlanner.MaxOverlays)
                    {
                        Log.Warning("CornerGif: more than {Max} enabled corner-GIF slots - ignoring the rest", CornerGifPlanner.MaxOverlays);
                        break;
                    }
                    queued++;
                    QueueShow(i, o);
                }
                return;
            }

            CloseSlot(index);
            if (overlays == null || index >= overlays.Count || index >= CornerGifPlanner.MaxOverlays) return;
            if (overlays[index] is { Enabled: true } setting) QueueShow(index, setting);
        }

        /// <summary>WPF StopAll: every window closes and every queued realization is cancelled. Panic,
        /// shell exit. The settings are untouched (a panic stops what is on screen, it does not reconfigure).</summary>
        public static void StopAll()
        {
            foreach (var i in Windows.Keys.ToList()) Close(i);
            foreach (var i in Seq.Keys.ToList()) Seq[i]++;
            Queue.Clear();
            Decoding.Clear();
            _pump?.Stop();
            _pump = null;
            _nextRealizeTick = 0;
            SyncSentinel();
        }

        /// <summary>WPF RestoreOnStartup (#709/#954/#958): a surviving sentinel means the last run died
        /// with a corner GIF on screen, so the slots are switched off (and the user told) instead of replayed.</summary>
        public static void RestoreOnStartup(Visual host)
        {
            var overlays = CoreSettings.Current.CornerGifOverlays;
            bool anyEnabled = overlays?.Any(o => o is { Enabled: true }) == true;
            bool survived = File.Exists(SentinelPath);
            if (survived) ClearSentinel();
            if (!anyEnabled) return;
            if (!survived) { Refresh(host); return; }
            foreach (var o in overlays!) if (o != null) o.Enabled = false;
            CoreSettings.Save();
            Log.Warning("CornerGif: the previous run ended with a corner-GIF overlay on screen - all slots force-disabled so this launch can start. Re-enable them from the Spiral card.");
            CoreProgram.Notify(Loc.Get("corner_gif_force_disabled"), "Warning", TimeSpan.FromSeconds(12));
        }

        private static void CloseSlot(int index)
        {
            Seq[index] = Seq.GetValueOrDefault(index) + 1;
            Queue.Remove(index);
            Decoding.Remove(index);
            Close(index);
            SyncSentinel();
        }

        private static void Close(int index)
        {
            if (!Windows.Remove(index, out var s)) return;
            s.Timer?.Stop();
            s.Window.Spiral.Source = null;   // WPF ReleaseAnimator
            try { s.Window.Close(); } catch (Exception ex) { Log.Debug("CornerGif: close failed: {E}", ex.Message); }
            foreach (var f in s.Frames) f.Dispose();
        }

        private static void QueueShow(int index, CornerGifOverlaySetting setting)
        {
            Seq[index] = Seq.GetValueOrDefault(index) + 1;
            var delay = CornerGifPlanner.NextRealizeDelayMs(ref _nextRealizeTick, NowMs);
            Queue[index] = new Pending(setting, NowMs + Math.Max(0, delay), 0);
            SyncSentinel();
            Pump();
        }

        /// <summary>Realizes every queued slot whose time has come and re-arms one timer for the next.
        /// Tests advance <see cref="Clock"/> and call this.</summary>
        internal static void Pump()
        {
            _pump?.Stop();
            _pump = null;
            var now = NowMs;
            foreach (var (index, p) in Queue.ToList())
            {
                if (p.DueMs > now) continue;
                if (DisplayChangeCoordinator.SpawnsSuppressed && p.Defers < SpawnDeferMaxAttempts)
                {
                    Queue[index] = p with { DueMs = now + SpawnRetryMs, Defers = p.Defers + 1 };
                    continue;
                }
                Queue.Remove(index);
                try { Realize(index, p.Setting); }
                catch (Exception ex) { Log.Error(ex, "CornerGif: realize failed for slot {Index}", index); }
            }
            SyncSentinel();
            if (Queue.Count == 0) return;
            var next = Math.Max(1, Queue.Values.Min(p => p.DueMs) - now);
            _pump = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(next) };
            _pump.Tick += (_, _) => Pump();
            _pump.Start();
        }

        /// <summary>WPF ShowOne up to the decode: resolve the art, read its size, place it.</summary>
        private static void Realize(int index, CornerGifOverlaySetting setting)
        {
            if (_host is not { } host) return;
            if (!SkipPlatformChecksForTests && (_refused || !X11Overlay.IsAvailable || !X11Overlay.IsCompositing))
            {
                Log.Debug("CornerGif: no composited click-through overlay on this platform, so no corner GIF");
                return;
            }
            var path = CornerGifPlanner.ResolveSourcePath(setting, CoreSettings.Current.SpiralPath)
                       ?? CoreModArt.SpiralOverridePath()
                       ?? Path.Combine(AppContext.BaseDirectory, "Resources", "spiral_corner.gif");
            int srcW, srcH;
            try
            {
                using var codec = SKCodec.Create(path);
                (srcW, srcH) = codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
            }
            catch { (srcW, srcH) = (0, 0); }

            var screens = ScreenList.Enumerate(host);
            var screen = screens.FirstOrDefault(s => s.IsPrimary) ?? screens.FirstOrDefault();
            if (screen is null) return;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            var place = CornerGifPlanner.Place(setting, srcW, srcH,
                screen.Bounds.Width / scale, screen.Bounds.Height / scale,
                CornerGifPlanner.CountEarlierSlotsInCorner(CoreSettings.Current.CornerGifOverlays, index, setting.Position));
            if (place is not { } p)
            {
                Log.Warning("CornerGif: unreadable or degenerate GIF {W}x{H} ({Path}) for slot {Index} - skipping overlay", srcW, srcH, path, index);
                return;
            }
            var bounds = new PixelRect(screen.Bounds.X + (int)Math.Round(p.Left * scale), screen.Bounds.Y + (int)Math.Round(p.Top * scale),
                Math.Max(1, (int)Math.Round(p.Width * scale)), Math.Max(1, (int)Math.Round(p.Height * scale)));

            var seq = Seq.GetValueOrDefault(index);
            Decoding.Add(index);
            LastDecode = DecodeThenShow(index, seq, path, bounds, p.Opacity);
        }

        private static async Task DecodeThenShow(int index, int seq, string path, PixelRect bounds, double opacity)
        {
            var (frames, delay) = await Task.Run(() => SpiralOverlay.Decode(path, Math.Max(bounds.Width, bounds.Height)));
            if (Seq.GetValueOrDefault(index) != seq || frames.Count == 0)
            {
                if (frames.Count == 0) Log.Warning("CornerGif: no frames decoded from {Path}", path);
                foreach (var f in frames) f.Dispose();
                if (Seq.GetValueOrDefault(index) == seq) { Decoding.Remove(index); SyncSentinel(); }
                return;
            }
            Decoding.Remove(index);
            var w = new SpiralOverlayWindow();
            w.Spiral.Stretch = global::Avalonia.Media.Stretch.Uniform;
            w.Spiral.Source = frames[0];
            w.Spiral.Opacity = opacity;
            if (!X11Overlay.SetClickThrough(w, true) | !X11Overlay.SetOverrideRedirect(w, bounds) && !SkipPlatformChecksForTests)
            {
                Log.Warning("CornerGif: the platform refused a click-through topmost overlay window, so no corner GIF");
                _refused = true;
                w.Close();
                foreach (var f in frames) f.Dispose();
                SyncSentinel();
                return;
            }
            var shown = new Shown { Window = w, Frames = frames };
            Windows[index] = shown;   // tracked BEFORE Show, so a throwing Show stays reachable (#709)
            try { w.Show(); }
            catch (Exception ex)
            {
                Log.Error(ex, "CornerGif: Show failed for slot {Index} - overlay discarded", index);
                Close(index);
                SyncSentinel();
                return;
            }
            if (frames.Count > 1 && !ManualFramesForTests)
            {
                shown.Timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = delay };
                shown.Timer.Tick += (_, _) => Advance(shown);
                shown.Timer.Start();
            }
            SyncSentinel();
            Log.Information("CornerGif: overlay {Index} shown ({Path}, {W}x{H}px, {Opacity:P0})", index, path, bounds.Width, bounds.Height, opacity);
        }

        /// <summary>One animation frame. Allocation-free.</summary>
        internal static void Advance(int slot) { if (Windows.TryGetValue(slot, out var s)) Advance(s); }

        private static void Advance(Shown s)
        {
            s.Index = (s.Index + 1) % s.Frames.Count;
            s.Window.Spiral.Source = s.Frames[s.Index];
        }

        /// <summary>Armed while an overlay is up or on its way; cleared once none is (WPF SyncSentinel).</summary>
        private static void SyncSentinel()
        {
            bool wanted = HasActiveOverlays;
            if (wanted == _sentinelArmed) return;
            _sentinelArmed = wanted;
            if (!wanted) { ClearSentinel(); return; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SentinelPath)!);
                File.WriteAllText(SentinelPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch { /* a guard that throws is worse than no guard */ }
        }

        private static void ClearSentinel()
        {
            try { if (File.Exists(SentinelPath)) File.Delete(SentinelPath); } catch { }
        }
    }
}
