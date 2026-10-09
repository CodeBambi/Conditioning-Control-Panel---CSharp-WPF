using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The media half of WPF FlashService on this head: the burst's voice line (GetNextSound,
    /// PlaySound, ducking, lifetime following the clip), moving online clips (FlashClipPlayer),
    /// the chaos picture pool (GetChaosImagePaths), the file-cache drop on an asset selection
    /// change (ClearFileCache) and the online temp folder sweep at exit.
    /// </summary>
    internal static partial class FlashOverlay
    {
        private static Action? _stopSound;
        private static bool _mediaSeeded;

        /// <summary>Poster bitmap -> downloaded clip of an online flash (set by LoadPictures).</summary>
        private static readonly ConditionalWeakTable<Bitmap, string> Clips = new();

        /// <summary>Once at startup: asset selection changes drop the cached lists, and the chaos
        /// overlays deal from this pool.</summary>
        internal static void SeedMedia()
        {
            if (_mediaSeeded) return;
            _mediaSeeded = true;
            AssetSelection.Changed += ClearFileCache;
            ChaosImagePool.PathsProvider = GetChaosImagePaths;
        }

        /// <summary>WPF ClearFileCache: the next draw re-deals the disk walk, refetches online clips
        /// for the current selection and rebuilds the voice-line queue.</summary>
        internal static void ClearFileCache()
        {
            lock (DiskBag) DiskBag.Reset();
            FlashVoicePool.Reset();
            RemoteFlashSource.ClearReady();
            try { Dispatcher.UIThread.Post(ChaosImagePool.Invalidate); } catch { ChaosImagePool.Invalidate(); }
        }

        /// <summary>Shell exit: stop the flash clip and sweep the online temp folder.</summary>
        internal static void ShutdownMedia()
        {
            StopFlashSound();
            RemoteFlashSource.CleanupCache();
        }

        // ---- the burst's voice line ----

        /// <summary>The clip a burst speaks and its length in seconds, or null (audio off, voice
        /// silenced, empty pool). Off the UI thread: the length probe parses the file.</summary>
        internal static (string Path, double Seconds)? PickSound(AppSettings s)
        {
            if (!FlashVoicePool.ShouldPlay(s)) return null;
            var path = FlashVoicePool.Next(s);
            if (path == null || !File.Exists(path)) return null;
            return (path, ProbeSeconds(path) ?? 5.0);   // WPF PlaySound answers 5 s when it cannot read the clip
        }

        /// <summary>Clip length test seam; default = a LibVLC local parse (2 s cap).</summary>
        internal static Func<string, double?> ProbeSeconds = ProbeWithVlc;

        private static double? ProbeWithVlc(string path)
        {
            try
            {
                if (LibVlcAudio.Shared is not { } vlc) return null;
                using var media = new Media(vlc, path, FromType.FromPath);
                var status = media.Parse(MediaParseOptions.ParseLocal, 2000).GetAwaiter().GetResult();
                return status == MediaParsedStatus.Done && media.Duration > 0 ? media.Duration / 1000.0 : null;
            }
            catch (Exception ex) { Log.Debug("Flash: clip length probe failed: {E}", ex.Message); return null; }
        }

        /// <summary>WPF ShowImages: lifetime = the clip's seconds (when one plays, overriding any
        /// custom duration) or the requested/setting duration, plus 1 s.</summary>
        internal static TimeSpan BurstLifetime(int? durationMs, AppSettings s, (string Path, double Seconds)? sound)
            => TimeSpan.FromMilliseconds(sound is { } snd
                ? FlashVoicePool.LifetimeMs(snd.Seconds)
                : (durationMs ?? s.FlashDuration * 1000) + 1000);

        /// <summary>Plays the burst's clip when its first flash lands (WPF: after the 1 s pre-announce),
        /// unless the burst was cancelled by then.</summary>
        internal static void ScheduleBurstSound((string Path, double Seconds)? sound, int generation, TimeSpan delay)
        {
            if (sound is not { } snd) return;
            DispatcherTimer.RunOnce(() =>
            {
                if (_closed || generation != _generation) return;
                PlayBurstSound(snd.Path, snd.Seconds);
            }, delay);
        }

        /// <summary>WPF PlaySound + FlashAudioPlaying + Duck/ScheduleUnduck (the unduck is never cancelled).</summary>
        internal static void PlayBurstSound(string path, double seconds)
        {
            var s = CoreSettings.Current;
            if (!FlashVoicePool.ShouldPlay(s)) return;
            try
            {
                StopFlashSound();
                _stopSound = CoreAudio.PlayStoppable(path, FlashVoicePool.Volume(s.MasterVolume), "flash-sound");
                _soundUntil = DateTime.Now.AddSeconds(seconds);
                CoreTubeEvents.RaiseFlashAudioPlaying(FlashVoicePool.Caption(path));
                if (s.AudioDuckingEnabled)
                {
                    CoreAudio.Duck(s.DuckingLevel);
                    var gen = CoreAudio.DuckGeneration;
                    DispatcherTimer.RunOnce(() => CoreAudio.Unduck(gen), TimeSpan.FromMilliseconds(FlashVoicePool.UnduckDelayMs(seconds)));
                }
            }
            catch (Exception ex) { Log.Debug("Flash: could not play sound: {E}", ex.Message); }
        }

        private static DateTime _soundUntil = DateTime.MinValue;

        /// <summary>WPF _soundPlayingForCurrentFlash: a voice line is playing right now (hydra
        /// children spawned under it pay the 8 XP base).</summary>
        internal static bool SoundPlaying => DateTime.Now < _soundUntil;

        /// <summary>WPF StopCurrentSound (engine stop, panic, a newer burst).</summary>
        internal static void StopFlashSound()
        {
            var stop = _stopSound;
            _stopSound = null;
            _soundUntil = DateTime.MinValue;
            try { stop?.Invoke(); } catch { }
        }

        // ---- moving online clips ----

        /// <summary>LoadPictures: this poster belongs to an online flash whose clip is on disk.</summary>
        internal static void RememberClip(Bitmap poster, string? clipPath)
        {
            if (!string.IsNullOrEmpty(clipPath)) Clips.AddOrUpdate(poster, clipPath);
        }

        /// <summary>Spawn: start the poster's clip in its window (WPF heartbeat ClipPath).</summary>
        internal static void AttachClip(FlashOverlayWindow w, Bitmap poster)
        {
            if (!Clips.TryGetValue(poster, out var clip)) return;
            Clips.Remove(poster);
            if (!File.Exists(clip)) return;
            w.PlayClip(LibVlcAudio.Shared, clip, CoreSettings.Current.FlashGifSpeedMultiplier);
        }

        // ---- chaos pool ----

        /// <summary>
        /// WPF GetChaosImagePaths: up to <paramref name="count"/> distinct pictures from the flash
        /// pool - the SAME disk walk the flashes deal from (#627), online stills when on (remote
        /// first, the pool that can decline). Online picks are the downloaded poster files.
        /// </summary>
        internal static List<string> GetChaosImagePaths(int count)
        {
            var result = new List<string>();
            if (count <= 0) return result;
            var s = CoreSettings.Current;
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "images");
            var files = Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => FlashPlacement.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList()
                : new List<string>();
            files = AssetFolderExclusion.Enabled(files, root, s);
            RemoteFlashSource.EnsurePrefetch();
            var remoteReady = RemoteFlashSource.ReadyCount;
            var haveLocal = files.Count > 0;
            var want = Math.Min(count, files.Count + remoteReady);
            var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chosenRemote = new HashSet<string>(StringComparer.Ordinal);
            for (int guard = 0, max = (files.Count + remoteReady) * 8 + 16; result.Count < want && guard < max; guard++)
            {
                var remoteSpent = remoteReady <= 0 || chosenRemote.Count >= remoteReady;
                if (!haveLocal && remoteSpent) break;
                if (!remoteSpent && FlashSourceRules.ShouldDrawRemote(s, haveLocal, Rng))
                {
                    if (RemoteFlashSource.TryTake(Rng) is { } item)
                    {
                        if (chosenRemote.Add(item.Url)) result.Add(item.PosterPath);
                        continue;
                    }
                    remoteReady = 0;
                    if (!haveLocal) break;
                }
                if (NextPath(files) is { } local && chosen.Add(local)) result.Add(local);
            }
            return result;
        }
    }
}
