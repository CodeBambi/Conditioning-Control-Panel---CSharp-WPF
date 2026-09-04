using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE LOCKER DRESSES THE DESKTOP.
///
/// <para><b>The ask.</b> A member asked on 2026-09-01 why the outfit they bought and armed in the
/// Arcademy's Locker was not on the EMI who lives on their desktop. It was not, because the seam
/// had no callers: <c>SetOutfit</c> painted the overlay only, nothing read the meta key, and
/// <c>BodyPath</c> took no outfit at all - so the best that could have happened was a lab coat's
/// collar floating over her school shirt.</para>
///
/// <para><b>What is pinned here.</b> Three things, and they are the three that were missing:
/// the BODY resolves per outfit (with a per-frame fall back to the standard sheet, never a blank),
/// a swap actually repaints the body she is already standing in - the pose is unchanged across a
/// wardrobe change, so the "that pose is already up" early-out is exactly where this feature dies
/// if nobody is watching - and the wiring that carries the pick from the campus to the widget
/// exists on both roads (every summon, and live while she is on screen).</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class EmiLockerOutfitTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", Path.Combine(parts)));

    [Fact]
    public void Every_shipped_outfit_has_a_whole_body_sheet_beside_the_exe()
    {
        // A garment is TWO sheets and the overlay half was already pinned next door. This is the
        // other half: lose one body PNG from any of the four and that pose silently falls back to
        // the standard art mid-wardrobe, which reads as a costume change between blinks.
        foreach (var outfit in EmiChains.Outfits)
            foreach (var frame in EmiChains.BodyFrameFile.Keys)
            {
                var path = EmiChains.BodyPath(frame, outfit);
                Assert.NotNull(path);
                Assert.Equal(Path.Combine(outfit, EmiChains.BodyFrameFile[frame]),
                    Path.Combine(Path.GetFileName(Path.GetDirectoryName(path)!), Path.GetFileName(path)));
            }
    }

    [Fact]
    public void An_outfit_the_tree_never_heard_of_wears_the_standard_art()
    {
        // NEVER A BLANK. The overlay is allowed to answer null (there is no standard overlay); the
        // body is not, because a null body is an invisible click target sitting on the desktop.
        foreach (var name in new string?[] { null, "", "   ", "no-such-outfit", "../../escape", "swim/../.." })
        {
            var path = EmiChains.BodyPath("idle", name);
            Assert.NotNull(path);
            Assert.Equal(EmiChains.BodyPath("idle"), path);
        }

        // ...and the gate every road passes agrees about which names are real.
        Assert.Null(EmiChains.OutfitName("no-such-outfit"));
        Assert.Null(EmiChains.OutfitName("../../escape"));
        Assert.Null(EmiChains.OutfitName(null));
        Assert.Equal("swim", EmiChains.OutfitName(" swim "));
        foreach (var outfit in EmiChains.Outfits) Assert.Equal(outfit, EmiChains.OutfitName(outfit));
    }

    [Fact]
    public void A_wardrobe_change_repaints_the_body_she_is_already_standing_in()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            EmiDeskWindow? w = null;
            try
            {
                w = new EmiDeskWindow();
                var body = (Image)w.FindName("BodyImage")!;
                var over = (Image)w.FindName("OutfitOverImage")!;

                w.SetOutfit(null);
                w.SetPose("idle");
                var plain = body.Source as BitmapImage;
                Assert.NotNull(plain);

                // THE REGRESSION THIS TEST EXISTS FOR: the pose does not change when the outfit
                // does - she is nearly always at idle - so a body repaint keyed on the pose alone
                // swallows the whole feature and leaves the overlay hanging on the standard sheet.
                w.SetOutfit("labcoat");
                Assert.Equal("labcoat", w.Outfit);
                Assert.NotNull(body.Source);
                Assert.NotSame(plain, body.Source);
                Assert.NotNull(over.Source);

                // A second sheet is a second body, not just a second overlay.
                var coat = body.Source;
                w.SetOutfit("swim");
                Assert.NotSame(coat, body.Source);

                // And taking it off puts the standard art back, on the same pose.
                w.SetOutfit(null);
                Assert.Null(w.Outfit);
                Assert.Equal(plain!.UriSource, (body.Source as BitmapImage)?.UriSource);
                Assert.Null(over.Source);
            }
            finally
            {
                try { w?.Close(); } catch (Exception ex) { Assert.Fail("widget teardown threw: " + ex); }
            }
        });
    }

    [Fact]
    public void The_desk_and_the_locker_gate_the_same_four_prizes()
    {
        // TWO CLAMPS, ONE LIST. locker.js refuses to arm an outfit whose sku is not in the wallet,
        // and ArcademyHostService refuses to hand one to the desk for the same reason - because the
        // page's clamp lives in the file that writes the key, so a blob carrying a garment nobody
        // bought would otherwise dress her anyway. If the two maps drift, one of the clamps is
        // guarding a prize that does not exist.
        var locker = Read("Resources", "web", "arcademy", "shell", "locker.js");
        var host = Read("Services", "Arcademy", "ArcademyHostService.cs");

        foreach (var outfit in EmiChains.Outfits)
        {
            var sku = "emi_" + outfit;
            Assert.Contains(outfit + ": '" + sku + "'", locker);
            Assert.Contains("[\"" + outfit + "\"] = \"" + sku + "\"", host);
        }

        // The key itself, spelled the same on both sides of the bridge.
        Assert.Contains("OUTFIT_KEY = 'lockerOutfit'", locker);
        Assert.Contains("EmiOutfitKey = \"lockerOutfit\"", host);
    }

    [Fact]
    public void The_pick_reaches_the_widget_on_both_roads()
    {
        // The seam used to exist with ZERO callers, which is the whole reason the feature was
        // missing rather than broken. Both roads are asserted so a refactor cannot quietly take one
        // away: the SUMMON re-reads (she outlives a dismiss and can be re-dressed while away), and
        // the meta-command hook pushes LIVE (an equip made with her on screen must land without a
        // restart).
        var summon = Read("Services", "EmiDesk", "EmiDeskService.cs");
        Assert.Contains("win.RefreshOutfit();", summon);

        var host = Read("Services", "Arcademy", "ArcademyHostService.cs");
        Assert.Contains("PushEmiOutfitToDesk();", host);
        Assert.Contains("App.EmiDesk?.Window?.RefreshOutfit()", host);

        // ...and the widget dresses itself before its first pose is painted, so she is never built
        // wearing the standard art and then swapped a frame later.
        var win = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml.cs");
        var iRefresh = win.IndexOf("RefreshOutfit();\n        SetPose(\"idle\");", StringComparison.Ordinal);
        Assert.True(iRefresh > 0 || win.Contains("RefreshOutfit();\r\n        SetPose(\"idle\");"),
            "the constructor must RefreshOutfit() immediately before its first SetPose(\"idle\")");
    }
}
