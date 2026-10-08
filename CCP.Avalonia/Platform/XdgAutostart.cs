using System;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// This head's StartupManager (ConditioningControlPanel/Services/StartupManager.cs): WPF drops a
    /// shortcut into the Windows Startup folder, the freedesktop equivalent is a <c>.desktop</c>
    /// entry in <c>$XDG_CONFIG_HOME/autostart</c> (default <c>~/.config/autostart</c>), which every
    /// XDG session (GNOME, KDE, XFCE, ...) launches at login. Same "--startup" argument as WPF.
    /// </summary>
    internal static class XdgAutostart
    {
        private const string FileName = "conditioning-control-panel.desktop";

        /// <summary>Test seam: the autostart folder. Null means the XDG location.</summary>
        internal static string? DirectoryOverride { get; set; }

        internal static string EntryPath
        {
            get
            {
                var dir = DirectoryOverride;
                if (dir is null)
                {
                    var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                    if (string.IsNullOrEmpty(config))
                        config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                    dir = Path.Combine(config, "autostart");
                }
                return Path.Combine(dir, FileName);
            }
        }

        internal static bool IsRegistered()
        {
            try { return File.Exists(EntryPath); }
            catch { return false; }
        }

        /// <summary>WPF StartupManager.SetStartupState: true when the OS now matches <paramref name="enabled"/>.</summary>
        internal static bool SetStartupState(bool enabled)
        {
            var path = EntryPath;
            try
            {
                if (!enabled)
                {
                    if (File.Exists(path)) File.Delete(path);
                    Log.Information("Unregistered from session autostart");
                    return true;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path,
                    "[Desktop Entry]\nType=Application\nName=Conditioning Control Panel\n" +
                    $"Exec={ExecLine()} --startup\nX-GNOME-Autostart-enabled=true\n");
                Log.Information("Registered for session autostart: {Path}", path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update session autostart");
                return false;
            }
        }

        /// <summary>The running executable; a framework-dependent run (<c>dotnet CCP.Avalonia.dll</c>)
        /// also names the entry assembly. Quoted per the Desktop Entry spec.</summary>
        internal static string ExecLine()
        {
            var exe = Environment.ProcessPath ?? "";
            var line = Quote(exe);
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                && System.Reflection.Assembly.GetEntryAssembly()?.Location is { Length: > 0 } dll)
                line += " " + Quote(dll);
            return line;

            static string Quote(string s) =>
                "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%") + "\"";
        }
    }
}
