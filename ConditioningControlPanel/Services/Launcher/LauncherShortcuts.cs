using System;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// Desktop shortcuts for the launcher's tiles. A game shortcut carries <c>--game &lt;id&gt;</c>
/// so the boot decision lands in the game; the panel shortcut carries <c>--panel</c>. Same COM
/// writer as the startup entry (<see cref="StartupManager"/>), same exe path rules.
/// </summary>
public static class LauncherShortcuts
{
    public const string PanelFileName = "Conditioning Control Panel.lnk";
    private const string GamePrefix = "CC Labs - ";

    /// <summary>
    /// The pure half: file name and arguments for a tile. Null or "panel" means the panel.
    /// A title that is blank or unsafe on disk falls back to the id; characters a file name
    /// cannot carry are dropped rather than replaced, so the name stays readable.
    /// </summary>
    public static (string FileName, string Arguments) Describe(string? gameId, string? title)
    {
        if (string.IsNullOrWhiteSpace(gameId) ||
            string.Equals(gameId.Trim(), LauncherCatalogue.PanelId, StringComparison.OrdinalIgnoreCase))
            return (PanelFileName, "--panel");

        var id = gameId.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((title ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (safe.Length == 0) safe = id;
        return (GamePrefix + safe + ".lnk", "--game " + id);
    }

    /// <summary>
    /// Writes the shortcut to the desktop. True when it exists afterwards, including when it was
    /// already there; false (logged, never thrown) on an unknown id or any writer failure.
    /// </summary>
    public static bool TryCreateDesktopShortcut(string? gameId)
    {
        try
        {
            string? title = null;
            if (!string.IsNullOrWhiteSpace(gameId) &&
                !string.Equals(gameId.Trim(), LauncherCatalogue.PanelId, StringComparison.OrdinalIgnoreCase))
            {
                var entry = LauncherCatalogue.Find(gameId);
                if (entry == null)
                {
                    Log.Warning("[Launcher] no shortcut for unknown game {Id}", gameId);
                    return false;
                }
                gameId = entry.Id;
                title = entry.Title;
            }

            var (fileName, arguments) = Describe(gameId, title);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) { Log.Warning("[Launcher] no desktop folder"); return false; }
            var path = Path.Combine(desktop, fileName);
            if (File.Exists(path)) return true;

            var exe = StartupManager.GetExecutablePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Log.Warning("[Launcher] executable path not found for the shortcut");
                return false;
            }

            StartupManager.CreateShortcut(path, exe, Path.GetDirectoryName(exe) ?? "",
                Path.GetFileNameWithoutExtension(fileName), arguments);
            Log.Information("[Launcher] shortcut written: {Path} {Args}", path, arguments);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Launcher] shortcut for {Id} failed", gameId);
            return false;
        }
    }
}
