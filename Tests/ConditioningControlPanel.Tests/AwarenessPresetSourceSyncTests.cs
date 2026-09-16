using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The custom-Awareness-preset source sync
/// (<c>KeywordTriggerPresetService.SyncCustomSourceFromClones</c>), which is what stands between a
/// deactivate click and the user's trigger list.
///
/// <para><b>The data loss (ccp-bugs#1185).</b> An installed preset lives twice: the SOURCE list in
/// <c>preset.Triggers</c>, and one LIVE CLONE per trigger in <c>settings.KeywordTriggers</c> under
/// the id prefix <c>preset:&lt;presetId&gt;:</c>. The editor writes to the clones; a trigger added
/// while the preset is active is appended to the source list BLANK. Uninstall used to delete the
/// clones with no mirror-back, so everything authored since activation went with them - the
/// reporter's "only the first two triggers had any info in them, the rest are just a list of empty
/// word triggers and the blank subliminal action". The mirror now runs inside
/// <c>UninstallPreset</c>, so the card's Activate pill is covered as well as the detail dialog.</para>
///
/// <para>The helper is a pure static over two lists, so these run with no App spine, no settings
/// file and no WPF.</para>
/// </summary>
public class AwarenessPresetSourceSyncTests
{
    private const string PresetId = "custom.mine";
    private const string Prefix = "preset:" + PresetId + ":";

    private static KeywordTrigger Trigger(string id, string keyword, params KeywordAction[] actions) => new()
    {
        Id = id,
        Keyword = keyword,
        Enabled = true,
        Actions = actions.ToList(),
    };

    private static KeywordAction Comment(string prompt) =>
        new AvatarCommentAction { PromptTemplate = prompt, RequireAiAvailable = false };

    private static KeywordTriggerPreset Preset(params KeywordTrigger[] triggers) => new()
    {
        Id = PresetId,
        Name = "My Words",
        IsBuiltIn = false,
        MasterEnabled = true,
        Triggers = triggers.ToList(),
    };

    /// <summary>Everything InstallPreset does to a source trigger: clone it, prefix its id.</summary>
    private static List<KeywordTrigger> Install(KeywordTriggerPreset preset)
    {
        var live = new List<KeywordTrigger>();
        foreach (var source in preset.Triggers)
        {
            var clone = source.Clone();
            clone.Id = Prefix + source.Id;
            live.Add(clone);
        }
        return live;
    }

    /// <summary>Everything UninstallPreset does after the mirror: drop this preset's clones.</summary>
    private static void Uninstall(List<KeywordTrigger> live) =>
        live.RemoveAll(t => t.Id?.StartsWith(Prefix, StringComparison.Ordinal) == true);

    [Fact]
    public void Sync_copies_live_edits_back_over_the_source_list()
    {
        var preset = Preset(Trigger("t1", "good girl", Comment("praise her")));
        var live = Install(preset);

        live[0].Keyword = "such a good girl";
        live[0].Actions.Add(Comment("and again"));

        var count = KeywordTriggerPresetService.SyncCustomSourceFromClones(preset, live);

        Assert.Equal(1, count);
        Assert.Equal("such a good girl", preset.Triggers[0].Keyword);
        Assert.Equal(2, preset.Triggers[0].Actions.Count);
    }

    [Fact]
    public void Sync_strips_the_preset_prefix_so_a_reinstall_does_not_double_it()
    {
        var preset = Preset(Trigger("t1", "drop", Comment("down you go")));
        var live = Install(preset);

        KeywordTriggerPresetService.SyncCustomSourceFromClones(preset, live);

        Assert.Equal("t1", preset.Triggers[0].Id);
        Assert.Equal(DateTime.MinValue, preset.Triggers[0].LastTriggeredAt);
    }

    /// <summary>
    /// The reported case, end to end: five triggers, three of them authored while the preset was
    /// active (so their source rows are blank), then Activate is clicked twice.
    /// </summary>
    [Fact]
    public void Deactivate_then_reactivate_keeps_all_five_triggers_whole()
    {
        // Two triggers authored BEFORE activation - these are the two that used to survive.
        var preset = Preset(
            Trigger("t1", "good girl", Comment("praise 1")),
            Trigger("t2", "sleepy", Comment("praise 2")));
        var live = Install(preset);

        // Three added WHILE installed. The editor appends a blank source row and puts the real
        // content on the live clone only - this asymmetry is the bug's fuel.
        for (int i = 3; i <= 5; i++)
        {
            preset.Triggers.Add(Trigger("t" + i, ""));
            live.Add(Trigger(Prefix + "t" + i, "keyword " + i, Comment("praise " + i)));
        }

        // Deactivate (mirror, then drop the clones) and Activate again.
        KeywordTriggerPresetService.SyncCustomSourceFromClones(preset, live);
        Uninstall(live);
        var reinstalled = Install(preset);

        Assert.Equal(5, preset.Triggers.Count);
        Assert.Equal(5, reinstalled.Count);
        Assert.All(preset.Triggers, t => Assert.False(string.IsNullOrWhiteSpace(t.Keyword)));
        Assert.All(preset.Triggers, t => Assert.Single(t.Actions));
        Assert.Equal("keyword 5", preset.Triggers[4].Keyword);
    }

    [Fact]
    public void Sync_ignores_clones_belonging_to_another_preset()
    {
        var preset = Preset(Trigger("t1", "mine", Comment("mine")));
        var live = Install(preset);
        live.Add(Trigger("preset:someone.else:t1", "theirs", Comment("theirs")));
        live.Add(Trigger("loose", "not from a preset at all"));

        KeywordTriggerPresetService.SyncCustomSourceFromClones(preset, live);

        Assert.Single(preset.Triggers);
        Assert.Equal("mine", preset.Triggers[0].Keyword);
    }

    /// <summary>
    /// The safety catch. No clones means "not installed" (or already uninstalled), NOT "the user
    /// deleted everything" - overwriting the source with an empty list there would turn the mirror
    /// into a second, worse wipe.
    /// </summary>
    [Fact]
    public void Sync_leaves_the_source_alone_when_there_are_no_clones()
    {
        var preset = Preset(Trigger("t1", "keep me", Comment("still here")));

        var count = KeywordTriggerPresetService.SyncCustomSourceFromClones(preset, new List<KeywordTrigger>());

        Assert.Equal(0, count);
        Assert.Single(preset.Triggers);
        Assert.Equal("keep me", preset.Triggers[0].Keyword);
    }

    /// <summary>
    /// A built-in preset's source list is the shipped pack, not the user's work: it must never be
    /// rewritten from the clones, or the next app update could not correct it.
    /// </summary>
    [Fact]
    public void Sync_never_touches_a_built_in_preset()
    {
        var preset = Preset(Trigger("t1", "shipped", Comment("shipped")));
        preset.IsBuiltIn = true;
        var live = Install(preset);
        live[0].Keyword = "edited live";

        var count = KeywordTriggerPresetService.SyncCustomSourceFromClones(preset, live);

        Assert.Equal(0, count);
        Assert.Equal("shipped", preset.Triggers[0].Keyword);
    }

    [Fact]
    public void Sync_tolerates_nulls()
    {
        Assert.Equal(0, KeywordTriggerPresetService.SyncCustomSourceFromClones(null, new List<KeywordTrigger>()));
        Assert.Equal(0, KeywordTriggerPresetService.SyncCustomSourceFromClones(Preset(), null));
    }
}
