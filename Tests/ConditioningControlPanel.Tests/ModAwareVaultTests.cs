using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The mod-awareness sweep, Velvet Vault lane. Everything here guards one shape of bug: a surface
/// that reads the active mod ONCE - at construction, at first paint, at first Start - on a tab that
/// is built once and then only ever repainted.
///
/// <para><b>The reported bug.</b> Launch under Dronification (accent #00FF41), switch to BambiSleep,
/// and every vault card kept glimmering green until the app was restarted.
/// <see cref="CardSheenAdorner"/> documented its tint as "read at Start", which is what the
/// mod-switch repaint relies on, but the constructor actually baked the colour into the gradient
/// stops - and the adorners are constructed once per card for the life of the window.</para>
///
/// <para>The colour half of this suite runs on the shared WPF harness because
/// <see cref="FxTheme"/> reads <c>Application.Current.Resources</c>; the wiring half is
/// source-level, for the same reason the nav-rail half of ModAwareDecodedArtTests is - realizing
/// MainWindow in a test host is not affordable, and the contracts that actually break (a refresh
/// that never re-resolves art, a build action that leaves art unreachable by pack://) are wiring
/// contracts.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ModAwareVaultTests
{
    private static readonly Color DroneGreen = Color.FromRgb(0x00, 0xFF, 0x41);
    private static readonly Color BambiPink = Color.FromRgb(0xFF, 0x69, 0xB4);

    // =====================================================================================
    //  the sheen: tint must be re-read on every Start
    // =====================================================================================

    /// <summary>The adorner's three stops, which are private and stay that way.</summary>
    private static GradientStop[] Stops(CardSheenAdorner sheen)
    {
        var field = typeof(CardSheenAdorner).GetField("_stops",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field != null, "CardSheenAdorner._stops is gone - re-read the class before fixing this test");
        return (GradientStop[])field!.GetValue(sheen)!;
    }

    /// <summary>Sets the FX glow the way FxTheme.ApplyForActiveMod does, then restores it.</summary>
    private static void WithGlow(Color color, Action body)
    {
        var res = Application.Current!.Resources;
        var had = res.Contains("FxGlowColor");
        var previous = had ? res["FxGlowColor"] : null;
        res["FxGlowColor"] = color;
        try { body(); }
        finally
        {
            if (had) res["FxGlowColor"] = previous;
            else res.Remove("FxGlowColor");
        }
    }

    [Fact]
    public void TheSheenTakesItsTintFromTheModAtStartNotAtConstruction()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var res = Application.Current!.Resources;
            var had = res.Contains("FxGlowColor");
            var previous = had ? res["FxGlowColor"] : null;

            try
            {
                // Built while Dronification is active...
                res["FxGlowColor"] = DroneGreen;
                var sheen = new CardSheenAdorner(new Border { Width = 336, Height = 200 }, 12);
                Assert.Equal(DroneGreen.G, Stops(sheen)[1].Color.G);

                // ...and the user switches to BambiSleep. The adorner is NOT rebuilt; the tab's
                // repaint restarts it, and that has to be enough.
                res["FxGlowColor"] = BambiPink;
                try
                {
                    sheen.Start();

                    var core = Stops(sheen)[1].Color;
                    Assert.Equal(BambiPink.R, core.R);
                    Assert.Equal(BambiPink.G, core.G);
                    Assert.Equal(BambiPink.B, core.B);
                    Assert.Equal(0x4A, core.A);   // PeakAlpha survived the re-tint

                    // The two transparent tails follow the same hue - a stale tail bleeds the old
                    // mod's colour at the edges of the band.
                    foreach (var i in new[] { 0, 2 })
                    {
                        Assert.Equal(0, Stops(sheen)[i].Color.A);
                        Assert.Equal(BambiPink.G, Stops(sheen)[i].Color.G);
                    }
                }
                finally { sheen.Stop(); }   // never leave a Forever clock on the harness thread
            }
            finally
            {
                if (had) res["FxGlowColor"] = previous;
                else res.Remove("FxGlowColor");
            }
        });
    }

    [Fact]
    public void StopThenStartIsAFullReTint()
    {
        // This is exactly what RestartExclusiveSheens does on a mod switch, so it has to work
        // without any bespoke re-tint entry point.
        WpfRenderHarness.OnStaThread(() =>
        {
            WithGlow(BambiPink, () =>
            {
                var sheen = new CardSheenAdorner(new Border { Width = 336, Height = 200 }, 12);
                try
                {
                    sheen.Start();
                    Application.Current!.Resources["FxGlowColor"] = DroneGreen;
                    sheen.Stop();
                    sheen.Start();

                    var core = Stops(sheen)[1].Color;
                    Assert.Equal(DroneGreen.R, core.R);
                    Assert.Equal(DroneGreen.G, core.G);
                    Assert.Equal(DroneGreen.B, core.B);
                }
                finally { sheen.Stop(); }
            });
        });
    }

    [Fact]
    public void NeitherTheStopsNorTheBrushMayBeFrozen()
    {
        // A frozen stop is how this fix silently reverts: the re-tint would throw, be swallowed by
        // Start's catch, and the sheen would go back to wearing the colour it was born with.
        WpfRenderHarness.OnStaThread(() =>
        {
            var sheen = new CardSheenAdorner(new Border { Width = 100, Height = 60 }, 12);
            foreach (var stop in Stops(sheen))
                Assert.False(stop.IsFrozen, "a frozen GradientStop makes the mod re-tint throw");

            var brush = typeof(CardSheenAdorner)
                .GetField("_brush", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(sheen) as Freezable;
            Assert.True(brush is { IsFrozen: false }, "the sheen brush must stay unfrozen for the re-tint");
        });
    }

    // =====================================================================================
    //  the accent pair
    // =====================================================================================

    private static Color ShiftHue(Color c, double degrees)
    {
        var method = typeof(ConditioningControlPanel.MainWindow).GetMethod("ShiftHue",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.True(method != null, "MainWindow.ShiftHue is gone - the vault's accent pair derives from it");
        return (Color)method!.Invoke(null, new object[] { c, degrees })!;
    }

    [Fact]
    public void ThePartnerHueReproducesTheShippedPair()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            // #FF69B4 -> #B478FF is the pair every badge, pill and card edge on this tab was
            // authored in. Derived rather than hardcoded, it has to land on the same violet, or
            // the flagship mod's vault changes colour for no reason.
            var partner = ShiftHue(BambiPink, -59.0);
            Assert.InRange(partner.R, 175, 191);
            Assert.InRange(partner.G, 97, 128);
            Assert.InRange(partner.B, 248, 255);
        });
    }

    [Fact]
    public void ThePartnerHueFollowsANonPinkAccent()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            // The whole point: under Dronification the partner is a neighbouring green, not a
            // violet borrowed from BambiSleep.
            var partner = ShiftHue(DroneGreen, -59.0);
            Assert.True(partner.G > partner.R && partner.G > partner.B,
                $"a green accent produced {partner} - the partner hue left the accent's neighbourhood");
            Assert.True(partner.R > DroneGreen.R,
                "rotating -59 degrees from green should warm it toward yellow");
        });
    }

    [Fact]
    public void HueShiftPreservesAlphaAndLeavesGreyAlone()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var translucent = Color.FromArgb(0x4D, 0xFF, 0x69, 0xB4);
            Assert.Equal(0x4D, ShiftHue(translucent, -59).A);

            var grey = Color.FromRgb(0x80, 0x80, 0x80);
            Assert.Equal(grey, ShiftHue(grey, -59));

            // A full turn is the identity, within rounding.
            var round = ShiftHue(BambiPink, 360);
            Assert.InRange(round.R, BambiPink.R - 1, BambiPink.R);
            Assert.InRange(round.G, BambiPink.G - 1, BambiPink.G + 1);
            Assert.InRange(round.B, BambiPink.B - 1, BambiPink.B + 1);
        });
    }

    // =====================================================================================
    //  the backdrop: moved into Resources/ so a mod can finally shadow it
    // =====================================================================================

    private const string BackdropKey = "exclusives/vault_backdrop.png";

    [Fact]
    public void TheVaultBackdropResolvesThroughTheModChain()
    {
        // It used to be loose Content under assets\, read off AppContext.BaseDirectory, which put
        // it outside every mod's reach AND outside pack://. A null here means the build action
        // regressed to Content and the embedded fallback is gone.
        var art = ModResourceResolver.ResolveImageDecoded(BackdropKey, 1400);
        Assert.NotNull(art);
    }

    [Fact]
    public void TheBackdropIsDeclaredAsAResourceNotContent()
    {
        var csproj = AppFile("ConditioningControlPanel.csproj");

        Assert.Contains(@"<Resource Include=""Resources\exclusives\*.png"" />", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain(@"<Content Include=""assets\exclusives\*.png"">", csproj, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources",
                                             "exclusives", "vault_backdrop.png")),
            "the vault backdrop is not in Resources\\exclusives - the mod editor already declares that key");
    }

    [Fact]
    public void TheVaultViewResolvesTheBackdropAndFollowsTheActiveMod()
    {
        var view = AppFile("Views", "Tabs", "ExclusivesTabView.xaml.cs");

        Assert.Contains("ResolveImageDecoded(BackdropResource", view, StringComparison.Ordinal);
        Assert.Contains($"\"{BackdropKey}\"", view, StringComparison.Ordinal);
        // No disk read of its own any more (the prose still names the old path, hence the
        // narrower assertions): the resolver owns the chain, including the embedded fallback.
        Assert.DoesNotContain("Path.Combine(AppContext.BaseDirectory", view, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Exists", view, StringComparison.Ordinal);
        Assert.DoesNotContain("new BitmapImage()", view, StringComparison.Ordinal);

        // Hooked and unhooked in pairs, and marshalled - ModChanged may be raised off the UI thread.
        Assert.Contains("ModChanged += OnModChanged", view, StringComparison.Ordinal);
        Assert.Contains("ModChanged -= OnModChanged", view, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", view, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  card art: through the resolver, and Takeover's per-mod fork
    // =====================================================================================

    [Fact]
    public void VaultArtGoesThroughTheResolverAndIsReResolvedOnRefresh()
    {
        var vault = AppFile("MainWindow", "MainWindow.Exclusives.cs");

        // The loader itself.
        Assert.Contains("ModResourceResolver.ResolvePackPath(relativePath, decodePixelWidth)", vault,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new Uri(\"pack://application:,,,/\" + relativePath", vault, StringComparison.Ordinal);

        // ...and the repaint that makes a live mod switch land.
        var refresh = Body(vault, "internal void RefreshExclusivesTab()", "private void RetintVaultChrome()");
        Assert.Contains("LoadPackImage(ExclusiveArtPath(ui.Feature))", refresh, StringComparison.Ordinal);
        Assert.Contains("ui.Art.Source = art", refresh, StringComparison.Ordinal);
        Assert.Contains("ApplySpotlightArt(spot)", refresh, StringComparison.Ordinal);
        Assert.Contains("heroBrush.ImageSource = heroArt", refresh, StringComparison.Ordinal);
        Assert.Contains("RetintVaultChrome()", refresh, StringComparison.Ordinal);
        Assert.Contains("RestartExclusiveSheens()", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void TakeoverArtForksTheSameWayEverywhereElseInTheApp()
    {
        var vault = AppFile("MainWindow", "MainWindow.Exclusives.cs");
        var main = AppFile("MainWindow", "MainWindow.xaml.cs");

        // MainWindow.xaml.cs owns the original fork; the vault replicates the condition rather
        // than inventing one, so the shelf card can never disagree with the feature's own tab.
        Assert.Contains("features/bambi takeover.png", main, StringComparison.Ordinal);
        Assert.Contains("BuiltInMods.BambiSleepId", main, StringComparison.Ordinal);

        var fork = Body(vault, "private static string ExclusiveArtPath(", "private const int ExclusiveCardDecodeWidth");
        Assert.Contains("BuiltInMods.BambiSleepId", fork, StringComparison.Ordinal);
        Assert.Contains("\"features/bambi takeover.png\"", fork, StringComparison.Ordinal);
        Assert.Contains("\"bambitakeover\"", fork, StringComparison.Ordinal);

        // Both cuts must exist, or the fork paints a hole on one mod.
        var features = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "features");
        Assert.True(File.Exists(Path.Combine(features, "bambi takeover.png")));
        Assert.True(File.Exists(Path.Combine(features, "takeover.png")));
    }

    [Fact]
    public void EveryRosterArtPathIsOnDisk()
    {
        // ResolvePackPath tolerates the roster's "Resources/" prefix, which is why no roster data
        // had to change - but a path that resolves to nothing is still a blank card.
        foreach (var feature in ConditioningControlPanel.Models.ExclusiveFeature.All)
        {
            Assert.NotNull(ModResourceResolver.ResolvePackPath(feature.ArtResource, 64));
            if (!string.IsNullOrWhiteSpace(feature.BannerArtResource))
                Assert.NotNull(ModResourceResolver.ResolvePackPath(feature.BannerArtResource!, 64));
        }
    }

    // =====================================================================================
    //  chrome: mod accent everywhere except the two families that mean something
    // =====================================================================================

    [Fact]
    public void NoBrandLiteralsAreLeftInTheVaultChrome()
    {
        // TWO files since the tier-livery pass: the shelf's edge vocabulary moved to
        // Features\VaultLivery.cs so a render suite could reach it without a live MainWindow. The
        // brand-literal guard follows the code - a violet hairline is just as wrong in its new home.
        foreach (var file in new[]
                 {
                     AppFile("MainWindow", "MainWindow.Exclusives.cs"),
                     AppFile("Features", "VaultLivery.cs"),
                 })
        {
            var code = StripComments(file);

            // Bambi pink and its violet partner, in the two spellings these files used.
            foreach (var literal in new[] { "0xFF, 0x69, 0xB4", "0xB4, 0x78, 0xFF" })
                Assert.DoesNotContain(literal, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheFreeTodayGoldFamilyStaysConstant()
    {
        // Documented contract: gold means "open for one day only", and it reads the same on the
        // vault, the dashboard's ? box and the rail. It is NOT decor and must not follow the mod.
        var vault = AppFile("MainWindow", "MainWindow.Exclusives.cs");

        Assert.Contains("FreeTodayGold = Color.FromRgb(0xFF, 0xD2, 0x7A)", vault, StringComparison.Ordinal);
        // The gold EDGE moved to Features\VaultLivery.cs with the rest of the livery vocabulary
        // (tier-livery pass) - same literal, new home. Asserted there, and still asserted, because
        // the contract is about the colour surviving the mod chain, not about which file holds it.
        Assert.Contains("EdgeFree = Frozen(Color.FromArgb(0xE6, 0xFF, 0xD2, 0x7A))",
            AppFile("Features", "VaultLivery.cs"), StringComparison.Ordinal);
        Assert.Contains("Color = FreeTodayGold", vault, StringComparison.Ordinal);

        // The pill's own gradient and the PassReady chip are gold too, and equally untouched.
        Assert.Contains("FreeTodayGold, Color.FromRgb(0xFF, 0x9C, 0x4A)", vault, StringComparison.Ordinal);
        Assert.Contains("Color.FromRgb(0xFF, 0xD2, 0x7A)", vault, StringComparison.Ordinal);

        // ...and the tier plates are commerce: RefreshExclusiveTierPlates only moves opacity.
        var plates = Body(vault, "private void RefreshExclusiveTierPlates()", "// ============================== motion");
        Assert.DoesNotContain("VaultAccent", plates, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChromeReTintCoversEverySurfaceTheTabBuilds()
    {
        var vault = AppFile("MainWindow", "MainWindow.Exclusives.cs");
        var retint = Body(vault, "private void RetintVaultChrome()", "private static void TintShadow(");

        Assert.Contains("_exclusiveAccentStops", retint, StringComparison.Ordinal);   // badges + unlock pills
        Assert.Contains("_exclusiveAccentShadows", retint, StringComparison.Ordinal); // title plates
        Assert.Contains("_exclusiveTeaserMarks", retint, StringComparison.Ordinal);   // the "?" silhouettes
        Assert.Contains("_exclusiveTeaserCards", retint, StringComparison.Ordinal);   // their dim edges
        Assert.Contains("TxtSpotTitle", retint, StringComparison.Ordinal);            // XAML-authored shadow
        Assert.Contains("SpotBadgeFill", retint, StringComparison.Ordinal);
        Assert.Contains("SpotVeilPillFill", retint, StringComparison.Ordinal);
    }

    [Fact]
    public void TheViewStillNamesTheThreeXamlSurfacesTheReTintReachesFor()
    {
        var xaml = AppFile("Views", "Tabs", "ExclusivesTabView.xaml");

        foreach (var name in new[] { "VaultHeroBrush", "SpotBadgeFill", "SpotVeilPillFill" })
            Assert.True(xaml.Contains($"x:Name=\"{name}\"", StringComparison.Ordinal),
                $"{name} is gone from the view - RefreshExclusivesTab has nothing to re-tint");

        // The vault gold hue keys are the tier livery, not decor: they stay literal.
        Assert.Contains("<SolidColorBrush x:Key=\"TabHueBrush\" Color=\"#FFC94E\"/>", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePadlockGlowIsReColouredOnEveryRepaintNotOnlyWhenCreated()
    {
        // The effect outlives the mod that built it: assigning Color only inside the
        // "if there is no effect yet" branch is what kept a padlock glowing green under Bambi.
        var vault = AppFile("MainWindow", "MainWindow.Exclusives.cs");
        var breath = Body(vault, "private static void ApplyVeilLockBreath(",
                                 "private static Storyboard? ApplyFreeTodayPulse(");

        Assert.Contains("glow.Color = FxTheme.GlowColor;", breath, StringComparison.Ordinal);
        // Assigned after the guard, not inside the object initializer.
        Assert.DoesNotContain("Color = FxTheme.GlowColor,", breath, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  helpers
    // =====================================================================================

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

    /// <summary>The source between two anchors, so a scrape fails loudly when the file is reorganized.</summary>
    private static string Body(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"\"{from}\" is gone - re-read the file, then fix the scrape");
        var end = source.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"\"{to}\" no longer follows \"{from}\" - re-read the file, then fix the scrape");
        return source.Substring(start, end - start);
    }

    /// <summary>Drops // and /// lines so a colour named in prose does not look like a live literal.</summary>
    private static string StripComments(string source)
    {
        var kept = new System.Text.StringBuilder();
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            kept.Append(line).Append('\n');
        }
        return kept.ToString();
    }
}
