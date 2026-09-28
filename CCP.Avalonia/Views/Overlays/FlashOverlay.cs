using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
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
    /// <para>Every window is override-redirect and click-through (X11Overlay). Where that is not
    /// available - Windows, a native Wayland backend, headless - nothing is shown and it is logged
    /// once: a topmost picture that swallows clicks is worse than no picture.</para>
    ///
    /// <para>ponytail: not here yet, each a later branch - the session scheduler (Start/Stop,
    /// FlashFrequency), audio + ducking (lifetime then follows the sound's length), clickable
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
        private static bool _busy, _warnedUnavailable, _warnedEmpty;

        /// <summary>Fire one burst. Any attached visual works as <paramref name="host"/>; it only
        /// reaches <c>Screens</c>. Skipped while the previous burst is still loading, like WPF.</summary>
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
            try
            {
                var s = CoreSettings.Current;
                var pictures = await Task.Run(() => LoadPictures(s.SimultaneousImages));
                if (pictures.Count == 0)
                {
                    if (!_warnedEmpty) Log.Warning("Flash: no images found in {Path}", ImagesPath());
                    _warnedEmpty = true;
                    return;
                }

                var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
                var targets = PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
                var lifetime = TimeSpan.FromMilliseconds(s.FlashDuration * 1000 + 1000);
                var fade = TimeSpan.FromSeconds(s.FadeDuration * FlashPlacement.FadeSecondsPerPercent);
                var alpha = Math.Clamp(s.FlashOpacity / 100.0, 0, 1);

                // WPF waits 1 s after FlashAboutToDisplay so the avatar's line lands first, then
                // staggers 300 ms per image. Dispatcher timers, not Task.Delay: measured on this
                // head, a Task.Delay(300) continuation intermittently landed 1.7 s late (thread-pool
                // timer), while dispatcher timers stayed on time.
                var refused = false;
                for (var i = 0; i < pictures.Count; i++)
                {
                    var bmp = pictures[i];
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (refused || Active.Count >= MaxConcurrent) { bmp.Dispose(); return; }
                        var screen = screens[targets[Rng.Next(targets.Length)]];
                        var rect = Place(screen.Bounds, screen.Scaling, bmp.PixelSize.Width, bmp.PixelSize.Height, s, Rng,
                            Active.Select(a => a.Rect));
                        try { refused = !Spawn(bmp, rect, alpha, fade, lifetime); }
                        catch (Exception ex) { refused = true; Log.Error(ex, "Flash: spawn failed"); }
                    }, TimeSpan.FromMilliseconds(1000 + i * StaggerMs));
                }
            }
            catch (Exception ex) { Log.Error(ex, "Flash: burst failed"); }
            finally { _busy = false; }
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

        /// <summary>The pre-map recipe proved by --overlay-check: override-redirect with geometry,
        /// then click-through, then Show. False (window closed) when the platform refuses.</summary>
        private static bool Spawn(Bitmap bmp, PixelRect rect, double alpha, TimeSpan fade, TimeSpan lifetime)
        {
            // Resample once to the display size, as WPF decodes at display size: the window then
            // holds a picture its own size, not a full-resolution source.
            var fitted = bmp.CreateScaledBitmap(rect.Size);
            bmp.Dispose();
            var w = new FlashOverlayWindow(fitted);
            if (!X11Overlay.SetOverrideRedirect(w, rect) || !X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOpacity(w, 0))
            {
                if (!_warnedUnavailable) Log.Warning("Flash: the X server refused an override-redirect click-through window; flashes skipped");
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

        private static string ImagesPath() => Path.Combine(CorePaths.EffectiveAssets, "images");

        /// <summary>
        /// Up to <paramref name="count"/> decoded pictures, drawn with replacement from the enabled
        /// images (FlashService.GetNextImages / GetMediaFiles), re-drawing past unreadable files the
        /// way LoadImagesUntilAsync does (at most max(count*5, 20) tries).
        /// </summary>
        private static List<Bitmap> LoadPictures(int count)
        {
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "images");
            var result = new List<Bitmap>(count);
            if (!Directory.Exists(dir)) return result;

            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => FlashPlacement.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            files = AssetFolderExclusion.Enabled(files, root, CoreSettings.Current);
            if (files.Count == 0) return result;

            for (var tries = Math.Max(count * 5, 20); result.Count < count && tries > 0; tries--)
            {
                var path = files[Rng.Next(files.Count)];
                try { result.Add(new Bitmap(path)); }
                catch (Exception ex) { Log.Debug("Flash: could not decode {Path}: {E}", path, ex.Message); }
            }
            return result;
        }
    }
}
