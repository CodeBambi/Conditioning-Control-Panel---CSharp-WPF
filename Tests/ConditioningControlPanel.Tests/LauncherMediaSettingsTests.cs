using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The launcher's "Game media" dialog promises that one choice reaches every room. The
/// promise is kept by <see cref="LauncherMediaSettings.Apply"/>: it writes the app-wide source
/// the games read, and it clears the Back Room's overrides so the room follows again. The
/// dialog is a thin shell over it, so these rows are the whole contract.
/// </summary>
public class LauncherMediaSettingsTests
{
    private static AppSettings Fresh()
    {
        var s = new AppSettings
        {
            MediaSource = "local",
            RemoteMediaRatio = 30,
            FypOnlineNiches = new List<string> { "hypno" },
            BackRoomMediaSource = "bundled",
            BackRoomMediaSubs = new List<string> { "bimbo", "sissyhypno" },
            BackRoomMediaSubsOff = new List<string> { "hentai" }
        };
        return s;
    }

    [Fact]
    public void Apply_writes_the_app_wide_fields()
    {
        var s = Fresh();

        LauncherMediaSettings.Apply(s, "mixed", 60, new[] { "bimbo", "sissy" });

        Assert.Equal("mixed", s.MediaSource);
        Assert.Equal(60, s.RemoteMediaRatio);
        Assert.Equal(new[] { "bimbo", "sissy" }, s.FypOnlineNiches);
    }

    [Fact]
    public void Apply_points_the_back_room_back_at_the_app()
    {
        var s = Fresh();

        LauncherMediaSettings.Apply(s, "online", 30, new[] { "hypno" });

        Assert.Equal("auto", s.BackRoomMediaSource);
        Assert.Empty(s.BackRoomMediaSubs);
        Assert.Empty(s.BackRoomMediaSubsOff);
    }

    [Theory]
    [InlineData("reddit")]
    [InlineData("")]
    [InlineData("Local")]
    public void Apply_rejects_an_unknown_source(string source)
    {
        var s = Fresh();

        Assert.Throws<ArgumentException>(() => LauncherMediaSettings.Apply(s, source, 30, new[] { "hypno" }));

        // Nothing moved: the room override is still the user's own.
        Assert.Equal("local", s.MediaSource);
        Assert.Equal("bundled", s.BackRoomMediaSource);
    }

    [Fact]
    public void Apply_keeps_at_least_one_niche()
    {
        var s = Fresh();

        var kept = LauncherMediaSettings.Apply(s, "online", 30, Array.Empty<string>());

        var first = FypOnlineCoordinator.Catalog[0].Id;
        Assert.Equal(new[] { first }, kept);
        Assert.Equal(new[] { first }, s.FypOnlineNiches);
    }

    [Fact]
    public void Apply_drops_unknown_niches_and_keeps_catalogue_order()
    {
        var s = Fresh();

        var kept = LauncherMediaSettings.Apply(s, "mixed", 30, new[] { "sissy", "nope", "HYPNO" });

        Assert.Equal(new[] { "hypno", "sissy" }, kept);
    }

    [Fact]
    public void Apply_clamps_the_share_like_the_property_does()
    {
        var s = Fresh();

        LauncherMediaSettings.Apply(s, "mixed", 200, new[] { "hypno" });
        Assert.Equal(95, s.RemoteMediaRatio);

        LauncherMediaSettings.Apply(s, "mixed", -4, new[] { "hypno" });
        Assert.Equal(5, s.RemoteMediaRatio);
    }
}
