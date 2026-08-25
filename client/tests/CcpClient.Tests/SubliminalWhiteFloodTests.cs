using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The white-screen hypothesis, tested rather than argued.</b>
///
/// <para>The owner saw a full white screen twice during a session, unreproduced. One surviving
/// candidate is this module: the subliminal card is FULL-MONITOR, its outline is white
/// (<c>#FFFFFF</c>, <c>CCP.Core/Models/AppSettings.cs:1380</c>), and that outline is drawn as EIGHT
/// offset copies of the phrase UNDER the main text
/// (<c>Services/Subliminal/SubliminalService.cs:990-1008</c>). If any text-metric failure could make
/// those eight copies cover the card, the user would see a white screen for the length of one
/// envelope — and a card that is on screen for 200 ms is exactly the kind of event that is seen and
/// never reproduced.</para>
///
/// <para><b>How it is tested.</b> <see cref="GdiPlusSubliminalFrameSource.Render"/> needs no window
/// at all, so every candidate input is driven straight through it and the produced frame's pixels
/// are counted. Nothing here opens a surface, and nothing here waits.</para>
///
/// <para><b>What the measurement is.</b> NEAR-WHITE, not exactly-white: the rasteriser antialiases
/// (<c>TextRenderingHintAntiAlias</c>), so a genuinely white-flooded card would still hold a fringe
/// of blend pixels, and an exact-colour count would understate a flood. A pixel is near-white when
/// its dimmest channel is at least <see cref="NearWhiteFloor"/>.</para>
/// </summary>
public class SubliminalWhiteFloodTests
{
    /// <summary>The dimmest channel a pixel may have and still count as near-white. 230 of 255 —
    /// well above the magenta text (whose green channel is 0) and above any blend that is more text
    /// than outline, so this counts outline pixels and their halo and nothing else.</summary>
    public const int NearWhiteFloor = 230;

    /// <summary>"Mostly white" for the purposes of the owner's report: half the card or more. A
    /// full-monitor card at this fraction is a white screen to anyone looking at it.</summary>
    public const double FloodFraction = 0.5;

    private readonly ITestOutputHelper _output;

    public SubliminalWhiteFloodTests(ITestOutputHelper output) => _output = output;

    // ---------------------------------------------------------------------------------
    //  the sweep
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// <b>The answer.</b> Every adversarial input this module can be handed — empty, whitespace,
    /// enormous, unmappable, and every card geometry from 1×1 to 4K — produces a card that is not
    /// mostly white. The exact near-white fraction of each is written to the test output so the
    /// negative result is a MEASUREMENT and not an opinion.
    /// </summary>
    [Fact]
    public void NoAdversarialPhraseOrCardGeometry_FloodsTheCardWhite()
    {
        var run = SubliminalFloodProbe.Run;

        // The sweep cannot be silently empty: on a build with GDI+ every geometry rasters, and on a
        // build without one none does. Either way this compares a MACHINE property against a
        // PRODUCT property rather than skipping.
        Assert.Equal(GdiPlusRuntime.Available, run.Rendered.Count > 0);

        var flooded = new List<string>();
        foreach (var card in run.Rendered)
        {
            _output.WriteLine(
                $"{card.Label,-28} {card.Width}x{card.Height}  near-white {card.NearWhiteFraction:P3}  "
                + $"background {card.BackgroundFraction:P3}  text {card.TextFraction:P3}");
            if (card.NearWhiteFraction >= FloodFraction)
            {
                flooded.Add($"{card.Label} ({card.Width}x{card.Height}): {card.NearWhiteFraction:P2} near-white");
            }
        }

        Assert.Empty(flooded);
    }

    /// <summary>
    /// The negative above would be worthless if the probe could not SEE white, so the same counter
    /// is pointed at a card whose outline colour is the whole palette. This is the positive control:
    /// it proves the measurement detects a flood when there is one to detect.
    /// </summary>
    [Fact]
    public void TheSameCounter_DoesReportAFlood_WhenTheCardReallyIsWhite()
    {
        var run = SubliminalFloodProbe.Run;
        _output.WriteLine($"all-white control: {run.WhiteControlFraction:P3}");

        Assert.Equal(GdiPlusRuntime.Available, run.WhiteControlFraction >= FloodFraction);
    }

    /// <summary>
    /// The shipped card, for contrast: overwhelmingly the BACKGROUND colour, with the phrase and its
    /// outline occupying a few percent. This is the number the flood hypothesis would have had to
    /// overturn.
    /// </summary>
    [Fact]
    public void TheShippedCard_IsOverwhelminglyBackground_WithOnlyAFringeOfOutline()
    {
        var run = SubliminalFloodProbe.Run;
        var shipped = run.Rendered.SingleOrDefault(c => c.Label == SubliminalFloodProbe.ShippedLabel);

        Assert.Equal(GdiPlusRuntime.Available, shipped is not null);
        Assert.Equal(GdiPlusRuntime.Available, shipped is { BackgroundFraction: > 0.9, NearWhiteFraction: < 0.05 });
    }

    /// <summary>
    /// <b>The card CAN flood — and the colour it floods in is the TEXT colour, never the outline
    /// colour.</b> A phrase of solid block glyphs on twelve lines covers a 1080p card almost
    /// entirely: measured here, over 90 % of the card is the phrase's own colour and under 1 % of it
    /// is near-white. That is the mechanism the white-screen hypothesis needed and the reason it
    /// still fails — the eight outline copies are drawn UNDER the phrase
    /// (<c>Services/Subliminal/SubliminalService.cs:996-1008</c>), so the phrase paints over all but
    /// a three-pixel fringe of them however much ink there is.
    ///
    /// <para><b>Why this is worth pinning rather than just observing.</b> It says the flood is real
    /// and its colour is a SETTING. With upstream's shipped magenta a dense phrase gives a
    /// full-screen magenta card; the same phrase with the text colour set to white would give a
    /// white one. That is a path to the reported symptom, but it needs the user to have chosen white
    /// text — it is not something the module can do on its own.</para>
    /// </summary>
    [Fact]
    public void TheCardCanFloodOnADensePhrase_ButInTheTextColour_NotTheOutlineColour()
    {
        var run = SubliminalFloodProbe.Run;
        var dense = run.Rendered.SingleOrDefault(c => c.Label == SubliminalFloodProbe.DenseLabel);
        _output.WriteLine($"dense phrase: {dense?.TextFraction:P3} text, {dense?.NearWhiteFraction:P3} near-white");

        Assert.Equal(GdiPlusRuntime.Available, dense is { TextFraction: > FloodFraction });
        Assert.Equal(GdiPlusRuntime.Available, dense is { NearWhiteFraction: < 0.05 });
    }

    // ---------------------------------------------------------------------------------
    //  the two inputs that produce NO card at all, which is the safe outcome
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// A phrase the font cannot load at all: the rasteriser gives back <c>null</c>, which the
    /// presenter counts as an unrasterised phrase and shows nothing. <b>A failed font is a missing
    /// card, never a blank one</b> — the partially-drawn buffer is discarded rather than blitted, so
    /// this candidate cannot produce a white screen either.
    /// </summary>
    [Fact]
    public void AFontThisMachineCannotLoad_ProducesNoCardAtAll_NotABlankOne()
    {
        Assert.Null(SubliminalFloodProbe.Run.MissingFontFrame);
    }

    /// <summary>
    /// An empty phrase is refused at the door — <c>ArgumentException</c> from
    /// <c>ArgumentException.ThrowIfNullOrEmpty</c>, on the CALLER's thread, before any buffer is
    /// allocated. Recorded because "empty phrase" was one of the named candidates and its real
    /// behaviour is neither a white card nor a silent null: the pool never draws one
    /// (<see cref="SubliminalPhrasePool"/> returns null instead), so the throw is unreachable in the
    /// product, and the fact exists so that stays true.
    /// </summary>
    [Fact]
    public void AnEmptyPhrase_IsRefusedBeforeAnythingIsAllocated()
    {
        var source = new GdiPlusSubliminalFrameSource();
        Assert.Throws<ArgumentException>(() => source.Render("", 320, 200, SubliminalPalette.Default));
    }
}

/// <summary>
/// The one white-flood measurement this assembly takes, hoisted out of the facts for the reason
/// <see cref="SubliminalCardObservations"/> gives: the rasteriser is Windows-only, so every fact can
/// compare a machine property against a product property with no platform branch of its own.
/// </summary>
public sealed class SubliminalFloodProbe
{
    /// <summary>U+2588 FULL BLOCK: the densest glyph in a common font, and the worst case for
    /// ink coverage. Named rather than typed so the source carries no invisible characters.</summary>
    public const char FullBlock = '█';

    /// <summary>The shipped card: the real default palette, a real phrase, a real 1080p monitor.</summary>
    public const string ShippedLabel = "shipped 1080p card";

    /// <summary>The densest phrase a user can put in the pool: solid block glyphs on enough lines
    /// to cover a 1080p card at the fixed 120 px size.</summary>
    public const string DenseLabel = "full blocks, twelve lines";

    /// <summary>A font family no machine has. GDI+ answers <c>FontFamilyNotFound</c> and the draw
    /// abandons the buffer.</summary>
    public const string MissingFont = "CCP No Such Font Family";

    private static readonly Lazy<SubliminalFloodProbe> Lazy =
        new(Measure, LazyThreadSafetyMode.ExecutionAndPublication);

    private SubliminalFloodProbe(
        IReadOnlyList<Card> rendered, double whiteControlFraction, OverlayFrame? missingFontFrame)
    {
        Rendered = rendered;
        WhiteControlFraction = whiteControlFraction;
        MissingFontFrame = missingFontFrame;
    }

    /// <summary>Every input that produced pixels, with those pixels already counted.</summary>
    public IReadOnlyList<Card> Rendered { get; }

    /// <summary>The positive control: a card whose background, text and outline are ALL white.</summary>
    public double WhiteControlFraction { get; }

    /// <summary>What the rasteriser returned when its font family did not exist.</summary>
    public OverlayFrame? MissingFontFrame { get; }

    public static SubliminalFloodProbe Run => Lazy.Value;

    /// <summary>One measured card: what it was asked to draw, and what came back.</summary>
    /// <param name="Label">The input, for the report.</param>
    /// <param name="Width">Card width in pixels.</param>
    /// <param name="Height">Card height in pixels.</param>
    /// <param name="NearWhiteFraction">Share of pixels whose dimmest channel is ≥ the near-white floor.</param>
    /// <param name="BackgroundFraction">Share of pixels still exactly the background colour.</param>
    /// <param name="TextFraction">Share of pixels exactly the phrase's own colour.</param>
    public sealed record Card(
        string Label, int Width, int Height,
        double NearWhiteFraction, double BackgroundFraction, double TextFraction);

    private static SubliminalFloodProbe Measure()
    {
        var source = new GdiPlusSubliminalFrameSource();
        var palette = SubliminalPalette.Default;

        // A full monitor, because that is the card's real size (SubliminalService.cs:1046 sizes the
        // window to the screen's own bounds); then the geometries either side of it.
        var inputs = new List<(string Label, string Text, int Width, int Height)>
        {
            (ShippedLabel, "GOOD GIRL", 1920, 1080),
            ("whitespace only", "   ", 1920, 1080),
            ("a single space", " ", 1920, 1080),
            ("500 wide glyphs", new string('W', 500), 1920, 1080),
            ("5000 wide glyphs", new string('W', 5000), 1920, 1080),
            // Private-use code points: no font on any machine maps these, so GDI+ falls back to
            // whatever it uses for a missing glyph. Built rather than typed so the source carries
            // no invisible characters.
            ("unmappable private use", new string((char)0xE000, 40), 1920, 1080),
            ("unpaired surrogate", "\uD800\uD800\uD800", 1920, 1080),
            // The densest phrase a user can actually type into the pool: solid block glyphs, on
            // enough lines to cover a 1080p card at the fixed 120 px size.
            ("full blocks, twelve lines", BlockGrid(rows: 12, columns: 40), 1920, 1080),
            ("full blocks, one line", new string(FullBlock, 40), 1920, 1080),
            ("1x1 card", "GOOD GIRL", 1, 1),
            ("2x2 card", "GOOD GIRL", 2, 2),
            ("4x4 card", "GOOD GIRL", 4, 4),
            ("8x8 card, blocks", new string(FullBlock, 4), 8, 8),
            ("64x64 card", "GOOD GIRL", 64, 64),
            ("120x120 card, one block", FullBlock.ToString(), 120, 120),
            ("4K card", "GOOD GIRL", 3840, 2160),
        };

        var rendered = new List<Card>();
        foreach (var (label, text, width, height) in inputs)
        {
            var frame = source.Render(text, width, height, palette);
            if (frame is not null)
            {
                rendered.Add(Count(label, frame, palette.BackgroundArgb, palette.TextArgb));
            }
        }

        // Positive control: every colour in the palette is white, so a successful raster MUST come
        // back flooded. If this does not, the counter is broken and every negative above is void.
        var allWhite = new SubliminalPalette(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF);
        var control = source.Render("GOOD GIRL", 640, 360, allWhite);
        var controlFraction = control is null
            ? 0
            : Count("all white", control, allWhite.BackgroundArgb, allWhite.TextArgb).NearWhiteFraction;

        var missingFont = new GdiPlusSubliminalFrameSource(MissingFont)
            .Render("GOOD GIRL", 1920, 1080, palette);

        return new SubliminalFloodProbe(rendered, controlFraction, missingFont);
    }

    /// <summary>Rows of solid block glyphs separated by newlines — GDI+ lays these out as real
    /// lines, so this is the largest amount of ink a single phrase can put on the card.</summary>
    private static string BlockGrid(int rows, int columns) =>
        string.Join('\n', Enumerable.Repeat(new string('█', columns), rows));

    private static Card Count(string label, OverlayFrame frame, uint backgroundArgb, uint textArgb)
    {
        var background = backgroundArgb & 0x00FFFFFF;
        var text = textArgb & 0x00FFFFFF;
        long nearWhite = 0;
        long isBackground = 0;
        long isText = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var colour = frame.ColourAt(x, y);
                if (colour == background)
                {
                    isBackground++;
                }

                if (colour == text)
                {
                    isText++;
                }

                // COLORREF is 0x00BBGGRR: the dimmest channel decides how close to white it is.
                var blue = (int)((colour >> 16) & 0xFF);
                var green = (int)((colour >> 8) & 0xFF);
                var red = (int)(colour & 0xFF);
                if (Math.Min(red, Math.Min(green, blue)) >= SubliminalWhiteFloodTests.NearWhiteFloor)
                {
                    nearWhite++;
                }
            }
        }

        double total = (long)frame.Width * frame.Height;
        return new Card(
            label, frame.Width, frame.Height, nearWhite / total, isBackground / total, isText / total);
    }
}
