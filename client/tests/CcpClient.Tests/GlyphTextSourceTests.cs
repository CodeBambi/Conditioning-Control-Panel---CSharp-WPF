using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Glyph;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-115. The GDI+ rasteriser that turns a word into per-pixel-alpha pixels.
///
/// <para><b>These facts exist because the sweep found the file completely uncovered.</b> Round 1
/// mutated the premultiplied pixel format to a straight-alpha one, the transparent clear to an
/// OPAQUE BLACK one, the premultiplication repair to a no-op and the margin to zero — and every one
/// of them survived. An opaque clear is D83's black screen restored in one constant, which is the
/// single most dangerous line in this packet, and nothing was watching it.</para>
///
/// <para>No window is involved: the rasteriser produces a buffer, and every claim here is about
/// those bytes. Whether GDI+ is present is a property of the machine and is asserted rather than
/// assumed, so a build with no rasteriser reports the absence instead of passing quietly.</para>
/// </summary>
public class GlyphTextSourceTests
{
    private static BouncingTextPresentation Dials(bool outline = false, int size = 100) =>
        new(true, 5, size, 100, BouncingTextColourMode.Random, null, outline, ["OBEY"]);

    [Fact]
    public void THISBUILDHASARASTERISER_OrEveryFactBelowIsAboutNothing()
    {
        // GDI+ is part of Windows and this port's other rasteriser already depends on it. The
        // positive control: without it, "the frame is transparent at the margin" would be equally
        // true of no frame at all.
        Assert.Equal(OperatingSystem.IsWindows(), GdiPlusRuntime.Available);
    }

    [Fact]
    public void THEBACKGROUNDISFULLYTRANSPARENT_WhichIsTheONELineThatMakesThisModulePossible()
    {
        // An opaque clear here is exactly D83: at the shipped default opacity of 100 the module
        // would put a black rectangle with a word on it in front of the user, which is worse than
        // the absent module. The margin is transparent by construction, so it is where this is
        // read.
        var source = new GdiPlusGlyphTextSource();
        var frame = source.Render("OBEY", 0x00FF00FF, 300, 140, Dials());

        Assert.Equal(OperatingSystem.IsWindows(), frame is not null);
        if (frame is null)
        {
            return;
        }

        Assert.Equal(0, frame.AlphaAt(1, 1));
        Assert.Equal(0, frame.AlphaAt(frame.Width - 2, 1));
        Assert.Equal(0, frame.AlphaAt(1, frame.Height - 2));
        Assert.Equal(0, frame.AlphaAt(frame.Width - 2, frame.Height - 2));
        Assert.Equal(0x000000u, frame.PremultipliedColourAt(1, 1));
    }

    [Fact]
    public void THERASTERCARRIESPROVABLEINK_OrTheSurfaceCouldNotConfirmItAtAll()
    {
        var source = new GdiPlusGlyphTextSource();
        var frame = source.Render("OBEY", 0x00FF00FF, 300, 140, Dials());

        Assert.Equal(OperatingSystem.IsWindows(), frame is not null);
        if (frame is null)
        {
            return;
        }

        Assert.True(frame.HasProvableInk,
            "the raster produced no fully-opaque non-black pixel, so the surface could not distinguish it from "
            + "a window that composites nothing and would refuse it");
    }

    [Fact]
    public void ANDTHEBUFFERISPREMULTIPLIED_ProvenBecauseTheFrameTypeWouldHaveTHROWNOtherwise()
    {
        // GlyphFrame's constructor throws on any channel above its own alpha, so a frame coming
        // back at all IS the premultiplication proof. The mutation this catches is the pixel format
        // being changed to straight ARGB, which makes GDI+ produce exactly that buffer.
        var source = new GdiPlusGlyphTextSource();
        var frame = source.Render("SUBMIT", 0x00FFFFFF, 320, 150, Dials());

        Assert.Equal(OperatingSystem.IsWindows(), frame is not null);
        if (frame is null)
        {
            return;
        }

        // Re-asserted directly, because "the constructor did not throw" reads as an absence.
        var offenders = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var alpha = frame.AlphaAt(x, y);
                var colour = frame.PremultipliedColourAt(x, y);
                if (((colour >> 16) & 0xFF) > alpha || ((colour >> 8) & 0xFF) > alpha || (colour & 0xFF) > alpha)
                {
                    offenders++;
                }
            }
        }

        Assert.Equal(0, offenders);
    }

    [Fact]
    public void THEMEASUREINCLUDESTHEMARGIN_SoAnAntialiasedEdgeIsNeverClipped()
    {
        // The margin is not decoration: it is what guarantees the frame has fully-transparent
        // pixels at known positions, which is what the surface's read-back samples to catch an
        // opaque plate composited where the glyph should have holes.
        var source = new GdiPlusGlyphTextSource();
        var measured = source.Measure("OBEY", Dials());

        Assert.Equal(OperatingSystem.IsWindows(), measured.Width > 0);
        if (measured.Width == 0)
        {
            return;
        }

        Assert.True(measured.Width > 2 * GdiPlusGlyphTextSource.Margin,
            $"the measured width {measured.Width} does not even contain the margin");
        Assert.True(measured.Height > 2 * GdiPlusGlyphTextSource.Margin);

        // A frame rendered at that size really is transparent inside the margin band.
        var frame = source.Render("OBEY", 0x00FF00FF, measured.Width, measured.Height, Dials());
        Assert.NotNull(frame);
        Assert.Equal(0, frame!.AlphaAt(2, measured.Height / 2));
        Assert.Equal(0, frame.AlphaAt(measured.Width - 3, measured.Height / 2));
    }

    [Fact]
    public void ABIGGERSIZEDIALREALLYPRODUCESABIGGERWORD()
    {
        var source = new GdiPlusGlyphTextSource();
        var small = source.Measure("OBEY", Dials(size: 50));
        var large = source.Measure("OBEY", Dials(size: 300));

        Assert.Equal(OperatingSystem.IsWindows(), small.Width > 0);
        if (small.Width == 0)
        {
            return;
        }

        Assert.True(large.Width > small.Width, $"size 300 measured {large.Width} px wide and size 50 measured "
            + $"{small.Width}; the dial is not reaching the raster");
        Assert.True(large.Height > small.Height);
    }

    [Fact]
    public void THEOUTLINESTYLEProducesADifferentRasterFromTheShadowStyle()
    {
        var source = new GdiPlusGlyphTextSource();
        var shadow = source.Render("DROP", 0x00FF00FF, 300, 140, Dials());
        var outline = source.Render("DROP", 0x00FF00FF, 300, 140, Dials(outline: true));

        Assert.Equal(OperatingSystem.IsWindows(), shadow is not null);
        if (shadow is null || outline is null)
        {
            return;
        }

        Assert.False(shadow.Pixels.AsSpan().SequenceEqual(outline.Pixels),
            "the outline dial produced a byte-identical raster, so the style is not reaching the draw");
    }

    [Fact]
    public void ANEMPTYWORDOrAZeroSizedFrameRendersNOTHINGRatherThanThrowing()
    {
        var source = new GdiPlusGlyphTextSource();

        Assert.Null(source.Render(string.Empty, 0x00FF00FF, 100, 40, Dials()));
        Assert.Null(source.Render("OBEY", 0x00FF00FF, 0, 40, Dials()));
        Assert.Null(source.Render("OBEY", 0x00FF00FF, 100, 0, Dials()));
        Assert.Equal((0, 0), source.Measure(string.Empty, Dials()));
    }

    [Fact]
    public void THEINKISTHECOLOURTHATWASASKEDFOR_NotWhateverTheBrushDefaultedTo()
    {
        var source = new GdiPlusGlyphTextSource();
        var frame = source.Render("PINK", 0x0000FFFF, 320, 150, Dials());

        Assert.Equal(OperatingSystem.IsWindows(), frame is not null);
        if (frame is null)
        {
            return;
        }

        // Every fully-opaque ink point is the requested colour. The shadow is drawn at partial alpha
        // and the outline in black at alpha 255 - so this is asserted over the ink set the surface
        // itself would sample, which excludes black by construction.
        Assert.NotEmpty(frame.ProvableInk);
        foreach (var (x, y) in frame.ProvableInk)
        {
            Assert.Equal(0x0000FFFFu, frame.PremultipliedColourAt(x, y));
        }
    }
}
