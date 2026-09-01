using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Mod art used to be cropped by rectangles drawn for OUR art. The premium rail chips carry
/// per-image Viewbox rects hand-tuned to the embedded PNGs (they frame the illustration and push a
/// burned-in wordmark out of the chip), but a mod's art was swapped in by mutating
/// <c>ImageBrush.ImageSource</c> and nothing else — so an author's picture inherited a window chosen
/// for a completely different picture. <see cref="ModArtFramingRegistry"/> is the fix, and its
/// arithmetic is deliberately Dispatcher-free so it can be pinned here.
///
/// <para>The load-bearing assertion in this file is
/// <see cref="ResolveViewbox_ModArtWithoutFraming_DoesNotInheritOurRect"/> — everything else is
/// arithmetic, but that one IS the bug.</para>
/// </summary>
public class ModArtFramingTests
{
    private const double Tol = 1e-9;

    // ── ToViewbox: the zoom-1 window must equal what UniformToFill already shows ──

    [Fact]
    public void ToViewbox_AtZoomOne_SourceWiderThanSurface_TakesFullHeightAndCropsWidth()
    {
        // 4:1 source into a 16:9 frame: the frame is proportionally taller, so height is spent
        // whole and width is inset.
        var rect = ModArtFramingRegistry.ToViewbox(null, sourceAspect: 4.0, surfaceAspect: 16.0 / 9.0);

        Assert.Equal(1.0, rect.Height, Tol);
        Assert.Equal((16.0 / 9.0) / 4.0, rect.Width, Tol);
        Assert.Equal(0.5, rect.X + rect.Width / 2, Tol);   // centred by default
        Assert.Equal(0.5, rect.Y + rect.Height / 2, Tol);
    }

    [Fact]
    public void ToViewbox_AtZoomOne_SourceTallerThanSurface_TakesFullWidthAndCropsHeight()
    {
        // Square source into the rail chip's 69x42: the frame is wider, so width is spent whole.
        var chipAspect = 69.0 / 42.0;
        var rect = ModArtFramingRegistry.ToViewbox(null, sourceAspect: 1.0, surfaceAspect: chipAspect);

        Assert.Equal(1.0, rect.Width, Tol);
        Assert.Equal(1.0 / chipAspect, rect.Height, Tol);
    }

    [Fact]
    public void ToViewbox_AtZoomOne_MatchingAspects_IsTheWholeImage()
    {
        var rect = ModArtFramingRegistry.ToViewbox(null, sourceAspect: 1.5, surfaceAspect: 1.5);
        Assert.Equal(new Rect(0, 0, 1, 1), rect);
    }

    [Fact]
    public void ToViewbox_Zoom_ShrinksTheWindowAndPreservesItsShape()
    {
        var framing = new ModArtFraming { CenterX = 0.5, CenterY = 0.5, Zoom = 2.0 };
        var wide = ModArtFramingRegistry.ToViewbox(null, 4.0, 2.0);
        var zoomed = ModArtFramingRegistry.ToViewbox(framing, 4.0, 2.0);

        Assert.Equal(wide.Width / 2.0, zoomed.Width, Tol);
        Assert.Equal(wide.Height / 2.0, zoomed.Height, Tol);
        // Same shape: a zoom must not restretch the picture.
        Assert.Equal(wide.Width / wide.Height, zoomed.Width / zoomed.Height, 1e-6);
    }

    [Fact]
    public void ToViewbox_FocalPoint_MovesTheWindow()
    {
        var left = ModArtFramingRegistry.ToViewbox(
            new ModArtFraming { CenterX = 0.25, CenterY = 0.5, Zoom = 2.0 }, 1.0, 1.0);
        var right = ModArtFramingRegistry.ToViewbox(
            new ModArtFraming { CenterX = 0.75, CenterY = 0.5, Zoom = 2.0 }, 1.0, 1.0);

        Assert.True(right.X > left.X);
        Assert.Equal(left.Width, right.Width, Tol);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 0.02)]
    public void ToViewbox_FocalPointAtAnEdge_SlidesTheWindowInsteadOfShrinkingIt(double cx, double cy)
    {
        // Sliding rather than clamping is the whole point: clamping the rect would silently change
        // its aspect, and a wrong-shaped Viewbox re-introduces the stretching this feature removes.
        var framing = new ModArtFraming { CenterX = cx, CenterY = cy, Zoom = 3.0 };
        var centred = ModArtFramingRegistry.ToViewbox(
            new ModArtFraming { Zoom = 3.0 }, 2.0, 1.5);
        var edged = ModArtFramingRegistry.ToViewbox(framing, 2.0, 1.5);

        Assert.Equal(centred.Width, edged.Width, Tol);
        Assert.Equal(centred.Height, edged.Height, Tol);

        // ...and it stays entirely inside the source image.
        Assert.True(edged.X >= -Tol && edged.Y >= -Tol);
        Assert.True(edged.X + edged.Width <= 1 + Tol);
        Assert.True(edged.Y + edged.Height <= 1 + Tol);
    }

    [Fact]
    public void ToViewbox_ZoomIsClampedBothWays()
    {
        var under = ModArtFramingRegistry.ToViewbox(new ModArtFraming { Zoom = 0.1 }, 2.0, 2.0);
        Assert.Equal(new Rect(0, 0, 1, 1), under);   // below 1 is meaningless, treated as 1

        var over = ModArtFramingRegistry.ToViewbox(new ModArtFraming { Zoom = 1e9 }, 2.0, 2.0);
        var atMax = ModArtFramingRegistry.ToViewbox(
            new ModArtFraming { Zoom = ModArtFramingRegistry.MaxZoom }, 2.0, 2.0);
        Assert.Equal(atMax.Width, over.Width, Tol);
    }

    // ── Garbage in must never produce a rect WPF renders as nothing ──

    [Theory]
    [InlineData(double.NaN, 1.5)]
    [InlineData(1.5, double.NaN)]
    [InlineData(0.0, 1.5)]
    [InlineData(-2.0, 1.5)]
    [InlineData(double.PositiveInfinity, 1.5)]
    [InlineData(1.5, 0.0)]
    public void ToViewbox_UnusableAspect_FallsBackToTheWholeImage(double sourceAspect, double surfaceAspect)
    {
        // A NaN rect renders as NOTHING in WPF, which reads to a user as "the mod's art is
        // missing" rather than "the crop maths broke" — so a corrupt decode must degrade to the
        // full image, not to an empty window.
        var rect = ModArtFramingRegistry.ToViewbox(new ModArtFraming(), sourceAspect, surfaceAspect);
        Assert.Equal(new Rect(0, 0, 1, 1), rect);
    }

    [Fact]
    public void ToViewbox_NaNFraming_StillYieldsAFiniteRect()
    {
        var framing = new ModArtFraming { CenterX = double.NaN, CenterY = double.NaN, Zoom = double.NaN };
        var rect = ModArtFramingRegistry.ToViewbox(framing, 2.0, 1.5);

        Assert.False(double.IsNaN(rect.X) || double.IsNaN(rect.Y)
                     || double.IsNaN(rect.Width) || double.IsNaN(rect.Height));
        Assert.True(rect.Width > 0 && rect.Height > 0);
    }

    // ── ResolveViewbox: the actual bug ──

    [Fact]
    public void ResolveViewbox_BuiltInArt_KeepsItsShippedRect()
    {
        // Every rect below is the value that shipped in SettingsTabView.xaml. Anyone not running a
        // mod must see byte-identical framing to before this mechanism existed.
        foreach (var binding in ModArtFramingRegistry.Bindings)
        {
            var rect = ModArtFramingRegistry.ResolveViewbox(
                binding.ResourcePath, binding.SurfaceId,
                isModSupplied: false, sourceAspect: 1.79, framing: null);

            Assert.Equal(binding.ShippedViewbox, rect);
        }
    }

    [Fact]
    public void ResolveViewbox_BuiltInArt_IgnoresAnyFramingAModSupplied()
    {
        // Framing is scoped to the art it was drawn against. A mod that ships framing but NOT the
        // image must not re-crop ours.
        var rect = ModArtFramingRegistry.ResolveViewbox(
            "features/takeover.png", ModArtFramingRegistry.SurfaceRailChip,
            isModSupplied: false, sourceAspect: 1.79,
            framing: new ModArtFraming { CenterX = 0.1, CenterY = 0.9, Zoom = 4.0 });

        Assert.Equal(new Rect(0, 0.06, 1, 0.54), rect);
    }

    [Fact]
    public void ResolveViewbox_ModArtWithoutFraming_DoesNotInheritOurRect()
    {
        // THE BUG. features/takeover.png ships framed to 0,0.06,1,0.54 to dodge the wordmark burned
        // into OUR art. An author's replacement has no wordmark there and must not be cut by it;
        // with nothing declared they get an honest centre crop they can predict.
        const string path = "features/takeover.png";
        var shipped = ModArtFramingRegistry.ShippedViewbox(path, ModArtFramingRegistry.SurfaceRailChip);
        Assert.Equal(new Rect(0, 0.06, 1, 0.54), shipped!.Value);

        var rect = ModArtFramingRegistry.ResolveViewbox(
            path, ModArtFramingRegistry.SurfaceRailChip,
            isModSupplied: true, sourceAspect: 16.0 / 9.0, framing: null);

        Assert.NotEqual(shipped.Value, rect);

        // ...and what it IS is the full-fill window for the chip's shape.
        var chip = ModArtFramingRegistry.FindSurface(ModArtFramingRegistry.SurfaceRailChip)!;
        Assert.Equal(ModArtFramingRegistry.ToViewbox(null, 16.0 / 9.0, chip.AspectRatio), rect);
    }

    [Fact]
    public void ResolveViewbox_ModArtWithFraming_UsesTheAuthorsChoice()
    {
        var chip = ModArtFramingRegistry.FindSurface(ModArtFramingRegistry.SurfaceRailChip)!;
        var framing = new ModArtFraming { CenterX = 0.3, CenterY = 0.7, Zoom = 2.5 };

        var rect = ModArtFramingRegistry.ResolveViewbox(
            "features/fyp.png", ModArtFramingRegistry.SurfaceRailChip,
            isModSupplied: true, sourceAspect: 1376.0 / 768.0, framing: framing);

        Assert.Equal(ModArtFramingRegistry.ToViewbox(framing, 1376.0 / 768.0, chip.AspectRatio), rect);
    }

    [Fact]
    public void ResolveViewbox_UnknownSurface_DegradesToTheWholeImage()
    {
        // A mod framed on a later build naming a surface this one does not have must load, not throw.
        var rect = ModArtFramingRegistry.ResolveViewbox(
            "features/fyp.png", "surfaceFromTheFuture",
            isModSupplied: true, sourceAspect: 1.79, framing: new ModArtFraming { Zoom = 3 });

        Assert.Equal(new Rect(0, 0, 1, 1), rect);
    }

    // ── Table guards: catch a typo in the registry rather than on someone's screen ──

    [Fact]
    public void EverySurfaceIdInTheTableResolves()
    {
        foreach (var binding in ModArtFramingRegistry.Bindings)
            Assert.NotNull(ModArtFramingRegistry.FindSurface(binding.SurfaceId));
    }

    [Fact]
    public void EveryShippedRectIsInsideTheImage()
    {
        foreach (var b in ModArtFramingRegistry.Bindings)
        {
            var r = b.ShippedViewbox;
            Assert.True(r.Width > 0 && r.Height > 0, $"{b.ResourcePath}/{b.SurfaceId} has an empty rect");
            Assert.True(r.X >= 0 && r.Y >= 0, $"{b.ResourcePath}/{b.SurfaceId} starts outside the image");
            Assert.True(r.X + r.Width <= 1.0001 && r.Y + r.Height <= 1.0001,
                        $"{b.ResourcePath}/{b.SurfaceId} runs past the edge of the image");
        }
    }

    [Fact]
    public void EverySurfaceHasASaneAspectAndAUniqueId()
    {
        Assert.Equal(ModArtFramingRegistry.Surfaces.Count,
                     ModArtFramingRegistry.Surfaces.Select(s => s.Id).Distinct().Count());

        foreach (var s in ModArtFramingRegistry.Surfaces)
        {
            Assert.True(s.AspectRatio > 0.05 && s.AspectRatio < 20, $"{s.Id} has an implausible aspect");
            Assert.False(string.IsNullOrWhiteSpace(s.DisplayName), $"{s.Id} needs a display name");
        }
    }

    [Fact]
    public void SharedFilesAreBoundToMoreThanOneSurface()
    {
        // The reason framing is stored per surface instead of baked into the PNG: one image feeds
        // several differently-shaped frames. If this ever returns one binding, either the wall was
        // restructured or a row was dropped — and a baked crop silently became "correct".
        Assert.True(ModArtFramingRegistry.BindingsFor("features/lab_quiz_hero.png").Count() >= 3);
        Assert.True(ModArtFramingRegistry.BindingsFor("features/fyp.png").Count() >= 2);
    }

    [Fact]
    public void BindingsForIsCaseInsensitive()
    {
        // mod.json is hand-authored as often as editor-written.
        Assert.NotEmpty(ModArtFramingRegistry.BindingsFor("Features/FYP.png"));
    }

    // ── IsDefault decides what the editor bothers to write into a manifest ──

    [Fact]
    public void IsDefault_IsTrueOnlyForTheNoOpFraming()
    {
        Assert.True(new ModArtFraming().IsDefault);
        Assert.True(new ModArtFraming { CenterX = 0.5, CenterY = 0.5, Zoom = 1.0 }.IsDefault);
        Assert.False(new ModArtFraming { Zoom = 1.5 }.IsDefault);
        Assert.False(new ModArtFraming { CenterX = 0.4 }.IsDefault);
        Assert.False(new ModArtFraming { CenterY = 0.6 }.IsDefault);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var original = new ModArtFraming { CenterX = 0.2, CenterY = 0.8, Zoom = 3 };
        var copy = original.Clone();
        copy.CenterX = 0.9;

        Assert.Equal(0.2, original.CenterX, Tol);
        Assert.Equal(0.8, copy.CenterY, Tol);
    }

    [Theory]
    [InlineData("0.25,0.75", 0.25, 0.75)]
    [InlineData(" 0.5 , 0.1 ", 0.5, 0.1)]
    public void TryParsePoint_AcceptsWhatAHandEditWouldLookLike(string text, double x, double y)
    {
        Assert.True(ModArtFramingRegistry.TryParsePoint(text, out var px, out var py));
        Assert.Equal(x, px, Tol);
        Assert.Equal(y, py, Tol);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.5")]
    [InlineData("0.5,0.5,0.5")]
    [InlineData("left,top")]
    public void TryParsePoint_RejectsGarbageAndDefaultsToCentre(string? text)
    {
        Assert.False(ModArtFramingRegistry.TryParsePoint(text, out var px, out var py));
        Assert.Equal(0.5, px, Tol);
        Assert.Equal(0.5, py, Tol);
    }

    // ─── XAML ↔ registry parity ──────────────────────────────────────
    //
    // The rail rects live in BOTH SettingsTabView.xaml and the registry, deliberately: the XAML
    // literal is what renders before LoadFeatureImages runs in the ctor, so deleting it would make
    // built-in framing depend on ctor ordering, while the registry is what the runtime re-decides
    // from. Comments in both files ask for manual sync, but a comment is not a guard: retune a rect
    // in the XAML only and the chip renders the new crop until the ctor snaps it back to the old
    // one — a visible flicker no other test would catch. This repo already answers that hazard with
    // source-scraping tests (PlayDoorRenderTests.PlayHeroMapFeedsEveryHeroBrush), so this does too.

    private static string ReadSource(params string[] parts) => SourceRoots.ReadProductFile(parts);

    /// <summary>The x:Key of each rail brush, the file it paints and the surface it is bound to.</summary>
    public static TheoryData<string, string, string> RailBrushKeys() => new()
    {
        { "ArtTakeover",  "features/takeover.png",       ModArtFramingRegistry.SurfaceRailChip },
        { "ArtAwareness", "features/awareness.png",      ModArtFramingRegistry.SurfaceRailChip },
        { "ArtHaptics",   "features/vibe.png",           ModArtFramingRegistry.SurfaceRailChip },
        { "ArtIntake",    "features/lab_quiz_hero.png",  ModArtFramingRegistry.SurfaceRailChip },
        { "ArtRemote",    "features/remote_control.png", ModArtFramingRegistry.SurfaceRailChip },
        { "ArtFyp",       "features/fyp.png",            ModArtFramingRegistry.SurfaceRailChip },
        { "ArtBlink",     "features/blink_trainer.png",  ModArtFramingRegistry.SurfaceRailCard },
        { "ArtLockdown",  "lockdown_icon.png",           ModArtFramingRegistry.SurfaceRailCard },
    };

    [Theory]
    [MemberData(nameof(RailBrushKeys))]
    public void RailChipViewboxInXaml_MatchesTheRegistrysShippedRect(string xamlKey, string resourcePath, string surfaceId)
    {
        var xaml = ReadSource("Views", "Tabs", "SettingsTabView.xaml");
        var literal = ExtractViewbox(xaml, $"x:Key=\"{xamlKey}\"");
        Assert.NotNull(literal);

        var shipped = ModArtFramingRegistry.ShippedViewbox(resourcePath, surfaceId);
        Assert.NotNull(shipped);
        AssertRectsMatch(shipped!.Value, literal!.Value, $"{xamlKey} / {resourcePath}");
    }

    [Fact]
    public void GoonViewboxInXaml_MatchesTheRegistrysShippedRect()
    {
        var xaml = ReadSource("Views", "Tabs", "PlayTabView.xaml");
        var literal = ExtractViewbox(xaml, "x:Name=\"PlayGoonHeroBrush\"");
        Assert.NotNull(literal);

        var shipped = ModArtFramingRegistry.ShippedViewbox("features/goon_game.png",
                                                           ModArtFramingRegistry.SurfacePlayCardTall);
        Assert.NotNull(shipped);
        AssertRectsMatch(shipped!.Value, literal!.Value, "PlayGoonHeroBrush");
    }

    // ─── The declared shape has to be the REAL frame's shape ─────────
    //
    // A binding pointed at a surface whose aspect does not match its actual frame does not merely
    // preview wrong: UniformToFill re-crops the stored window down to the real frame, so the author
    // loses image on BOTH passes. The second review caught two of these, and in the Loom case the
    // result was mod art cropped harder than before framing existed at all.

    [Theory]
    // 16:9 is what the editor's own slot labels ask authors to supply (1376x768).
    [InlineData("features/loom.png", ModArtFramingRegistry.SurfaceLoomStrip, 216.0 / 118.0)]
    [InlineData("features/remote_control.png", ModArtFramingRegistry.SurfacePlayCardTall, 394.0 / 168.0)]
    [InlineData("features/fyp.png", ModArtFramingRegistry.SurfacePlayCard, 394.0 / 138.0)]
    [InlineData("features/lab_quiz_hero.png", ModArtFramingRegistry.SurfaceIntakeStrip, 240.0 / 68.0)]
    public void ABindingsSurfaceAspectIsTheOneItsRealFrameHas(string path, string surfaceId, double realAspect)
    {
        // Guards the binding table against the class of mistake the review found: the surface a path
        // is bound to must be the shape of the box the art is actually painted into.
        var surface = ModArtFramingRegistry.FindSurface(surfaceId);
        Assert.NotNull(surface);
        Assert.Equal(realAspect, surface!.AspectRatio, 1e-9);
        Assert.Contains(ModArtFramingRegistry.BindingsFor(path), b => b.SurfaceId == surfaceId);
    }

    [Fact]
    public void TheLoomStrip_DoesNotOverCropSixteenNineModArt()
    {
        // THE regression this file exists to prevent a second time. The strip is ~1.83:1 and a 16:9
        // source is 1.78:1, so an un-framed author should keep essentially their whole image - which
        // is what a bare UniformToFill gave them before framing existed. Bound to the 2.85:1 card
        // plate it kept about 62% of the height, and then UniformToFill took another bite.
        const double sixteenNine = 1376.0 / 768.0;
        var rect = ModArtFramingRegistry.ResolveViewbox(
            "features/loom.png", ModArtFramingRegistry.SurfaceLoomStrip,
            isModSupplied: true, sourceAspect: sixteenNine, framing: null);

        Assert.Equal(1.0, rect.Width, 1e-9);
        Assert.True(rect.Height > 0.95,
            $"the Loom strip is keeping only {rect.Height:P0} of the height of a 16:9 image; " +
            "it is a ~1.83:1 frame, so an un-framed author should lose almost nothing");
    }

    [Fact]
    public void NoFramableBindingIsPointedAtAFrameShapeItDoesNotHave()
    {
        // Every framable pair must resolve to a surface, and no framable pair may sit on the plain
        // 138 playCard while its plate is one of the two overridden to 168.
        foreach (var b in ModArtFramingRegistry.Bindings.Where(x => x.Framable))
        {
            Assert.NotNull(ModArtFramingRegistry.FindSurface(b.SurfaceId));
            if (b.ResourcePath is "features/remote_control.png" or "features/goon_game.png")
                Assert.NotEqual(ModArtFramingRegistry.SurfacePlayCard, b.SurfaceId);
            if (b.ResourcePath == "features/loom.png")
                Assert.Equal(ModArtFramingRegistry.SurfaceLoomStrip, b.SurfaceId);
        }
    }

    [Fact]
    public void TheGoonCardIsNotOfferedForFraming()
    {
        // Stretch=Uniform over a #FF161622 plate, so its square wordmark letterboxes rather than
        // cropping. A crop preview would be lying about the one surface this registry exists to keep
        // honest, so the editor must never offer it - while the shipped rect above is still needed.
        Assert.False(ModArtFramingRegistry.IsFramable("features/goon_game.png",
                                                      ModArtFramingRegistry.SurfacePlayCardTall));
        Assert.Empty(ModArtFramingRegistry.FramableBindingsFor("features/goon_game.png"));
    }

    /// <summary>
    /// Pulls the <c>Viewbox="x,y,w,h"</c> off the element that carries <paramref name="anchor"/>.
    /// Scoped to the 400 characters after the anchor so it cannot wander into a sibling brush, and
    /// returns null when the element has no Viewbox at all (a bare UniformToFill, which means the
    /// whole image and is not something the registry mirrors).
    /// </summary>
    private static Rect? ExtractViewbox(string xaml, string anchor)
    {
        var at = xaml.IndexOf(anchor, StringComparison.Ordinal);
        if (at < 0) return null;

        var window = xaml.Substring(at, Math.Min(400, xaml.Length - at));
        var end = window.IndexOf('>');
        if (end > 0) window = window.Substring(0, end);

        var m = Regex.Match(window, @"Viewbox\s*=\s*""\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*""");
        if (!m.Success) return null;

        return new Rect(
            double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AssertRectsMatch(Rect registry, Rect xaml, string what)
    {
        Assert.True(Math.Abs(registry.X - xaml.X) < 1e-9
                    && Math.Abs(registry.Y - xaml.Y) < 1e-9
                    && Math.Abs(registry.Width - xaml.Width) < 1e-9
                    && Math.Abs(registry.Height - xaml.Height) < 1e-9,
            $"{what}: the XAML Viewbox and ModArtFramingRegistry have drifted apart. " +
            $"XAML says {xaml.X},{xaml.Y},{xaml.Width},{xaml.Height} and the registry says " +
            $"{registry.X},{registry.Y},{registry.Width},{registry.Height}. Both are load-bearing " +
            "(the XAML literal renders before the ctor runs, the registry is what it is then " +
            "overwritten with), so change them together.");
    }
}
