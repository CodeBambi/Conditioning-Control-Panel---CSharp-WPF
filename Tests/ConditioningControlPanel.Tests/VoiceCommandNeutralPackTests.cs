using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The voice-command confirmation packs (AutonomyService.VoiceCommands.cs).
///
/// Until this pass, <c>ModKey()</c> fell through to "sissy", so an unmodded install spoke the sissy
/// pack for every voice confirmation - including the safe-word line ("shh... everything's off.
/// you're safe now, good girl."). It now falls through to "neutral", which means the neutral pack
/// IS the vanilla pack: a table that forgets a "neutral" entry drops the unmodded user onto
/// <c>Confirm.Values.FirstOrDefault()</c>, and for the panic intent that is a themed line for a mod
/// they do not have. So parity with the sissy pack is the invariant worth pinning mechanically -
/// there are 40 tables and adding a 41st is a one-line edit that is easy to half-finish.
///
/// These tests touch no App statics: <see cref="AutonomyService.ModKeyFor"/> is the pure half of
/// ModKey, and the intent list is built from constant data (the Execute closures are never invoked).
/// </summary>
public class VoiceCommandNeutralPackTests
{
    private const string Neutral = "neutral";
    private const string Sissy = "sissy";

    public static TheoryData<string, string> AllTables()
    {
        var data = new TheoryData<string, string>();
        foreach (var (intent, table, _) in AutonomyService.VoiceConfirmTablesForTests())
            data.Add(intent, table);
        return data;
    }

    private static IReadOnlyDictionary<string, string> Table(string intent, string table)
        => AutonomyService.VoiceConfirmTablesForTests()
            .Single(t => t.Intent == intent && t.Table == table).Lines;

    // ── The invariant ──────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllTables))]
    public void EveryTable_HasNeutralWhereverItHasSissy(string intent, string table)
    {
        var lines = Table(intent, table);
        Assert.True(lines.ContainsKey(Sissy), $"{intent}.{table} has no sissy line - the pack shape changed, update this test.");
        Assert.True(lines.ContainsKey(Neutral),
            $"{intent}.{table} has a sissy line but no neutral one. Unmodded users hit this table, " +
            "and with no neutral entry they get whichever themed line happens to be first.");
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void EveryNeutralLine_IsNonEmpty(string intent, string table)
        => Assert.False(string.IsNullOrWhiteSpace(Table(intent, table)[Neutral]),
            $"{intent}.{table} neutral line is blank; a blank falls through to a themed line.");

    /// <summary>
    /// Neutral is written first in every table so the last-ditch <c>Values.FirstOrDefault()</c> in
    /// ExecuteIntentAndConfirm (reached when a future mod key matches no entry) lands on the neutral
    /// line rather than on Bambi's.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTables))]
    public void NeutralIsTheFirstEntry_SoTheLastDitchFallbackIsNeutral(string intent, string table)
        => Assert.Equal(Neutral, Table(intent, table).Keys.First());

    /// <summary>The line this whole pass exists for: the safe word must never go silent on vanilla.</summary>
    [Fact]
    public void PanicConfirmation_HasANeutralLine()
    {
        var panic = Table("panic", "Confirm");
        Assert.False(string.IsNullOrWhiteSpace(panic[Neutral]));
        Assert.Contains("safe", VocabTokens.Apply(panic[Neutral]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSetOfTables_IsNotAccidentallyEmpty()
        => Assert.True(AutonomyService.VoiceConfirmTablesForTests().Count >= 40);

    // ── The neutral pack is actually neutral ───────────────────────────────────

    [Theory]
    [MemberData(nameof(AllTables))]
    public void NeutralLines_CarryNoGenderedAddress(string intent, string table)
    {
        var line = VocabTokens.Apply(Table(intent, table)[Neutral]);
        foreach (var banned in new[] { "good girl", "good boy", " girl", " boy", "sissy", "bimbo", "bambi" })
            Assert.DoesNotContain(banned, line, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The neutral pack writes {petname} and ExecuteIntentAndConfirm resolves it through VocabTokens
    /// (these strings are not localized, so LocalizationManager.Get never sees them). A typo'd token
    /// would be printed verbatim in the speech bubble, so check every line survives the pass.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTables))]
    public void NeutralLines_LeaveNoUnresolvedToken(string intent, string table)
    {
        var resolved = VocabTokens.Apply(Table(intent, table)[Neutral]);
        Assert.DoesNotContain("{", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void MostNeutralLines_UseThePetnameToken()
    {
        // Not all of them can (a few sissy lines never addressed the user), but the pack should be
        // overwhelmingly petname-bearing rather than quietly dropping the user's name everywhere.
        var tables = AutonomyService.VoiceConfirmTablesForTests();
        var withToken = tables.Count(t => t.Lines[Neutral].Contains(VocabTokens.PetNameToken, StringComparison.Ordinal));
        Assert.True(withToken >= tables.Count * 2 / 3, $"only {withToken}/{tables.Count} neutral lines use {VocabTokens.PetNameToken}");
    }

    // ── ModKey: vanilla is neutral, themed mods are untouched ──────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("builtin-ccp-default")]
    [InlineData("some-third-party-mod")]
    public void UnmoddedAndUnknown_ResolveToNeutral(string? modId)
        => Assert.Equal(Neutral, AutonomyService.ModKeyFor(modId));

    [Theory]
    [InlineData("builtin-bambisleep", "bambi")]
    [InlineData("builtin-sissyhypno", "sissy")]
    [InlineData("builtin-locked", "circe")]
    [InlineData("drone-mode", "sissy")]   // deliberately unchanged: drone has no pack of its own
    public void ThemedMods_KeepTheirPack(string modId, string expected)
        => Assert.Equal(expected, AutonomyService.ModKeyFor(modId));

    /// <summary>Every key ModKeyFor can produce must exist in every table, or the lookup misses.</summary>
    [Theory]
    [MemberData(nameof(AllTables))]
    public void EveryTable_CoversEveryModKey(string intent, string table)
    {
        var lines = Table(intent, table);
        foreach (var modId in new[] { null, "", "builtin-ccp-default", "builtin-bambisleep", "builtin-sissyhypno", "builtin-locked", "drone-mode" })
            Assert.True(lines.ContainsKey(AutonomyService.ModKeyFor(modId)),
                $"{intent}.{table} has no entry for mod '{modId}' (key '{AutonomyService.ModKeyFor(modId)}').");
    }
}
