using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using System.Xml;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The mod-awareness sweep, lane D: the eight art plates on the four premium feature tabs
/// (Haptics, She's Listening, Deeper, Bambi Takeover).
///
/// <para><b>The bug this suite exists to prevent.</b> Each of those plates painted a hardcoded
/// <c>pack://application:,,,/Resources/features/*.png</c> straight from XAML. That compiles,
/// renders and looks correct on a default install - and it is completely blind to the active mod,
/// so a .ccpmod shipping <c>resources/features/vibe.png</c> reskinned the rest of the app while
/// these four pages kept showing the base art. Nothing throws when that happens, which is exactly
/// why it survived several passes.</para>
///
/// <para><b>Why source-scrape.</b> The contract that breaks is a WIRING one: an <c>x:Name</c> the
/// code-behind no longer sets, a repaint hung off something other than <c>ModChanged</c>, or a
/// resolver call that quietly went back to the full-resolution <c>ResolveImage</c> (a mod is free
/// to ship a 2048px plate, and the side plates are 800px wide at most). Realizing these tabs in a
/// test host needs a live MainWindow and an initialized App; the scrape catches every one of those
/// regressions without it. Mirrors ModAwareDecodedArtTests' nav-rail half.</para>
///
/// <para>The one structural assertion that is NOT a scrape is the Haptics hero's parentage: it is
/// checked against the parsed XAML tree, because "the hero is inside HapticsContentGrid" is an
/// entitlement control, not a cosmetic one. See that test.</para>
/// </summary>
public class ModAwarePremiumPlateTests
{
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// One row per tab: the view, the art path its plates resolve, and the two brush names the
    /// code-behind writes. None of these five strings may be renamed - the paths are what the
    /// .ccpmod files on disk already target (mod contract rule 2), the names are what
    /// ApplyFeatureArt holds.
    /// </summary>
    public static readonly IEnumerable<object[]> Plates = new[]
    {
        new object[] { "HapticsTabView",       "features/vibe.png",            "HapticsHeroArtBrush",       "HapticsSideArtBrush" },
        new object[] { "SheListeningTabView",  "features/audio_whispers.png",  "SheListeningHeroArtBrush",  "SheListeningSideArtBrush" },
        new object[] { "DeeperTabView",        "features/deeper.png",          "DeeperHeroArtBrush",        "DeeperSideArtBrush" },
        new object[] { "BambiTakeoverTabView", "features/takeover.png",        "TakeoverHeroArtBrush",      "TakeoverSideArtBrush" },
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string TabPath(string file)
        => Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", file);

    private static string TabText(string file) => File.ReadAllText(TabPath(file));

    // =====================================================================================
    //  the XAML side: named brushes that still carry their authored fallback
    // =====================================================================================

    [Theory]
    [MemberData(nameof(Plates))]
    public void BothPlatesAreNamedAndKeepTheirAuthoredFallback(string view, string art, string heroBrush, string sideBrush)
    {
        var xaml = TabText(view + ".xaml");

        foreach (var name in new[] { heroBrush, sideBrush })
            Assert.True(xaml.Contains($"x:Name=\"{name}\"", StringComparison.Ordinal),
                $"{view}.xaml no longer names {name} - ApplyFeatureArt has nothing to repaint");

        // The pack:// URI stays in XAML on purpose: it is what the plate falls back to when the
        // resolver comes back null. Deleting it would trade "base art" for "empty rectangle".
        Assert.True(xaml.Contains($"pack://application:,,,/Resources/{art}", StringComparison.Ordinal),
            $"{view}.xaml dropped its authored {art} fallback - a null resolve now blanks the plate");

        // Both decode caps must survive: a hero at 800 or a side plate at 480 is a silent
        // quality/memory regression, and neither throws.
        Assert.True(xaml.Contains("DecodePixelWidth=\"480\"", StringComparison.Ordinal),
            $"{view}.xaml lost the hero's 480px decode cap");
        Assert.True(xaml.Contains("DecodePixelWidth=\"800\"", StringComparison.Ordinal),
            $"{view}.xaml lost the side plate's 800px decode cap");
    }

    [Theory]
    [MemberData(nameof(Plates))]
    public void TheArtIsOnDisk(string view, string art, string heroBrush, string sideBrush)
    {
        _ = view; _ = heroBrush; _ = sideBrush;
        var file = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources",
            art.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"{art} is missing from Resources/ - a blank plate on a default install");
    }

    // =====================================================================================
    //  the code-behind side: ApplyFeatureArt, on ModChanged, through the decoded resolver
    // =====================================================================================

    [Theory]
    [MemberData(nameof(Plates))]
    public void ApplyFeatureArtRepaintsBothPlatesThroughTheDecodedResolver(string view, string art, string heroBrush, string sideBrush)
    {
        var cs = TabText(view + ".xaml.cs");

        // The path the code-behind resolves has to be the one the XAML falls back to (Takeover's
        // BambiSleep fork is the deliberate second spelling of the same feature's art).
        Assert.True(cs.Contains($"\"{art}\"", StringComparison.Ordinal),
            $"{view}.xaml.cs resolves something other than {art} - the plate and its fallback have drifted apart");

        Assert.True(cs.Contains("private void ApplyFeatureArt()", StringComparison.Ordinal),
            $"{view}.xaml.cs has no ApplyFeatureArt - the plates stopped following the active mod");
        Assert.True(cs.Contains(heroBrush, StringComparison.Ordinal), $"{view}.xaml.cs never writes {heroBrush}");
        Assert.True(cs.Contains(sideBrush, StringComparison.Ordinal), $"{view}.xaml.cs never writes {sideBrush}");

        // The DECODED resolver, not ResolveImage: a mod may ship a 2048px plate, and a full
        // decode behind a 240px hero is megabytes per tab.
        Assert.True(cs.Contains("ResolveImageDecoded", StringComparison.Ordinal),
            $"{view}.xaml.cs resolves the plate art without a decode cap");
        Assert.Contains("480", cs, StringComparison.Ordinal);
        Assert.Contains("800", cs, StringComparison.Ordinal);

        // Degrade to the authored art, never to nothing.
        Assert.True(cs.Contains("hero != null", StringComparison.Ordinal) || cs.Contains("!= null &&", StringComparison.Ordinal),
            $"{view}.xaml.cs assigns the resolve result unguarded - a miss would blank the plate");

        // Mutate the existing brush in place. Replacing Border.Background instead would drop the
        // authored Stretch/AlignmentX/Opacity and the OpacityMask the hero fades under.
        Assert.True(cs.Contains(".ImageSource = ", StringComparison.Ordinal),
            $"{view}.xaml.cs no longer mutates the brush in place");
        Assert.True(cs.Contains("IsFrozen", StringComparison.Ordinal),
            $"{view}.xaml.cs writes the brush without checking IsFrozen - a frozen brush throws on assign");
    }

    [Theory]
    [MemberData(nameof(Plates))]
    public void TheRepaintHangsOffModChangedAndIsDispatcherMarshalled(string view, string art, string heroBrush, string sideBrush)
    {
        _ = art; _ = heroBrush; _ = sideBrush;
        var cs = TabText(view + ".xaml.cs");

        // ModChanged is THE authoritative signal. ApplyActiveModChange is not a substitute:
        // uninstalling the ACTIVE mod never reaches it (ModService activates the fallback
        // itself), and the page would keep painting the deleted mod's art.
        Assert.True(cs.Contains("ModChanged += ", StringComparison.Ordinal),
            $"{view}.xaml.cs never subscribes to ModChanged - a mod switch leaves stale art on the plates");
        Assert.True(cs.Contains("ModChanged -= ", StringComparison.Ordinal),
            $"{view}.xaml.cs subscribes to ModChanged without ever detaching - ModService outlives the view");

        // First paint has to happen too: a tab that only repaints on ModChanged shows base art
        // until the user switches mods.
        Assert.True(cs.Contains("Loaded += ", StringComparison.Ordinal),
            $"{view}.xaml.cs never applies the art on Loaded - the first paint would be the base art");

        // ModChanged may be raised off the UI thread.
        Assert.True(cs.Contains("Dispatcher.CheckAccess()", StringComparison.Ordinal),
            $"{view}.xaml.cs touches the brushes straight from the ModChanged callback - that thread is not guaranteed to be the UI one");
    }

    // =====================================================================================
    //  the two constraints specific to a single tab
    // =====================================================================================

    /// <summary>
    /// The Haptics hero carries the master enable, and MainWindow.Patreon.cs gates entitlement by
    /// dimming + un-hit-testing HapticsContentGrid. A hero moved OUT of that grid to make the art
    /// plumbing tidier would hand free users a live master toggle sitting on top of the veil, and
    /// nothing in a compile, a render or a scrape of the code-behind would notice. Hence a real
    /// tree assertion rather than a text one.
    /// </summary>
    [Fact]
    public void TheHapticsHeroStaysInsideTheGatedGrid()
    {
        var doc = new XmlDocument();
        doc.Load(TabPath("HapticsTabView.xaml"));

        var grid = FindByName(doc.DocumentElement, "HapticsContentGrid");
        Assert.True(grid != null, "HapticsContentGrid is gone - MainWindow.Patreon.cs dereferences it to gate the tab");
        Assert.True(FindByName(grid, "HapticsHeroArtBrush") != null,
            "the Haptics hero left HapticsContentGrid - free users now get a live master enable over the premium veil");
    }

    /// <summary>
    /// Takeover is the one feature with two base files. BambiSleep's art is "bambi takeover.png",
    /// every other mod's is "takeover.png", and MainWindow.xaml.cs picks the same pair for the
    /// collapsed ImgBambiTakeoverDesc in the description card. If this view's fork drifts from that
    /// one, the page shows two different takeovers at once.
    /// </summary>
    [Fact]
    public void TheTakeoverForkMatchesTheOneMainWindowUses()
    {
        var cs = TabText("BambiTakeoverTabView.xaml.cs");

        Assert.Contains("BuiltInMods.BambiSleepId", cs, StringComparison.Ordinal);
        Assert.Contains("\"features/bambi takeover.png\"", cs, StringComparison.Ordinal);
        Assert.Contains("\"features/takeover.png\"", cs, StringComparison.Ordinal);

        var main = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs"));
        Assert.Contains("\"features/bambi takeover.png\"", main, StringComparison.Ordinal);
        Assert.Contains("BuiltInMods.BambiSleepId", main, StringComparison.Ordinal);
    }

    [Fact]
    public void BothTakeoverBaseFilesShipWithTheApp()
    {
        var resources = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources");
        Assert.True(File.Exists(Path.Combine(resources, "features", "takeover.png")));
        Assert.True(File.Exists(Path.Combine(resources, "features", "bambi takeover.png")));
    }

    // =====================================================================================
    //  the resolver actually answers for these paths, at these widths
    // =====================================================================================

    [Theory]
    [InlineData("features/vibe.png")]
    [InlineData("features/audio_whispers.png")]
    [InlineData("features/deeper.png")]
    [InlineData("features/takeover.png")]
    [InlineData("features/bambi takeover.png")]   // the space is real; a Uri that chokes on it is silent
    public void EveryPlatePathResolvesAtBothDecodeWidths(string art)
    {
        foreach (var width in new[] { 480, 800 })
        {
            var img = ModResourceResolver.ResolveImageDecoded(art, width);
            Assert.True(img != null, $"{art} would not resolve at {width}px - the plate falls back to its XAML art forever");
            Assert.Equal(width, Assert.IsAssignableFrom<BitmapSource>(img).PixelWidth);
        }
    }

    // =====================================================================================

    private static XmlElement? FindByName(XmlNode? node, string name)
    {
        if (node is not XmlElement element) return null;
        if (element.GetAttribute("Name", XamlNs) == name) return element;

        foreach (XmlNode child in element.ChildNodes)
        {
            var hit = FindByName(child, name);
            if (hit != null) return hit;
        }
        return null;
    }
}
