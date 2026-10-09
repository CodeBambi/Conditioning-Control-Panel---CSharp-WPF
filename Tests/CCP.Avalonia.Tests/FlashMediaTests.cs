using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 7.1.5 flash media on this head: the burst's voice line (GetNextSound, PlaySound's
/// curve, IsCompanionVoiceSilenced, lifetime from the clip), and the online clip player's sizing.</summary>
public sealed class FlashMediaTests
{
    [Fact]
    public void Volume_follows_the_wpf_curve_with_a_floor_but_a_real_mute()
    {
        Assert.Equal(0f, FlashVoicePool.Volume(0));
        Assert.Equal(0.85f, FlashVoicePool.Volume(100), 3);
        Assert.Equal(0.05f * 0.85f, FlashVoicePool.Volume(1), 4);   // floor on the curve
        Assert.Equal((float)Math.Pow(0.5, 1.5) * 0.85f, FlashVoicePool.Volume(50), 4);
    }

    [Fact]
    public void Voice_is_silenced_by_master_mute_avatar_mute_or_voice_line_mute_only()
    {
        AppSettings On() => new() { FlashAudioEnabled = true, MasterVolume = 60, AvatarMuted = false, CompanionVoiceLinesMuted = false };
        Assert.True(FlashVoicePool.ShouldPlay(On()));
        var off = On(); off.FlashAudioEnabled = false; Assert.False(FlashVoicePool.ShouldPlay(off));
        var m = On(); m.MasterVolume = 0; Assert.False(FlashVoicePool.ShouldPlay(m));
        var a = On(); a.AvatarMuted = true; Assert.False(FlashVoicePool.ShouldPlay(a));
        var v = On(); v.CompanionVoiceLinesMuted = true; Assert.False(FlashVoicePool.ShouldPlay(v));
        var hidden = On(); hidden.AvatarEnabled = false; Assert.True(FlashVoicePool.ShouldPlay(hidden));   // hiding the tube is not a mute
        Assert.False(FlashVoicePool.ShouldPlay(null));
    }

    [Fact]
    public void A_playing_clip_sets_the_lifetime_even_over_a_custom_duration()
    {
        var s = new AppSettings { FlashDuration = 5 };
        Assert.Equal(TimeSpan.FromMilliseconds(6000), FlashOverlay.BurstLifetime(null, s, null));
        Assert.Equal(TimeSpan.FromMilliseconds(3000), FlashOverlay.BurstLifetime(2000, s, null));
        Assert.Equal(TimeSpan.FromMilliseconds(3500), FlashOverlay.BurstLifetime(2000, s, ("x.mp3", 2.5)));
        Assert.Equal(4000, FlashVoicePool.UnduckDelayMs(2.5));
        Assert.Equal("good pet", FlashVoicePool.Caption("/a/b/good pet.mp3"));
    }

    [Fact]
    public void Voice_lines_deal_a_full_shuffled_cycle_before_repeating_and_reset_rebuilds()
    {
        var old = FlashVoicePool.Build;
        try
        {
            var pool = new List<string> { "a.mp3", "b.mp3", "c.mp3" };
            var builds = 0;
            FlashVoicePool.Build = _ => { builds++; return new List<string>(pool); };
            FlashVoicePool.Reset();
            var cycle = Enumerable.Range(0, 3).Select(_ => FlashVoicePool.Next(null)).ToList();
            Assert.Equal(pool.OrderBy(x => x), cycle.OrderBy(x => x));
            Assert.Equal(1, builds);
            FlashVoicePool.Reset();
            FlashVoicePool.Next(null);
            Assert.Equal(2, builds);
            FlashVoicePool.Build = _ => new List<string>();
            FlashVoicePool.Reset();
            Assert.Null(FlashVoicePool.Next(null));   // recorded clips only: an empty pool stays silent
        }
        finally { FlashVoicePool.Build = old; FlashVoicePool.Reset(); }
    }

    [Fact]
    public void Clip_decodes_at_most_480_on_the_long_side_and_clamps_the_rate()
    {
        Assert.Equal((480, 270), FlashClipPlayer.DecodeSize(1920, 1080));
        Assert.Equal((300, 200), FlashClipPlayer.DecodeSize(300, 200));
        Assert.Equal(.25f, FlashClipPlayer.Rate(0.1));
        Assert.Equal(4f, FlashClipPlayer.Rate(9));
        Assert.Equal(1.5f, FlashClipPlayer.Rate(1.5));
        Assert.Null(FlashClipPlayer.Start(null, "x.mp4", 100, 100, _ => { }));   // no libvlc: poster stays
    }

    [Fact]
    public void Remote_clear_drops_the_warm_clips()
    {
        RemoteFlashSource.ResetForTests();
        var oldSettings = RemoteFlashSource.Settings;
        try
        {
            RemoteFlashSource.Settings = () => new AppSettings { MediaSource = "online", RemoteMediaConsented = true };
            RemoteFlashSource.AddReady(new RemoteFlashItem("https://x/1.mp4", "p1.jpg", "c1.mp4"));
            Assert.Equal(1, RemoteFlashSource.ReadyCount);
            RemoteFlashSource.ClearReady();
            Assert.Equal(0, RemoteFlashSource.ReadyCount);
        }
        finally { RemoteFlashSource.Settings = oldSettings; RemoteFlashSource.ResetForTests(); }
    }
}
