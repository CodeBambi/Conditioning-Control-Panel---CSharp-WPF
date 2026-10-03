using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1142: an asset preset also carries the Scrolller selection and media source.</summary>
public class AssetPresetOnlineChoiceTests
{
    private static AppSettings Settings(string source = "local", bool consent = false) => new()
    {
        RemoteMediaConsented = consent,
        MediaSource = source,
        FypOnlineNiches = new List<string> { "hypno" },
        FypOnlineCustomSubs = new List<string> { "oldsub" },
    };

    [Fact]
    public void Old_preset_without_online_fields_leaves_the_choice_alone()
    {
        var s = Settings("mixed", consent: true);
        var changed = AssetPresetService.ApplyOnlineChoice(s, new AssetPreset());

        Assert.False(changed);
        Assert.Equal("mixed", s.MediaSource);
        Assert.Equal(new[] { "hypno" }, s.FypOnlineNiches);
        Assert.Equal(new[] { "oldsub" }, s.FypOnlineCustomSubs);
    }

    [Fact]
    public void Preset_switches_niches_subs_and_source_with_consent()
    {
        var s = Settings("local", consent: true);
        var preset = new AssetPreset
        {
            OnlineNiches = new List<string> { "bimbo", "latex" },
            OnlineCustomSubs = new List<string> { "newsub" },
            MediaSource = "online",
        };

        Assert.True(AssetPresetService.ApplyOnlineChoice(s, preset));
        Assert.Equal(new[] { "bimbo", "latex" }, s.FypOnlineNiches);
        Assert.Equal(new[] { "newsub" }, s.FypOnlineCustomSubs);
        Assert.True(s.LibraryHasSub("newsub"));
        Assert.Equal("online", s.MediaSource);
    }

    [Fact]
    public void Preset_never_turns_online_on_without_consent()
    {
        var s = Settings("local", consent: false);
        AssetPresetService.ApplyOnlineChoice(s, new AssetPreset { MediaSource = "mixed" });
        Assert.Equal("local", s.MediaSource);
    }

    [Fact]
    public void Preset_can_always_go_back_to_local()
    {
        var s = Settings("online", consent: true);
        s.RemoteMediaConsented = false;   // consent withdrawn, source still online
        AssetPresetService.ApplyOnlineChoice(s, new AssetPreset { MediaSource = "local" });
        Assert.Equal("local", s.MediaSource);
    }

    [Fact]
    public void Same_selection_in_another_order_is_not_a_change()
    {
        var s = Settings();
        s.FypOnlineNiches = new List<string> { "a", "b" };
        var changed = AssetPresetService.ApplyOnlineChoice(s, new AssetPreset { OnlineNiches = new List<string> { "B", "a" } });
        Assert.False(changed);
    }

    [Fact]
    public void Json_round_trip_keeps_the_fields_and_an_old_file_reads_null()
    {
        var preset = new AssetPreset
        {
            OnlineNiches = new List<string> { "bimbo" },
            OnlineCustomSubs = new List<string>(),
            MediaSource = "mixed",
        };
        var back = JsonConvert.DeserializeObject<AssetPreset>(JsonConvert.SerializeObject(preset))!;
        Assert.Equal(new[] { "bimbo" }, back.OnlineNiches);
        Assert.NotNull(back.OnlineCustomSubs);
        Assert.Empty(back.OnlineCustomSubs!);
        Assert.Equal("mixed", back.MediaSource);

        var old = JsonConvert.DeserializeObject<AssetPreset>("{\"Id\":\"x\",\"Name\":\"Old\"}")!;
        Assert.Null(old.OnlineNiches);
        Assert.Null(old.OnlineCustomSubs);
        Assert.Null(old.MediaSource);
    }

    [Fact]
    public void Default_all_assets_preset_carries_no_online_choice()
    {
        var d = AssetPreset.CreateDefault();
        Assert.Null(d.OnlineNiches);
        Assert.Null(d.MediaSource);
    }
}
