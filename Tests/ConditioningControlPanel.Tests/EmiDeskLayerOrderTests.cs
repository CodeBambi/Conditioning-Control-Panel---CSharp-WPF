using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE SKIN LAW, pinned.
///
/// <para><b>The rule.</b> The outfit / skin layer is the TOPMOST thing in EMI's composition: it is
/// drawn above the face and above the takeover glass, and face art may never paint over a garment.
/// Her face is not part of the body PNG - it is a canvas laid over the glass rect, with the glass a
/// second canvas on the same rect - so anything a coat, a collar or a pair of goggles draws across
/// that rect is buried behind two layers of her own face unless the garment gets a layer of its own
/// in front of them. The owner caught it as a labcoat collar sitting UNDER her screen
/// (2026-08-30).</para>
///
/// <para><b>Why a test and not a comment.</b> The guarantee is child order in a
/// <see cref="Grid"/>, which is exactly the kind of thing a later XAML edit reorders without
/// noticing - and a buried collar is invisible until someone puts a garment on her, which on the
/// desk nobody does yet. So the order is asserted on the REAL realized tree: build the widget, walk
/// <c>BodyRoot</c>, and require the overlay to come after the face layer. Reorder those two nodes
/// and this suite fails.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class EmiDeskLayerOrderTests
{
    private static string Read(params string[] parts) => SourceRoots.ReadProductFile(parts);

    /// <summary>Runs a body against a freshly built widget, and always tears it down.</summary>
    private static void WithWidget(Action<EmiDeskWindow> body)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            EmiDeskWindow? w = null;
            try
            {
                w = new EmiDeskWindow();
                body(w);
            }
            finally
            {
                // Never Show()n, so this is only the Closed cleanup: it stops the idle, sway and
                // glass timers the constructor started.
                try { w?.Close(); } catch (Exception ex) { Assert.Fail("widget teardown threw: " + ex); }
            }
        });
    }

    [Fact]
    public void The_outfit_overlay_is_drawn_above_the_face_and_the_glass()
    {
        WithWidget(w =>
        {
            var root = (Grid)w.FindName("BodyRoot")!;
            var face = (FrameworkElement)w.FindName("FaceLayer")!;
            var over = (FrameworkElement)w.FindName("OutfitOverImage")!;
            var bodyImage = (UIElement)w.FindName("BodyImage")!;

            var kids = root.Children.Cast<UIElement>().ToList();

            int iBody = kids.IndexOf(bodyImage);
            int iFace = kids.IndexOf(face);
            int iOver = kids.IndexOf(over);

            Assert.True(iBody >= 0, "BodyImage must be a direct child of BodyRoot");
            Assert.True(iFace >= 0, "FaceLayer must be a direct child of BodyRoot");
            Assert.True(iOver >= 0,
                "the outfit overlay must be a direct child of BodyRoot, so it inherits the CRT "
                + "squash, the click squash, the drag wobble and the move offsets exactly as the "
                + "body does");

            // A later child of a Grid paints later, i.e. on top. THIS is the law.
            Assert.True(iOver > iFace,
                "THE SKIN LAW: the outfit overlay must be authored AFTER FaceLayer inside BodyRoot "
                + "so a garment paints OVER her face and glass, never under them. Found the "
                + "overlay at index " + iOver + " and the face at " + iFace + ".");
            Assert.True(iFace > iBody, "the face still has to paint over the body PNG");

            // The glass lives INSIDE FaceLayer, so being above the face layer is being above the
            // glass - but only while that stays true, so pin that too.
            var glass = (FrameworkElement)w.FindName("GlassCanvas")!;
            Assert.Same(face, glass.Parent);
        });
    }

    [Fact]
    public void The_overlay_costs_nothing_at_rest_and_never_eats_a_click()
    {
        WithWidget(w =>
        {
            var over = (Image)w.FindName("OutfitOverImage")!;
            var body = (Image)w.FindName("BodyImage")!;

            // Nothing on the desk chooses an outfit yet: she must come up wearing no overlay, with
            // no Source decoded and nothing on screen.
            Assert.Null(w.Outfit);
            Assert.Equal(Visibility.Collapsed, over.Visibility);
            Assert.Null(over.Source);

            // A click anywhere on her is the pet / drag gesture on BodyRoot; a hit-testable overlay
            // would swallow it.
            Assert.False(over.IsHitTestVisible);

            // Same pixel-art rendering as the body, or the garment would resample differently from
            // the sprite it is sitting on.
            Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode(over));
            Assert.Equal(RenderOptions.GetBitmapScalingMode(body), RenderOptions.GetBitmapScalingMode(over));
            Assert.Equal(body.Stretch, over.Stretch);
        });
    }

    [Fact]
    public void An_outfit_with_no_overlay_sheet_stays_silent()
    {
        // This used to say "three of the four have no overlay art and never will". They do now:
        // the campus side of THE SKIN LAW drew the missing 30 sheets, so the outfit with no art
        // is no longer one of hers - it is a name the art tree has never heard of. The seam must
        // still take that quietly: no throw, no blank Image over her face, and the widget still
        // reports what it was asked to wear.
        foreach (var frame in EmiChains.BodyFrameFile.Keys)
            Assert.Null(EmiChains.OverPath("no-such-outfit", frame));

        WithWidget(w =>
        {
            var over = (Image)w.FindName("OutfitOverImage")!;

            w.SetOutfit("no-such-outfit");
            Assert.Equal("no-such-outfit", w.Outfit);
            Assert.Equal(Visibility.Collapsed, over.Visibility);
            Assert.Null(over.Source);

            w.SetOutfit(null);
            Assert.Null(w.Outfit);
            Assert.Equal(Visibility.Collapsed, over.Visibility);
        });
    }

    [Fact]
    public void Every_shipped_outfit_now_carries_a_complete_overlay_set()
    {
        // The desk has no outfit picker, but it renders out of the campus's art tree, so the day
        // the campus gained its sheets the desk could dress her fully too. A HALF-PRESENT SET IS
        // NO SET (PaintOutfitOver stands the whole layer down rather than show one pose's collar
        // on every other pose), so completeness is pinned per outfit, not per file: lose one PNG
        // from any of the four and that outfit goes silent on the desk.
        foreach (var outfit in EmiChains.Outfits)
            foreach (var frame in EmiChains.BodyFrameFile.Keys)
                Assert.NotNull(EmiChains.OverPath(outfit, frame));

        WithWidget(w =>
        {
            var over = (Image)w.FindName("OutfitOverImage")!;

            foreach (var outfit in EmiChains.Outfits)
            {
                w.SetOutfit(outfit);
                Assert.Equal(outfit, w.Outfit);
                Assert.Equal(Visibility.Visible, over.Visibility);
                Assert.NotNull(over.Source);
            }

            w.SetOutfit(null);
            Assert.Equal(Visibility.Collapsed, over.Visibility);
            Assert.Null(over.Source);
        });
    }

    [Fact]
    public void The_swim_goggles_actually_reach_the_layer()
    {
        // THE ART IS ALREADY HERE. The desk has no outfit picker, but it renders out of the SAME
        // Resources/web/arcademy/art/emi/ tree the campus does, and that tree ships as Content - so
        // swim/over-body-*.png is already sitting beside the exe. This is the end-to-end proof that
        // the desk's overlay slot is wired to real files through the campus's naming contract, not
        // just to a hypothetical one: put swim on her and the goggles decode and go up.
        Assert.Equal(10, EmiChains.BodyFrameFile.Count);
        foreach (var frame in EmiChains.BodyFrameFile.Keys)
            Assert.NotNull(EmiChains.OverPath("swim", frame));

        WithWidget(w =>
        {
            var over = (Image)w.FindName("OutfitOverImage")!;

            w.SetOutfit("swim");
            Assert.Equal(Visibility.Visible, over.Visibility);
            Assert.NotNull(over.Source);

            // ...and it follows the pose, because SetPose repaints it - the overlay is the body's
            // shadow, never its own animation.
            var idle = over.Source;
            w.SetPose("sad");
            Assert.NotNull(over.Source);
            Assert.NotSame(idle, over.Source);

            // Taking it off puts the layer back to costing nothing.
            w.SetOutfit(null);
            Assert.Equal(Visibility.Collapsed, over.Visibility);
            Assert.Null(over.Source);
        });
    }

    [Fact]
    public void The_overlay_resolves_on_the_same_naming_contract_as_the_campus()
    {
        // `<outfit>/over-<pose file>.png` beside `<outfit>/<pose file>.png` - widget.js `overSrc`.
        foreach (var pair in EmiChains.BodyFrameFile)
            Assert.Equal("over-" + pair.Value, EmiChains.OverFileName(pair.Key));

        // Junk resolves to the resting pose rather than throwing, exactly like FrameKey.
        Assert.Equal("over-" + EmiChains.BodyFrameFile["idle"], EmiChains.OverFileName("not-a-pose"));

        // There is no standard overlay, and a missing sheet answers null - never a guess at the
        // body art, because half an overlay is worse than none.
        Assert.Null(EmiChains.OverPath(null, "idle"));
        Assert.Null(EmiChains.OverPath("", "idle"));
        Assert.Null(EmiChains.OverPath("../../escape", "idle"));

        // The four names are the campus's, and the desk owns none of them as a feature.
        Assert.Equal(new[] { "varsity", "labcoat", "cheer", "swim" }, EmiChains.Outfits.ToArray());
    }

    [Fact]
    public void The_law_is_written_down_where_the_next_person_will_look()
    {
        var chainsSrc = Read("Services", "EmiDesk", "EmiChains.cs");
        Assert.Contains("THE SKIN LAW", chainsSrc);
        Assert.Contains("THE OUTFIT / SKIN LAYER IS THE TOPMOST THING IN EMI'S COMPOSITION", chainsSrc);

        var xaml = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml");
        int face = xaml.IndexOf("x:Name=\"FaceLayer\"", StringComparison.Ordinal);
        int over = xaml.IndexOf("x:Name=\"OutfitOverImage\"", StringComparison.Ordinal);
        Assert.True(face >= 0 && over > face,
            "the overlay must be authored after FaceLayer in the XAML source too, so a reader sees "
            + "the order without running anything");

        // Pinned to the WPF head on purpose. This is the head's own working doc, not product
        // source, and any other root that grows a CLAUDE.md would make a multi-root probe
        // ambiguous — a red test on someone else's unit, for a file that never moves.
        var claude = File.ReadAllText(
            Path.Combine(SourceRoots.RepoRoot, "ConditioningControlPanel", "CLAUDE.md"));
        Assert.Contains("outfit / skin layer is the TOPMOST thing", claude);
        Assert.Contains("OutfitOverImage", claude);
    }
}
