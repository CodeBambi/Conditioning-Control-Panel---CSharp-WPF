using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Owner call 2026-10-10: the head's installer removes the old WPF program files on an upgrade and
/// keeps all data. <c>installer-wpf-cleanup.iss</c> is a hand-kept list, so its rules are pinned here: every
/// entry is one explicit path inside {app}, none of them can match a wildcard, and none names anything that
/// is (or sounds like) user data.</summary>
public sealed class InstallerWpfCleanupTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "installer.iss"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Raw => File.ReadAllText(Path.Combine(Root(), "installer-wpf-cleanup.iss"));

    private static readonly Regex Entry = new(
        @"^Type: (?<type>files|filesandordirs|dirifempty);\s+Name: ""\{app\}\\(?<path>[A-Za-z0-9_.\-]+(?:\\[A-Za-z0-9_.\-]+)*)""$",
        RegexOptions.Compiled);

    private static List<(string Type, string Path)> Entries()
    {
        var list = new List<(string, string)>();
        foreach (var line in Raw.Split("\r\n"))
        {
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)) continue;
            var m = Entry.Match(line);
            Assert.True(m.Success, "Not an explicit {app} delete entry: " + line);
            list.Add((m.Groups["type"].Value, m.Groups["path"].Value));
        }
        return list;
    }

    [Fact]
    public void Every_entry_is_one_explicit_path_inside_the_app_folder()
    {
        var entries = Entries();
        Assert.NotEmpty(entries);
        foreach (var (_, path) in entries)
        {
            Assert.DoesNotContain("*", path);
            Assert.DoesNotContain("?", path);
            Assert.DoesNotContain("{", path);                       // no second constant: {localappdata}, {userappdata}, {commonappdata}
            Assert.All(path.Split('\\'), seg => Assert.False(seg is "." or "..", path));
        }
        Assert.Equal(entries.Count, entries.Select(e => e.Path.ToLowerInvariant()).Distinct().Count());

        // Nothing else in the file can delete: no stray directive, section or constant outside the comments.
        foreach (var line in Raw.Split("\r\n"))
        {
            if (line.StartsWith(";", StringComparison.Ordinal) || line.Length == 0) continue;
            Assert.StartsWith("Type: ", line);
            Assert.Single(Regex.Matches(line, @"{"));
        }
    }

    /// <summary>Names of things that are the player's (the user-data folder's own contents, and the folders in
    /// {app} a player may have dropped files into or that hold shipped content rather than binaries).</summary>
    private static readonly string[] DataNames =
    {
        "settings.json", "settings", "logs", "log", "mods", "builtin_mods", "content", "packs", "media", "images", "videos",
        "audio", "spirals", "wallpapers", "companion", "achievements.json", "chaster_tab.json", "browser_data", "arcademy",
        "backroom", "ebwebview", "resources", "localization", "languages", "lockedmod", "dronemod", "sessions", "prompts",
        "knowledge.json", "userdata", "user_data", "data", "profiles", "cache", "temp", ".temp", "backups", "enhancements",
    };

    [Fact]
    public void No_entry_names_user_data_or_a_data_folder()
    {
        foreach (var (type, path) in Entries())
        {
            var segs = path.ToLowerInvariant().Split('\\');
            foreach (var seg in segs)
            {
                Assert.False(DataNames.Contains(seg), $"{path}: '{seg}' is a data name");
                Assert.False(seg.StartsWith("browser_data", StringComparison.Ordinal), path);
                Assert.False(seg.EndsWith(".dat", StringComparison.Ordinal), path);
                Assert.False(seg.EndsWith(".log", StringComparison.Ordinal), path);
                Assert.False(seg.EndsWith(".ccpmod", StringComparison.Ordinal), path);
                Assert.False(seg.Contains("webview2") && !seg.EndsWith("loader.dll", StringComparison.Ordinal), path);   // a web profile folder
            }
            // A whole folder goes only from the two program trees WPF shipped and the head no longer reads.
            if (type != "files")
                Assert.True(path.StartsWith(@"assets\Chaos\", StringComparison.Ordinal) || path.StartsWith(@"libvlc\", StringComparison.Ordinal),
                    path + ": a folder delete outside assets\\Chaos and libvlc");
            // Top-level entries are files, never a top-level folder (and so never the app folder's own children wholesale).
            if (segs.Length == 1) Assert.Equal("files", type);
        }
        // The app's own installed files are never on the list.
        var paths = Entries().Select(e => e.Path).ToList();
        Assert.DoesNotContain("ConditioningControlPanel.exe", paths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"assets\Chaos\bubbles\braindrain_melt.png", paths, StringComparer.OrdinalIgnoreCase);   // still read by the Melt bubble
        Assert.DoesNotContain(@"assets\Chaos", paths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"assets\Chaos\bubbles", paths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("assets", paths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("libvlc", paths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"libvlc\win-x64", paths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_list_is_crlf_ascii_and_only_the_head_installer_includes_it()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root(), "installer-wpf-cleanup.iss"));
        Assert.All(bytes, b => Assert.True(b < 0x80));                                   // no BOM, no stray encoding
        var text = Raw;
        Assert.Equal(Regex.Matches(text, "\n").Count, Regex.Matches(text, "\r\n").Count);   // .iss files are CRLF

        var iss = File.ReadAllText(Path.Combine(Root(), "installer.iss"));
        Assert.Equal(Regex.Matches(iss, "\n").Count, Regex.Matches(iss, "\r\n").Count);
        const string include = "#include \"installer-wpf-cleanup.iss\"";
        Assert.Single(Regex.Matches(iss, Regex.Escape(include)));
        Assert.Contains("#ifdef AvaloniaHead\r\n" + include + "\r\n#endif", iss);       // never in the WPF installer
        var at = iss.IndexOf(include, StringComparison.Ordinal);
        Assert.True(at > iss.IndexOf("[InstallDelete]", StringComparison.Ordinal) && at < iss.IndexOf("[Icons]", StringComparison.Ordinal));
    }

    [Fact]
    public void The_retired_chaos_art_on_the_list_is_what_the_head_stopped_shipping()
    {
        // The head's csproj names the one Chaos sprite it still ships; everything listed under assets\Chaos must be something else.
        var csproj = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "CCP.Avalonia.csproj"));
        Assert.Contains(@"assets\Chaos\bubbles\braindrain_melt.png", csproj);
        Assert.DoesNotContain(@"assets\Chaos\**", csproj);
        var art = Path.Combine(Root(), "ConditioningControlPanel", "assets", "Chaos");
        if (!Directory.Exists(art)) return;   // a checkout without the WPF tree
        // Every top-level thing WPF shipped there is either listed or deliberately kept.
        var listed = Entries().Select(e => e.Path).Where(p => p.StartsWith(@"assets\Chaos\", StringComparison.Ordinal))
            .Select(p => p.Substring(@"assets\Chaos\".Length)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFileSystemEntries(art))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith("_staging", StringComparison.Ordinal) || name == "bubbles") continue;   // never shipped / per file below
            if (Path.GetExtension(name) is not ("" or ".png" or ".gif" or ".json")) continue;       // WPF's csproj copied these only
            Assert.True(listed.Contains(name), name + " shipped with WPF and is not on the cleanup list");
        }
        foreach (var sprite in Directory.EnumerateFiles(Path.Combine(art, "bubbles"), "*.png"))
        {
            var name = Path.GetFileName(sprite);
            Assert.Equal(name != "braindrain_melt.png", listed.Contains(@"bubbles\" + name));
        }
    }
}
