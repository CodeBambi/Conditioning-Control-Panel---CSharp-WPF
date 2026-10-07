using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Logging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Plays a video or audio file from inside the user's assets root. Path is normalized
    /// and rejected if it escapes the assets directory after resolution. The AI doesn't
    /// know which files actually exist on disk. A request that names nothing is a random pick
    /// of the right kind. A named video plays the closest local match by name
    /// (<see cref="VideoTitleMatcher"/>), or opens a HypnoTube link in the browser takeover, or
    /// plays nothing (see <see cref="Decide"/>); a named audio still falls back to a random one.
    /// </summary>
    public class MediaCommand : ICommand
    {
        private readonly Media _data;
        private readonly AICommandType _kind;

        public MediaCommand(Media data, AICommandType kind = AICommandType.video)
        {
            _data = data;
            _kind = kind;
        }

        /// <summary>Head video surface on the UI thread: a validated path, or null for a random
        /// clip (WPF App.Video PlaySpecificVideo(path, strict: false) / TriggerVideo). False when
        /// nothing started (no player, one already playing). Unseeded: refused.</summary>
        public static volatile Func<string?, bool>? VideoSurface;

        /// <summary>Head audio surface: plays one file at full volume and answers whether it really
        /// started (WPF App.Audio.PlaySound, #1120). Unseeded: refused.</summary>
        public static volatile Func<string, Task<bool>>? AudioSurface;


        /// <summary>Head browser surface: opens a HypnoTube video page in the fullscreen browser
        /// takeover (WPF MediaCommand.OpenHypnoTube body, main ab381fa6a / e4764063c): skipped while a
        /// remote controller is connected or a video plays. True when the navigation was issued.
        /// Unseeded: refused.</summary>
        public static volatile Func<string, bool>? HypnoTubeSurface;

        /// <summary>What an AI media request plays.</summary>
        internal enum MediaPick { Random, Named, HypnoTube, Nothing }

        /// <summary>Pure decision (ccp-bugs #1325, #1330). An explicit Random, or a request that
        /// names nothing at all ("play any video"), is a random pick. A request that NAMES
        /// something (a Title or a Path) plays the local file it resolves to: the path itself, or
        /// for a video the closest library match by name (<paramref name="namedFilePlayable"/>).
        /// A named video with no local match opens its HypnoTube link in the browser takeover when
        /// Path or Title is one (<paramref name="hypnoTubeLink"/>). Otherwise a video request plays
        /// NOTHING: the chat text was about that one video, and a random local file in its place is
        /// the unrelated video #1325 describes. Audio keeps its random fallback.</summary>
        internal static MediaPick Decide(Media data, AICommandType kind, bool namedFilePlayable, bool hypnoTubeLink = false)
        {
            if (data.Random) return MediaPick.Random;
            var named = !string.IsNullOrWhiteSpace(data.Path) || !string.IsNullOrWhiteSpace(data.Title);
            if (!named) return MediaPick.Random;
            if (namedFilePlayable) return MediaPick.Named;
            if (kind == AICommandType.audio) return MediaPick.Random;
            return hypnoTubeLink ? MediaPick.HypnoTube : MediaPick.Nothing;
        }

        /// <summary>The first of Path, Title that is a HypnoTube video page, or null.</summary>
        internal static string? HypnoTubeLinkOf(Media data)
        {
            var path = data.Path?.Trim();
            if (HtUrlHelper.IsEligibleHtUrl(path)) return path;
            var title = data.Title?.Trim();
            if (HtUrlHelper.IsEligibleHtUrl(title)) return title;
            return null;
        }

        /// <summary>The AI names a pool video by its TITLE ("Sissy Dreams 3"), never by URL, so a
        /// request resolves through the mod's link pool: an exact name first, then the closest name.
        /// Without this every effect-control video came back "didn't play" (ccp-bugs #1344).</summary>
        internal static string? PoolLinkOf(Media data, IReadOnlyDictionary<string, string>? pool)
        {
            if (pool == null || pool.Count == 0) return null;
            foreach (var q in new[] { data.Title, data.Path })
            {
                var name = q?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                foreach (var kv in pool)
                    if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && HtUrlHelper.IsEligibleHtUrl(kv.Value))
                        return kv.Value;
            }
            var names = pool.Where(kv => HtUrlHelper.IsEligibleHtUrl(kv.Value)).Select(kv => kv.Key).ToList();
            var hit = VideoTitleMatcher.FindBest(new[] { data.Title, data.Path }, names);
            return hit != null && pool.TryGetValue(hit, out var url) ? url : null;
        }

        public async Task<bool> ExecuteAsync()
        {
            var fullPath = string.IsNullOrWhiteSpace(_data.Path) ? null : GetValidatedPath(_data.Path);
            var ext = fullPath == null ? "" : Path.GetExtension(fullPath).ToLowerInvariant();
            var playable = fullPath != null && (IsVideo(ext) || IsAudio(ext));

            // A named video that is not a literal file: the closest match by name in the library.
            if (!playable && !_data.Random && _kind == AICommandType.video)
            {
                var match = VideoTitleMatcher.FindBest(new[] { _data.Path, _data.Title }, LocalVideoLibrary());
                if (match != null)
                {
                    fullPath = match;
                    ext = Path.GetExtension(match).ToLowerInvariant();
                    playable = true;
                    Log.Information("MediaCommand: AI named video matched a library file by name");
                }
            }

            var htLink = HypnoTubeLinkOf(_data)
                ?? (_kind == AICommandType.video ? PoolLinkOf(_data, CoreMods.Service?.GetVideoLinks()) : null);
            switch (Decide(_data, _kind, playable, htLink != null))
            {
                case MediaPick.Random:
                    if (_kind == AICommandType.audio)
                        return await PlayRandomAudio();
                    return PlayRandomVideo();
                case MediaPick.HypnoTube:
                    return OpenHypnoTube(htLink!);
                case MediaPick.Nothing:
                    // No title or path in the log: a named video can be a private file name.
                    Log.Information("MediaCommand: AI named a video with no close local match and no HypnoTube link, playing nothing");
                    return false;
            }

            Log.Information("MediaCommand: AI play media {Path}", fullPath);

            if (IsVideo(ext))
            {
                // The main Videos feature toggle is authoritative for AI-triggered videos
                // too (#512). Guarded at the playback sink so every path is covered; an
                // audio-kind request that resolved to a video file still gets its audio
                // fallback instead of a silent drop.
                if (CoreSettings.Current?.MandatoryVideosEnabled != true)
                {
                    Log.Information("MediaCommand: AI video ignored — Videos feature is disabled");
                    if (_kind == AICommandType.audio) return await PlayRandomAudio();
                    return false;
                }

                return VideoSurface?.Invoke(fullPath) == true;
            }

            return await PlayFile(fullPath!);   // Named means video or audio
        }

        /// <summary>
        /// Opens a HypnoTube video page in the embedded browser's fullscreen takeover, the way the
        /// companion's own web videos open (AutonomyService web video): the Videos toggle governs
        /// it like any AI video, it never navigates over a video that is playing, and it claims the
        /// fullscreen slot before navigating. True when the navigation was issued.
        /// </summary>
        private static bool OpenHypnoTube(string url)
        {
            if (CoreSettings.Current?.MandatoryVideosEnabled != true)
            {
                Log.Information("MediaCommand: AI HypnoTube video ignored, Videos feature is disabled");
                return false;
            }

            return HypnoTubeSurface?.Invoke(url) == true;
        }

        /// <summary>Every local video the random picker could draw: under the assets videos
        /// folder, minus the files and folders the user switched off.</summary>
        private static IReadOnlyList<string> LocalVideoLibrary()
        {
            try
            {
                var assetsRoot = CorePaths.EffectiveAssets;
                var videosRoot = Path.Combine(assetsRoot, "videos");
                if (!Directory.Exists(videosRoot)) return Array.Empty<string>();

                var settings = CoreSettings.Current;
                static string Norm(string p) => p.Replace('\\', '/');
                var disabled = new HashSet<string>(
                    (settings?.DisabledAssetPaths ?? Enumerable.Empty<string>()).Select(Norm),
                    StringComparer.OrdinalIgnoreCase);
                var folders = settings?.DisabledAssetFolders?.ToArray() ?? Array.Empty<string>();

                return Directory.EnumerateFiles(videosRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsVideo(Path.GetExtension(f).ToLowerInvariant()))
                    .Where(f =>
                    {
                        var rel = Norm(Path.GetRelativePath(assetsRoot, f));
                        return !disabled.Contains(rel) && !AssetFolderExclusion.IsUnderAny(rel, folders);
                    })
                    .Take(5000)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MediaCommand: video library scan threw");
                return Array.Empty<string>();
            }
        }

        private static bool PlayRandomVideo()
        {
            // Playback sink gate: the main Videos feature toggle governs AI videos (#512).
            if (CoreSettings.Current?.MandatoryVideosEnabled != true)
            {
                Log.Information("MediaCommand: random video ignored — Videos feature is disabled");
                return false;
            }

            return VideoSurface?.Invoke(null) == true;
        }

        private static async Task<bool> PlayRandomAudio()
        {
            try
            {
                var assetsRoot = CorePaths.EffectiveAssets;
                var audioRoot = Path.Combine(assetsRoot, "audio");
                string[] candidates = Array.Empty<string>();
                if (Directory.Exists(audioRoot))
                {
                    candidates = Directory.GetFiles(audioRoot, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsAudio(Path.GetExtension(f).ToLowerInvariant()))
                        .ToArray();
                }

                if (candidates.Length == 0)
                {
                    Log.Warning("MediaCommand: no audio files under {Root} — cannot fulfill random audio", audioRoot);
                    return false;
                }

                var pick = candidates[new Random().Next(candidates.Length)];
                Log.Information("MediaCommand: random audio pick {Path}", pick);
                return await PlayFile(pick);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MediaCommand: random audio pick threw");
                return false;
            }
        }

        /// <summary>
        /// Hands one audio file to the head audio surface (WPF AudioService.PlaySound) and reports whether it
        /// actually started. PlaySound returns the clip duration and 0 when it could not play
        /// (no audio service yet, unreadable file, no output device), so the caller can tell the
        /// user "nothing played" instead of claiming success (#1120).
        /// </summary>
        private static async Task<bool> PlayFile(string path)
        {
            var started = AudioSurface is { } play && await play(path);
            if (!started)
            {
                Log.Warning("MediaCommand: audio {Path} did not start playing", path);
                return false;
            }
            return true;
        }

        private static bool IsVideo(string ext)
        {
            var v = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" };
            return v.Contains(ext);
        }

        private static bool IsAudio(string ext)
        {
            var a = new[] { ".mp3", ".wav", ".wma", ".ogg", ".flac", ".aac", ".m4a" };
            return a.Contains(ext);
        }

        private static string? GetValidatedPath(string path)
        {
            try
            {
                // Defense-in-depth: reject obvious traversal attempts up front.
                if (path.Contains("..", StringComparison.Ordinal)) return null;

                var assetsRoot = Path.GetFullPath(CorePaths.EffectiveAssets);
                var fullPath = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(assetsRoot, path));

                // After normalization the resolved path must still live under assets root.
                if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) return null;
                if (!File.Exists(fullPath)) return null;
                return fullPath;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MediaCommand: path validation threw");
                return null;
            }
        }
    }
}
