using System;
using System.Linq;
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
}
