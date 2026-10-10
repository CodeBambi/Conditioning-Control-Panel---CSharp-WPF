using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>One caller's hold on the spiral: up at <see cref="Opacity"/> (0..1, painted as given)
    /// whatever the user's own Spiral switch says, which is never written.</summary>
    /// <param name="Path">The file to show instead of the user's spiral (the Back Room's woven one); null = the user's.</param>
    /// <param name="AllScreens">Every monitor instead of the user's target monitor.</param>
    /// <param name="Slow">Half speed (reduced motion), never a still.</param>
    internal sealed record SpiralHold(double Opacity, string? Path = null, bool AllScreens = false, bool Slow = false);

    // The hold entry (WPF OverlayService.ShowOverlaySustained / ShowOverlayTimed for "spiral", and the
    // Back Room's own Loom spiral window) and the video spiral (WPF MediaElement).
    internal static partial class SpiralOverlay
    {
        /// <summary>Owner of the Deeper overlay bands' hold.</summary>
        internal const string DeeperOwner = "deeper";
        /// <summary>Owner of the Back Room's hold. It outranks any other while it is up.</summary>
        internal const string RoomOwner = "backroom";

        private static readonly Dictionary<string, SpiralHold> Holds = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, DispatcherTimer> HoldTimers = new(StringComparer.Ordinal);

        /// <summary>The hold the spiral follows now, or null when only the user's switch decides.</summary>
        internal static SpiralHold? ActiveHold =>
            Holds.Count == 0 ? null : Holds.TryGetValue(RoomOwner, out var room) ? room : Holds.Values.First();

        internal static bool IsHeldBy(string owner) => Holds.ContainsKey(owner);

        /// <summary>
        /// Show the spiral for <paramref name="owner"/> regardless of the user's switch. With
        /// <paramref name="durationMs"/> above 0 the hold lets go by itself after that long; otherwise it
        /// stays until <see cref="Release"/>. Calling it again for the same owner replaces the hold (a
        /// caller's fade is a run of these). UI thread.
        /// </summary>
        internal static void Hold(Visual? host, string owner, SpiralHold hold, int durationMs = 0)
        {
            Holds[owner] = hold;
            if (HoldTimers.Remove(owner, out var old)) old.Stop();
            if (durationMs > 0)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, durationMs)) };
                t.Tick += (_, _) => Release(owner, HoldHost?.Invoke() ?? host);
                HoldTimers[owner] = t;
                t.Start();
            }
            if (host != null) Refresh(host);
        }

        /// <summary>Where a timed hold repaints from when it ends (the shell). Null = the visual it was shown from.</summary>
        internal static Func<Visual?>? HoldHost;

        /// <summary>Let go. The spiral goes back to the user's own switch: down at once when that is off.
        /// Never behind an effect door: a stop must not wait.</summary>
        internal static void Release(string owner, Visual? host)
        {
            if (HoldTimers.Remove(owner, out var t)) t.Stop();
            if (!Holds.Remove(owner)) return;
            if (host != null) Refresh(host);
            else if (ActiveHold == null) CloseAll();   // no visual to ask the screens: nothing may stay up on a dead hold
        }

        /// <summary>Panic, suspend, shutdown: every hold goes and the windows with them.</summary>
        internal static void ReleaseAllHolds()
        {
            foreach (var t in HoldTimers.Values) t.Stop();
            HoldTimers.Clear();
            if (Holds.Count == 0) return;
            Holds.Clear();
            CloseAll();
        }

        // ---- the video spiral ------------------------------------------------------------------

        private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v", ".wmv" };

        /// <summary>A spiral file that plays instead of decoding to frames.</summary>
        internal static bool IsVideo(string? path) =>
            !string.IsNullOrEmpty(path) && VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        private static bool _video, _shownSlow;
        private static IDisposable? _videoPlayer;
        private static int _videoGen;

        /// <summary>A video spiral is playing into the windows.</summary>
        internal static bool IsVideoPlaying => _videoPlayer != null;

        /// <summary>The decode size of a video spiral's long side and its frame gap (30 fps).</summary>
        internal const int VideoLongSide = 960, VideoFrameGapMs = 33;

        /// <summary>
        /// Starts the silent, looping player for <c>path</c> and hands every frame to the callback (UI
        /// thread). Null when nothing can play. Tests swap it: headless runs decode no video. The default
        /// is LibVLC through <see cref="FlashClipPlayer"/>, whose media carries <c>:no-audio</c> (never
        /// Mute or Volume: those are process-wide).
        /// </summary>
        internal static Func<string, bool, Action<Bitmap>, Task<IDisposable?>> VideoStart = StartWithVlc;

        private static async Task<IDisposable?> StartWithVlc(string path, bool slow, Action<Bitmap> show)
        {
            if (LibVlcAudio.Shared is not { } vlc) return null;
            var (w, h) = await Task.Run(() => ProbeVideoSize(vlc, path));
            return FlashClipPlayer.Start(vlc, path, w, h, frame => show(frame), slow ? 0.5 : 1, VideoLongSide, VideoFrameGapMs);
        }

        /// <summary>The clip's own picture size (a local parse, 2 s cap), 16:9 when it does not say.</summary>
        private static (int W, int H) ProbeVideoSize(LibVLC vlc, string path)
        {
            try
            {
                using var media = new Media(vlc, path, FromType.FromPath);
                var status = media.Parse(MediaParseOptions.ParseLocal, 2000).GetAwaiter().GetResult();
                if (status == MediaParsedStatus.Done)
                    foreach (var track in media.Tracks)
                        if (track.TrackType == TrackType.Video && track.Data.Video.Width > 0 && track.Data.Video.Height > 0)
                            return ((int)track.Data.Video.Width, (int)track.Data.Video.Height);
            }
            catch (Exception ex) { Log.Debug("Spiral: video size probe failed: {E}", ex.Message); }
            return (1280, 720);
        }

        private static async void StartVideo(string path, bool slow)
        {
            StopVideo();
            var gen = _videoGen;
            try
            {
                var player = await VideoStart(path, slow, ShowVideoFrame);
                // The spiral went down, or moved to another file, while the player was starting.
                if (gen != _videoGen || Windows.Count == 0) { player?.Dispose(); return; }
                if (player == null) { Log.Warning("Spiral: the video spiral {Path} cannot play here", path); return; }
                _videoPlayer = player;
            }
            catch (Exception ex) { Log.Warning("Spiral: video spiral failed to start: {E}", ex.Message); }
        }

        private static void ShowVideoFrame(Bitmap frame)
        {
            for (var i = 0; i < Windows.Count; i++)
            {
                var image = Windows[i].Spiral;
                if (!ReferenceEquals(image.Source, frame)) image.Source = frame;
                image.InvalidateVisual();   // the same bitmap, new pixels
            }
        }

        private static void StopVideo()
        {
            _videoGen++;
            var player = _videoPlayer;
            _videoPlayer = null;
            try { player?.Dispose(); } catch (Exception ex) { Log.Debug("Spiral: video stop: {E}", ex.Message); }
        }
    }
}
