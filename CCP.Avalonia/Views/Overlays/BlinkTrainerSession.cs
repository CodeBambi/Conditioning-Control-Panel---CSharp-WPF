using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;               // MonitorTarget
using ConditioningControlPanel.Services.Webcam;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// A Blink Trainer session: WPF <c>BlinkTrainerService</c> (Services/BlinkTrainerService.cs). One
    /// transparent, topmost, click-through overlay per screen shows a random pool asset and swaps it on
    /// every <see cref="WebcamTracker.OnBlink"/>; auto-stops after the configured duration.
    /// Same refusals as <see cref="PinkFilterOverlay"/>: no click-through or no transparency = no overlay.
    /// ponytail: GIF/animated webp show their first frame (no animated decoder on this head); mix mode
    /// prefers same-aspect images only among those already shown (WPF warms the aspect cache in the
    /// background); the explicit "Tracking monitor" pick is not offered on this head, so placement is
    /// always the app-wide convention.
    /// </summary>
    internal static class BlinkTrainerSession
    {
        internal sealed class OverlayWindow : Window
        {
            public readonly Panel Host = new() { ClipToBounds = true };

            public OverlayWindow()
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
                Title = "BlinkTrainerOverlay";
                Width = 640;
                Height = 360;
                Content = Host;
            }

            public void PlaceOn(Screen screen)
            {
                var b = screen.Bounds;
                var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
                Position = new PixelPoint(b.X, b.Y);
                Width = b.Width / scale;
                Height = b.Height / scale;
            }

            public double Aspect => Width > 0 && Height > 0 ? Width / Height : 16.0 / 9.0;
        }

        private static readonly List<OverlayWindow> Overlays = new();
        private static readonly Dictionary<string, double> Aspects = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Bitmap> Shown = new();
        private static BlinkTrainerAssetPool? _pool;
        private static string? _last;
        private static DispatcherTimer? _duration;
        private static MediaPlayer? _player;
        private static Media? _media;
        private static VlcFrameSink? _sink;
        private static Window? _owner;

        /// <summary>Builds and shows the overlays; tests replace it (headless has no X11 to accept them).</summary>
        internal static Func<Visual, List<OverlayWindow>> CreateOverlays = DefaultCreateOverlays;

        public static bool IsRunning { get; private set; }
        public static string LastError { get; private set; } = "";
        public static DateTime? StartedAt { get; private set; }
        public static TimeSpan? Duration { get; private set; }
        internal static IReadOnlyList<OverlayWindow> Windows => Overlays;
        /// <summary>Tests shorten it to prove the auto-stop.</summary>
        internal static DispatcherTimer? DurationTimer => _duration;

        public static TimeSpan Remaining
        {
            get
            {
                if (!IsRunning || StartedAt == null || Duration == null) return TimeSpan.Zero;
                var rem = Duration.Value - (DateTime.UtcNow - StartedAt.Value);
                return rem < TimeSpan.Zero ? TimeSpan.Zero : rem;
            }
        }

        /// <summary>Fires when IsRunning flips (and after a refused start), on the UI thread.</summary>
        public static event Action? StateChanged;

        /// <summary>WPF Start: the same checks in the same order, the same error keys.</summary>
        public static bool Start(Visual host)
        {
            if (IsRunning) return true;
            try
            {
                var s = CoreSettings.Current;
                var pool = BlinkTrainerAssetPool.Build(s.BlinkTrainerFolders, s.BlinkTrainerIncludeVideos);
                if (pool.IsEmpty)
                    return Fail(s.BlinkTrainerFolders.Count == 0 ? Loc.Get("blink_trainer_error_no_folders") : Loc.Get("blink_trainer_error_no_assets"));
                if (!WebcamConsent.IsCurrent(s)) return Fail(Loc.Get("blink_trainer_error_no_consent"));
                // WPF #743: never start the engine from here; the caller brings the tracker up off the UI thread.
                if (!WebcamTracker.Instance.IsRunning) return Fail(Loc.Get("blink_trainer_error_webcam_not_running"));

                Overlays.AddRange(CreateOverlays(host));
                if (Overlays.Count == 0) { Cleanup(); return Fail(Loc.Get("blink_trainer_error_overlay_create")); }
                var opacity = Math.Clamp(s.BlinkTrainerOpacity, 1, 100) / 100.0;
                foreach (var ov in Overlays) ov.Host.Opacity = opacity;

                _pool = pool;
                _last = null;
                // WPF LabTab.cs:1000: Closing += Stop - Closing, not Closed, so a close cancelled to the tray still stops it.
                _owner = TopLevel.GetTopLevel(host) as Window;
                if (_owner != null) _owner.Closing += OnOwnerClosing;
                WebcamTracker.Instance.OnBlink += HandleBlink;
                Duration = TimeSpan.FromMinutes(Math.Clamp(s.BlinkTrainerDurationMinutes, 1, 180));
                StartedAt = DateTime.UtcNow;
                _duration = new DispatcherTimer { Interval = Duration.Value };
                _duration.Tick += (_, _) => { Log.Information("BlinkTrainer: duration elapsed — auto-stopping"); Stop(); };
                _duration.Start();
                IsRunning = true;
                global::ConditioningControlPanel.Services.SeasonFeatureTracker.TrackFeature(global::ConditioningControlPanel.Models.SeasonFeatureKeys.BlinkTrainer);   // WPF BlinkTrainerService:177
                LastError = "";
                Log.Information("BlinkTrainer: started — pool={Count} assets, duration={Mins}m, opacity={Opacity}%, screens={Screens}",
                    pool.Paths.Count, s.BlinkTrainerDurationMinutes, s.BlinkTrainerOpacity, Overlays.Count);
                ShowRandom();
                Raise();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BlinkTrainer: start failed");
                Cleanup();
                return Fail(Loc.Get("blink_trainer_error_overlay_create"));
            }
        }

        private static bool Fail(string error)
        {
            LastError = error;
            Raise();
            return false;
        }

        /// <summary>Bumped by every Stop, so a start queued behind the panic-shortcut bind can tell it was cancelled.</summary>
        internal static int Generation { get; private set; }

        public static void Stop()
        {
            Generation++;
            if (!IsRunning && Overlays.Count == 0) return;
            Cleanup();
            IsRunning = false;
            StartedAt = null;
            Duration = null;
            Log.Debug("BlinkTrainer: stopped");
            Raise();
        }

        private static void Raise()
        {
            try { StateChanged?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "BlinkTrainer StateChanged handler threw"); }
        }

        private static void OnOwnerClosing(object? sender, WindowClosingEventArgs e) => Stop();

        private static void Cleanup()
        {
            if (_owner != null) { _owner.Closing -= OnOwnerClosing; _owner = null; }
            WebcamTracker.Instance.OnBlink -= HandleBlink;
            _duration?.Stop();
            _duration = null;
            StopVideo();
            foreach (var ov in Overlays)
            {
                ov.Host.Children.Clear();
                try { ov.Close(); } catch (Exception ex) { Log.Debug("BlinkTrainer: overlay close failed: {E}", ex.Message); }
            }
            Overlays.Clear();
            DisposeShown();
            Aspects.Clear();
            _pool = null;
            _last = null;
        }

        /// <summary>WPF HandleBlink: swap, haptic pulse, quest credit.</summary>
        internal static void HandleBlink()
        {
            if (!IsRunning) return;
            ShowRandom();
            // WPF TriggerBlinkHaptic: off the UI thread, never blocking blink handling; gates itself.
            if (CoreHaptics.Service is { } haptics)
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try { await haptics.BlinkPulseAsync(); }
                    catch (Exception ex) { Log.Debug(ex, "BlinkTrainer: blink haptic failed"); }
                });
            try { App.Quests?.TrackBlinkTrainerBlink(); } catch (Exception ex) { Log.Debug("blink quest credit: {E}", ex.Message); }
        }

        private static void ShowRandom()
        {
            if (_pool == null || Overlays.Count == 0) return;
            var path = _pool.PickRandom(_last);
            if (path == null) return;
            _last = path;
            try
            {
                if (BlinkTrainerAssetPool.IsVideo(path)) { ShowVideo(path); return; }
                StopVideo();
                var mix = CoreSettings.Current.BlinkTrainerMixImages;
                var lead = Load(path);
                if (lead == null) return;
                foreach (var ov in Overlays) ov.Host.Children.Clear();
                DisposeShown();
                Shown.Add(lead);
                foreach (var ov in Overlays) Fill(ov, path, lead, mix);
            }
            catch (Exception ex) { Log.Warning(ex, "BlinkTrainer: failed to apply asset {Path}", path); }
        }

        /// <summary>WPF ApplyAsset / ApplyAssetMixed: one Uniform tile when aspects match, else a
        /// UniformToFill grid (Core <see cref="BlinkTrainerState.TileGrid"/>) of the lead, or in mix
        /// mode of other images of the same aspect bucket.</summary>
        private static void Fill(OverlayWindow ov, string leadPath, Bitmap lead, bool mix)
        {
            var aspect = lead.Size.Height > 0 ? lead.Size.Width / lead.Size.Height : 1.0;
            var (cols, rows) = BlinkTrainerState.TileGrid(aspect, ov.Aspect);
            if (cols == 1 && rows == 1) { ov.Host.Children.Add(new Image { Source = lead, Stretch = Stretch.Uniform }); return; }

            var grid = new UniformGrid { Columns = cols, Rows = rows };
            var extras = mix ? MixCandidates(leadPath, aspect) : new List<string>();
            for (int i = 0; i < cols * rows; i++)
            {
                Bitmap? bmp = lead;
                if (i > 0 && extras.Count > 0)
                {
                    bmp = Load(extras[(i - 1) % extras.Count]) ?? lead;
                    if (bmp != lead) Shown.Add(bmp);
                }
                grid.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill });
            }
            ov.Host.Children.Add(grid);
        }

        private static List<string> MixCandidates(string leadPath, double leadAspect)
        {
            var images = _pool!.Paths.Where(p => !BlinkTrainerAssetPool.IsVideo(p) && !string.Equals(p, leadPath, StringComparison.OrdinalIgnoreCase)).ToList();
            var bucket = images.Where(p => Aspects.TryGetValue(p, out var a) && Bucket(a) == Bucket(leadAspect)).ToList();
            var list = bucket.Count >= 2 ? bucket : images;   // WPF GetCompatibleImages fallback
            return list.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        private static int Bucket(double aspect) => aspect < 0.85 ? 0 : aspect > 1.15 ? 2 : 1;

        private static Bitmap? Load(string path)
        {
            try
            {
                var bmp = new Bitmap(path);
                if (bmp.Size.Height > 0) Aspects[path] = bmp.Size.Width / bmp.Size.Height;
                return bmp;
            }
            catch (Exception ex) { Log.Warning(ex, "BlinkTrainer: bitmap load failed for {Path}", path); return null; }
        }

        private static void DisposeShown()
        {
            foreach (var b in Shown.Distinct()) b.Dispose();
            Shown.Clear();
        }

        /// <summary>WPF ApplyVideoToAllOverlays: one muted decoder, looped, every screen shows its frames.</summary>
        private static void ShowVideo(string path)
        {
            var vlc = LibVlcAudio.Shared;
            if (vlc == null) { Log.Warning("BlinkTrainer: LibVLC unavailable - cannot play {Path}", path); return; }
            StopVideo();
            foreach (var ov in Overlays) ov.Host.Children.Clear();
            DisposeShown();
            var images = Overlays.Select(ov => { var i = new Image { Stretch = Stretch.UniformToFill }; ov.Host.Children.Add(i); return i; }).ToList();
            _player = new MediaPlayer(vlc) { EnableHardwareDecoding = true, Mute = true };
            _sink = new VlcFrameSink(_player, () => _media, bmp => images.ForEach(i => i.Source = bmp), () => images.ForEach(i => i.InvalidateVisual()));
            _media = new Media(vlc, path, FromType.FromPath);
            _media.AddOption(":no-audio");
            _media.AddOption(":input-repeat=65535");   // WPF MediaEnded -> Position 0 -> Play
            _player.Play(_media);
        }

        private static void StopVideo()
        {
            if (_player != null)
            {
                try { _player.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }   // joins the decoder thread
                try { _player.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            _player = null;
            try { _media?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _media = null;
            _sink?.Free();
            _sink = null;
        }

        /// <summary>WPF GazeContentScreenPolicy minus the explicit pick: DualMonitorEnabled ? every
        /// screen : the primary. Each window must accept click-through and transparency or none show.</summary>
        private static List<OverlayWindow> DefaultCreateOverlays(Visual host)
        {
            var made = new List<OverlayWindow>();
            if (!X11Overlay.IsAvailable) { Log.Warning("BlinkTrainer: no X11 display, so no click-through overlay on this platform"); return made; }
            var screens = ScreenList.Enumerate(host);
            var primary = -1;
            for (var i = 0; i < screens.Count; i++) if (screens[i].IsPrimary) { primary = i; break; }
            foreach (var i in PinkFilterOverlay.ResolveScreenIndices(MonitorTarget.FollowGlobal, CoreSettings.Current.DualMonitorEnabled, screens.Count, primary))
            {
                var w = new OverlayWindow();
                w.PlaceOn(screens[i]);
                w.Show();
                made.Add(w);
                if (!X11Overlay.SetClickThrough(w, true) || w.ActualTransparencyLevel == WindowTransparencyLevel.None)
                {
                    Log.Warning("BlinkTrainer: overlay refused (no click-through or no transparency) - a topmost full-screen window that swallows clicks or paints opaque would lock the desktop");
                    foreach (var m in made) m.Close();
                    made.Clear();
                    break;
                }
            }
            return made;
        }
    }
}
