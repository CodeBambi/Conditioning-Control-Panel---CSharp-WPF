using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE TYPO GUARD for EMI Desk's moment bus (MOMENTS 4).
///
/// <para>A moment is fired by string: <c>App.EmiDesk?.Fire("sessionHalfway", ...)</c>. The bus is
/// deliberately forgiving - an id nothing knows about is dropped in silence, because a companion
/// must never throw into the host path she is only observing. That forgiveness is exactly what
/// makes a typo invisible: <c>"sessionHalfWay"</c> compiles, runs, costs nothing and says nothing,
/// forever, and no play-test can tell it from a line that simply lost its odds roll.</para>
///
/// <para>So the ids are checked here instead. Every literal the codebase fires, releases or maps a
/// bark trigger onto is read out of the source and matched against the shipped
/// <c>Resources/emi/desk-lines.json</c>. Source text rather than reflection because the fire sites
/// are scattered across twenty host files and most of them cannot be reached without a running
/// WPF app, a session engine and a webcam.</para>
/// </summary>
public class EmiMomentIdWiringTests
{
    // =====================================================================================
    //  locating the tree
    // =====================================================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppDir() => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    /// <summary>Every C# file that ships, across every product root, ignoring build output. A
    /// moment id raised from code that has moved to CCP.Core still has to be in the vocabulary,
    /// so this walks the roots rather than the WPF head alone.</summary>
    private static IEnumerable<string> SourceFiles() => SourceRoots.EnumerateProductSources("*.cs");

    // =====================================================================================
    //  the shipped vocabulary
    // =====================================================================================

    private sealed record Vocabulary(
        IReadOnlyDictionary<string, bool> Moments,   // id -> is a hold
        IReadOnlySet<string> Deferred);

    private static Vocabulary LoadVocabulary()
    {
        var path = Path.Combine(AppDir(), "Resources", "emi", "desk-lines.json");
        Assert.True(File.Exists(path), "desk-lines.json is missing at " + path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var moments = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var m in root.GetProperty("moments").EnumerateObject())
        {
            bool hold = m.Value.TryGetProperty("hold", out var h) && h.ValueKind == JsonValueKind.True;
            moments[m.Name] = hold;
        }

        var deferred = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("deferred", out var d) && d.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in d.EnumerateArray())
            {
                var id = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id)) deferred.Add(id!);
            }
        }

        Assert.True(moments.Count > 0, "desk-lines.json declared no moments at all");
        return new Vocabulary(moments, deferred);
    }

    // =====================================================================================
    //  reading the fire sites out of the source
    // =====================================================================================

    /// <summary>A moment id found in the source, with where it came from for the failure message.</summary>
    private sealed record Site(string Id, string Where);

    private static readonly Regex FireLiteral =
        new(@"\bFire\(\s*""([A-Za-z][A-Za-z0-9]*)""", RegexOptions.Compiled);

    // Two moments are chosen inside the Fire call itself, by a ternary on the target id. Read both
    // branches: a typo in the arm that is not the common one is the hardest kind to notice. The
    // trailing [,)] is what keeps this off the ctx payload, where a ternary between two ordinary
    // strings (an answer of "yes" or "no") is not a moment id at all.
    private static readonly Regex FireTernary =
        new(@"\bFire\(\s*[^;]{0,160}?\?\s*""([A-Za-z][A-Za-z0-9]*)""\s*:\s*""([A-Za-z][A-Za-z0-9]*)""\s*[,)]",
            RegexOptions.Compiled);

    // The voice holds are armed by id through a helper rather than through Fire, because the same
    // call also books the release the poll owes them.
    private static readonly Regex ArmVoiceHoldLiteral =
        new(@"\bArmVoiceHold\(\s*""([A-Za-z][A-Za-z0-9]*)""", RegexOptions.Compiled);

    private static readonly Regex ReleaseLiteral =
        new(@"\bReleaseHold\(\s*""([A-Za-z][A-Za-z0-9]*)""", RegexOptions.Compiled);

    // The bark bridge maps triggers onto moments through a table of `new("momentId", ...)` rows,
    // and a handful of rows choose between two ids in a `Pick` ternary. Neither shape goes through
    // Fire( in that file, so both are read out separately - a typo in the table is exactly as silent
    // as a typo in a Fire call, and rather more likely, because the table is long.
    private static readonly Regex BridgeRow =
        new(@"=\s*new\(\s*""([A-Za-z][A-Za-z0-9]*)""", RegexOptions.Compiled);

    private static readonly Regex BridgePickBranch =
        new(@"[?:]\s*""([A-Za-z][A-Za-z0-9]*)""", RegexOptions.Compiled);

    /// <summary>Every moment id this codebase can actually fire.</summary>
    private static List<Site> FiredIds()
    {
        var found = new List<Site>();

        foreach (var path in SourceFiles())
        {
            var text = File.ReadAllText(path);
            var name = Path.GetFileName(path);

            foreach (Match m in FireLiteral.Matches(text))
                found.Add(new Site(m.Groups[1].Value, name + " Fire()"));

            foreach (Match m in FireTernary.Matches(text))
            {
                found.Add(new Site(m.Groups[1].Value, name + " Fire() ternary"));
                found.Add(new Site(m.Groups[2].Value, name + " Fire() ternary"));
            }

            foreach (Match m in ArmVoiceHoldLiteral.Matches(text))
                found.Add(new Site(m.Groups[1].Value, name + " ArmVoiceHold()"));

            if (string.Equals(name, "EmiBarkBridge.cs", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in BridgeRow.Matches(text))
                    found.Add(new Site(m.Groups[1].Value, name + " table row"));
                foreach (Match m in BridgePickBranch.Matches(text))
                    found.Add(new Site(m.Groups[1].Value, name + " Pick branch"));
            }
        }

        return found;
    }

    /// <summary>Every moment id this codebase releases a hold on.</summary>
    private static List<Site> ReleasedIds()
    {
        var found = new List<Site>();
        foreach (var path in SourceFiles())
        {
            var text = File.ReadAllText(path);
            foreach (Match m in ReleaseLiteral.Matches(text))
                found.Add(new Site(m.Groups[1].Value, Path.GetFileName(path)));
        }
        return found;
    }

    // =====================================================================================
    //  the tests
    // =====================================================================================

    /// <summary>
    /// Nothing fires into the void. Every id in the source exists in the shipped lines file, so a
    /// mistyped moment fails the build instead of quietly costing her a beat for a release cycle.
    /// </summary>
    [Fact]
    public void EveryFiredMomentIdIsInTheLinesFile()
    {
        var vocab = LoadVocabulary();
        var sites = FiredIds();

        Assert.True(sites.Count > 20,
            $"only {sites.Count} moment ids were found in the source - the scan is broken, not the wiring");

        var unknown = sites
            .Where(s => !vocab.Moments.ContainsKey(s.Id) && !vocab.Deferred.Contains(s.Id))
            .Select(s => $"{s.Id} ({s.Where})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0,
            "these moment ids are fired but do not exist in Resources/emi/desk-lines.json:\n  "
            + string.Join("\n  ", unknown));
    }

    /// <summary>
    /// The deferred list is the round-2 vocabulary: ids that are written down but have no pools
    /// behind them yet. Wiring one is not an error the app can see - it just fires and says nothing
    /// - so it is caught here, where the scope of the wave is still legible.
    /// </summary>
    [Fact]
    public void NoDeferredMomentIsWiredYet()
    {
        var vocab = LoadVocabulary();

        var early = FiredIds()
            .Where(s => vocab.Deferred.Contains(s.Id) && !vocab.Moments.ContainsKey(s.Id))
            .Select(s => $"{s.Id} ({s.Where})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(early.Count == 0,
            "these moments are still DEFER in desk-lines.json but something already fires them:\n  "
            + string.Join("\n  ", early));
    }

    /// <summary>
    /// A release is only meaningful against a hold. Releasing a plain moment is a no-op, and worse,
    /// it reads in the source like the silence is being ended when nothing was ever holding it.
    /// </summary>
    [Fact]
    public void EveryReleasedIdIsAHoldMoment()
    {
        var vocab = LoadVocabulary();
        var sites = ReleasedIds();

        Assert.True(sites.Count > 0, "no ReleaseHold call sites were found at all - the scan is broken");

        var wrong = sites
            .Where(s => !vocab.Moments.TryGetValue(s.Id, out var hold) || !hold)
            .Select(s => $"{s.Id} ({s.Where})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(wrong.Count == 0,
            "these ids have a ReleaseHold call but are not hold moments in desk-lines.json:\n  "
            + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// THE OTHER HALF OF THE SILENCE LAW. A hold that says "holdUntilReleased" and never gets a
    /// release is a companion who goes quiet and stays quiet: no timer, no tail, no way back short
    /// of a restart. Every such moment must have a release somewhere in the source.
    /// </summary>
    [Fact]
    public void EveryUntilReleasedHoldHasAReleasePath()
    {
        var path = Path.Combine(AppDir(), "Resources", "emi", "desk-lines.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var needRelease = new List<string>();
        foreach (var m in doc.RootElement.GetProperty("moments").EnumerateObject())
        {
            bool untilReleased = m.Value.TryGetProperty("holdUntilReleased", out var u)
                                 && u.ValueKind == JsonValueKind.True;
            if (untilReleased) needRelease.Add(m.Name);
        }

        // Fired only, never released, is fine for the two the service releases itself off its own
        // poll (the voice holds) - those are collected here the same way, from the source.
        var released = new HashSet<string>(ReleasedIds().Select(s => s.Id), StringComparer.Ordinal);
        foreach (var path2 in SourceFiles())
        {
            var text = File.ReadAllText(path2);
            // EmiDeskService releases its voice holds through a loop over a set, so the ids never
            // appear next to ReleaseHold. They are armed through ArmVoiceHold instead.
            foreach (Match m in Regex.Matches(text, @"\bArmVoiceHold\(\s*""([A-Za-z][A-Za-z0-9]*)"""))
                released.Add(m.Groups[1].Value);
        }

        var stuck = needRelease.Where(id => !released.Contains(id))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(stuck.Count == 0,
            "these holds are holdUntilReleased but nothing in the app ever releases them, so she "
            + "would go silent for the rest of the run:\n  " + string.Join("\n  ", stuck));
    }
}
