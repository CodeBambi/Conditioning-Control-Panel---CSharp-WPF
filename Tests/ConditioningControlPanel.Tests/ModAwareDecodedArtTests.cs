using System;
using System.IO;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The mod-awareness sweep's foundation lane: the decoded resolver, and the nav rail that is its
/// first new consumer.
///
/// <para><b>The bug this suite exists to prevent.</b> <see cref="ModResourceResolver.ResolveImage"/>
/// caches on <c>{skin}:{mod}:{path}</c>. The decoded variant takes a width as well, and the same
/// path is legitimately asked for at three different widths in one session (128 for a nav
/// medallion, 512 for a Play hero, 1024 for a wide tile). Sharing the width-blind key would have
/// been silent: every later caller would get whatever resolution the FIRST one happened to ask
/// for - a 128px thumbnail stretched across a 1024px banner, or a 1024px decode behind a 44px
/// tile, and neither throws.</para>
///
/// <para>The nav-rail half is source-level on purpose. Realizing MainWindow in a test host is not
/// something this suite can afford, and the contract that actually breaks is a wiring one: an
/// <c>x:Name</c> that ApplyDoorArt does not set, or a path that no longer exists on disk. Missing
/// nav art has crashed this app before, which is also why ApplyDoorArt keeps the authored Source
/// on a null resolve rather than blanking the rail.</para>
/// </summary>
public class ModAwareDecodedArtTests
{
    // A file every install has, small enough that the decode caps below are all downscales.
    private const string Probe = "nav/door_home.png";

    private static BitmapSource Decoded(string path, int width)
    {
        var img = ModResourceResolver.ResolveImageDecoded(path, width);
        Assert.NotNull(img);
        return Assert.IsAssignableFrom<BitmapSource>(img);
    }

    // =====================================================================================
    //  the decoded resolver
    // =====================================================================================

    [Fact]
    public void DecodedResolveHonoursTheWidthCap()
    {
        Assert.Equal(32, Decoded(Probe, 32).PixelWidth);
    }

    [Fact]
    public void TwoWidthsOfOnePathDoNotCollide()
    {
        // The whole reason the decoded cache is a second dictionary with the width in its key.
        // Ask small first: a collision would hand the second call the 32px bitmap back.
        var small = Decoded(Probe, 32);
        var large = Decoded(Probe, 48);

        Assert.Equal(32, small.PixelWidth);
        Assert.Equal(48, large.PixelWidth);
        Assert.NotSame(small, large);
    }

    [Fact]
    public void TheSameWidthTwiceIsServedFromCache()
    {
        Assert.Same(Decoded(Probe, 40), Decoded(Probe, 40));
    }

    [Fact]
    public void TheDecodedCacheDoesNotPoisonTheFullResolveCache()
    {
        // ResolveImage's answer must stay full resolution no matter what widths ran before it -
        // the two caches are separate, and this is the assertion that says so.
        var capped = Decoded(Probe, 16);
        var full = ModResourceResolver.ResolveImage(Probe);

        Assert.NotNull(full);
        var fullBitmap = Assert.IsAssignableFrom<BitmapSource>(full);
        Assert.Equal(16, capped.PixelWidth);
        Assert.True(fullBitmap.PixelWidth > 16,
            $"ResolveImage came back at {fullBitmap.PixelWidth}px - the decoded cache leaked into it");
    }

    [Fact]
    public void ANonPositiveWidthMeansFullResolution()
    {
        // Routed to ResolveImage so both callers share one entry rather than caching the same
        // full-res bitmap twice under two keys.
        Assert.Same(ModResourceResolver.ResolveImage(Probe), ModResourceResolver.ResolveImageDecoded(Probe, 0));
    }

    [Fact]
    public void ClearCacheDropsTheDecodedEntriesToo()
    {
        // A decoded entry that survived a mod switch would repaint the OLD mod's art at the new
        // mod's call site. ModService calls ClearCache immediately before raising ModChanged.
        var before = Decoded(Probe, 24);
        ModResourceResolver.ClearCache();
        var after = Decoded(Probe, 24);

        Assert.NotSame(before, after);
        Assert.Equal(24, after.PixelWidth);
    }

    [Fact]
    public void DecodedResolveRejectsTraversal()
    {
        Assert.Null(ModResourceResolver.ResolveImageDecoded("../secrets.png", 64));
        Assert.Null(ModResourceResolver.ResolveImageDecoded(@"..\secrets.png", 64));
        Assert.Null(ModResourceResolver.ResolveImageDecoded("", 64));
    }

    [Theory]
    [InlineData(Probe)]
    [InlineData("Resources/" + Probe)]
    [InlineData("pack://application:,,,/Resources/" + Probe)]
    public void ResolvePackPathTakesEveryShapeOfThatPath(string spelling)
    {
        var img = ModResourceResolver.ResolvePackPath(spelling, 32);
        Assert.NotNull(img);
        Assert.Equal(32, Assert.IsAssignableFrom<BitmapSource>(img).PixelWidth);
    }

    [Fact]
    public void ResolvePackPathDefaultsToFullResolution()
    {
        Assert.Same(ModResourceResolver.ResolveImage(Probe), ModResourceResolver.ResolvePackPath("Resources/" + Probe));
    }

    [Fact]
    public void ResolvePackPathSurvivesNonsense()
    {
        Assert.Null(ModResourceResolver.ResolvePackPath(""));
        Assert.Null(ModResourceResolver.ResolvePackPath("Resources/"));
        Assert.Null(ModResourceResolver.ResolvePackPath("Resources/../secrets.png", 64));
    }

    // =====================================================================================
    //  the nav rail's medallions (eight since v6.8.0: seven tab doors + the Web App launcher)
    // =====================================================================================

    /// <summary>
    /// x:Name in MainWindow.xaml → the mod-contract art path. Neither half is ever renamed: the
    /// name is what ApplyDoorArt holds, the path is what every .ccpmod on disk already targets.
    /// </summary>
    private static readonly (string Name, string Path)[] NavDoorArt =
    {
        ("ImgDoorHome",      "nav/door_home.png"),
        ("ImgDoorStudio",    "nav/door_studio.png"),
        ("ImgDoorCompanion", "nav/door_companion.png"),
        ("ImgDoorPlay",      "nav/door_play.png"),
        ("ImgDoorYou",       "nav/door_you.png"),
        ("ImgDoorLibrary",   "nav/door_library.png"),
        // v6.8.0: the Web App launcher door - a full medallion, mod-reskinnable like the rest.
        ("ImgDoorWebApp",    "nav/door_webapp.png"),
        ("ImgDoorSettings",  "nav/door_settings.png"),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine("ConditioningControlPanel", Path.Combine(parts))));

    [Fact]
    public void EveryDoorMedallionIsNamedAndItsArtIsOnDisk()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        var resources = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources");

        foreach (var (name, path) in NavDoorArt)
        {
            Assert.True(xaml.Contains($"x:Name=\"{name}\"", StringComparison.Ordinal),
                $"{name} is gone from MainWindow.xaml - ApplyDoorArt has nothing to point at that door");
            Assert.True(xaml.Contains($"Resources/{path}", StringComparison.Ordinal),
                $"{path} is no longer the authored Source - the fallback art and the mod contract path have drifted apart");
            Assert.True(File.Exists(Path.Combine(resources, path.Replace('/', Path.DirectorySeparatorChar))),
                $"{path} is missing from Resources/ - a blank rail on a default install");
        }
    }

    [Fact]
    public void ApplyDoorArtCoversEveryDoorAndNeverBlanksOne()
    {
        var navRail = AppFile("MainWindow", "MainWindow.NavRail.cs");

        var start = navRail.IndexOf("private void ApplyDoorArt()", StringComparison.Ordinal);
        Assert.True(start >= 0, "ApplyDoorArt is gone - the rail stopped following the active mod");
        var end = navRail.IndexOf("private void CacheNavRailParts", start, StringComparison.Ordinal);
        Assert.True(end > start, "ApplyDoorArt's neighbourhood moved - fix the scrape, then re-read the method");
        var body = navRail.Substring(start, end - start);

        foreach (var (name, path) in NavDoorArt)
        {
            Assert.True(body.Contains(name, StringComparison.Ordinal), $"ApplyDoorArt never touches {name}");
            Assert.True(body.Contains($"\"{path}\"", StringComparison.Ordinal), $"ApplyDoorArt never resolves {path}");
        }

        // The rail must degrade to its authored art, not to nothing.
        Assert.Contains("if (art != null)", body, StringComparison.Ordinal);
        // And it must go through the DECODED resolver: a mod is free to ship a 2048px door.
        Assert.Contains("ResolveImageDecoded", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ModChangedRepaintsTheDoorsAndThePalette()
    {
        // ModChanged is THE authoritative repaint signal. RefreshThemeAwareElements used to hang
        // off ApplyActiveModChange alone, which uninstalling the active mod never reaches -
        // ModService activates the fallback itself, and the whole app kept the dead mod's accent.
        var main = AppFile("MainWindow", "MainWindow.xaml.cs");

        Assert.Contains("ModChanged += (_, _) => Dispatcher.Invoke(ApplyDoorArt)", main, StringComparison.Ordinal);
        Assert.Contains("ModChanged += (_, _) => Dispatcher.Invoke(RefreshThemeAwareElements)", main, StringComparison.Ordinal);
    }
}
