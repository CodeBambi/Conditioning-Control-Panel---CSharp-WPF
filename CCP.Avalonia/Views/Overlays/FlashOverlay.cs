using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// One burst of flash images, the head half of WPF's <c>FlashService.TriggerFlashOnce()</c>
    /// with default arguments (ConditioningControlPanel/Services/Flash/FlashService.cs:696):
    /// SimultaneousImages pictures from the enabled pool, each on a random targeted monitor,
    /// sized and placed by <see cref="FlashPlacement"/> (the math WPF itself calls), staggered
    /// 300 ms, faded by the Fade/Opacity sliders, living FlashDuration + 1 s.
    ///
    /// <para>Every window is override-redirect and click-through (X11Overlay; on Windows its
    /// Win32Overlay half). Where that is not available - a native Wayland backend, headless -
    /// nothing is shown and it is logged
    /// once: a topmost picture that swallows clicks is worse than no picture.</para>
    ///
    /// <para>The ambient rhythm is Core <c>CoreFlash</c>, which calls this.</para>
    ///
    /// <para>ponytail: not here yet, each a later branch - audio + ducking (lifetime then follows the sound's length), clickable
    /// flashes (hydra multiply / XP / pops - FlashClickable is ignored, always click-through),
    /// the Pendulum motion style (Drift and Bounce is below), glow, content-pack and remote pools, avatar pre-announce.</para>
    /// </summary>
    internal static partial class FlashOverlay
    {
        // FlashService.ResolveFlashCap on the default (compositor) path.
        private const int MaxConcurrent = 30;
        private const int StaggerMs = 300;

        internal static readonly List<(FlashOverlayWindow Window, PixelRect Rect)> Active = new();
        private static readonly Random Rng = new();
        // #627: one shuffled walk of the folder (WPF FlashService DiskBag), so every image comes up
        // before any repeats. Not thread-safe: LoadPictures draws under the bag's own lock.
        internal static readonly ShuffleBag<string> DiskBag = new(p => p, Rng);
        private static bool _busy, _warnedUnavailable, _warnedEmpty, _closed;
        private static int _generation;

        /// <summary>Bumped by every CloseAll; a burst scheduled under an older one never spawns.</summary>
        internal static int Generation => _generation;

        /// <summary>A burst is still spawning (WPF <c>_isBusy</c>); the ambient tick skips.</summary>
        public static bool IsBusy => _busy;

        /// <summary>Fire one burst. Any attached visual works as <paramref name="host"/>; it only
        /// reaches <c>Screens</c>. Like WPF's _isBusy, a second press is ignored from the click
        /// until the burst's last flash has spawned. The overrides are WPF TriggerFlashOnce's
        /// (amount, duration ms, size %); null keeps the user's setting.</summary>
        public static async void TriggerOnce(Visual host, int? amount = null, int? durationMs = null, int? size = null)
        {
            if (_busy) return;
            if (!X11Overlay.IsAvailable)
            {
                if (!_warnedUnavailable) Log.Warning("Flash: this platform cannot show click-through overlays, so flashes are skipped");
                _warnedUnavailable = true;
                return;
            }

            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return;
            _busy = true;
            _fxHost = host;   // FlashOverlay.Fx.cs: hydra children re-read the screens
            CoreTubeEvents.RaiseFlashAboutToDisplay();   // WPF FlashService.FlashAboutToDisplay (tube#T5)
            var generation = _generation;
            var scheduled = false;
            try
            {
                var s = CoreSettings.Current;
                var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
                var targets = PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
                var occupied = Active.Select(a => a.Rect).ToList();
                var flashes = await Task.Run(() => LoadPictures(BurstCount(amount, s, Rng), screens, targets, s, occupied, size));
                if (flashes.Count == 0)
                {
                    if (!_warnedEmpty) Log.Warning("Flash: no images found in {Path} and no online clips ready yet (remote on: {Remote})", ImagesPath(), FlashSourceRules.RemoteEnabled(CoreSettings.Current));
                    _warnedEmpty = true;
                    return;
                }

                var lifetime = TimeSpan.FromMilliseconds((durationMs ?? s.FlashDuration * 1000) + 1000);
                var fade = TimeSpan.FromSeconds(s.FadeDuration * FlashPlacement.FadeSecondsPerPercent);
                var alpha = Math.Clamp(s.FlashOpacity / 100.0, 0, 1);

                // WPF waits 1 s after FlashAboutToDisplay so the avatar's line lands first, then
                // staggers 300 ms per image. Dispatcher timers, not Task.Delay: measured on this
                // head, a Task.Delay(300) continuation intermittently landed 1.7 s late (thread-pool
                // timer), while dispatcher timers stayed on time.
                var refused = false;
                for (var i = 0; i < flashes.Count; i++)
                {
                    var (bmp, rect, path, screen, frames) = flashes[i];
                    var last = i == flashes.Count - 1;
                    DispatcherTimer.RunOnce(() =>
                    {
                        try
                        {
                            if (refused || _closed || generation != _generation || Active.Count >= SpawnCap(CoreSettings.Current))
                            {
                                bmp.Dispose();
                                if (frames != null) foreach (var f in frames.Value.Frames) f.Dispose();
                                return;
                            }
                            refused = !Spawn(bmp, rect, screen, alpha, fade, lifetime, frames);
                            // WPF FlashService.cs:1608 records the batch; per shown image here, so the
                            // log's media count is exactly what reached the screen.
                            if (!refused) App.Sessions?.SessionLog.RecordImages(new[] { path });
                            if (!refused) App.MediaHistory?.RecordImages(new[] { path });   // WPF MediaHistoryService.OnFlashDisplayed
                            // WPF FlashService.cs:2102 -> AchievementService.cs:416: one quest tick per image.
                            if (!refused) App.Quests?.TrackFlashImage();
                        }
                        catch (Exception ex) { refused = true; Log.Error(ex, "Flash: spawn failed"); }
                        finally { if (last) _busy = false; }
                    }, TimeSpan.FromMilliseconds(1000 + i * StaggerMs));
                }
                scheduled = true;
            }
            catch (Exception ex) { Log.Error(ex, "Flash: burst failed"); }
            finally { if (!scheduled) _busy = false; }
        }

        /// <summary>
        /// Where one flash goes on one screen, in physical pixels. WPF does the math in that
        /// monitor's DIPs (bounds / DPI scale, truncated) - the 40% fit, the 50-DIP edge padding,
        /// the #770 avoid-center box, 10 re-rolls away from &gt;30% overlap - and so does this, then
        /// scales the answer back onto the screen.
        /// </summary>
        internal static PixelRect Place(PixelRect screen, double scaling, int imgW, int imgH, AppSettings s, Random rng,
            IEnumerable<PixelRect> occupied, int? size = null)
        {
            var k = scaling > 0 ? scaling : 1.0;
            int monW = (int)(screen.Width / k), monH = (int)(screen.Height / k);
            var (w, h) = FlashPlacement.FitSize(imgW, imgH, monW, monH, (size ?? s.ImageScale) / 100.0);

            PixelRect ToPx(int x, int y) => new(screen.X + (int)(x * k), screen.Y + (int)(y * k), (int)(w * k), (int)(h * k));
            var others = occupied.Select(r => (r.X, r.Y, r.Width, r.Height)).ToList();

            PixelRect Pick()
            {
                var (x, y) = FlashPlacement.PickSpawnPoint(0, 0, monW, monH, w, h, s.FlashAvoidCenter,
                    s.FlashCenterExclusionPercent, rng, out var fellBack);
                if (fellBack) Log.Debug("Flash avoid-center: no legal band for {W}x{H} on {MW}x{MH}; unconstrained", w, h, monW, monH);
                return ToPx(x, y);
            }

            var rect = Pick();
            for (var attempt = 0; attempt < 10 && FlashPlacement.IsOverlapping(rect.X, rect.Y, rect.Width, rect.Height, others); attempt++)
                rect = Pick();
            return rect;
        }

        /// <summary>The pre-map recipe proved by --overlay-check, with every attribute requested
        /// BEFORE SetOverrideRedirect so its XSync covers them: the window maps already
        /// click-through and at alpha 0, never as one opaque or clickable frame. False (window
        /// closed) when the platform refuses.</summary>
        private static bool Spawn(Bitmap bmp, PixelRect rect, PixelRect screen, double alpha, TimeSpan fade, TimeSpan lifetime,
            (List<Bitmap> Frames, TimeSpan Delay)? anim = null)
        {
            FlashOverlayWindow w;
            if (anim is { } a)
            {
                bmp.Dispose();   // the still is only the fallback; frame 0 stands in for it
                w = new FlashOverlayWindow(a.Frames, FlashGifFrames.ScaleFrameDelay(a.Delay, CoreSettings.Current.FlashGifSpeedMultiplier));
            }
            else w = new FlashOverlayWindow(bmp);
            var clickable = CoreSettings.Current.FlashClickable;
            if (clickable)
            {
                w.MakeClickable();
                // WPF OnFlashClicked: tube event, haptic, hydra (FlashOverlay.Fx.cs).
                w.Popped += fromGaze => OnFlashPopped(w, fromGaze);
            }
            lifetime = ResolveLifetime(lifetime, CoreSettings.Current);   // stay-until-popped / hydra child
            rect = ApplyFx(w, rect, screen, lifetime, CoreSettings.Current, Rng);   // lucky, glow, corners, XP
            if (!X11Overlay.SetClickThrough(w, !clickable) || !X11Overlay.SetOpacity(w, 0) || !X11Overlay.SetOverrideRedirect(w, rect))
            {
                if (!_warnedUnavailable) Log.Warning("Flash: the platform refused a click-through topmost overlay window; flashes skipped");
                _warnedUnavailable = true;
                w.Close();
                return false;
            }
            var entry = (w, rect);
            w.Closed += (_, _) => Active.RemoveAll(e => e.Window == w);
            Active.Add(entry);
            w.Show();
            w.Run(alpha, fade, lifetime);
            if (BuildMotion(rect, screen, CoreSettings.Current, Rng) is { } motion) StartDrift(w, motion);
            return true;
        }

        // ---- Flashes v2 motion: Drift and Bounce (WPF FlashService.SpawnLayerVisual + FlashLayer tick) ----

        /// <summary>Stepped clock for the drift tick (tests swap it).</summary>
        internal static TimeProvider Clock = TimeProvider.System;
        internal static readonly List<(Window Window, FlashMotionState Motion)> Drifting = new();
        private static DispatcherTimer? _driftTimer;
        private static long _lastDriftTick;

        /// <summary>
        /// The motion one flash plays, or null for Still. WPF ResolveMotionStyle + FlashMotion.Create
        /// + the #1265 speed (FlashDriftSpeed x the rolled velocity), in screen pixels over the spawn
        /// monitor, like WPF's world px. Ownership is <see cref="PrizeOwnership"/>; this head has no
        /// pendulum yet, so a Pendulum pick (synced profile) plays Still and Mix rolls Still or Drift.
        /// </summary>
        internal static FlashMotionState? BuildMotion(PixelRect rect, PixelRect screen, AppSettings s, Random rng)
        {
            if (s.FlashMotionStyle == FlashMotionStyle.Still) return null;
            var style = FlashMotion.Resolve(s.FlashMotionStyle, PrizeOwnership.IsGranted(PrizeOwnership.FlashDriftBounce),
                ownsPendulum: false, s.MotionLevel, rng);
            if (style != FlashMotionStyle.DriftBounce) return null;
            var m = FlashMotion.Create(style, rect.X, rect.Y, rect.Width, rect.Height,
                screen.X, screen.Y, screen.Width, screen.Height, s.MotionLevel, rng);
            if (m.Style != FlashMotionStyle.DriftBounce) return null;
            m.Vx *= s.FlashDriftSpeed;
            m.Vy *= s.FlashDriftSpeed;
            return m;
        }

        /// <summary>Moves <paramref name="w"/> with <paramref name="m"/> on the one shared tick, which
        /// runs only while some flash drifts: closing the last (expiry, Stop, panic via CloseAll)
        /// stops it.</summary>
        internal static void StartDrift(Window w, FlashMotionState m)
        {
            var entry = (w, m);
            Drifting.Add(entry);
            w.Closed += (_, _) =>
            {
                Drifting.Remove(entry);
                if (Drifting.Count == 0) { _driftTimer?.Stop(); _driftTimer = null; }
            };
            if (_driftTimer != null) return;
            _lastDriftTick = Clock.GetTimestamp();
            _driftTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => DriftTick());
            _driftTimer.Start();
        }

        /// <summary>True while the shared drift tick is running.</summary>
        internal static bool DriftRunning => _driftTimer != null;

        /// <summary>One frame: real elapsed time since the last (P40), FlashMotion.Step, and a window
        /// move only when the position changed - a move, never a re-render.</summary>
        internal static void DriftTick()
        {
            var now = Clock.GetTimestamp();
            var dt = Clock.GetElapsedTime(_lastDriftTick, now).TotalSeconds;
            _lastDriftTick = now;
            foreach (var (w, m) in Drifting)
                if (FlashMotion.Step(m, dt))
                {
                    var p = new PixelPoint((int)Math.Round(m.X), (int)Math.Round(m.Y));
                    w.Position = p;
                    // The overlap check of later bursts reads the LIVE rect, as WPF reads MotionState X/Y.
                    for (var i = 0; i < Active.Count; i++)   // a loop, not FindIndex: no closure per frame
                        if (Active[i].Window == w) Active[i] = (Active[i].Window, new PixelRect(p, Active[i].Rect.Size));
                }
        }

        /// <summary>Close every flash on screen and drop the spawns still queued. <paramref name="final"/>
        /// (shell closing) refuses every later burst; the tray's Stop everything passes false so a
        /// re-enabled flash can show again.</summary>
        public static void CloseAll(bool final = true)
        {
            _generation++;
            _closed |= final;
            foreach (var (w, _) in Active.ToList()) w.Close();
        }

        /// <summary>Images in one burst (WPF TriggerFlashOnce, #658): the caller's override, else
        /// the flat count or a roll between Fewest and Images, rolled once per burst.</summary>
        internal static int BurstCount(int? amount, AppSettings s, Random rng) =>
            amount ?? AppSettings.RollFlashImageCount(s.SimultaneousImagesRandom, s.SimultaneousImagesMin, s.SimultaneousImages, rng);

        /// <summary>The next file of the shuffled walk; the caller re-lists the folder per burst.</summary>
        internal static string? NextPath(IReadOnlyList<string> files)
        {
            lock (DiskBag) return DiskBag.TryNext(files, out var p) ? p : null;
        }

        private static string ImagesPath() => Path.Combine(CorePaths.EffectiveAssets, "images");

        /// <summary>
        /// Up to <paramref name="count"/> placed pictures, dealt from <see cref="DiskBag"/> over the enabled
        /// images (FlashService.GetNextImages / GetMediaFiles), re-drawing past unreadable files the
        /// way LoadImagesUntilAsync does (at most max(count*5, 20) tries). Placement happens first,
        /// from the header size alone, so each picture is decoded AT its display size like WPF's
        /// decode-at-display-size - never a full-resolution source held per window.
        /// </summary>
        private static List<(Bitmap Bitmap, PixelRect Rect, string Path, PixelRect Screen, (List<Bitmap> Frames, TimeSpan Delay)? Anim)> LoadPictures(int count, IReadOnlyList<Screen> screens,
            int[] targets, AppSettings s, List<PixelRect> occupied, int? size)
        {
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "images");
            var result = new List<(Bitmap, PixelRect, string, PixelRect, (List<Bitmap> Frames, TimeSpan Delay)?)>(count);
            if (targets.Length == 0) return result;

            var files = Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => FlashPlacement.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList()
                : new List<string>();
            files = AssetFolderExclusion.Enabled(files, root, s);

            // WPF GetNextImages: the online clip pool is the third source beside disk (and packs).
            // Non-blocking: kicks a background top-up and reads what is already warm.
            RemoteFlashSource.EnsurePrefetch();
            var haveLocal = files.Count > 0;
            var remoteReady = RemoteFlashSource.ReadyCount;
            if (!haveLocal && remoteReady == 0) return result;

            for (var tries = Math.Max(count * 5, 20); result.Count < count && tries > 0; tries--)
            {
                string path, identity;
                var remote = remoteReady > 0 && FlashSourceRules.ShouldDrawRemote(s, haveLocal, Rng);
                if (remote && RemoteFlashSource.TryTake(Rng) is { } item)
                {
                    path = item.PosterPath;   // decoded like any local still
                    identity = item.Url;      // history + session log key on the source, never the temp file
                }
                else
                {
                    if (remote) { remoteReady = 0; if (!haveLocal) break; }   // pool went cold: silently local
                    if (NextPath(files) is not { } local) break;
                    path = identity = local;
                }
                try
                {
                    SkiaSharp.SKImageInfo info;
                    using (var codec = SkiaSharp.SKCodec.Create(path))
                    {
                        if (codec is null) continue;
                        info = codec.Info;
                    }
                    var screen = screens[targets[Rng.Next(targets.Length)]];
                    var rect = Place(screen.Bounds, screen.Scaling, info.Width, info.Height, s, Rng, occupied, size);
                    using var stream = File.OpenRead(path);
                    // WPF LoadGifFrames / TryLoadAnimatedWebpFrames: an animated file plays, at display size.
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    var anim = ext is ".gif" or ".webp" ? FlashGifFrames.Decode(path, rect.Width, rect.Height) : null;
                    result.Add((Bitmap.DecodeToWidth(stream, rect.Width), rect, identity, screen.Bounds, anim));
                    occupied.Add(rect);
                }
                catch (Exception ex) { Log.Debug("Flash: could not decode {Path}: {E}", path, ex.Message); }
            }
            return result;
        }
    }
}
