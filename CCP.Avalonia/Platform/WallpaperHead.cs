using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The desktop wallpaper for Takeover (WPF App.Wallpaper + AutonomyService.TriggerWallpaperChange).
    /// Windows: SystemParametersInfo, as WPF. Linux: the desktop's own setting, GNOME-family through
    /// gsettings (the key the current colour scheme reads), KDE Plasma through plasma-apply-wallpaperimage
    /// with the original read from the Plasma applet config. Any other desktop reads as "cannot say", and
    /// then nothing is ever changed (documented exception: wallpaper changes need GNOME, a GNOME-based
    /// desktop, or KDE Plasma on Linux).
    /// </summary>
    internal static class WallpaperHead
    {
        private static WallpaperService? _service;
        private static IDisposable? _revert;

        /// <summary>Test seam: the backend the service is built over.</summary>
        internal static Func<IWallpaperBackend> Backend = Create;

        /// <summary>The one service. Building it puts back a wallpaper a dead session left behind (#692).</summary>
        internal static WallpaperService Service => _service ??= new WallpaperService(Backend());

        internal static void ResetForTest() { CancelRevert(); _service = null; Backend = Create; }

        internal static bool Supported => Backend() is not NoBackend;

        /// <summary>WPF TriggerWallpaperChange: shuffle; unless the user asked for changes to stay, the
        /// original comes back after WallpaperPulseSeconds, and each new change REPLACES the pending
        /// revert instead of stacking a second one (#694).</summary>
        internal static void TriggerChange(Func<Action, TimeSpan, IDisposable>? after = null)
        {
            if (!Service.Shuffle()) return;   // nothing changed: nothing to revert
            CancelRevert();
            if (CoreSettings.Current?.WallpaperEnabled == true)
            {
                Log.Debug("Autonomy: Wallpaper change left in place (user asked for it to stay)");
                return;
            }
            var seconds = CoreSettings.Current?.WallpaperPulseSeconds ?? 30;
            after ??= (act, wait) => DispatcherTimer.RunOnce(act, wait);
            _revert = after(() =>
            {
                _revert = null;
                // Skip if the user flipped "keep it" on while the pulse was running.
                if (CoreSettings.Current?.WallpaperEnabled != true) Service.Deactivate();
            }, TimeSpan.FromSeconds(seconds));
        }

        /// <summary>Drop any pending revert without touching the current wallpaper.</summary>
        internal static void CancelRevert()
        {
            var r = _revert;
            _revert = null;
            try { r?.Dispose(); } catch { }
        }

        /// <summary>WPF AutonomyService.Stop: the desktop comes back, unless the user asked her changes to stay.</summary>
        internal static void OnTakeoverStopped()
        {
            CancelRevert();
            if (_service == null) return;
            if (CoreSettings.Current?.WallpaperEnabled != true && _service.IsActive)
            {
                Log.Information("AutonomyService: Restoring desktop wallpaper");
                _service.Deactivate();
            }
        }

        /// <summary>Panic and app close (WPF App.xaml.cs:1260, :5803): always back, whatever "keep" says.</summary>
        internal static void Restore()
        {
            CancelRevert();
            _service?.Deactivate();
        }

        private static IWallpaperBackend Create()
        {
            if (OperatingSystem.IsWindows()) return new WindowsBackend();
            if (OperatingSystem.IsLinux())
            {
                var desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").ToUpperInvariant();
                if (desktop.Contains("KDE")) return new KdeBackend();
                if (new[] { "GNOME", "UNITY", "CINNAMON", "BUDGIE", "PANTHEON", "POP" }.Any(desktop.Contains)) return new GnomeBackend();
            }
            return new NoBackend();
        }

        private sealed class NoBackend : IWallpaperBackend
        {
            public string? Read() => null;
            public bool Set(string path) => false;
        }

        private sealed class WindowsBackend : IWallpaperBackend
        {
            [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern int SetInfo(int uAction, int uParam, string? lpvParam, int fuWinIni);

            [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern int GetInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

            private const int SPI_SETDESKWALLPAPER = 0x0014, SPI_GETDESKWALLPAPER = 0x0073, SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

            public bool Set(string path) => SetInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE) != 0;

            /// <summary>Well past MAX_PATH: OneDrive-redirected Pictures folders run longer and the API
            /// truncates silently. Empty is normal for solid colour, slideshow, theme and Spotlight desktops.</summary>
            public string? Read()
            {
                var sb = new StringBuilder(4096);
                if (GetInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0) == 0)
                {
                    Log.Warning("[Wallpaper] SPI_GETDESKWALLPAPER failed (win32 {Err})", Marshal.GetLastWin32Error());
                    return null;
                }
                var path = sb.ToString();
                if (string.IsNullOrWhiteSpace(path)) return null;
                if (!File.Exists(path)) { Log.Warning("[Wallpaper] Reported wallpaper does not exist on disk: {Path}", path); return null; }
                return path;
            }
        }

        /// <summary>GNOME and its family: org.gnome.desktop.background, the key the colour scheme in use reads.</summary>
        private sealed class GnomeBackend : IWallpaperBackend
        {
            private const string Schema = "org.gnome.desktop.background";

            private static string Key() =>
                (Run("gsettings", "get", "org.gnome.desktop.interface", "color-scheme") ?? "").Contains("prefer-dark") ? "picture-uri-dark" : "picture-uri";

            public string? Read() => PathFromGsettings(Run("gsettings", "get", Schema, Key()));

            public bool Set(string path) => Run("gsettings", "set", Schema, Key(), new Uri(path).AbsoluteUri) != null;
        }

        /// <summary>'file:///home/me/a%20b.png' (gsettings' quoted uri) to a file that exists, else null.</summary>
        internal static string? PathFromGsettings(string? value)
        {
            var v = (value ?? "").Trim().Trim((char)39, (char)34);
            if (v.Length == 0) return null;
            try
            {
                var path = v.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? new Uri(v).LocalPath : v;
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        /// <summary>KDE Plasma: set through plasma-apply-wallpaperimage; the original is the Image= line of
        /// the desktop containment in plasma-org.kde.plasma.desktop-appletsrc.</summary>
        private sealed class KdeBackend : IWallpaperBackend
        {
            public string? Read()
            {
                try
                {
                    var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                    if (string.IsNullOrEmpty(config)) config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                    var file = Path.Combine(config, "plasma-org.kde.plasma.desktop-appletsrc");
                    return File.Exists(file) ? PathFromPlasmaConfig(File.ReadAllLines(file)) : null;
                }
                catch { return null; }
            }

            public bool Set(string path) => Run("plasma-apply-wallpaperimage", path) != null;
        }

        /// <summary>The first Image= under a [...][Wallpaper][org.kde.image][General] group that names a file.</summary>
        internal static string? PathFromPlasmaConfig(string[] lines)
        {
            bool inGroup = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith('[')) { inGroup = line.Contains("[Wallpaper][org.kde.image][General]"); continue; }
                if (!inGroup || !line.StartsWith("Image=", StringComparison.Ordinal)) continue;
                var found = PathFromGsettings(line["Image=".Length..]);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Run a desktop tool, 3 s at most. Its output on exit code 0, else null (missing tool included).</summary>
        private static string? Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p == null) return null;
                var output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return null; }
                return p.ExitCode == 0 ? output : null;
            }
            catch (Exception ex)
            {
                Log.Debug("[Wallpaper] {Tool} failed: {E}", file, ex.Message);
                return null;
            }
        }
    }
}
