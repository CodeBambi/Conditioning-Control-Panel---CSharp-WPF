using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Plays a video or audio file from inside the user's assets root. Path is normalized
    /// and rejected if it escapes the assets directory after resolution.
    /// </summary>
    public class MediaCommand : ICommand
    {
        private readonly Media _data;
        public MediaCommand(Media data) { _data = data; }

        public Task<bool> ExecuteAsync()
        {
            if (_data.Random)
            {
                return Task.FromResult(Application.Current.Dispatcher.Invoke(() =>
                {
                    if (App.Video == null || App.Video.IsPlaying) return false;
                    App.Video.TriggerVideo();
                    return true;
                }));
            }

            if (string.IsNullOrEmpty(_data.Path)) return Task.FromResult(false);

            var fullPath = GetValidatedPath(_data.Path);
            if (fullPath == null)
            {
                App.Logger?.Warning("MediaCommand: path rejected: {Path}", _data.Path);
                return Task.FromResult(false);
            }

            App.Logger?.Information("MediaCommand: AI play media {Path}", fullPath);

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (IsVideo(ext))
            {
                return Task.FromResult(Application.Current.Dispatcher.Invoke(() =>
                {
                    if (App.Video == null || App.Video.IsPlaying) return false;
                    App.Video.PlaySpecificVideo(fullPath, false);
                    return true;
                }));
            }

            if (IsAudio(ext))
            {
                Application.Current.Dispatcher.Invoke(() => App.Audio?.PlaySound(fullPath, 100));
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
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
