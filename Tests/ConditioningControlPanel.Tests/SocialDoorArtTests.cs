using System;
using System.IO;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Polish wave 11, lane ICONS: the Social door gets its own medallion in every rail art set.
///
/// <para>CCP Default (also Bambi Sleep and Sissy Hypno, which wear its doors) ships
/// <c>Resources/nav/door_social.png</c>. The three mods with their own door art keep that art in
/// their .ccpmod packs, which cannot be rebuilt in this wave, so their Social doors ride as
/// embedded twins under <c>Resources/nav/mods/&lt;mod-id&gt;/</c>. The resolver tries a pack file
/// first, then the embedded twin, then our default: a pack rebuilt with its own door_social.png
/// wins without a code change.</para>
/// </summary>
public class SocialDoorArtTests
{
    private const string SocialDoor = "nav/door_social.png";

    private static readonly string[] ModsWithOwnDoors =
    {
        BuiltInMods.DronificationId,
        BuiltInMods.InfectionControlId,
        BuiltInMods.LockedId,
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static BitmapSource LoadEmbedded(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri($"pack://application:,,,/Resources/{path}", UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    public void TheDefaultSocialDoorIsEmbeddedAtTheSetsSize()
    {
        // Same 64x64 as door_home.png and the rest of the CCP Default set.
        var social = LoadEmbedded(SocialDoor);
        var home = LoadEmbedded("nav/door_home.png");
        Assert.Equal(home.PixelWidth, social.PixelWidth);
        Assert.Equal(home.PixelHeight, social.PixelHeight);

        Assert.NotNull(ModResourceResolver.ResolveImageDecoded(SocialDoor, 128));
    }

    [Theory]
    [InlineData(BuiltInMods.DronificationId)]
    [InlineData(BuiltInMods.InfectionControlId)]
    [InlineData(BuiltInMods.LockedId)]
    public void EveryModWithItsOwnDoorsHasAnEmbeddedSocialTwin(string modId)
    {
        var twin = ModResourceResolver.EmbeddedModTwin(SocialDoor, modId);
        Assert.Equal($"nav/mods/{modId}/door_social.png", twin);
        Assert.Equal(twin, ModResourceResolver.EmbeddedPathFor(SocialDoor, modId));

        var art = LoadEmbedded(twin!);
        Assert.Equal(art.PixelWidth, art.PixelHeight);
        // Decoded at 128 for a 40 px icon: the twin carries 2x headroom and no more.
        Assert.Equal(256, art.PixelWidth);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(BuiltInMods.CCPDefaultId)]
    [InlineData(BuiltInMods.BambiSleepId)]
    [InlineData(BuiltInMods.SissyHypnoId)]
    [InlineData("some-third-party-mod")]
    public void AModWithoutATwinFallsBackToOurDefault(string? modId)
    {
        Assert.Equal(SocialDoor, ModResourceResolver.EmbeddedPathFor(SocialDoor, modId));
    }

    [Fact]
    public void ATwinOnlyReplacesTheFileItWasDrawnFor()
    {
        // Drone has a social twin and no other: its home door still comes from its pack, and
        // when there is no pack, from ours. A twin must never shadow a different door.
        Assert.Equal("nav/door_home.png",
            ModResourceResolver.EmbeddedPathFor("nav/door_home.png", BuiltInMods.DronificationId));
    }

    [Theory]
    [InlineData("features/vault.png", "drone-mode")]   // not nav art
    [InlineData("nav/mods/drone-mode/door_social.png", "drone-mode")] // already a twin
    [InlineData(SocialDoor, "")]
    [InlineData(SocialDoor, "../evil")]
    [InlineData(SocialDoor, "a/b")]
    [InlineData(SocialDoor, "a\\b")]
    [InlineData(SocialDoor, "C:\\x")]
    public void TheTwinPathIsOnlyEverOneSafeSegmentUnderNav(string path, string modId)
    {
        Assert.Null(ModResourceResolver.EmbeddedModTwin(path, modId));
    }

    [Fact]
    public void TheRailChipAndTheLobbyTabWearTheSocialDoor()
    {
        var door = FavoritesRailArt.For("door.social");
        var tab = FavoritesRailArt.For("tab.availablesubjects");
        Assert.NotNull(door);
        Assert.NotNull(tab);
        Assert.Equal(SocialDoor, door!.ResourcePath);
        Assert.Equal(SocialDoor, tab!.ResourcePath);
        Assert.Equal(RailArtFit.Plate, door.Fit);
        Assert.Equal(RailArtFit.Plate, tab.Fit);
    }

    [Fact]
    public void TheTwinsAreOnDiskAndCoveredByTheCsprojGlob()
    {
        var root = RepoRoot();
        var app = Path.Combine(root, "ConditioningControlPanel");
        Assert.True(File.Exists(Path.Combine(app, "Resources", "nav", "door_social.png")));
        foreach (var id in ModsWithOwnDoors)
            Assert.True(File.Exists(Path.Combine(app, "Resources", "nav", "mods", id, "door_social.png")), id);

        var csproj = File.ReadAllText(Path.Combine(app, "ConditioningControlPanel.csproj"));
        Assert.Contains("<Resource Include=\"Resources\\nav\\mods\\*\\*.png\" />", csproj, StringComparison.Ordinal);
    }
}
