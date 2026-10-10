using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// PORTED from WPF 7.1.5 Services/Launcher/LauncherShortcuts.cs TryCreateDesktopShortcut: the
    /// writing half. Windows writes the same .lnk through the same COM writer the startup entry uses
    /// (<see cref="WindowsStartupShortcut.CreateShortcut"/>). Linux writes a desktop entry to the
    /// user's applications folder (the app menu) and to the desktop folder when there is one.
    /// The names and arguments are Core's (<see cref="LauncherShortcuts"/>).
    /// </summary>
    internal static class LauncherShortcutWriter
    {
        /// <summary>Tests: where the desktop is. Null = the OS answer.</summary>
        internal static string? DesktopOverride { get; set; }

        /// <summary>Tests: the Linux applications folder. Null = $XDG_DATA_HOME/applications.</summary>
        internal static string? ApplicationsOverride { get; set; }

        /// <summary>Tests: write the Linux entry on any OS.</summary>
        internal static bool? ForceDesktopEntry { get; set; }

        /// <summary>
        /// Writes the shortcut. True when it exists afterwards, including when it was already there;
        /// false (logged, never thrown) on an unknown id or any writer failure.
        /// </summary>
        internal static bool TryCreateDesktopShortcut(string? gameId)
        {
            try
            {
                string? title = null;
                if (!string.IsNullOrWhiteSpace(gameId) &&
                    !string.Equals(gameId.Trim(), LauncherShortcuts.PanelId, StringComparison.OrdinalIgnoreCase))
                {
                    var card = LauncherCards.Find(gameId);
                    if (card == null)
                    {
                        Log.Warning("[Launcher] no shortcut for unknown game {Id}", gameId);
                        return false;
                    }
                    gameId = card.Id;
                    try { title = Loc.Get(card.TitleKey); } catch { title = null; }
                }

                var exe = WindowsStartupShortcut.GetExecutablePath();
                // A test host is not the app: a shortcut to it would be a dead icon on a real desktop
                // (a headless test once clicked a tile's button and wrote one). Tests that mean to
                // write name their own folders.
                if (DesktopOverride == null && ApplicationsOverride == null && IsTestHost(exe))
                {
                    Log.Information("[Launcher] no shortcut from a test host ({Exe})", Path.GetFileName(exe));
                    return false;
                }
                var dir = string.IsNullOrEmpty(exe) ? AppContext.BaseDirectory : Path.GetDirectoryName(exe) ?? "";
                var icon = LauncherShortcuts.ResolveIcon(gameId, dir);

                bool entry = ForceDesktopEntry ?? !OperatingSystem.IsWindows();
                return entry ? WriteDesktopEntry(gameId, title, icon) : WriteLnk(gameId, title, exe, dir, icon);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Launcher] shortcut for {Id} failed", gameId);
                return false;
            }
        }

        internal static bool IsTestHost(string? exe)
        {
            var name = Path.GetFileNameWithoutExtension(exe ?? "");
            return name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);
        }

        private static string DesktopFolder() =>
            DesktopOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        private static bool WriteLnk(string? gameId, string? title, string exe, string dir, string? icon)
        {
            var (fileName, arguments) = LauncherShortcuts.Describe(gameId, title);
            var desktop = DesktopFolder();
            if (string.IsNullOrEmpty(desktop)) { Log.Warning("[Launcher] no desktop folder"); return false; }
            var path = Path.Combine(desktop, fileName);

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Log.Warning("[Launcher] executable path not found for the shortcut");
                return File.Exists(path);
            }

            // An existing shortcut is rewritten, not skipped: one made before the icons shipped gets
            // its picture the next time the player presses the button (WPF).
            WindowsStartupShortcut.CreateShortcut(path, exe, dir,
                Path.GetFileNameWithoutExtension(fileName), arguments, icon);
            Log.Information("[Launcher] shortcut written: {Path} {Args}", path, arguments);
            return true;
        }

        private static bool WriteDesktopEntry(string? gameId, string? title, string? icon)
        {
            var (fileName, content) = LauncherShortcuts.DescribeDesktopEntry(gameId, title, XdgAutostart.ExecLine(), icon);

            var folders = new List<string>();
            var apps = ApplicationsOverride;
            if (apps == null)
            {
                var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrEmpty(data))
                    data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                apps = Path.Combine(data, "applications");
            }
            folders.Add(apps);
            var desktop = DesktopFolder();
            // A session with no desktop folder (a tiling window manager) still gets the menu entry.
            if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop)) folders.Add(desktop);

            bool any = false;
            foreach (var folder in folders)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, fileName);
                    File.WriteAllText(path, content);
                    // A desktop entry on the desktop must be executable or the shell will not launch it.
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
                    Log.Information("[Launcher] desktop entry written: {Path}", path);
                    any = true;
                }
                catch (Exception ex) { Log.Warning(ex, "[Launcher] desktop entry in {Folder} failed", folder); }
            }
            return any;
        }
    }
}
