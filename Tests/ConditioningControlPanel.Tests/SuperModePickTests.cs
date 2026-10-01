using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Super;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Classic / Both / Super only row's pure rules: what shows lit, what a press does, the arrow keys.</summary>
public class SuperModePickTests
{
    [Theory]
    [InlineData(SuperMode.Both)]
    [InlineData(SuperMode.SuperOnly)]
    [InlineData(SuperMode.Classic)]
    public void A_locked_player_sees_classic_and_a_free_try_shows_both(SuperMode stored)
    {
        Assert.Equal(SuperMode.Classic, SuperModePick.Shown(SuperEffect.Vortex, unlocked: false, freeTrying: false, stored));
        Assert.Equal(SuperMode.Both, SuperModePick.Shown(SuperEffect.Vortex, unlocked: true, freeTrying: true, stored));
        Assert.Equal(stored, SuperModePick.Shown(SuperEffect.Vortex, unlocked: true, freeTrying: false, stored));
    }

    [Fact]
    public void Super_only_never_shows_on_an_effect_that_cannot_replace()
        => Assert.Equal(SuperMode.Both, SuperModePick.Shown(SuperEffect.FlickerDeck, true, false, SuperMode.SuperOnly));

    [Fact]
    public void A_locked_press_refuses_every_super_segment_and_classic_never_refuses()
    {
        Assert.Equal(SuperPickOutcome.Refuse, SuperModePick.Decide(false, false, SuperMode.Classic, SuperMode.Both));
        Assert.Equal(SuperPickOutcome.Refuse, SuperModePick.Decide(false, false, SuperMode.Classic, SuperMode.SuperOnly));
        Assert.Equal(SuperPickOutcome.Ignore, SuperModePick.Decide(false, false, SuperMode.Classic, SuperMode.Classic));
    }

    [Fact]
    public void An_unlocked_press_stores_a_new_pick_and_ignores_the_shown_one()
    {
        Assert.Equal(SuperPickOutcome.Store, SuperModePick.Decide(true, false, SuperMode.Both, SuperMode.Classic));
        Assert.Equal(SuperPickOutcome.Store, SuperModePick.Decide(true, false, SuperMode.Classic, SuperMode.SuperOnly));
        Assert.Equal(SuperPickOutcome.Ignore, SuperModePick.Decide(true, false, SuperMode.Both, SuperMode.Both));
    }

    [Theory]
    [InlineData(SuperMode.Classic)]
    [InlineData(SuperMode.SuperOnly)]
    public void A_running_free_try_holds_the_row_still(SuperMode target)
        => Assert.Equal(SuperPickOutcome.Ignore, SuperModePick.Decide(true, true, SuperMode.Both, target));

    [Fact]
    public void Arrows_walk_the_offered_segments_and_hold_at_the_ends()
    {
        Assert.Equal(SuperMode.Both, SuperModePick.Step(SuperEffect.Creep, SuperMode.Classic, +1));
        Assert.Equal(SuperMode.SuperOnly, SuperModePick.Step(SuperEffect.Creep, SuperMode.Both, +1));
        Assert.Equal(SuperMode.SuperOnly, SuperModePick.Step(SuperEffect.Creep, SuperMode.SuperOnly, +1));
        Assert.Equal(SuperMode.Classic, SuperModePick.Step(SuperEffect.Creep, SuperMode.Classic, -1));
        // Two segments: Both is the right end.
        Assert.Equal(SuperMode.Both, SuperModePick.Step(SuperEffect.Scrawl, SuperMode.Both, +1));
        Assert.Equal(SuperMode.Classic, SuperModePick.Step(SuperEffect.Scrawl, SuperMode.Both, -1));
    }

    [Fact]
    public void Only_lighting_up_earns_the_burst()
    {
        Assert.True(SuperModePick.LightsUp(SuperMode.Classic, SuperMode.Both));
        Assert.True(SuperModePick.LightsUp(SuperMode.Classic, SuperMode.SuperOnly));
        Assert.False(SuperModePick.LightsUp(SuperMode.Both, SuperMode.SuperOnly));
        Assert.False(SuperModePick.LightsUp(SuperMode.Both, SuperMode.Classic));
    }

    [Fact]
    public void Every_label_and_line_is_in_all_nine_languages_without_em_dashes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Localization", "Languages"), "*.json");
        Assert.Equal(9, files.Length);
        var keys = Enum.GetValues<SuperMode>().SelectMany(m => new[] { SuperModePick.LabelKey(m), SuperModePick.LineKey(m) }).ToArray();
        foreach (var f in files)
        {
            var json = JObject.Parse(File.ReadAllText(f));
            foreach (var k in keys)
            {
                var v = (string?)json[k];
                Assert.False(string.IsNullOrWhiteSpace(v), $"{Path.GetFileName(f)} misses {k}");
                Assert.DoesNotContain("—", v);
            }
        }
    }
}
