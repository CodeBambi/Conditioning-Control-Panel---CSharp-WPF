using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner pivot 2026-09-25: CCP Default is plain and gender-neutral. It must never borrow the
/// Bambi-voiced shared whisper clips, never download the Bambi flash voice pack, never giggle,
/// and never show the themed awareness presets. The themed mods keep all of it.
/// </summary>
public class ModAudioPolicyTests
{
    [Theory]
    [InlineData(BuiltInMods.BambiSleepId, true)]
    [InlineData(BuiltInMods.SissyHypnoId, true)]
    [InlineData(BuiltInMods.DronificationId, true)]
    [InlineData(BuiltInMods.LockedId, false)]
    [InlineData("some-user-mod", true)]
    [InlineData(BuiltInMods.CCPDefaultId, false)]
    [InlineData("BUILTIN-CCP-DEFAULT", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void SharedSubAudio_NeverForCcpDefault(string? modId, bool expected)
        => Assert.Equal(expected, ModAudioPolicy.UsesSharedSubAudio(modId));

    [Theory]
    [InlineData(BuiltInMods.BambiSleepId, true)]
    [InlineData(BuiltInMods.CCPDefaultId, false)]
    [InlineData(BuiltInMods.SissyHypnoId, false)]
    [InlineData(BuiltInMods.LockedId, false)]
    [InlineData(BuiltInMods.DronificationId, false)]
    [InlineData(null, false)]
    public void BaselineVoicePack_OnlyForBambiSleep(string? modId, bool expected)
        => Assert.Equal(expected, ModAudioPolicy.UsesBaselineVoicePack(modId));

    [Fact]
    public void BaselineVoicePack_MatchesTheModsThatPlayIt()
    {
        // Only a mod that actually plays the baseline flash voice may pay for the download.
        foreach (var id in new[] { BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId, BuiltInMods.CCPDefaultId,
                                   BuiltInMods.LockedId, BuiltInMods.DronificationId, BuiltInMods.InfectionControlId })
        {
            if (ModAudioPolicy.UsesBaselineVoicePack(id))
                Assert.True(ConditioningControlPanel.Services.Companion.CompanionContentResolver.OwnsBaselineVoiceLines(id));
        }
    }

    [Theory]
    [InlineData(BuiltInMods.CCPDefaultId, true)]
    [InlineData(BuiltInMods.BambiSleepId, true)]
    [InlineData(BuiltInMods.SissyHypnoId, false)]
    [InlineData(BuiltInMods.LockedId, false)]
    [InlineData(null, false)]
    public void GiggleSfx_SilentOnCcpDefault(string? modId, bool expected)
        => Assert.Equal(expected, ModAudioPolicy.SuppressesGiggleSfx(modId));

    [Theory]
    [InlineData("builtin.bimbo")]
    [InlineData("builtin.puppy")]
    [InlineData("builtin.chastity")]
    public void ThemedPresets_HiddenUnderCcpDefault(string presetId)
        => Assert.False(ModAudioPolicy.AwarenessPresetVisible(presetId, BuiltInMods.CCPDefaultId, installed: false, modIsBuiltIn: true));

    [Fact]
    public void ThemedPresets_ShowUnderTheirOwnMods()
    {
        Assert.True(ModAudioPolicy.AwarenessPresetVisible("builtin.bimbo", BuiltInMods.BambiSleepId, false, true));
        Assert.True(ModAudioPolicy.AwarenessPresetVisible("builtin.puppy", BuiltInMods.SissyHypnoId, false, true));
        Assert.True(ModAudioPolicy.AwarenessPresetVisible("builtin.chastity", BuiltInMods.LockedId, false, true));
        Assert.False(ModAudioPolicy.AwarenessPresetVisible("builtin.chastity", BuiltInMods.BambiSleepId, false, true));
        Assert.False(ModAudioPolicy.AwarenessPresetVisible("builtin.bimbo", BuiltInMods.LockedId, false, true));
    }

    [Fact]
    public void TranceAndCustomPresets_ShowEverywhere()
    {
        Assert.True(ModAudioPolicy.AwarenessPresetVisible("builtin.trance", BuiltInMods.CCPDefaultId, false, true));
        Assert.True(ModAudioPolicy.AwarenessPresetVisible("custom.abc123", BuiltInMods.CCPDefaultId, false, true));
    }

    [Fact]
    public void InstalledThemedPreset_StaysVisible_SoItCanBeSwitchedOff()
        => Assert.True(ModAudioPolicy.AwarenessPresetVisible("builtin.bimbo", BuiltInMods.CCPDefaultId, installed: true, modIsBuiltIn: true));

    [Fact]
    public void UserInstalledMod_KeepsTheWholeLibrary()
        => Assert.True(ModAudioPolicy.AwarenessPresetVisible("builtin.chastity", "someones-mod", false, modIsBuiltIn: false));

    [Fact]
    public void ThemedPresetIds_MatchTheShippedFiles()
    {
        // The policy keys on preset ids; a renamed id in the JSON would silently show it everywhere.
        var dir = Path.Combine(ProjectRoot(), "Resources", "AwarenessPresets");
        foreach (var (file, id) in new[] { ("bimbo.json", "builtin.bimbo"), ("puppy.json", "builtin.puppy"), ("chastity.json", "builtin.chastity") })
            Assert.Contains($"\"id\": \"{id}\"", File.ReadAllText(Path.Combine(dir, file)));
    }

    [Fact]
    public void OrphanedBambiClips_AreGoneFromTheSoundsRoot_ButQuizClipsStay()
    {
        var sounds = Path.Combine(ProjectRoot(), "Resources", "sounds");
        foreach (var gone in new[] { "BAMBI FREEZE.mp3", "BAMBI SLEEP.mp3", "SNAP AND FORGET.MP3", "DROP FOR COCK.mp3", "GIGGLETIME.mp3" })
            Assert.False(File.Exists(Path.Combine(sounds, gone)), gone);
        // QuizWindow plays these two by name (Bambi praise sting, fallback drone hum).
        Assert.True(File.Exists(Path.Combine(sounds, "GOOD GIRL.mp3")));
        Assert.True(File.Exists(Path.Combine(sounds, "00 Bimbo Drone.mp3")));
        // Bambi Sleep and Sissy Hypno (no InstalledPath) still read their trigger clips from here.
        Assert.True(Directory.Exists(Path.Combine(ProjectRoot(), "Resources", "sub_audio")));
    }

    private static string ProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "ConditioningControlPanel");
            if (Directory.Exists(Path.Combine(candidate, "Resources"))) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("project root not found from " + AppContext.BaseDirectory);
    }
}
