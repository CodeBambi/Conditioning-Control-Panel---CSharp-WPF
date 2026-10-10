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

    /// <summary>Surfaces a panic must never lose. Dropping one fails here; a new surface needs no edit.</summary>
    private static readonly string[] SafetyCritical =
    {
        "intake", "voice-capture", "ai-followups", "blink-trainer", "gaze-minigame", "mantra", "chaos", "haptics",
        "remote-haptics", "takeover", "program-session", "engine", "pink-rush", "corner-gif", "lock-cards", "attention-test", "deeper-editor-audio", "camera",
    };

    /// <summary>The order that is contract: (stops first, stops later). WPF refs are
    /// ConditioningControlPanel/MainWindow/MainWindow.xaml.cs unless named otherwise. Only surfaces that
    /// carry an ordering constraint belong here; register anything else with one line in PanicSurfaces.</summary>
    private static readonly (string First, string Then, string Why)[] Before =
    {
        // Avalonia decision (PanicSurfaces.cs "intake" comment): say-it loops end before the capture abort reads as silence.
        ("intake", "voice-capture", "intake loops end before the mic abort"),
        // decisions "Panic <-> mic": capture and the command chain end before anything can react to them.
        ("voice-capture", "ai-followups", "mic first"),
        // WPF :1543 CancelPendingAi() is the first stop in HandlePanicKeyPress, long before the tail's StopEngine (:1827).
        ("ai-followups", "engine", "AI cancel early"),
        // WPF :1714-1721 game surfaces + Lab minigames close BEFORE RunPanicStopTail (:1722); the engine stop never reaches them.
        ("blink-trainer", "engine", "Lab minigames before the engine"),
        ("gaze-minigame", "engine", "Lab minigames before the engine"),
        ("chaos", "engine", "WPF GameSurfaces.cs:50 'chaos' closes before the tail"),
        // WPF :1808 KillAllAudio (App.xaml.cs:1780 Mantra.Dispose) runs before StopEngine (:1827).
        ("mantra", "engine", "audio killed before the engine stop"),
        // WPF :1786 remote haptics and :1992 haptics PanicStop come before StopEngine (:1827).
        ("haptics", "engine", "toys to zero before the engine stop"),
        ("remote-haptics", "engine", "toys to zero before the engine stop"),
        // WPF :1811 Autonomy.CancelActivePulses before StopEngine (:1827).
        ("takeover", "engine", "takeover pulses cancelled before the engine stop"),
        // WPF StopEngine -> MainWindow.StartStop.cs:505 App.SkillTree?.Stop(): the 3x ends with/after the engine.
        ("engine", "pink-rush", "pink rush ends after the engine"),
        // WPF :1963-1967: session corner GIF (engine-owned) first, standalone slots LAST as the final word.
        ("engine", "corner-gif", "standalone corner slots after the session's"),
        // WPF :2030 StopAdHocEffects closes lock cards after the media stops; engine before lock cards.
        ("engine", "lock-cards", "engine before lock cards"),
        // gaze minigame ends before the camera it reads (PanicSurfaces.cs comment).
        ("gaze-minigame", "camera", "gaze ends before the camera stops"),
    };

    [Fact]
    public void TheSurfaceListIsExactAndOrdered() =>
        Assert.Equal(new[] { "intake", "games", "friends-landing", "voice-capture", "ai-followups", "blink-trainer", "gaze-minigame", "mantra", "chaos", "haptics", "remote-haptics",
            "takeover", "program-session", "engine", "pink-rush", "corner-gif", "tube", "lock-cards", "attention-test", "deeper-editor-audio", "camera" }, PanicSurfaces.All.Select(x => x.Id));

    [Fact]
    public void EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered()
    {
        var ids = PanicSurfaces.All.Select(x => x.Id).ToList();
        var dupes = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Duplicate panic surface ids: " + string.Join(", ", dupes));
        var missing = SafetyCritical.Except(ids).ToList();
        Assert.True(missing.Count == 0, "Safety-critical surfaces missing: " + string.Join(", ", missing));
    }

    /// <summary>Reordering two constrained lines fails here. Intake first and camera last (decision C:
    /// fire-and-forget, nothing after it may wait on it) are exact.</summary>
    [Fact]
    public void TheOrderingConstraintsHold()
    {
        var ids = PanicSurfaces.All.Select(x => x.Id).ToList();
        Assert.Equal("intake", ids[0]);
        Assert.Equal("camera", ids[^1]);
        var broken = Before.Where(c => !(ids.IndexOf(c.First) is >= 0 and var a && ids.IndexOf(c.Then) is var b && a < b))
            .Select(c => $"{c.First} before {c.Then} ({c.Why})").ToList();
        Assert.True(broken.Count == 0, "Panic order broken: " + string.Join("; ", broken));
    }

    /// <summary>P23: comments and string literals never count as code.</summary>
    private static string Code(string src) => Regex.Replace(src,
        @"//[^\n]*|/\*.*?\*/|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'", " ", RegexOptions.Singleline);

    /// <summary>What "starts something a panic must stop" looks like in source.</summary>
    private static readonly Regex Starts = new(
        @"\.Recognize\w*Async\(|[Tt]racker\.StartAsync\(|WebcamTracker\.Instance\.StartAsync\(|new MediaPlayer\(" +
        @"|new X11\w*(Overlay|Window)\w*\(|\bclass \w+(Host|HostService)\b", RegexOptions.Compiled);

    /// <summary>Types that start something but need no entry of their own, and why.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["LockCardWindow"] = "phrase mic + cards: the 'lock-cards' surface (StopLockCards) closes every card",
        ["DevicesSettingsSection"] = "camera preview/calibration: the 'camera' surface stops WebcamTracker",
        ["BlinkTrainerTabView"] = "camera: the 'camera' and 'blink-trainer' surfaces",
        ["SettingsTabView"] = "the Home browser card's webcam pill: the 'camera' surface stops WebcamTracker",
        ["LibVlcAudio"] = "the CoreAudio sink: Core services stop their clips on the 'engine' surface",
        ["MandatoryVideoOverlay"] = "Core video sink: the 'engine' surface (CoreEngine.Stop) closes it",
        ["FlashClipPlayer"] = "a flash's muted clip: the 'engine' surface (FlashOverlay.CloseAll) closes the window, which disposes it",
        ["BubbleCountWindow"] = "Core bubble-count sink: the 'engine' surface closes it",
        ["BubbleCountHost"] = "Core bubble-count sink: the 'engine' surface closes it",
        ["PopQuizHost"] = "OnEngineStopped closes it after the 'engine' surface",
        ["Program"] = "--speech-check CLI diagnostic: no window, exits when done",
        ["WebHost"] = "a control, not a surface: the window hosting it registers",
        ["LayeredAudio"] = "WPF parity (#668: only stopped when the master switch is off)",
        ["MiniPlayerWindow"] = "only --video-check (a CLI diagnostic) opens it",
        ["DeeperLocalAudio"] = "the Deeper editor's transport: the 'deeper-editor-audio' surface pauses every open editor",
        ["MediaHistoryWindow"] = "WPF parity: the Media Log's muted preview of a user-picked row; WPF panic leaves that window alone",
        ["LeashTaskHost"] = "registers itself at startup (LeashTaskHost.HookPanic: the leash-task stop, right after intake)",
        ["LeashExplainHost"] = "the leash '?' help router: a still explainer card the player opened, starts no feature; WPF panic leaves it alone",
        ["FriendsFeedHost"] = "the friends feed log: opens no surface, nothing to stop",
        ["RaceTrackPlayer"] = "the race's own track: the 'games' surface closes the race window, whose Closed funnel (DisposeRace -> DisposeRaceTracks) stops and disposes it (RaceTrackTests)",
        ["WinRtScreenReader"] = "screen OCR (RecognizeAsync is not the mic): WPF parity, a panic leaves the keyword screen reader to its switches (WPF stops it only in the panic fallback path and restarts it); it shows nothing itself",
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
