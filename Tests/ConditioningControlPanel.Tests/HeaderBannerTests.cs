using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The support banner, 0813: the PlatinumPuppets thanks beat was retired and the banner was
/// nested into the header row so the window gets its own full-width row back.
///
/// <para>Source-text assertions for the same reason <see cref="YouLibraryDoorTests"/> uses them:
/// the surface is <c>MainWindow.xaml</c>, and MainWindow cannot be instantiated in a unit test
/// without the whole service graph. Every failure guarded here still ships from a clean compile:
/// a re-added tertiary TextBlock rotates a beat the owner asked to retire; a banner host that
/// drifts out of the header (or grows past the 28px capsule already in that row) silently gives
/// the vertical row back; and a rotation modulus that outruns the array is an
/// IndexOutOfRangeException on a 4-second timer.</para>
/// </summary>
public class HeaderBannerTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string MainWindowXaml() => ReadSource("MainWindow", "MainWindow.xaml");

    // =====================================================================================
    //  1. the retired beat
    // =====================================================================================

    [Fact]
    public void TheBannerNoLongerCarriesTheThanksBeat()
    {
        var xaml = MainWindowXaml();

        Assert.DoesNotContain("TxtBannerTertiary", xaml);
        Assert.DoesNotContain("PlatinumPuppets", xaml);
        Assert.DoesNotContain("label_a_special_thanks_to_platinum_puppets_for_prov", xaml);

        // Every beat is exactly one element. A second copy is how a rotation ends up fading a
        // TextBlock nobody can see. (TxtBannerWeb is the v6.8.0 One Account beat - conditional
        // in the ROTATION, always singular in the markup.)
        Assert.Single(Regex.Matches(xaml, "x:Name=\"TxtBannerPrimary\""));
        Assert.Single(Regex.Matches(xaml, "x:Name=\"TxtBannerSecondary\""));
        Assert.Single(Regex.Matches(xaml, "x:Name=\"TxtBannerWeb\""));
    }

    [Fact]
    public void NoCodeBehindStillPaintsTheRetiredBeat()
    {
        // Three partials used to reach for TxtBannerTertiary by name (mod accent, lockdown
        // crimson, the rotation itself). The loc key is deliberately left in the nine language
        // files - it is inert once nothing binds it - so only the code is asserted here.
        var stragglers = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "ConditioningControlPanel"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("TxtBannerTertiary", StringComparison.Ordinal))
            .ToArray();

        Assert.True(stragglers.Length == 0,
            "TxtBannerTertiary is gone from the XAML but still referenced in: " + string.Join(", ", stragglers));
    }

    [Fact]
    public void TheRotationFollowsTheBeatsArray()
    {
        // v6.8.0: the rotation reads _bannerBeats, built once at init - support + welcome-back
        // always, plus the One Account beat while SeenFeatureIntros lacks "banner-web". The tick
        // must never rebuild the array itself (the conditional lives in BuildBannerBeats and
        // RetireWebBannerBeat is the only writer after init).
        var marquee = ReadSource("MainWindow", "MainWindow.Marquee.cs");
        var tick = Regex.Match(marquee, @"void BannerRotationTimer_Tick\(.*?\n        \}", RegexOptions.Singleline);
        Assert.True(tick.Success, "BannerRotationTimer_Tick has moved or changed shape");

        Assert.Contains("var banners = _bannerBeats;", tick.Value);
        Assert.DoesNotContain("new[]", tick.Value);

        // The modulus follows the array rather than a hard-coded count: a literal % 3 over a
        // two-element array throws on the first tick, four seconds after launch. And the tick
        // guards both a degenerate array and a stale index (RetireWebBannerBeat shrinks the
        // array mid-session).
        Assert.Contains("% banners.Length", tick.Value);
        Assert.DoesNotContain("% 3", tick.Value);
        Assert.Contains("banners.Length < 2", tick.Value);
        Assert.Contains("_bannerCurrentIndex >= banners.Length", tick.Value);

        // The builder carries the conditional: spent = two beats, unspent = three.
        var builder = Regex.Match(marquee, @"TextBlock\[\] BuildBannerBeats\(\).*?\n        \}", RegexOptions.Singleline);
        Assert.True(builder.Success, "BuildBannerBeats has moved or changed shape");
        Assert.Contains("WebBannerSeenKey", builder.Value);
        Assert.Contains("new[] { TxtBannerPrimary, TxtBannerSecondary }", builder.Value);
        Assert.Contains("new[] { TxtBannerPrimary, TxtBannerSecondary, TxtBannerWeb }", builder.Value);
    }

    [Fact]
    public void TheOneAccountBeatRetiresFromEverySurfaceThatCounts()
    {
        // "The nudge worked" has three shapes: the beat's own hyperlink, the Web App door, and
        // the one-account card's CTA. All three must spend the same key through
        // RetireWebBannerBeat, or the beat keeps nagging people who already went.
        var marquee = ReadSource("MainWindow", "MainWindow.Marquee.cs");
        Assert.Contains("internal const string WebBannerSeenKey = \"banner-web\";", marquee);
        Assert.Contains("internal void RetireWebBannerBeat()", marquee);

        var link = Regex.Match(marquee, @"void BannerWebLink_Click\(.*?\n        \}", RegexOptions.Singleline);
        Assert.True(link.Success, "BannerWebLink_Click has moved or changed shape");
        // BrowserLauncher, never a bare Process.Start: the no-default-browser machines are
        // exactly who this nudge is for.
        Assert.Contains("BrowserLauncher", link.Value);
        Assert.Contains("RetireWebBannerBeat()", link.Value);

        var tabNav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");
        var door = Regex.Match(tabNav, @"void DoorWebApp_Click\(.*?\n        \}", RegexOptions.Singleline);
        Assert.True(door.Success, "DoorWebApp_Click has moved or changed shape");
        Assert.Contains("BrowserLauncher", door.Value);
        Assert.Contains("RetireWebBannerBeat()", door.Value);

        var intros = ReadSource("Windows", "FeatureIntroPopup.xaml.cs");
        var card = Regex.Match(intros, "\\[\"one-account\"\\].*?\\n            \\},", RegexOptions.Singleline);
        Assert.True(card.Success, "the one-account intro card is gone from FeatureIntros.All");
        Assert.Contains("BrowserLauncher", card.Value);
        Assert.Contains("RetireWebBannerBeat()", card.Value);
    }

    // =====================================================================================
    //  2. the new slot - header row, between the version text and the right-hand chrome
    // =====================================================================================

    [Fact]
    public void TheBannerSitsBetweenTheVersionTextAndTheLanguagePill()
    {
        var xaml = MainWindowXaml();

        var version = xaml.IndexOf("x:Name=\"TxtHeaderVersion\"", StringComparison.Ordinal);
        var host = xaml.IndexOf("x:Name=\"HeaderBannerHost\"", StringComparison.Ordinal);
        var pill = xaml.IndexOf("x:Name=\"CmbLanguagePill\"", StringComparison.Ordinal);

        Assert.True(version >= 0, "TxtHeaderVersion is gone from the header");
        Assert.True(host >= 0, "HeaderBannerHost is gone - the banner has no home in the header");
        Assert.True(pill >= 0, "CmbLanguagePill is gone from the header");

        Assert.True(version < host && host < pill,
            "the banner host left the header slot between the version text and the language pill");

        // Both beats live inside that host, not loose in the header.
        var block = Regex.Match(xaml, "<Border Grid.Column=\"5\" x:Name=\"HeaderBannerHost\".*?</Border>\\s*</Grid>",
                                RegexOptions.Singleline);
        Assert.True(block.Success, "HeaderBannerHost is no longer the header cluster's column-5 host");
        Assert.Contains("x:Name=\"TxtBannerPrimary\"", block.Value);
        Assert.Contains("x:Name=\"TxtBannerSecondary\"", block.Value);
        Assert.Contains("x:Name=\"TxtBannerWeb\"", block.Value);

        // The sheen moved with them - MainWindow.ChromeFx.SweepBannerSheen drives all three names.
        foreach (var name in new[] { "BannerSheenHost", "BannerSheen", "BannerSheenSlide" })
            Assert.Contains("x:Name=\"" + name + "\"", block.Value);
    }

    [Fact]
    public void TheBannerCannotGrowTheHeaderRowOrShoveTheChromeOut()
    {
        var xaml = MainWindowXaml();
        var block = Regex.Match(xaml, "<Border Grid.Column=\"5\" x:Name=\"HeaderBannerHost\".*?>",
                                RegexOptions.Singleline);
        Assert.True(block.Success, "HeaderBannerHost is gone");

        // 28 is the tallest thing already in this row (BtnManageMods' capsule). A banner taller
        // than that grows the header and gives back the row this change reclaimed.
        var height = Regex.Match(block.Value, "Height=\"(\\d+)\"");
        Assert.True(height.Success, "HeaderBannerHost lost its explicit Height - it now sizes to its text");
        Assert.True(int.Parse(height.Groups[1].Value) <= 28,
            "HeaderBannerHost is taller than the 28px capsule already in the header row");

        // Capped and centred in a star column, so it takes leftover width and nothing else.
        Assert.Contains("MaxWidth=", block.Value);
        Assert.Contains("HorizontalAlignment=\"Center\"", block.Value);

        // Trim, never wrap: a wrapping beat is the other way this row grows. Three since
        // v6.8.0 - the One Account beat trims like its siblings.
        var body = Regex.Match(xaml, "<Border Grid.Column=\"5\" x:Name=\"HeaderBannerHost\".*?</Border>\\s*</Grid>",
                               RegexOptions.Singleline).Value;
        Assert.Equal(3, Regex.Matches(body, "TextTrimming=\"CharacterEllipsis\"").Count);
        Assert.DoesNotContain("TextWrapping", body);
    }

    [Fact]
    public void TheOldFullWidthBannerRowIsGoneAndItsHeightIsReclaimed()
    {
        var xaml = MainWindowXaml();

        // Nothing occupies canvas row 2 any more...
        Assert.DoesNotContain("Grid.Row=\"2\" Grid.Column=\"1\"", xaml);

        // ...and the row definition is pinned to 0, so the space is reclaimed rather than left
        // as an Auto row waiting for the next thing to drop into it. The row is KEPT (not
        // deleted) so rows 3/4/5 hold the indices the rest of the window is authored against.
        var defs = Regex.Match(xaml, "<Grid.RowDefinitions>.*?</Grid.RowDefinitions>", RegexOptions.Singleline);
        Assert.True(defs.Success, "the canvas row definitions have moved");

        var heights = Regex.Matches(defs.Value, "<RowDefinition Height=\"([^\"]+)\"/>")
                           .Select(m => m.Groups[1].Value)
                           .ToArray();
        Assert.Equal(new[] { "36", "Auto", "0", "Auto", "*", "Auto" }, heights);
    }
}
