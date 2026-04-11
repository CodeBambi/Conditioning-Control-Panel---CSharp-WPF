using System.Linq;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class MediaCommand(Media commandData) : ICommand
{
    public bool Execute()
    {
        if (commandData.Random)
        {
            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (App.Video.IsPlaying) return false;
                App.Video.TriggerVideo();
                return true;
            });
        }

        if (string.IsNullOrEmpty(commandData.Path)) return false;

        var fullPath = GetValidatedPath(commandData.Path);
        if (fullPath == null)
        {
            App.Logger.Warning("MediaCommand: Path is not allowed: {Path}", commandData.Path);
            return false;
        }

        // Check file extension to decide between video and audio
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();

        if (IsVideo(extension))
        {
            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (App.Video.IsPlaying) return false;
                App.Video.PlaySpecificVideo(fullPath, false);
                return true;
            });
        }

        if (IsAudio(extension))
        {
            App.Audio.PlaySound(fullPath, 100);
            return true;
        }

        return false;
    }

    private bool IsVideo(string extension)
    {
        var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" };
        return videoExtensions.Contains(extension);
    }

    private bool IsAudio(string extension)
    {
        var audioExtensions = new[] { ".mp3", ".wav", ".wma", ".ogg", ".flac", ".aac", ".m4a" };
        return audioExtensions.Contains(extension);
    }

    private string? GetValidatedPath(string path)
    {
        try
        {
            string assetsRoot = Path.GetFullPath(App.EffectiveAssetsPath);
            
            // If path is relative, combine it with assets root
            string fullPath = Path.IsPathRooted(path) 
                ? Path.GetFullPath(path) 
                : Path.GetFullPath(Path.Combine(assetsRoot, path));

            // Ensure the path is within the assets root
            if (fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex, "MediaCommand: Error validating path {Path}", path);
        }

        return null;
    }
}