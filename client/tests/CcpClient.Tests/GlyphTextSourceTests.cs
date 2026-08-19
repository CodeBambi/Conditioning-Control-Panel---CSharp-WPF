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
/// single most dangerous line in this packet, and nothing was watching it. The sweep also found a
/// real defect here: see <c>TryRepairRounding</c>.</para>
///
/// <para>No window is involved: the rasteriser produces a buffer, and every claim here is about
/// those bytes. Every fact is conditioned on <see cref="GdiPlusRuntime.Available"/> — the runtime's
/// own answer, asked once at the top — rather than on a platform predicate of the test's, so a build
/// with no rasteriser reports the absence instead of passing quietly, and there is no second opinion
/// about which platform this is.</para>
/// </summary>
public class GlyphTextSourceTests
{
    private static BouncingTextPresentation Dials(bool outline = false, int size = 100) =>
        new(true, 5, size, 100, BouncingTextColourMode.Random, null, outline, ["OBEY"]);

    private static GlyphFrame? Render(string text = "OBEY", uint colour = 0x00FF00FF,
        int width = 300, int height = 140, bool outline = false) =>
        new GdiPlusGlyphTextSource().Render(text, colour, width, height, Dials(outline));

    [Fact]
    public void THISBUILDHASARASTERISER_OrEveryFactBelowIsAboutNothing()
    {
        // The positive control: without it, "the frame is transparent at the margin" would be
        // equally true of no frame at all. Asked of the rasteriser itself rather than of the OS, so
        // this file holds no independent opinion about the platform that could drift from the
        // product's.
        var measured = new GdiPlusGlyphTextSource().Measure("OBEY", Dials());

        Assert.Equal(GdiPlusRuntime.Available, measured.Width > 0);
        Assert.Equal(GdiPlusRuntime.Available, measured.Height > 0);
        Assert.Equal(GdiPlusRuntime.Available, Render() is not null);
    }

    [Fact]
    public void THEBACKGROUNDISFULLYTRANSPARENT_WhichIsTheONELineThatMakesThisModulePossible()
    {
        // An opaque clear here is exactly D83: at the shipped default opacity of 100 the module
        // would put a black rectangle with a word on it in front of the user, which is worse than
        // the absent module. The margin is transparent by construction, so it is where this is read.
        var frame = Render();
        var transparentCorners = frame is not null
            && frame.AlphaAt(1, 1) == 0
            && frame.AlphaAt(frame.Width - 2, 1) == 0
            && frame.AlphaAt(1, frame.Height - 2) == 0
            && frame.AlphaAt(frame.Width - 2, frame.Height - 2) == 0
            && frame.PremultipliedColourAt(1, 1) == 0;

        Assert.Equal(GdiPlusRuntime.Available, transparentCorners);
    }

    [Fact]
    public void THERASTERCARRIESPROVABLEINK_OrTheSurfaceCouldNotConfirmItAtAll()
    {
        // Without a fully-opaque non-black pixel the surface could not distinguish this frame from a
        // window that composites nothing, and would refuse it.
        Assert.Equal(GdiPlusRuntime.Available, Render() is { HasProvableInk: true });
    }

    [Fact]
    public void ANDTHEBUFFERISPREMULTIPLIED_EveryChannelAtOrBelowItsOwnAlpha()
    {
        // GlyphFrame's constructor throws on any channel above its own alpha, so a frame coming back
        // at all is already the proof - but "the constructor did not throw" reads as an absence, so
        // it is re-asserted over the whole buffer. The mutation this catches is the pixel format
        // being changed to straight ARGB.
        var frame = Render("SUBMIT", 0x00FFFFFF, 320, 150);
        var offenders = 0;
        if (frame is not null)
        {
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    var alpha = frame.AlphaAt(x, y);
                    var colour = frame.PremultipliedColourAt(x, y);
                    if (((colour >> 16) & 0xFF) > alpha || ((colour >> 8) & 0xFF) > alpha
                        || (colour & 0xFF) > alpha)
                    {
                        offenders++;
                    }
                }
            }
        }

        Assert.Equal(GdiPlusRuntime.Available, frame is not null);
        Assert.Equal(0, offenders);
    }

    [Fact]
    public void THEMEASUREINCLUDESTHEMARGIN_SoAnAntialiasedEdgeIsNeverClipped()
    {
        // The margin is not decoration: it is what guarantees the frame has fully-transparent pixels
        // at known positions, which is what the surface's read-back samples to catch an opaque plate
        // composited where the glyph should have holes.
        var source = new GdiPlusGlyphTextSource();
        var measured = source.Measure("OBEY", Dials());
        var frame = measured.Width > 0
            ? source.Render("OBEY", 0x00FF00FF, measured.Width, measured.Height, Dials())
            : null;

        var marginHolds = measured.Width > 2 * GdiPlusGlyphTextSource.Margin
            && measured.Height > 2 * GdiPlusGlyphTextSource.Margin
            && frame is not null
            && frame.AlphaAt(2, measured.Height / 2) == 0
            && frame.AlphaAt(measured.Width - 3, measured.Height / 2) == 0;

        Assert.Equal(GdiPlusRuntime.Available, marginHolds);
    }

    [Fact]
    public void ABIGGERSIZEDIALREALLYPRODUCESABIGGERWORD()
    {
        var source = new GdiPlusGlyphTextSource();
        var small = source.Measure("OBEY", Dials(size: 50));
        var large = source.Measure("OBEY", Dials(size: 300));

        Assert.Equal(GdiPlusRuntime.Available, large.Width > small.Width && large.Height > small.Height);
    }

    [Fact]
    public void THEOUTLINESTYLEProducesADifferentRasterFromTheShadowStyle()
    {
        var shadow = Render("DROP");
        var outline = Render("DROP", outline: true);
        var differs = shadow is not null && outline is not null
            && !shadow.Pixels.AsSpan().SequenceEqual(outline.Pixels);

        Assert.Equal(GdiPlusRuntime.Available, differs);
    }

    [Fact]
    public void ANEMPTYWORDOrAZeroSizedFrameRendersNOTHINGRatherThanThrowing()
    {
        // True on every machine class, so this one is conditioned on nothing at all.
        var source = new GdiPlusGlyphTextSource();

        Assert.Null(source.Render(string.Empty, 0x00FF00FF, 100, 40, Dials()));
        Assert.Null(source.Render("OBEY", 0x00FF00FF, 0, 40, Dials()));
        Assert.Null(source.Render("OBEY", 0x00FF00FF, 100, 0, Dials()));
        Assert.Equal((0, 0), source.Measure(string.Empty, Dials()));
    }

    [Fact]
    public void THEINKISTHECOLOURTHATWASASKEDFOR_NotWhateverTheBrushDefaultedTo()
    {
        // Asserted over the ink set the SURFACE itself would sample, which excludes black by
        // construction - so the shadow (partial alpha) and the outline (black at alpha 255) are out
        // of it by definition and only the fill's own colour is left.
        var frame = Render("PINK", 0x0000FFFF, 320, 150);
        var everyInkPointIsTheFill = frame is { HasProvableInk: true };
        if (frame is not null)
        {
            foreach (var (x, y) in frame.ProvableInk)
            {
                everyInkPointIsTheFill &= frame.PremultipliedColourAt(x, y) == 0x0000FFFFu;
            }
        }

        Assert.Equal(GdiPlusRuntime.Available, everyInkPointIsTheFill);
    }
}
