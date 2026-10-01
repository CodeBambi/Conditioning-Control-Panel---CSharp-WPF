using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Plays a video or audio file from inside the user's assets root. Path is normalized
    /// and rejected if it escapes the assets directory after resolution. The AI doesn't
    /// know which files actually exist on disk, so when the path is missing or doesn't
    /// resolve we fall back to a random pick of the right kind — that way "play me a
    /// video" still does something even when the model hallucinates a filename.
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

        public async Task<bool> ExecuteAsync()
        {
            // Random pick — the AI can ask for "any video" / "any audio" without naming a file.
            if (_data.Random || string.IsNullOrEmpty(_data.Path))
            {
                if (_kind == AICommandType.audio)
                    return await PlayRandomAudio();
                return PlayRandomVideo();
            }

            var fullPath = GetValidatedPath(_data.Path);
            if (fullPath == null)
            {
                // AI named a file that doesn't exist (or escaped assets). Fall back to a
                // random pick so the request still produces something audible/visible —
                // matches what the user sees in the live actions feed.
                Log.Information("MediaCommand: path '{Path}' didn't resolve — falling back to random {Kind}",
                    _data.Path, _kind);
                if (_kind == AICommandType.audio) return await PlayRandomAudio();
                return PlayRandomVideo();
            }

            Log.Information("MediaCommand: AI play media {Path}", fullPath);

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
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

            if (IsAudio(ext))
            {
                return await PlayFile(fullPath);
            }

            Log.Information("MediaCommand: extension {Ext} not recognized as audio/video — falling back to random {Kind}", ext, _kind);
            if (_kind == AICommandType.audio) return await PlayRandomAudio();
            return PlayRandomVideo();
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
