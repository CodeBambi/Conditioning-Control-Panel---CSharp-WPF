using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// programs-3a decision (docs/avalonia-decisions.md; evidence/oracle/programs-3a-canenroll.md tests 4-5):
/// the head's static capability table decides which built-in programs it can ever finish.
/// </summary>
public sealed class ProgramCapabilitiesTests
{
    /// <summary>What each program still needs on this head. A new program fails until classified here;
    /// 3b (rituals + the seeded roadmap) emptied presentation and the_takeover.</summary>
    private static readonly Dictionary<string, string[]> Needs = new()
    {
        ["first_week"] = new string[0],
        ["presentation"] = new string[0],
        ["the_takeover"] = new string[0],
        ["kept"] = new[] { "KeywordTrigger" },
        ["firmware_install"] = new[] { "KeywordTrigger" },
    };

    [Fact]
    public void EveryBuiltInProgramIsClassifiedAndOnlyTheFinishableOnesAreEnrollable()
    {
        var all = BuiltInPrograms.All();
        Assert.Equal(Needs.Keys.OrderBy(k => k), all.Select(p => p.Id).OrderBy(k => k));
        foreach (var p in all)
        {
            var missing = ProgramService.UnavailableTasks(p, ProgramCapabilities.IsAvailable)
                .Select(t => t.Kind == ConditioningControlPanel.Models.Program.ProgramTaskKind.Ritual ? "Ritual" : t.Verifier.ToString())
                .Distinct().OrderBy(s => s);
            Assert.True(Needs[p.Id].OrderBy(s => s).SequenceEqual(missing), $"{p.Id}: {string.Join(",", missing)}");
        }
        Assert.Equal(new[] { "first_week", "presentation", "the_takeover" },
            all.Where(p => ProgramService.UnavailableTasks(p, ProgramCapabilities.IsAvailable).Count == 0).Select(p => p.Id));
    }

    /// <summary>The QuestService call that raises each category.</summary>
    private static readonly Dictionary<QuestCategory, string> TrackCall = new()
    {
        [QuestCategory.Flash] = "TrackFlashImage",
        [QuestCategory.Video] = "TrackVideoMinutes",
        [QuestCategory.Spiral] = "TrackSpiralMinutes",
        [QuestCategory.PinkFilter] = "TrackPinkFilterMinutes",
        [QuestCategory.Bubbles] = "TrackBubblePopped",
        [QuestCategory.LockCard] = "TrackLockCardCompleted",
        [QuestCategory.BubbleCount] = "TrackBubbleCountCompleted",
        [QuestCategory.Mantra] = "TrackMantraCompleted",
        [QuestCategory.Autonomy] = "TrackAutonomyMinutes",
        [QuestCategory.Lockdown] = "TrackLockdownCompleted",
        [QuestCategory.BlinkTrainer] = "TrackBlinkTrainerBlink",
    };

    [Fact]
    public void EveryRaisedCategoryHasAReachableTrackCall()
    {
        var code = string.Join("\n", new[] { "CCP.Avalonia", "CCP.Core" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot(), d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && Path.GetFileName(f) != "QuestService.cs")
            .Select(f => Code(File.ReadAllText(f))));

        foreach (var category in ProgramCapabilities.Raised)
        {
            Assert.True(TrackCall.TryGetValue(category, out var call), $"{category} has no Track call mapped");
            Assert.True(Regex.IsMatch(code, $@"\bQuests\??\.{call}\s*\("), $"{category}: no {call}( call in the head or Core");
        }
    }

    /// <summary>The hooks that make those calls fire: both overlays start/stop their minute tick when their
    /// windows open and close, and the Lockdown credit is subscribed at startup (WPF App.xaml.cs:3295).</summary>
    [Fact]
    public void OverlayAndLockdownHooksAreWired()
    {
        string Src(params string[] p) => Code(File.ReadAllText(Path.Combine(new[] { RepoRoot(), "CCP.Avalonia" }.Concat(p).ToArray())));
        foreach (var overlay in new[] { "PinkFilterOverlay.cs", "SpiralOverlay.cs" })
        {
            var src = Src("Views", "Overlays", overlay);
            Assert.Matches(@"QuestMinutes\.Follow\(Windows\.Count > 0\)", src);
            Assert.Matches(@"QuestMinutes\.Follow\(false\)", src);
        }
        Assert.Matches(@"LockdownDeactivated \+=[^;]*?TrackLockdownCompleted\(", Src("App.axaml.cs"));
    }

    // Strips comments and string literals (P23), as ShellInitializerScanTests does.
    private static string Code(string src) =>
        Regex.Replace(src, @"//[^\n]*|/\*.*?\*/|'(?:[^'\\]|\\.)'|[$@]{0,2}""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
