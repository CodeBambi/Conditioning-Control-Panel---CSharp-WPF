using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Migrations;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The one-shot rewrite of Circe's Lock text saved before the gender-neutral pass. The manifest
/// change alone never reaches a returning Circe user: their per-mod pool backups, their Phrase
/// Manager toggles and any pasted prompt all hold the old strings, and nothing re-derives them.
///
/// Everything here drives <see cref="CirceNeutralMigration.Apply"/> over a bare
/// <see cref="AppSettings"/>, so no App statics are needed.
/// </summary>
public class CirceNeutralMigrationTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static AppSettings CirceUser()
    {
        var s = new AppSettings { ActiveModId = BuiltInMods.LockedId };
        s.SubliminalPoolByMod = new() { [BuiltInMods.LockedId] = new() { ["OBEY HER"] = true, ["GOOD BOY"] = true, ["MINE"] = true } };
        s.LockCardPhrasesByMod = new() { [BuiltInMods.LockedId] = new() { ["CIRCE HOLDS MY KEY."] = true, ["GOOD BOYS DON'T DECIDE."] = true } };
        s.BouncingTextPoolByMod = new() { [BuiltInMods.LockedId] = new() { ["KEPT"] = true, ["GOOD BOY"] = true } };
        s.CustomTriggersByMod = new() { [BuiltInMods.LockedId] = new() { "GOOD BOY", "KNEEL" } };
        return s;
    }

    // ---- Exact replace ---------------------------------------------------------------------

    /// <summary>The reported defect: every Circe backup swaps the old default for the new one, in place.</summary>
    [Fact]
    public void Old_defaults_in_the_Circe_backups_are_renamed_in_place()
    {
        var s = CirceUser();

        var result = CirceNeutralMigration.Apply(s, Now);

        Assert.NotNull(result);
        Assert.Equal(new[] { "OBEY HER", "GOOD PET", "MINE" }, s.SubliminalPoolByMod![BuiltInMods.LockedId].Keys);
        Assert.Equal(new[] { "CIRCE HOLDS MY KEY.", "GOOD PETS DON'T DECIDE." }, s.LockCardPhrasesByMod![BuiltInMods.LockedId].Keys);
        Assert.Equal(new[] { "KEPT", "GOOD PET" }, s.BouncingTextPoolByMod![BuiltInMods.LockedId].Keys);
        Assert.Equal(new[] { "GOOD PET", "KNEEL" }, s.CustomTriggersByMod![BuiltInMods.LockedId]);
        Assert.Equal(4, result!.PoolEntries);
        Assert.True(s.CirceNeutralTextMigrated);
    }

    /// <summary>While Circe is active the flat pools are hers too, and the pool setters are what ModService reads first.</summary>
    [Fact]
    public void The_active_pools_are_renamed_while_Circe_is_the_active_mod()
    {
        var s = CirceUser();
        s.SubliminalPool = new() { ["GOOD BOY"] = true };
        s.LockCardPhrases = new() { ["GOOD BOYS DON'T DECIDE."] = true };
        s.BouncingTextPool = new() { ["GOOD BOY"] = true };
        s.CustomTriggers = new() { "GOOD BOY" };

        CirceNeutralMigration.Apply(s, Now);

        Assert.Equal(new[] { "GOOD PET" }, s.SubliminalPool.Keys);
        Assert.Equal(new[] { "GOOD PETS DON'T DECIDE." }, s.LockCardPhrases.Keys);
        Assert.Equal(new[] { "GOOD PET" }, s.BouncingTextPool.Keys);
        Assert.Equal(new[] { "GOOD PET" }, s.CustomTriggers);
    }

    // ---- Enabled flag kept -----------------------------------------------------------------

    [Fact]
    public void A_line_the_user_switched_off_stays_off_under_its_new_text()
    {
        var s = CirceUser();
        s.SubliminalPoolByMod![BuiltInMods.LockedId]["GOOD BOY"] = false;
        s.LockCardPhrasesByMod![BuiltInMods.LockedId]["GOOD BOYS DON'T DECIDE."] = false;

        CirceNeutralMigration.Apply(s, Now);

        Assert.False(s.SubliminalPoolByMod[BuiltInMods.LockedId]["GOOD PET"]);
        Assert.False(s.LockCardPhrasesByMod[BuiltInMods.LockedId]["GOOD PETS DON'T DECIDE."]);
        Assert.True(s.SubliminalPoolByMod[BuiltInMods.LockedId]["OBEY HER"]);
    }

    // ---- User-added untouched --------------------------------------------------------------

    /// <summary>Only an exact, case-sensitive match of the old default moves. Anything typed stays as typed.</summary>
    [Fact]
    public void Text_the_user_typed_or_edited_is_never_touched()
    {
        var s = CirceUser();
        var pool = s.SubliminalPoolByMod![BuiltInMods.LockedId];
        pool["good boy"] = true;
        pool["GOOD BOY!"] = false;
        pool["MY OWN WORDS"] = true;

        CirceNeutralMigration.Apply(s, Now);

        pool = s.SubliminalPoolByMod[BuiltInMods.LockedId];
        Assert.True(pool["good boy"]);
        Assert.False(pool["GOOD BOY!"]);
        Assert.True(pool["MY OWN WORDS"]);
    }

    /// <summary>A phrase the editor recorded as hand-added is the user's, even when it spells the old default.</summary>
    [Fact]
    public void A_phrase_recorded_as_hand_added_keeps_its_old_text()
    {
        var s = CirceUser();
        s.UserAddedSubliminals.Add("GOOD BOY");
        s.UserAddedCustomTriggers.Add("GOOD BOY");

        CirceNeutralMigration.Apply(s, Now);

        Assert.Contains("GOOD BOY", s.SubliminalPoolByMod![BuiltInMods.LockedId].Keys);
        Assert.Contains("GOOD BOY", s.CustomTriggersByMod![BuiltInMods.LockedId]);
        // No hand-added set exists for bouncing text, so that one is still Circe's default.
        Assert.Contains("GOOD PET", s.BouncingTextPoolByMod![BuiltInMods.LockedId].Keys);
    }

    /// <summary>Under another mod "GOOD BOY" is the user's own (or a third-party default), in the backup and in the live pool.</summary>
    [Fact]
    public void Another_mods_pools_are_left_alone()
    {
        var s = new AppSettings { ActiveModId = "some-creator-mod" };
        s.SubliminalPool = new() { ["GOOD BOY"] = true };
        s.CustomTriggers = new() { "GOOD BOY" };
        s.SubliminalPoolByMod = new() { ["some-creator-mod"] = new() { ["GOOD BOY"] = true } };

        var result = CirceNeutralMigration.Apply(s, Now);

        Assert.Equal(new[] { "GOOD BOY" }, s.SubliminalPool.Keys);
        Assert.Equal(new[] { "GOOD BOY" }, s.CustomTriggers);
        Assert.Equal(new[] { "GOOD BOY" }, s.SubliminalPoolByMod["some-creator-mod"].Keys);
        Assert.Equal(0, result!.PoolEntries);
        Assert.Null(s.PersonaVoiceFenceUtc);
    }

    // ---- No duplicate ----------------------------------------------------------------------

    /// <summary>
    /// Both texts present (the subliminal top-up already added the new one, or the user did): one
    /// entry survives at the earlier position, and it is off if either copy was off.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void Old_and_new_together_collapse_to_one_entry(bool oldEnabled, bool newEnabled, bool expected)
    {
        var s = CirceUser();
        s.SubliminalPoolByMod![BuiltInMods.LockedId] = new()
        {
            ["GOOD BOY"] = oldEnabled,
            ["MINE"] = true,
            ["GOOD PET"] = newEnabled
        };
        s.CustomTriggersByMod![BuiltInMods.LockedId] = new() { "KNEEL", "GOOD BOY", "GOOD PET" };

        CirceNeutralMigration.Apply(s, Now);

        var pool = s.SubliminalPoolByMod[BuiltInMods.LockedId];
        Assert.Equal(new[] { "GOOD PET", "MINE" }, pool.Keys);
        Assert.Equal(expected, pool["GOOD PET"]);
        Assert.Equal(new[] { "KNEEL", "GOOD PET" }, s.CustomTriggersByMod[BuiltInMods.LockedId]);
    }

    /// <summary>A deleted default must not come back under its new name through the top-up.</summary>
    [Fact]
    public void A_deleted_old_default_keeps_its_replacement_deleted()
    {
        var s = CirceUser();
        s.RemovedDefaultSubliminals.Add("GOOD BOY");

        CirceNeutralMigration.Apply(s, Now);

        Assert.Contains("GOOD PET", s.RemovedDefaultSubliminals);
        Assert.Contains("GOOD BOY", s.RemovedDefaultSubliminals);
    }

    // ---- Idempotent ------------------------------------------------------------------------

    /// <summary>One-shot: a user who types the old text back in after the migration keeps it.</summary>
    [Fact]
    public void A_second_run_changes_nothing()
    {
        var s = CirceUser();
        Assert.NotNull(CirceNeutralMigration.Apply(s, Now));

        s.SubliminalPoolByMod![BuiltInMods.LockedId]["GOOD BOY"] = true;
        s.DisabledPhraseIds.Add("VoiceLine:GOOD BOY");
        s.PersonaVoiceFenceUtc = null;

        Assert.Null(CirceNeutralMigration.Apply(s, Now.AddDays(1)));

        Assert.Contains("GOOD BOY", s.SubliminalPoolByMod[BuiltInMods.LockedId].Keys);
        Assert.DoesNotContain("VoiceLine:GOOD PET", s.DisabledPhraseIds);
        Assert.Null(s.PersonaVoiceFenceUtc);
    }

    /// <summary>Even without the flag, rerunning over already-migrated data is a no-op (a crash before the save).</summary>
    [Fact]
    public void Rerunning_over_migrated_data_without_the_flag_is_harmless()
    {
        var s = CirceUser();
        CirceNeutralMigration.Apply(s, Now);
        var pool = s.SubliminalPoolByMod![BuiltInMods.LockedId].ToList();
        s.CirceNeutralTextMigrated = false;

        var second = CirceNeutralMigration.Apply(s, Now);

        Assert.Equal(0, second!.PoolEntries);
        Assert.Equal(pool, s.SubliminalPoolByMod[BuiltInMods.LockedId].ToList());
    }

    // ---- Prompts ---------------------------------------------------------------------------

    [Fact]
    public void A_pasted_copy_of_an_old_Circe_prompt_becomes_the_new_prompt()
    {
        var s = new AppSettings();
        s.CompanionPrompt.Personality = CirceNeutralMigration.OldPersonalityPrompts["locked-keyholder"];
        s.CompanionPrompt.UseCustomPrompt = true;

        var result = CirceNeutralMigration.Apply(s, Now);

        var expected = BuiltInMods.Locked.Personalities!.Single(p => p.Id == "locked-keyholder").PromptSettings!["Personality"];
        Assert.Equal(expected, s.CompanionPrompt.Personality);
        Assert.True(s.CompanionPrompt.UseCustomPrompt);
        Assert.Equal(1, result!.Prompts);
        Assert.Equal(Now, s.PersonaVoiceFenceUtc);
    }

    /// <summary>One character of difference makes it the user's prompt, and it is left exactly as written.</summary>
    [Fact]
    public void An_edited_custom_prompt_is_left_alone()
    {
        var edited = CirceNeutralMigration.OldPersonalityPrompts["locked-circe"] + " Call me Mistress.";
        var s = new AppSettings();
        s.CompanionPrompt.Personality = edited;

        var result = CirceNeutralMigration.Apply(s, Now);

        Assert.Equal(edited, s.CompanionPrompt.Personality);
        Assert.Equal(0, result!.Prompts);
        Assert.Null(s.PersonaVoiceFenceUtc);
    }

    /// <summary>The Circe user's old-voice chat history is fenced off the wire, never deleted, and the fence never moves back.</summary>
    [Fact]
    public void A_Circe_user_gets_the_persona_fence_moved_forward_only()
    {
        var s = CirceUser();
        CirceNeutralMigration.Apply(s, Now);
        Assert.Equal(Now, s.PersonaVoiceFenceUtc);

        var later = CirceUser();
        later.PersonaVoiceFenceUtc = Now.AddDays(1);
        CirceNeutralMigration.Apply(later, Now);
        Assert.Equal(Now.AddDays(1), later.PersonaVoiceFenceUtc);
    }

    // ---- Toggle ids ------------------------------------------------------------------------

    /// <summary>
    /// A voice line is keyed on its filename and a text-only bark on its text, and both changed.
    /// The new id is added and the old one kept: a user whose content pack has not refreshed still
    /// has the old file, and it must stay off too.
    /// </summary>
    [Fact]
    public void Disabled_and_hidden_line_ids_follow_the_rename()
    {
        var s = new AppSettings();
        s.DisabledPhraseIds.Add("VoiceLine:Good boy. Pop.");
        s.RemovedPhraseIds.Add("VoiceLine:GOOD BOYS DON'T DECIDE.");
        var (rule, oldText, newText) = CirceNeutralMigration.TextOnlyBarks[0];
        var oldBarkId = CompanionPhraseIds.BarkLineId(rule, oldText, audio: null);
        s.DisabledPhraseIds.Add(oldBarkId);
        s.PhraseAudioOverrides["VoiceLine:Wiped clean. Good boy."] = "mine.mp3";
        s.PhrasePresets.Add(new PhrasePreset { DisabledPhraseIds = new() { "VoiceLine:GOOD BOY" } });

        var result = CirceNeutralMigration.Apply(s, Now);

        Assert.Contains("VoiceLine:Good pet. Pop.", s.DisabledPhraseIds);
        Assert.Contains("VoiceLine:Good boy. Pop.", s.DisabledPhraseIds);
        Assert.Contains("VoiceLine:GOOD PETS DON'T DECIDE.", s.RemovedPhraseIds);
        Assert.Contains(CompanionPhraseIds.BarkLineId(rule, newText, audio: null), s.DisabledPhraseIds);
        Assert.Equal("mine.mp3", s.PhraseAudioOverrides["VoiceLine:Wiped clean. Good pet."]);
        Assert.Contains("VoiceLine:GOOD PET", s.PhrasePresets[0].DisabledPhraseIds);
        Assert.Equal(5, result!.ToggleIds);
    }

    [Fact]
    public void The_migration_id_shapes_match_the_services_that_read_them()
    {
        Assert.Equal(
            CompanionPhraseIds.VoiceLineId(Path.Combine("x", "flashes_audio", "GOOD PET.mp3")),
            CirceNeutralMigration.VoiceLineIdFor("GOOD PET"));
        foreach (var (rule, _, newText) in CirceNeutralMigration.TextOnlyBarks)
            Assert.Equal(CompanionPhraseIds.BarkLineId(rule, newText, audio: null), CirceNeutralMigration.TextBarkIdFor(rule, newText));
    }

    // ---- The map itself --------------------------------------------------------------------

    /// <summary>
    /// Guards the old-to-new map against drift: every old string is gone from the shipped manifest
    /// and every new one is really there, and each old prompt names a preset that still exists.
    /// </summary>
    [Fact]
    public void The_map_matches_the_shipped_Circe_manifest()
    {
        var locked = BuiltInMods.Locked;
        foreach (var (oldText, newText) in CirceNeutralMigration.PoolPhraseMap)
        {
            Assert.DoesNotContain(oldText, locked.SubliminalPool!.Keys);
            Assert.Contains(newText, locked.SubliminalPool!.Keys);
            Assert.Contains(newText, locked.BouncingTextPool!.Keys);
            Assert.Contains(newText, locked.CustomTriggers!);
        }
        foreach (var (oldText, newText) in CirceNeutralMigration.LockCardPhraseMap)
        {
            Assert.DoesNotContain(oldText, locked.LockCardPhrases!.Keys);
            Assert.Contains(newText, locked.LockCardPhrases!.Keys);
        }

        var presetIds = locked.Personalities!.Select(p => p.Id).ToHashSet();
        Assert.Equal(6, CirceNeutralMigration.OldPersonalityPrompts.Count);
        foreach (var (id, oldPrompt) in CirceNeutralMigration.OldPersonalityPrompts)
        {
            Assert.Contains(id, presetIds);
            Assert.NotEqual(oldPrompt, locked.Personalities!.Single(p => p.Id == id).PromptSettings!["Personality"]);
            Assert.Contains("good boy", oldPrompt);
        }
    }
}
