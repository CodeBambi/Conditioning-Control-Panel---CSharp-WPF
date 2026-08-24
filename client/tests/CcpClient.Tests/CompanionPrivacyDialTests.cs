using System.Text.Json;
using CcpClient.Desktop.Features.Companion;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// A3 (her-room-divergence-audit.md row A3, ADOPT — copy and presentation) and D11's copy.
///
/// <para>The copy facts read UPSTREAM'S OWN localisation file at runtime rather than restating the
/// sentences a second time in a test: a hard-coded expectation would only prove the port agrees
/// with itself, which is exactly the drift this row exists to close. The WPF tree is read-only, so
/// this pin cannot be satisfied from the other direction either.</para>
/// </summary>
public class CompanionPrivacyDialTests
{
    // ---- the dial is DERIVED, never stored ----

    [Theory]
    [InlineData(false, 0, CompanionPrivacyStop.Off)]
    // Consent off outranks a non-empty list: her eyes are closed regardless of who is named.
    [InlineData(false, 3, CompanionPrivacyStop.Off)]
    [InlineData(true, 0, CompanionPrivacyStop.AppNamesOnly)]
    [InlineData(true, 1, CompanionPrivacyStop.PlusPageTitles)]
    [InlineData(true, 40, CompanionPrivacyStop.PlusPageTitles)]
    public void TheStopIsDerivedFromTheStateThatDrivesTheFilter(bool consent, int namedApps, CompanionPrivacyStop expected) =>
        Assert.Equal(expected, CompanionPrivacyDial.Derive(consent, namedApps));

    /// <summary>
    /// The load-bearing half of the derivation, stated on its own because it is the privacy
    /// failure WPF names: "the dial only reports 'Everything' once an app is actually listed — a
    /// stop that silently meant nothing would be the privacy failure that looks like a working
    /// feature" (Views/Controls/Companion/Runtime/AwarenessPrivacyRuntimeVm.cs:24-27).
    /// </summary>
    [Fact]
    public void ConsentAloneNeverReachesTheThirdStop()
    {
        Assert.Equal(CompanionPrivacyStop.AppNamesOnly, CompanionPrivacyDial.Derive(consentGranted: true, namedAppCount: 0));
        Assert.NotEqual(CompanionPrivacyStop.PlusPageTitles, CompanionPrivacyDial.Derive(consentGranted: true, namedAppCount: 0));
    }

    // ---- the copy is upstream's, read from upstream's own file ----

    // Argument order note: the port's constant sits in the `expected` slot because the xUnit
    // analyzer requires the constant there. The authority is still upstream's file — a mismatch
    // fails whichever way round the two are printed.
    [Fact]
    public void EveryDialSentenceIsUpstreamsOwn()
    {
        var strings = UpstreamStrings();

        Assert.Equal(CompanionPrivacyDial.Head, strings["companion_awareness_dial_head"]);
        Assert.Equal(strings["companion_awareness_dial_off"], CompanionPrivacyDial.LabelFor(CompanionPrivacyStop.Off));
        Assert.Equal(strings["companion_awareness_dial_broad"], CompanionPrivacyDial.LabelFor(CompanionPrivacyStop.AppNamesOnly));
        Assert.Equal(strings["companion_awareness_dial_everything"], CompanionPrivacyDial.LabelFor(CompanionPrivacyStop.PlusPageTitles));

        // The three sentences the row is actually about: each one says what does NOT travel.
        Assert.Equal(strings["companion_awareness_dial_hint_off"], CompanionPrivacyDial.HintFor(CompanionPrivacyStop.Off));
        Assert.Equal(strings["companion_awareness_dial_hint_broad"], CompanionPrivacyDial.HintFor(CompanionPrivacyStop.AppNamesOnly));
        Assert.Equal(strings["companion_awareness_dial_hint_everything"], CompanionPrivacyDial.HintFor(CompanionPrivacyStop.PlusPageTitles));
    }

    /// <summary>
    /// The three hints must be three. A dial whose stops read the same is a dial that explains
    /// nothing, and it is the failure a copy-paste in <c>HintFor</c> would produce silently.
    /// </summary>
    [Fact]
    public void TheThreeHintsAreThreeDifferentSentences()
    {
        string[] hints =
        [
            CompanionPrivacyDial.HintFor(CompanionPrivacyStop.Off),
            CompanionPrivacyDial.HintFor(CompanionPrivacyStop.AppNamesOnly),
            CompanionPrivacyDial.HintFor(CompanionPrivacyStop.PlusPageTitles),
        ];
        Assert.Equal(3, hints.Distinct(StringComparer.Ordinal).Count());
        Assert.All(hints, h => Assert.NotEmpty(h));
    }

    // ---- D11's copy, from the same file ----

    [Fact]
    public void EveryTranscriptSentenceIsUpstreamsOwn()
    {
        var strings = UpstreamStrings();

        Assert.Equal(CompanionTranscriptWindow.Heading, strings["companion_chat_history_title"]);
        Assert.Equal(CompanionTranscriptWindow.EmptyCopy, strings["companion_chat_history_empty"]);
        Assert.Equal(CompanionTranscriptWindow.YouLabel, strings["companion_chat_history_you"]);
        Assert.Equal(CompanionTranscriptWindow.HerLabel, strings["companion_chat_history_her"]);
        Assert.Equal(CompanionTranscriptWindow.StorageNote, strings["companion_memory_storage_note"]);
    }

    private static Dictionary<string, string> UpstreamStrings()
    {
        var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", "en.json");
        Assert.True(File.Exists(path), $"upstream's localisation file is the source of this pin and it is missing at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String)
            {
                strings[property.Name] = property.Value.GetString()!;
            }
        }

        Assert.NotEmpty(strings);
        return strings;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }
}
