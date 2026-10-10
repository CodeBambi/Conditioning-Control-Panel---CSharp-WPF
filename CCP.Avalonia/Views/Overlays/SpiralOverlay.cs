using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Services;                  // SpiralFrames
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The Spiral overlay: one click-through, topmost <see cref="SpiralOverlayWindow"/> per targeted
    /// monitor, all showing the same pre-decoded GIF frames. The Avalonia counterpart of
    /// <c>OverlayService</c>'s spiral region (ConditioningControlPanel/Services/Notifications/OverlayService.cs:
    /// StartSpiral, CreateSpiralGifWindow, GifFrameTimer_Tick, StopSpiral), shaped like
    /// <see cref="PinkFilterOverlay"/> (static, idempotent <see cref="Refresh"/>) and opening its windows
    /// the way <see cref="SubliminalOverlay"/> does (click-through + override-redirect before Show).
    ///
    /// <para>Frames decode once per path off the UI thread (WPF's compositor path; the legacy path
    /// hitched ~1s) through the shared <see cref="SpiralFrames.Plan"/> budget, and are cached across
    /// stop/start. A frame tick only swaps <c>Image.Source</c> on each window: no decode, no bitmap
    /// allocation. The timer exists only while windows are up, so nothing ticks while stopped.</para>
    /// </summary>
    internal static partial class SpiralOverlay
    {
        /// <summary>Headless tests have no X11 window to make click-through; this lets them drive the
        /// real show/animate/close path. Never set outside tests.</summary>
        internal static bool SkipPlatformChecksForTests;

        private static readonly List<SpiralOverlayWindow> Windows = new();
        private static int[] _shownOn = Array.Empty<int>();
        private static List<Bitmap> _frames = new();
        private static string _framesKey = "";
        private static TimeSpan _delay = TimeSpan.FromMilliseconds(50);
        private static DispatcherTimer? _timer;
        private static int _index;
        private static bool _refused;

        /// <summary>The off-thread decode in flight, if any (tests await it).</summary>
        internal static Task? Decoding { get; private set; }

        internal static bool IsShowing => Windows.Count > 0;
        internal static bool IsAnimating => _timer is { IsEnabled: true };
        internal static int FrameIndex => _index;
        internal static TimeSpan FrameDelay => _delay;
        internal static IReadOnlyList<SpiralOverlayWindow> Shown => Windows;

        /// <summary>WPF GetSpiralPath: with Randomize on, a random spiral from the pool, picked at
        /// overlay START only (never per tick: the decoded frames are keyed by path and a mid-run
        /// re-decode hitches); else the configured file if it exists, else the active mod's spiral,
        /// else the shipped one. ponytail: video spirals (.mp4 etc., WPF MediaElement) decode to no
        /// frames and show nothing.</summary>
        internal static string SourcePath()
        {
            var s = CoreSettings.Current;
            var p = s.SpiralPath;
            var configured = !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
            if (s.SpiralRandomize)
            {
                // Latched for the run: every Refresh until the spiral goes down gets the same file.
                _runPick ??= PickRandomSpiral(configured, Path.Combine(CorePaths.UserData, "Spirals"), Rng, ref _lastRandomSpiralPath);
                if (_runPick != null) return _runPick;
            }
            else _runPick = null;
            if (configured != null) return configured;
            return CoreModArt.SpiralOverridePath() ?? Path.Combine(AppContext.BaseDirectory, "Resources", "spiral.gif");
        }

        private static readonly string[] SpiralExtensions = { ".gif", ".png", ".jpg", ".jpeg", ".webp" };
        private static readonly Random Rng = new();
        private static string? _runPick, _lastRandomSpiralPath;

        /// <summary>Tests: is a pick latched for the current run.</summary>
        internal static string? RunPick => _runPick;

        /// <summary>The personal-folder refusal (#1053). A seam for tests.</summary>
        internal static Func<string?, bool> IsPersonalFolderRoot = path =>
            !string.IsNullOrEmpty(path) && Views.Windows.MainShellWindow.IsPersonalFolderRoot(path);

        /// <summary>WPF PickRandomSpiral (#641): the pool is the folder of the configured spiral if
        /// one is set, else the user Spirals library. A configured file in a personal or system
        /// folder (Desktop, Downloads, Pictures, a drive root) never widens into every image beside
        /// it (#1053): those roots are refused and the library is the pool. Null when there is no
        /// pool, so the caller falls back to the single spiral. Avoids the previous pick.</summary>
        internal static string? PickRandomSpiral(string? configured, string library, Random random, ref string? last)
        {
            try
            {
                var poolDir = !string.IsNullOrEmpty(configured) ? Path.GetDirectoryName(configured) : library;
                if (IsPersonalFolderRoot(poolDir))
                {
                    Log.Warning("[Overlay] Spiral randomize: refusing {Pool} as a spiral pool (personal/system folder) - falling back to the Spirals library", poolDir);
                    poolDir = library;
                }
                if (string.IsNullOrEmpty(poolDir) || !Directory.Exists(poolDir)) return null;

                var pool = Directory.GetFiles(poolDir)
                    .Where(f => SpiralExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();
                if (pool.Count == 0) return null;
                if (pool.Count == 1) return pool[0];

                string pick;
                do { pick = pool[random.Next(pool.Count)]; } while (pick == last);
                last = pick;
                return pick;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Overlay] Failed to pick random spiral");
                return null;
            }
        }

        /// <summary>The painted alpha: the slider through WPF's #722 curve.</summary>
        internal static double PaintedOpacity =>
            SpiralFrames.Paint((App.Sessions?.SpiralOpacity ?? CoreSettings.Current.SpiralOpacity) / 100.0);   // a session ramps it without writing it

        /// <summary>WPF RefreshOverlays' spiral half: show, hide, move or repaint to match the settings.</summary>
        public static void Refresh(Visual host)
        {
            var s = CoreSettings.Current;
            var hold = ActiveHold;   // a caller's band: up whatever the user's own switch says
            if (!(s.SpiralEnabled || hold != null) || !ShouldShow()) { _runPick = null; CloseAll(); return; }   // the next start rolls a fresh spiral

            var path = hold?.Path ?? SourcePath();
            if (_framesKey != path)
            {
                CloseAll();
                if (!IsVideo(path))
                {
                    _video = false;
                    BeginDecode(TopLevel.GetTopLevel(host) ?? host, path);
                    return;
                }
                // A video spiral has no frames to decode: it plays straight into the windows below.
                foreach (var old in _frames) old.Dispose();
                (_frames, _framesKey, _index, _video) = (new List<Bitmap>(), path, 0, true);
            }
            if (_frames.Count == 0 && !_video) { CloseAll(); return; }   // undecodable: logged once by the decode

            var screens = ScreenList.Enumerate(host);
            var primary = -1;
            for (var i = 0; i < screens.Count; i++) if (screens[i].IsPrimary) { primary = i; break; }
            var want = hold?.AllScreens == true
                ? Enumerable.Range(0, screens.Count).ToArray()
                : PinkFilterOverlay.ResolveScreenIndices(s.SpiralTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
            var opacity = hold != null ? Math.Clamp(hold.Opacity, 0, 1) : PaintedOpacity;
            var slow = hold?.Slow == true;

            if (Windows.Count > 0 && want.SequenceEqual(_shownOn) && slow == _shownSlow)
            {
                foreach (var w in Windows) w.Spiral.Opacity = opacity;   // WPF UpdateSpiralOpacity
                return;
            }

            CloseAll();
            // No compositor = no per-pixel alpha: the spiral would be an opaque screen-sized block.
            if (!SkipPlatformChecksForTests && (_refused || !X11Overlay.IsAvailable || !X11Overlay.IsCompositing))
            {
                Log.Debug("Spiral: no composited click-through overlay on this platform, so no spiral");
                return;
            }

            foreach (var i in want)
            {
                var w = new SpiralOverlayWindow();
                if (_frames.Count > 0) w.Spiral.Source = _frames[_index % _frames.Count];
                w.Spiral.Opacity = opacity;
                // Click-through and override-redirect (with the geometry) before Show, the order
                // FlashOverlay/SubliminalOverlay use: a stale WM replay cannot mis-size it, and a
                // topmost screen-sized window that ate clicks would lock the desktop.
                if (!X11Overlay.SetClickThrough(w, true) | !X11Overlay.SetOverrideRedirect(w, screens[i].Bounds)
                    && !SkipPlatformChecksForTests)
                {
                    Log.Warning("Spiral: the platform refused a click-through topmost overlay window, so the spiral is not shown");
                    _refused = true;
                    w.Close();
                    CloseAll();
                    return;
                }
                w.Show();
                Windows.Add(w);
            }
            _shownOn = want;
            _shownSlow = slow;
            QuestMinutes.Follow(Windows.Count > 0);
            if (_video && Windows.Count > 0) StartVideo(path, slow);
            if (_frames.Count > 1 && Windows.Count > 0)
            {
                // Reduced motion (a Back Room hold): the weave at half speed, never a still.
                _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = slow ? _delay + _delay : _delay };
                _timer.Tick += (_, _) => Tick();
                _timer.Start();
            }
            Log.Debug("Spiral showing on {Count} screen(s) at {Opacity}%", Windows.Count, s.SpiralOpacity);
        }

        /// <summary>WPF GifFrameTimer_Tick: next frame on every window. Allocation-free.</summary>
        internal static void Tick()
        {
            if (_frames.Count == 0 || Windows.Count == 0) return;
            _index = (_index + 1) % _frames.Count;
            var frame = _frames[_index];
            for (var i = 0; i < Windows.Count; i++) Windows[i].Spiral.Source = frame;
        }

        /// <summary>WPF StopSpiral: the windows and the clock go; the decoded frames stay cached.</summary>
        public static void CloseAll()
        {
            _timer?.Stop();
            _timer = null;
            StopVideo();
            foreach (var w in Windows)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("Spiral: failed to close a window: {E}", ex.Message); }
            }
            Windows.Clear();
            _shownOn = Array.Empty<int>();
            QuestMinutes.Follow(false);
            // WPF OverlayService.cs:2094: the fullscreen spiral left, so a session may raise its corner GIF again.
            try { App.Sessions?.RefreshCornerGifPolicy(); } catch (Exception ex) { Log.Debug("Spiral: corner policy: {E}", ex.Message); }
        }

        /// <summary>WPF AchievementService:276 - quest minutes while the spiral is on screen.</summary>
        internal static readonly OverlayQuestMinutes QuestMinutes = new(m => App.Quests?.TrackSpiralMinutes(m));

        /// <summary>Same gate as the pink tint (WPF RefreshOverlays returns early unless the engine runs):
        /// a running engine or session, not paused. Unseeded (renders, tests) means the card owns it.</summary>
        private static bool ShouldShow()
            => ActiveHold != null || global::ConditioningControlPanel.Services.RemoteCommands.OverlayHold || App.Sessions?.IsPaused != true
               && (CoreSession.IsEngineRunningProvider is null || CoreSession.IsEngineRunning || App.Sessions?.IsRunning == true);

        private static void BeginDecode(Visual host, string path)
        {
            if (Decoding is { IsCompleted: false }) return;   // the landing Refresh picks up any newer path
            Decoding = DecodeThenRefresh(host, path);
        }

        private static async Task DecodeThenRefresh(Visual host, string path)
        {
            var (frames, delay) = await Task.Run(() => Decode(path));
            if (frames.Count == 0) Log.Warning("Spiral: no frames decoded from {Path}; spiral not shown", path);
            foreach (var old in _frames) old.Dispose();
            (_frames, _framesKey, _delay, _index) = (frames, path, delay, 0);
            Decoding = null;   // or the Refresh below, still inside this task, cannot start a newer path's decode
            Refresh(host);   // the user may have stopped the engine or unticked it mid-decode
        }

        /// <summary>WPF DecodeGifFrames on SkiaSharp: every frame composited in order (so partial GIF
        /// frames are correct), the kept ones scaled to the <see cref="SpiralFrames.Plan"/> size.</summary>
        /// <param name="fitLongSide">When &gt; 0, frames are scaled further so the long side is at most this many
        /// pixels (the corner GIF decodes to the pixels its overlay occupies, WPF CornerGifMedia).</param>
        internal static (List<Bitmap> Frames, TimeSpan Delay) Decode(string path, int fitLongSide = 0)
        {
            var frames = new List<Bitmap>();
            try
            {
                using var codec = SKCodec.Create(path);
                if (codec is null) return (frames, TimeSpan.FromMilliseconds(50));
                var count = Math.Max(1, codec.FrameCount);
                var info = codec.FrameInfo;
                var plan = SpiralFrames.Plan(codec.Info.Width, codec.Info.Height, count,
                    codec.FrameCount > 0 ? codec.FrameInfo[0].Duration : 50);
                if (fitLongSide > 0 && Math.Max(plan.Width, plan.Height) > fitLongSide)
                {
                    var k = fitLongSide / (double)Math.Max(plan.Width, plan.Height);
                    plan.Width = Math.Max(1, (int)Math.Round(plan.Width * k));
                    plan.Height = Math.Max(1, (int)Math.Round(plan.Height * k));
                }
                var full = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKBitmap(full);
                using var scaled = new SKBitmap(new SKImageInfo(plan.Width, plan.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                for (var i = 0; i < count && frames.Count < plan.Frames; i++)
                {
                    // canvas holds frame i-1. Reuse it only when that is the frame i builds on; otherwise
                    // (restore-previous disposal) the codec decodes the required frame itself.
                    var required = i < info.Length ? info[i].RequiredFrame : -1;
                    var r = codec.GetPixels(full, canvas.GetPixels(), new SKCodecOptions(i, required == i - 1 ? i - 1 : -1));
                    if (r != SKCodecResult.Success && r != SKCodecResult.IncompleteInput) break;
                    if (i % plan.Step != 0) continue;
                    canvas.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    frames.Add(new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(),
                        new PixelSize(plan.Width, plan.Height), new Vector(96, 96), scaled.RowBytes));
                }
                Log.Information("Spiral: decoded {Count} frames ({W}x{H}) from {Path}", frames.Count, plan.Width, plan.Height, path);
                return (frames, TimeSpan.FromMilliseconds(plan.DelayMs));
            }
            catch (Exception ex)
            {
                Log.Warning("Spiral: failed to decode {Path}: {E}", path, ex.Message);
                return (frames, TimeSpan.FromMilliseconds(50));
            }
        }
    }

    /// <summary>One screen of spiral: WPF CreateSpiralGifWindow's borderless, transparent, topmost,
    /// no-taskbar, no-activate window holding a centred UniformToFill Image in a clipping Grid. The
    /// Win32 style mapping is <see cref="TintOverlayWindow"/>'s.</summary>
    internal sealed class SpiralOverlayWindow : Window
    {
        internal readonly Image Spiral = new()
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };

        public SpiralOverlayWindow()
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 640;
            Height = 400;
            Content = new Panel { ClipToBounds = true, Children = { Spiral } };
        }
    }
}
