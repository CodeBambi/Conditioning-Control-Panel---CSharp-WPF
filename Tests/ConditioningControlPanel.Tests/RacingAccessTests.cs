using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.Race;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class RacingAccessTests
{
    private const string Demo = "a15c22e0-d347-4d92-9f78-0fb37099e549";
    private const string First = "97a86947-7a16-499d-b0e2-804d743ef45f";
    private static readonly Dictionary<string, int> Catalog = new(StringComparer.OrdinalIgnoreCase)
        { [Demo] = 0, [First] = 1 };
    private static Func<string, bool> Owns(params string[] grants)
    {
        var owned = new HashSet<string>(grants, StringComparer.Ordinal);
        return owned.Contains;
    }

    [Theory]
    [InlineData("rt.original.00", true)]
    [InlineData("rt.original.01", true)]
    [InlineData("rt.original.10", true)]
    [InlineData("rt.original.11", false)]
    [InlineData("rt.original.0", false)]
    [InlineData("fx.jackpot_remix", false)]
    [InlineData("rt_demo", false)]
    [InlineData("", false)]
    public void OnlyKnownTrackGrantsOpenTheGame(string grant, bool expected)
        => Assert.Equal(expected, RacingAccess.AllowsLaunch(Owns(grant)));

    [Fact]
    public void BundleOneWorksWithoutDemoButDoesNotGrantDemo()
    {
        var owns = Owns("rt.original.01", "rt.original.02", "rt.original.03");
        Assert.True(RacingAccess.AllowsLaunch(owns));
        Assert.True(RacingAccess.AllowsCloud("https://bambicloud.com/file/" + First, Catalog, owns));
        Assert.False(RacingAccess.AllowsCloud("https://cdn.bambicloud.com/" + Demo + ".mp3", Catalog, owns));
    }

    [Fact]
    public void DirectCloudEntryChecksPurchaseAndPerTrackGrant()
    {
        var url = "https://cdn.bambicloud.com/" + First + ".mp3?token=example";
        Assert.False(RacingAccess.AllowsCloud(url, Catalog, Owns()));
        Assert.False(RacingAccess.AllowsCloud(url, Catalog, Owns("rt.original.00")));
        Assert.True(RacingAccess.AllowsCloud(url, Catalog, Owns("rt.original.01")));
    }

    [Fact]
    public void OwnedGameAllowsPersonalAudioButMissingCatalogFailsClosed()
    {
        const string personal = "https://cdn.bambicloud.com/another-personal-track.mp3";
        Assert.True(RacingAccess.AllowsCloud(personal, Catalog, Owns("rt.original.00")));
        Assert.False(RacingAccess.AllowsCloud(personal, Catalog, Owns()));
        Assert.False(RacingAccess.AllowsCloud(personal, null, Owns("rt.original.00")));
    }

    /// <summary>2026-09-18 owner decision: Racing Thoughts is a Back Room unlock. The door is
    /// armed in exactly one place; this pins it so a merge cannot quietly reopen it.</summary>
    [Fact]
    public void PurchaseDoorIsArmed()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Race", "RacingAccess.cs"));
        Assert.Contains("private const bool PurchaseDoorArmed = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoTrackNoLaunchAnyTrackLaunches()
    {
        Assert.False(RacingAccess.AllowsLaunch(Owns()));
        Assert.False(RacingAccess.AllowsLaunch(Owns("fx.jackpot_remix")));
        Assert.True(RacingAccess.AllowsLaunch(Owns("rt.original.00")));
        Assert.True(RacingAccess.AllowsLaunch(Owns("rt.original.07")));
    }

    [Fact]
    public void CatalogDoesNotCoerceMissingOrOutOfRangeNumbersIntoDemo()
    {
        var rows = RacingAccess.ParseCatalog(JToken.Parse("{sets:[{levels:[{id:'demo',trackNum:0},{id:'ten',trackNum:10},{id:'missing'},{id:'wrong',trackNum:11},{id:'text',trackNum:'0'}]}]}"));
        Assert.Equal(2, rows.Count);
        Assert.Equal(0, rows["demo"]);
        Assert.Equal(10, rows["ten"]);
    }
    [Fact]
    public void DeniedSelectionCanRetryAfterPurchaseWithoutAnotherSourceAnnouncement()
    {
        var pending = new CloudTrackRetry();
        var source = JObject.FromObject(new { src = "https://cdn.bambicloud.com/" + First + ".mp3", title = "Track one", durationSec = 80 });
        pending.Remember(source);
        Assert.Null(pending.TakeIfAllowed(url => RacingAccess.AllowsCloud(url, Catalog, Owns("rt.original.00"))));
        var retried = pending.TakeIfAllowed(url => RacingAccess.AllowsCloud(url, Catalog, Owns("rt.original.01")));
        Assert.NotNull(retried);
        Assert.Equal("Track one", (string?)retried["title"]);
        Assert.Null(pending.TakeIfAllowed(_ => true));
        pending.Remember(source); pending.Clear();
        Assert.Null(pending.TakeIfAllowed(_ => true));
    }
}
