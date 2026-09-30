using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Plays a video or audio file from inside the user's assets root. Path is normalized
    /// and rejected if it escapes the assets directory after resolution. The AI doesn't
    /// know which files actually exist on disk. A request that names nothing is a random pick
    /// of the right kind; a named video that does not resolve plays nothing (see
    /// <see cref="Decide"/>), a named audio still falls back to a random one.
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

        /// <summary>What an AI media request plays.</summary>
        internal enum MediaPick { Random, Named, Nothing }

        /// <summary>Pure decision (ccp-bugs #1325). An explicit Random, or a request that names
        /// nothing at all ("play any video"), is a random pick. A request that NAMES something
        /// (a Title or a Path) plays that file when it resolves to a playable file inside the
        /// assets root. When it does not, a video request plays NOTHING: the chat text was about
        /// that one video (often a Hypnotube recommendation), and a random local file in its place
        /// is the unrelated video the report describes. Audio keeps its random fallback.</summary>
        internal static MediaPick Decide(Media data, AICommandType kind, bool namedFilePlayable)
        {
            if (data.Random) return MediaPick.Random;
            var named = !string.IsNullOrWhiteSpace(data.Path) || !string.IsNullOrWhiteSpace(data.Title);
            if (!named) return MediaPick.Random;
            if (namedFilePlayable) return MediaPick.Named;
            return kind == AICommandType.audio ? MediaPick.Random : MediaPick.Nothing;
        }

        public Task<bool> ExecuteAsync()
        {
            var fullPath = string.IsNullOrWhiteSpace(_data.Path) ? null : GetValidatedPath(_data.Path);
            var ext = fullPath == null ? "" : Path.GetExtension(fullPath).ToLowerInvariant();
            var playable = fullPath != null && (IsVideo(ext) || IsAudio(ext));

            switch (Decide(_data, _kind, playable))
            {
                case MediaPick.Random:
                    if (_kind == AICommandType.audio)
                        return Task.FromResult(PlayRandomAudio());
                    return Task.FromResult(PlayRandomVideo());
                case MediaPick.Nothing:
                    // No title or path in the log: a named video can be a private file name.
                    App.Logger?.Information("MediaCommand: AI named a video that is not a playable local file, playing nothing");
                    return Task.FromResult(false);
            }

            App.Logger?.Information("MediaCommand: AI play media {Path}", fullPath);

            if (IsVideo(ext))
            {
                // The main Videos feature toggle is authoritative for AI-triggered videos
                // too (#512). Guarded at the playback sink so every path is covered; an
                // audio-kind request that resolved to a video file still gets its audio
                // fallback instead of a silent drop.
                if (App.Settings?.Current?.MandatoryVideosEnabled != true)
                {
                    App.Logger?.Information("MediaCommand: AI video ignored — Videos feature is disabled");
                    if (_kind == AICommandType.audio) return Task.FromResult(PlayRandomAudio());
                    return Task.FromResult(false);
                }

                return Task.FromResult(Application.Current.Dispatcher.Invoke(() =>
                {
                    if (App.Video == null) return false;
                    if (App.Video.IsPlaying)
                    {
                        App.Logger?.Information("MediaCommand: video already playing — skipping {Path}", fullPath);
                        return false;
                    }
                    App.Video.PlaySpecificVideo(fullPath, false);
                    return true;
                }));
            }

            return Task.FromResult(PlayFile(fullPath!));   // Named means video or audio
        }

        private static bool PlayRandomVideo()
        {
            // Playback sink gate: the main Videos feature toggle governs AI videos (#512).
            if (App.Settings?.Current?.MandatoryVideosEnabled != true)
            {
                App.Logger?.Information("MediaCommand: random video ignored — Videos feature is disabled");
                return false;
            }

            return Application.Current.Dispatcher.Invoke(() =>
            {
                if (App.Video == null) return false;
                if (App.Video.IsPlaying)
                {
                    App.Logger?.Information("MediaCommand: random video requested but a video is already playing — skipping");
                    return false;
                }
                App.Video.TriggerVideo();
                return true;
            });
        }

        private static bool PlayRandomAudio()
        {
            try
            {
                var assetsRoot = App.EffectiveAssetsPath;
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
                    App.Logger?.Warning("MediaCommand: no audio files under {Root} — cannot fulfill random audio", audioRoot);
                    return false;
                }

                var pick = candidates[new Random().Next(candidates.Length)];
                App.Logger?.Information("MediaCommand: random audio pick {Path}", pick);
                return PlayFile(pick);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaCommand: random audio pick threw");
                return false;
            }
        }

        /// <summary>
        /// Hands one audio file to <see cref="AudioService.PlaySound"/> and reports whether it
        /// actually started. PlaySound returns the clip duration and 0 when it could not play
        /// (no audio service yet, unreadable file, no output device), so the caller can tell the
        /// user "nothing played" instead of claiming success (#1120).
        /// </summary>
        private static bool PlayFile(string path)
        {
            var seconds = Application.Current.Dispatcher.Invoke(() => App.Audio?.PlaySound(path, 100) ?? 0);
            if (seconds <= 0)
            {
                App.Logger?.Warning("MediaCommand: audio {Path} did not start playing", path);
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

                var assetsRoot = Path.GetFullPath(App.EffectiveAssetsPath);
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
                App.Logger?.Warning(ex, "MediaCommand: path validation threw");
                return null;
            }
        }
    }
}
