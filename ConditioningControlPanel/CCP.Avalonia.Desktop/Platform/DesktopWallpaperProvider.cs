using System;
using System.Diagnostics;
using System.IO;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Platform
{
    /// <summary>
    /// Desktop wallpaper override for Linux and macOS.
    /// Windows uses the head-specific <see cref="ConditioningControlPanel.Avalonia.Desktop.Windows.WpfWallpaperProvider"/>.
    /// </summary>
    public sealed class DesktopWallpaperProvider : IWallpaperProvider
    {
        private string? _originalMacOsWallpaper;
        private string? _originalLinuxWallpaperUri;
        private bool _linuxOriginalCaptured;

        public void SetWallpaper(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return;
            if (!File.Exists(imagePath)) return;

            if (OperatingSystem.IsMacOS())
            {
                SetMacOsWallpaper(imagePath);
            }
            else if (OperatingSystem.IsLinux())
            {
                SetLinuxWallpaper(imagePath);
            }
        }

        public void RestoreOriginalWallpaper()
        {
            if (OperatingSystem.IsMacOS() && !string.IsNullOrEmpty(_originalMacOsWallpaper))
            {
                SetMacOsWallpaper(_originalMacOsWallpaper);
            }
            else if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(_originalLinuxWallpaperUri))
            {
                // Best-effort GNOME restore (WS0 lot 4 V2-2: turning the override off must
                // not leave the user's desktop permanently changed). Other DEs (feh/nitrogen)
                // have no queryable "current wallpaper"; they degrade gracefully.
                TryRun("gsettings", $"set org.gnome.desktop.background picture-uri {_originalLinuxWallpaperUri}");
                TryRun("gsettings", $"set org.gnome.desktop.background picture-uri-dark {_originalLinuxWallpaperUri}");
            }
        }

        private void SetMacOsWallpaper(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(_originalMacOsWallpaper))
                    _originalMacOsWallpaper = GetMacOsCurrentWallpaper();

                var script = $"tell application \"System Events\" to set picture of every desktop to \"{EscapeAppleScript(imagePath)}\"";
                RunProcess("osascript", $"-e '{script}'");
            }
            catch { /* wallpaper override is best-effort */ }
        }

        private static string? GetMacOsCurrentWallpaper()
        {
            try
            {
                var result = RunProcess("osascript", "-e 'tell application \"System Events\" to get picture of first desktop'");
                return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
            }
            catch { return null; }
        }

        private void SetLinuxWallpaper(string imagePath)
        {
            var absolute = Path.GetFullPath(imagePath);
            var uri = new Uri(absolute).AbsoluteUri;

            if (!_linuxOriginalCaptured)
            {
                // Capture once, before the first override; gsettings echoes the value
                // quoted (e.g. 'file:///...'), which set accepts back verbatim.
                _linuxOriginalCaptured = true;
                try
                {
                    var current = RunProcess("gsettings", "get org.gnome.desktop.background picture-uri");
                    _originalLinuxWallpaperUri = string.IsNullOrWhiteSpace(current) ? null : current.Trim();
                }
                catch { _originalLinuxWallpaperUri = null; }
            }

            // GNOME / Cinnamon
            if (TryRun("gsettings", $"set org.gnome.desktop.background picture-uri \"{uri}\""))
            {
                TryRun("gsettings", $"set org.gnome.desktop.background picture-uri-dark \"{uri}\"");
                return;
            }

            // MATE
            if (TryRun("gsettings", $"set org.mate.desktop.background picture-filename \"{absolute}\""))
                return;

            // feh / nitrogen generic fallbacks
            if (TryRun("feh", $"--bg-scale \"{absolute}\""))
                return;

            TryRun("nitrogen", $"--set-zoom \"{absolute}\"");
        }

        private static bool TryRun(string fileName, string arguments)
        {
            try
            {
                var result = RunProcess(fileName, arguments);
                return result != null;
            }
            catch
            {
                return false;
            }
        }

        private static string? RunProcess(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{fileName} failed: {process.StandardError.ReadToEnd().Trim()}");

            return output;
        }

        private static string EscapeAppleScript(string path)
            => path.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
