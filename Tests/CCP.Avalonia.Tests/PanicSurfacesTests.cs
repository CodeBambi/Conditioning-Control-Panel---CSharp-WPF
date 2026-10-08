using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>P06 made structural: every panic route stops the same registry, and every type that starts a
/// capture, a player or a host is in that registry (or allowlisted with the reason it needs no entry).</summary>
public sealed class PanicSurfacesTests
{
    [Fact]
    public void EachPanicRouteCallsStopAllExactlyOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (enabled, key) = (s.PanicKeyEnabled, s.PanicKey);
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        var prev = PanicSurfaces.All;
        var seen = new List<MainShellWindow?>();
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", sh => seen.Add(sh)) };
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            foreach (var (name, panic, owner) in new (string, Action, MainShellWindow?)[]
            {
                ("panic key", () => shell.HandlePanicKeyPress(new DateTime(2026, 1, 1)), shell),
                ("tray", MainShellWindow.StopEverything, MainShellWindow.Current),
                ("voice", shell.VoicePanic, shell),
            })
            {
                seen.Clear();
                panic();
                Assert.True(seen.Count == 1, $"{name} called StopAll {seen.Count} times");
                Assert.Same(owner, seen[0]);
            }
        }
        finally
        {
            PanicSurfaces.All = prev;
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey) = (enabled, key);
        }
    });

    [Fact]
    public void OneFailingStopNeverSkipsTheRest()
    {
        var prev = PanicSurfaces.All;
        var order = new List<string>();
        PanicSurfaces.All = new PanicSurfaces.Surface[]
        {
            new("a", _ => order.Add("a")),
            new("boom", _ => throw new InvalidOperationException()),
            new("b", _ => order.Add("b"), () => throw new InvalidOperationException()),
            new("game", _ => order.Add("game"), () => true),
        };
        try
        {
            Assert.True(PanicSurfaces.AnyOwnsTheScreen());
            PanicSurfaces.StopAll("test");
            Assert.Equal(new[] { "a", "b", "game" }, order);
        }
        finally { PanicSurfaces.All = prev; }
    }

    /// <summary>Deleting or reordering a line fails here: the order is part of the contract (mic first,
    /// camera last; the engine before lock cards).</summary>
    [Fact]
    public void TheSurfaceListIsExactAndOrdered() =>
        Assert.Equal(new[] { "intake", "voice-capture", "ai-followups", "blink-trainer", "mantra", "chaos", "haptics", "remote-haptics",
            "takeover", "engine", "corner-gif", "lock-cards", "attention-test", "camera" }, PanicSurfaces.All.Select(x => x.Id));

    /// <summary>P23: comments and string literals never count as code.</summary>
    private static string Code(string src) => Regex.Replace(src,
        @"//[^\n]*|/\*.*?\*/|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'", " ", RegexOptions.Singleline);

    /// <summary>What "starts something a panic must stop" looks like in source.</summary>
    private static readonly Regex Starts = new(
        @"\.Recognize\w*Async\(|tracker\.StartAsync\(|WebcamTracker\.Instance\.StartAsync\(|new MediaPlayer\(" +
        @"|new X11\w*(Overlay|Window)\w*\(|\bclass \w+(Host|HostService)\b", RegexOptions.Compiled);

    /// <summary>Types that start something but need no entry of their own, and why.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["LockCardWindow"] = "phrase mic + cards: the 'lock-cards' surface (StopLockCards) closes every card",
        ["DevicesSettingsSection"] = "camera preview/calibration: the 'camera' surface stops WebcamTracker",
        ["BlinkTrainerTabView"] = "camera: the 'camera' and 'blink-trainer' surfaces",
        ["LibVlcAudio"] = "the CoreAudio sink: Core services stop their clips on the 'engine' surface",
        ["MandatoryVideoOverlay"] = "Core video sink: the 'engine' surface (CoreEngine.Stop) closes it",
        ["BubbleCountWindow"] = "Core bubble-count sink: the 'engine' surface closes it",
        ["BubbleCountHost"] = "Core bubble-count sink: the 'engine' surface closes it",
        ["PopQuizHost"] = "OnEngineStopped closes it after the 'engine' surface",
        ["Program"] = "--speech-check CLI diagnostic: no window, exits when done",
        ["WebHost"] = "a control, not a surface: the window hosting it registers",
        ["LayeredAudio"] = "WPF parity (#668: only stopped when the master switch is off)",
        ["MiniPlayerWindow"] = "only --video-check (a CLI diagnostic) opens it",
        ["MediaHistoryWindow"] = "WPF parity: the Media Log's muted preview of a user-picked row; WPF panic leaves that window alone",
    };

    [Fact]
    public void EveryStarterIsRegisteredOrAllowlisted()
    {
        var root = RepoRoot();
        var registry = Code(File.ReadAllText(Path.Combine(root, "CCP.Avalonia/Views/Windows/PanicSurfaces.cs")));
        var starters = new HashSet<string>();
        var missing = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "CCP.Avalonia"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || f.EndsWith("PanicSurfaces.cs")) continue;
            var src = Code(File.ReadAllText(f));
            if (!Starts.IsMatch(src)) continue;
            // Every class the file declares, not only the first: the starter may be any of them.
            var types = Regex.Matches(src, @"\bclass (\w+)").Select(m => m.Groups[1].Value).ToList();
            starters.UnionWith(types);
            if (!types.Any(t => Allowed.ContainsKey(t) || Regex.IsMatch(registry, $@"\b{t}\b")))
                missing.Add($"{Path.GetFileName(f)} ({string.Join("/", types)})");
        }
        Assert.True(missing.Count == 0, "Not in PanicSurfaces.All and not allowlisted: " + string.Join(", ", missing));
        var stale = Allowed.Keys.Where(t => !starters.Contains(t)).ToList();
        Assert.True(stale.Count == 0, "Stale allowlist entries: " + string.Join(", ", stale));
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
