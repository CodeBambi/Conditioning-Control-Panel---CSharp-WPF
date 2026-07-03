using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform desktop wallpaper override.
/// Windows uses the native <c>SystemParametersInfo</c> SPI.
/// Linux falls back to <c>gsettings</c> (GNOME) and then <c>feh</c>.
/// macOS uses an <c>osascript</c> Finder command.
/// Unsupported platforms silently ignore the request.
/// </summary>
public sealed class AvaloniaWallpaperProvider : IWallpaperProvider
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, string pvParam, int fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoGet(int uiAction, int uiParam, System.Text.StringBuilder pvParam, int fWinIni);

    // The user's own wallpaper, captured before the first override so Deactivate can
    // put it back. WPF WallpaperService does the same via SPI_GETDESKWALLPAPER; before
    // this fix RestoreOriginalWallpaper was a no-op and turning the override off left
    // the desktop permanently changed (WS0 lot 4 V2-2, data integrity).
    private string? _originalWallpaperPath;
    private bool _originalCaptured;

    public void SetWallpaper(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return;

        try
        {
            CaptureOriginalWallpaperOnce();

            if (OperatingSystem.IsWindows())
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, Path.GetFullPath(imagePath), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                return;
            }

            if (OperatingSystem.IsLinux())
            {
                SetLinuxWallpaper(imagePath);
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                SetMacWallpaper(imagePath);
            }
        }
        catch
        {
            // Best-effort; wallpaper override is not critical to app function.
        }
    }

    public void RestoreOriginalWallpaper()
    {
        try
        {
            if (!_originalCaptured)
                return;

            var original = _originalWallpaperPath;
            _originalCaptured = false;
            _originalWallpaperPath = null;

            if (string.IsNullOrWhiteSpace(original) || !File.Exists(original))
                return;

            if (OperatingSystem.IsWindows())
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, Path.GetFullPath(original), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            else if (OperatingSystem.IsLinux())
            {
                SetLinuxWallpaper(original);
            }
            else if (OperatingSystem.IsMacOS())
            {
                SetMacWallpaper(original);
            }
        }
        catch
        {
            // Best-effort; wallpaper restore is not critical to app function.
        }
    }

    private void CaptureOriginalWallpaperOnce()
    {
        if (_originalCaptured)
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var buffer = new System.Text.StringBuilder(520);
                if (SystemParametersInfoGet(SPI_GETDESKWALLPAPER, buffer.Capacity, buffer, 0))
                {
                    _originalWallpaperPath = buffer.ToString();
                }
            }
            // Linux/macOS: reading the current wallpaper varies per desktop environment;
            // capture is Windows-only for now and Restore degrades gracefully elsewhere.
        }
        catch
        {
            _originalWallpaperPath = null;
        }
        finally
        {
            // Mark captured even on failure so we never overwrite the saved value with
            // one of our own overrides on a later SetWallpaper call.
            _originalCaptured = true;
        }
    }

    private static void SetLinuxWallpaper(string imagePath)
    {
        var fullPath = Path.GetFullPath(imagePath);
        var uri = "file://" + fullPath.Replace(" ", "%20");

        // GNOME / gsettings
        try
        {
            using var gsettings = Process.Start(new ProcessStartInfo("gsettings", $"set org.gnome.desktop.background picture-uri \"{uri}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            gsettings?.WaitForExit(2000);
            if (gsettings?.ExitCode == 0) return;
        }
        catch { /* ignore, try next */ }

        // feh fallback
        try
        {
            using var feh = Process.Start(new ProcessStartInfo("feh", $"--bg-scale \"{fullPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            feh?.WaitForExit(2000);
        }
        catch { /* ignore */ }
    }

    private static void SetMacWallpaper(string imagePath)
    {
        var script = $"tell application \"Finder\" to set desktop picture to POSIX file \"{imagePath.Replace("\"", "\\\"")}\"";
        try
        {
            using var osascript = Process.Start(new ProcessStartInfo("osascript", $"-e '{script}'")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            osascript?.WaitForExit(2000);
        }
        catch { /* ignore */ }
    }
}
