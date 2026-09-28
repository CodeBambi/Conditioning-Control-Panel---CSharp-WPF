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
    /// GIF animation (first frame only), glow, content-pack and remote pools, avatar pre-announce.</para>
    /// </summary>
    internal static class FlashOverlay
    {
        // FlashService.ResolveFlashCap on the default (compositor) path.
        private const int MaxConcurrent = 30;
        private const int StaggerMs = 300;

        private static readonly List<(FlashOverlayWindow Window, PixelRect Rect)> Active = new();
        private static readonly Random Rng = new();
        private static bool _busy, _warnedUnavailable, _warnedEmpty, _closed;
        private static int _generation;

        /// <summary>Bumped by every CloseAll; a burst scheduled under an older one never spawns.</summary>
        internal static int Generation => _generation;

        /// <summary>A burst is still spawning (WPF <c>_isBusy</c>); the ambient tick skips.</summary>
        public static bool IsBusy => _busy;

        /// <summary>Fire one burst. Any attached visual works as <paramref name="host"/>; it only
        /// reaches <c>Screens</c>. Like WPF's _isBusy, a second press is ignored from the click
        /// until the burst's last flash has spawned.</summary>
        public static async void TriggerOnce(Visual host)
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
            var generation = _generation;
            var scheduled = false;
            try
            {
                var s = CoreSettings.Current;
                var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
                var targets = PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
                var occupied = Active.Select(a => a.Rect).ToList();
                var flashes = await Task.Run(() => LoadPictures(s.SimultaneousImages, screens, targets, s, occupied));
                if (flashes.Count == 0)
                {
                    if (!_warnedEmpty) Log.Warning("Flash: no images found in {Path}", ImagesPath());
                    _warnedEmpty = true;
                    return;
                }

                var lifetime = TimeSpan.FromMilliseconds(s.FlashDuration * 1000 + 1000);
                var fade = TimeSpan.FromSeconds(s.FadeDuration * FlashPlacement.FadeSecondsPerPercent);
                var alpha = Math.Clamp(s.FlashOpacity / 100.0, 0, 1);

                // WPF waits 1 s after FlashAboutToDisplay so the avatar's line lands first, then
                // staggers 300 ms per image. Dispatcher timers, not Task.Delay: measured on this
                // head, a Task.Delay(300) continuation intermittently landed 1.7 s late (thread-pool
                // timer), while dispatcher timers stayed on time.
                var refused = false;
                for (var i = 0; i < flashes.Count; i++)
                {
                    var (bmp, rect, path) = flashes[i];
                    var last = i == flashes.Count - 1;
                    DispatcherTimer.RunOnce(() =>
                    {
                        try
                        {
                            if (refused || _closed || generation != _generation || Active.Count >= MaxConcurrent) { bmp.Dispose(); return; }
                            refused = !Spawn(bmp, rect, alpha, fade, lifetime);
                            // WPF FlashService.cs:1608 records the batch; per shown image here, so the
                            // log's media count is exactly what reached the screen.
                            if (!refused) App.Sessions?.SessionLog.RecordImages(new[] { path });
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
            IEnumerable<PixelRect> occupied)
        {
            var k = scaling > 0 ? scaling : 1.0;
            int monW = (int)(screen.Width / k), monH = (int)(screen.Height / k);
            var (w, h) = FlashPlacement.FitSize(imgW, imgH, monW, monH, s.ImageScale / 100.0);

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
        private static bool Spawn(Bitmap bmp, PixelRect rect, double alpha, TimeSpan fade, TimeSpan lifetime)
        {
            var w = new FlashOverlayWindow(bmp);
            if (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOpacity(w, 0) || !X11Overlay.SetOverrideRedirect(w, rect))
            {
                if (!_warnedUnavailable) Log.Warning("Flash: the platform refused a click-through topmost overlay window; flashes skipped");
                _warnedUnavailable = true;
                w.Close();
                return false;
            }
            var entry = (w, rect);
            w.Closed += (_, _) => Active.Remove(entry);
            Active.Add(entry);
            w.Show();
            w.Run(alpha, fade, lifetime);
            return true;
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

        private static string ImagesPath() => Path.Combine(CorePaths.EffectiveAssets, "images");

        /// <summary>
        /// Up to <paramref name="count"/> placed pictures, drawn with replacement from the enabled
        /// images (FlashService.GetNextImages / GetMediaFiles), re-drawing past unreadable files the
        /// way LoadImagesUntilAsync does (at most max(count*5, 20) tries). Placement happens first,
        /// from the header size alone, so each picture is decoded AT its display size like WPF's
        /// decode-at-display-size - never a full-resolution source held per window.
        /// </summary>
        private static List<(Bitmap Bitmap, PixelRect Rect, string Path)> LoadPictures(int count, IReadOnlyList<Screen> screens,
            int[] targets, AppSettings s, List<PixelRect> occupied)
        {
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "images");
            var result = new List<(Bitmap, PixelRect, string)>(count);
            if (!Directory.Exists(dir) || targets.Length == 0) return result;

            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => FlashPlacement.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            files = AssetFolderExclusion.Enabled(files, root, s);
            if (files.Count == 0) return result;

            for (var tries = Math.Max(count * 5, 20); result.Count < count && tries > 0; tries--)
            {
                var path = files[Rng.Next(files.Count)];
                try
                {
                    SkiaSharp.SKImageInfo info;
                    using (var codec = SkiaSharp.SKCodec.Create(path))
                    {
                        if (codec is null) continue;
                        info = codec.Info;
                    }
                    var screen = screens[targets[Rng.Next(targets.Length)]];
                    var rect = Place(screen.Bounds, screen.Scaling, info.Width, info.Height, s, Rng, occupied);
                    using var stream = File.OpenRead(path);
                    result.Add((Bitmap.DecodeToWidth(stream, rect.Width), rect, path));
                    occupied.Add(rect);
                }
                catch (Exception ex) { Log.Debug("Flash: could not decode {Path}: {E}", path, ex.Message); }
            }
            return result;
        }
    }
}
