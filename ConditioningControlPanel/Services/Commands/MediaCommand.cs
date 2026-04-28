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

        public Task<bool> ExecuteAsync()
        {
            // Random pick — the AI can ask for "any video" / "any audio" without naming a file.
            if (_data.Random || string.IsNullOrEmpty(_data.Path))
            {
                if (_kind == AICommandType.audio)
                    return Task.FromResult(PlayRandomAudio());
                return Task.FromResult(PlayRandomVideo());
            }

            var fullPath = GetValidatedPath(_data.Path);
            if (fullPath == null)
            {
                // AI named a file that doesn't exist (or escaped assets). Fall back to a
                // random pick so the request still produces something audible/visible —
                // matches what the user sees in the live actions feed.
                App.Logger?.Information("MediaCommand: path '{Path}' didn't resolve — falling back to random {Kind}",
                    _data.Path, _kind);
                if (_kind == AICommandType.audio) return Task.FromResult(PlayRandomAudio());
                return Task.FromResult(PlayRandomVideo());
            }

            App.Logger?.Information("MediaCommand: AI play media {Path}", fullPath);

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (IsVideo(ext))
            {
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

            if (IsAudio(ext))
            {
                Application.Current.Dispatcher.Invoke(() => App.Audio?.PlaySound(fullPath, 100));
                return Task.FromResult(true);
            }

            App.Logger?.Information("MediaCommand: extension {Ext} not recognized as audio/video — falling back to random {Kind}", ext, _kind);
            if (_kind == AICommandType.audio) return Task.FromResult(PlayRandomAudio());
            return Task.FromResult(PlayRandomVideo());
        }

        private static bool PlayRandomVideo()
        {
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
                Application.Current.Dispatcher.Invoke(() => App.Audio?.PlaySound(pick, 100));
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaCommand: random audio pick threw");
                return false;
            }
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
