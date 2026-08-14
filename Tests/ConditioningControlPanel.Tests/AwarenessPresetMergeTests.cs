using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The built-in Awareness preset merge contract
/// (<c>SettingsService.MergeBuiltInPresetInto</c>, the per-preset core of
/// <c>MergeBuiltInAwarenessPresets</c>).
///
/// <para><b>The bug this locks down.</b> The definition copy used to ride the version gate
/// (<c>preset.Version &gt; existing.Version</c>). A settings.json whose stored preset copies had
/// been mangled by rounds of UTF-8-read-as-cp1252 re-encoding — preset cards reading
/// <c>"smart", "focus" ÃƒÆ'Ã†â€™…</c> — was therefore unfixable: the stored copies were already at the
/// bundled version, so the clean bundled text never re-landed and the mojibake was frozen forever.
/// The definition is now copied from the JSON on every load.</para>
///
/// <para><b>And the half that must NOT change.</b> The re-install queue is still strictly
/// version-gated. If the unconditional copy started dragging that machinery along, every launch
/// would silently uninstall and re-install the user's presets — so the "same version" cases below
/// assert MasterEnabled survived and the queue stayed empty, which is the real regression risk.</para>
///
/// <para>The method is deliberately free of file IO and <c>App.*</c> statics, so these run with no
/// WPF app spine.</para>
/// </summary>
public class AwarenessPresetMergeTests
{
    /// <summary>Text as the owner's settings.json actually carried it: four re-encoding rounds deep.</summary>
    private const string Mojibake = "\"smart\", \"focus\" ÃƒÆ'Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ praise";

    private const string Clean = "\"smart\", \"focus\" → praise";

    private static KeywordTriggerPreset Bundled(int version = 3) => new()
    {
        Id = "builtin.bimbo",
        Name = "Bimbo Reinforcement",
        Icon = "💖",
        Description = Clean,
        LongDescription = "Rewards " + Clean,
        Author = "Built-in",
        Version = version,
        IsBuiltIn = true,
        RequiresAi = true,
        AvatarPromptTemplate = "template-v" + version,
        PhrasePools = new List<string> { "praise" },
        CannedPhrases = new Dictionary<string, List<string>> { ["praise"] = new() { "good girl" } },
        Triggers = new List<KeywordTrigger> { new() { Id = "t1", Keyword = "smart" } },
    };

    /// <summary>The corrupted copy sitting in settings.json — same id, same version, mangled text.</summary>
    private static KeywordTriggerPreset Stored(int version = 3, bool installed = false) => new()
    {
        Id = "builtin.bimbo",
        Name = "Bimbo Reinforcement",
        Icon = "ðŸ'–",
        Description = Mojibake,
        LongDescription = "Rewards " + Mojibake,
        Author = "Built-in",
        Version = version,
        IsBuiltIn = true,
        MasterEnabled = installed,
        Triggers = new List<KeywordTrigger> { new() { Id = "t1", Keyword = "smart" } },
    };

    private static AppSettings SettingsWith(params KeywordTriggerPreset[] presets)
    {
        var settings = new AppSettings();
        settings.KeywordTriggerPresets.Clear();
        foreach (var p in presets) settings.KeywordTriggerPresets.Add(p);
        return settings;
    }

    [Fact]
    public void SameVersion_MangledText_IsHealedFromTheBundledJson()
    {
        var stored = Stored();
        var settings = SettingsWith(stored);
        var queue = new List<string>();

        var result = SettingsService.MergeBuiltInPresetInto(settings, Bundled(), queue);

        Assert.Equal(SettingsService.BuiltInPresetMergeResult.Healed, result);
        Assert.Equal(Clean, stored.Description);
        Assert.Equal("Rewards " + Clean, stored.LongDescription);
        Assert.Equal("💖", stored.Icon);
        // Same object, healed in place — not a second card in the grid.
        Assert.Single(settings.KeywordTriggerPresets);
        Assert.Same(stored, settings.KeywordTriggerPresets[0]);
    }

    [Fact]
    public void SameVersion_AlsoRefreshesTheNonTextDefinitionFields()
    {
        var stored = Stored();
        var settings = SettingsWith(stored);

        SettingsService.MergeBuiltInPresetInto(settings, Bundled(), new List<string>());

        Assert.True(stored.RequiresAi);
        Assert.Equal("template-v3", stored.AvatarPromptTemplate);
        Assert.Equal(new[] { "praise" }, stored.PhrasePools);
        Assert.True(stored.CannedPhrases.ContainsKey("praise"));
        Assert.Single(stored.Triggers);
    }

    [Fact]
    public void SameVersion_InstalledPreset_StaysInstalledAndQueuesNoReinstall()
    {
        // The regression that would hurt most: healing must not look like an upgrade, or every
        // launch would uninstall/re-install the user's presets underneath them.
        var stored = Stored(installed: true);
        var settings = SettingsWith(stored);
        var queue = new List<string>();

        SettingsService.MergeBuiltInPresetInto(settings, Bundled(), queue);

        Assert.True(stored.MasterEnabled);
        Assert.Empty(queue);
    }

    [Fact]
    public void SameVersion_IdenticalDefinition_IsReportedUnchanged()
    {
        var settings = SettingsWith(Bundled());

        var result = SettingsService.MergeBuiltInPresetInto(settings, Bundled(), new List<string>());

        Assert.Equal(SettingsService.BuiltInPresetMergeResult.Unchanged, result);
    }

    [Fact]
    public void NewerVersion_StillUpgradesAndQueuesTheReinstall()
    {
        var stored = Stored(version: 3, installed: true);
        var settings = SettingsWith(stored);
        var queue = new List<string>();

        var result = SettingsService.MergeBuiltInPresetInto(settings, Bundled(version: 4), queue);

        Assert.Equal(SettingsService.BuiltInPresetMergeResult.Upgraded, result);
        Assert.Equal(4, stored.Version);
        // Flipped off so InstallPreset's "already installed" guard won't bail out on the drain.
        Assert.False(stored.MasterEnabled);
        Assert.Equal(new[] { "builtin.bimbo" }, queue);
    }

    [Fact]
    public void NewerVersion_UninstalledPreset_QueuesNothing()
    {
        var stored = Stored(version: 3, installed: false);
        var settings = SettingsWith(stored);
        var queue = new List<string>();

        SettingsService.MergeBuiltInPresetInto(settings, Bundled(version: 4), queue);

        Assert.False(stored.MasterEnabled);
        Assert.Empty(queue);
    }

    [Fact]
    public void OlderBundledVersion_HealsTextButNeverDowngradesIntoAReinstall()
    {
        // Defensive: a downgraded install must not be read as an upgrade.
        var stored = Stored(version: 4, installed: true);
        var settings = SettingsWith(stored);
        var queue = new List<string>();

        var result = SettingsService.MergeBuiltInPresetInto(settings, Bundled(version: 3), queue);

        Assert.Equal(SettingsService.BuiltInPresetMergeResult.Healed, result);
        Assert.Equal(Clean, stored.Description);
        Assert.True(stored.MasterEnabled);
        Assert.Empty(queue);
    }

    [Fact]
    public void UnknownPreset_IsAppendedUninstalled()
    {
        var settings = SettingsWith();

        var result = SettingsService.MergeBuiltInPresetInto(settings, Bundled(), new List<string>());

        Assert.Equal(SettingsService.BuiltInPresetMergeResult.Added, result);
        Assert.Single(settings.KeywordTriggerPresets);
        Assert.False(settings.KeywordTriggerPresets[0].MasterEnabled);
        Assert.True(settings.KeywordTriggerPresets[0].IsBuiltIn);
    }

    [Fact]
    public void RemovedByTheUser_IsNeitherAddedNorHealed()
    {
        var settings = SettingsWith();
        settings.RemovedBuiltInPresetIds.Add("builtin.bimbo");

        var result = SettingsService.MergeBuiltInPresetInto(settings, Bundled(), new List<string>());

        Assert.Equal(SettingsService.BuiltInPresetMergeResult.Skipped, result);
        Assert.Empty(settings.KeywordTriggerPresets);
    }
}
