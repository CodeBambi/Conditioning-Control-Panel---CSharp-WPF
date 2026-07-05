using System;
using System.IO;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>
/// Persists the main window's last on-screen position (physical px) to a dedicated
/// <c>window-placement.json</c> beside <c>settings.json</c>, so the Avalonia head remembers
/// where the user dragged it across launches.
/// </summary>
/// <remarks>
/// <para>
/// WPF relies on Win32 remembering top-level window placement; Avalonia does NOT persist it,
/// so <see cref="WindowStartupLocation.Manual"/> alone would re-open at the platform default
/// every launch. We persist explicitly instead. See the <c>avalonia-research</c> skill and
/// AvaloniaUI/Avalonia issue #14517 (window size/position restore flakiness is mainly the
/// Maximized+size+position conflict, which we avoid by storing <b>position only</b>).
/// </para>
/// <para>
/// This helper is intentionally portable (no Avalonia types): the geometry —
/// <c>Screens</c>, <c>RenderScaling</c>, the on-screen overlap test — lives in the
/// <c>MainWindow</c> code-behind that owns the <c>Window</c>. No DI registration is needed.
/// </para>
/// </remarks>
internal static class WindowPlacementStore
{
    private const string FileName = "window-placement.json";

    // Generous sanity bounds (physical px). Negative positions are valid on multi-monitor
    // setups (a display to the left/above the primary has negative origin); reject only the
    // absurd so a corrupt or hand-edited file can never strand the window off-screen.
    private const int MinBound = -100_000;
    private const int MaxBound = 100_000;

    public static string GetPath(string? userDataPath) =>
        Path.Combine(string.IsNullOrEmpty(userDataPath) ? string.Empty : userDataPath, FileName);

    /// <summary>Loads the saved position, or <c>null</c> if absent/corrupt/out of bounds.</summary>
    public static (int X, int Y)? Load(string? userDataPath)
    {
        try
        {
            var path = GetPath(userDataPath);
            if (!File.Exists(path)) return null;
            var data = JsonConvert.DeserializeObject<PlacementData>(File.ReadAllText(path));
            if (data is null) return null;
            if (data.X < MinBound || data.X > MaxBound || data.Y < MinBound || data.Y > MaxBound)
                return null;
            return (data.X, data.Y);
        }
        catch
        {
            // Non-critical: a corrupt/unreadable file just means "center on next launch".
            return null;
        }
    }

    /// <summary>
    /// Crash-safe atomic write (temp + rename), mirroring <c>SettingsService</c>'s pattern.
    /// Best-effort: window placement is non-critical, so this never throws.
    /// </summary>
    public static void Save(string? userDataPath, int x, int y)
    {
        try
        {
            var path = GetPath(userDataPath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(new PlacementData { X = x, Y = y });
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Replace(tmp, path, destinationBackupFileName: null);
            else
                File.Move(tmp, path);
        }
        catch
        {
            // Best-effort: never fail the app over window-placement persistence.
        }
    }

    private sealed class PlacementData
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
