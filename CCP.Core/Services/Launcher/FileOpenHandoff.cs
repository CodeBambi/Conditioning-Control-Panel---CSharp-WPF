using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// "Open with CCP": the <c>--play &lt;file&gt;</c> / <c>--edit &lt;file&gt;</c> command-line doors and
/// the second-instance handoff file that carries them (and a <see cref="LauncherHandoff"/> surface)
/// to the running app. PORTED from WPF App.xaml.cs:90-173 (ParseFileOpenArgs, ValidateMediaArgPath,
/// WriteFileOpenHandoff, ConsumeFileOpenHandoff): the file name, its two-line shape and the rules are
/// WPF's, so a WPF 7.1.5 instance and this head read each other's handoff.
/// </summary>
public static class FileOpenHandoff
{
    public const string FileName = "fileopen.pending";
    public const string PlayAction = "play";
    public const string EditAction = "edit";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v",
        ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v"
    };

    public static string PathIn(string userDataDir) => Path.Combine(userDataDir, FileName);

    /// <summary>True for the video half of the allowed list (the editor's blank needs the media type).</summary>
    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path ?? ""));

    /// <summary>The first <c>--play</c> / <c>--edit</c> with a valid file after it, or (null, null).</summary>
    public static (string? action, string? path) ParseArgs(string[]? args)
    {
        if (args == null) return (null, null);
        for (int i = 0; i < args.Length - 1; i++)
        {
            var a = args[i];
            if (a == "--play" || a == "--edit")
            {
                var validated = ValidateMediaArgPath(args[i + 1]);
                if (validated == null) return (null, null);
                return (a == "--play" ? PlayAction : EditAction, validated);
            }
        }
        return (null, null);
    }

    /// <summary>A local, existing media file with an allowed extension, as a full path; else null.</summary>
    public static string? ValidateMediaArgPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Reject UNC and extended-length prefixes: only local file paths allowed.
        if (raw.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        string full;
        try { full = Path.GetFullPath(raw); }
        catch { return null; }
        if (!Path.IsPathRooted(full)) return null;
        if (!File.Exists(full)) return null;
        return AllowedExtensions.Contains(Path.GetExtension(full)) ? full : null;
    }

    /// <summary>Best effort: a handoff that cannot be written reads as a bare relaunch.</summary>
    public static void Write(string userDataDir, string action, string path)
    {
        try
        {
            Directory.CreateDirectory(userDataDir);
            File.WriteAllText(PathIn(userDataDir), action + "\n" + path);
        }
        catch { /* best effort */ }
    }

    /// <summary>Reads and deletes the handoff. (surface action, payload), (play|edit, file) or (null, null).</summary>
    public static (string? action, string? path) Consume(string userDataDir)
    {
        try
        {
            var p = PathIn(userDataDir);
            if (!File.Exists(p)) return (null, null);
            var lines = File.ReadAllText(p).Split('\n');
            try { File.Delete(p); } catch { /* a leftover is re-read once, then gone */ }
            if (lines.Length < 2) return (null, null);
            var action = lines[0].Trim();
            if (action == LauncherHandoff.Action)
            {
                var surface = lines[1].Trim();
                return surface.Length == 0 ? (null, null) : (action, surface);
            }
            var path = ValidateMediaArgPath(lines[1].Trim());
            if (path == null) return (null, null);
            if (action != PlayAction && action != EditAction) return (null, null);
            return (action, path);
        }
        catch { return (null, null); }
    }
}
