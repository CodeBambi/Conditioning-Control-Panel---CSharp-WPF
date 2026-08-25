using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;

namespace CcpClient.Tests;

/// <summary>
/// One rasterised subliminal card, measured ONCE per test run, with every property a fact needs
/// already reduced to a value.
///
/// <para><b>Why the measurement is hoisted out of the facts</b> (the
/// <see cref="FlashDrawObservations"/> shape): the rasteriser is Windows-only, and a fact
/// that branched on the platform would be a silencing shape the vacuous-shape ledger has to carry.
/// Reducing the whole card to booleans first means every fact can compare a MACHINE property —
/// "does this build have GDI+" — against a product property, with no conditional and no skip, and
/// with both branches asserting something real. On Linux the expected answer is that the card does
/// not raster at all and nothing throws.</para>
/// </summary>
public sealed record SubliminalCardObservations(
    bool RasteriserAvailable,
    bool Rendered,
    int Width,
    int Height,
    bool CornersAreTheBackgroundColour,
    bool CarriesTheTextColour,
    bool CarriesTheOutlineColour,
    bool EmptySizeRefused,
    bool SurvivedAPhraseLongerThanTheCard)
{
    /// <summary>WPF's own card size at 1080p, which is the whole monitor (<c>SubliminalService.cs:1046</c>).</summary>
    public const int CardWidth = 640;

    /// <summary>Deliberately shorter than the 120 px glyphs are tall is NOT the case here: the card
    /// must be big enough that centred text really lands inside it, or "no text pixel" would be a
    /// measurement of the layout rather than of the rasteriser.</summary>
    public const int CardHeight = 360;

    /// <summary>The phrase the measurement uses. Short, upper-case and from WPF's own shipped pool
    /// (<c>CCP.Core/Models/AppSettings.cs:1295</c>).</summary>
    public const string Phrase = "GOOD GIRL";

    private static readonly Lazy<SubliminalCardObservations> Lazy = new(Measure, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The one measurement this assembly takes.</summary>
    public static SubliminalCardObservations Run => Lazy.Value;

    private static SubliminalCardObservations Measure()
    {
        var source = new GdiPlusSubliminalFrameSource();
        var available = GdiPlusRuntime.Available;
        var frame = source.Render(Phrase, CardWidth, CardHeight, SubliminalPalette.Default);

        // A zero-width card is a caller bug, not a raster failure: it must come back null rather
        // than throwing on a timer thread or allocating a zero-byte buffer the surface would blit.
        var emptyRefused = source.Render(Phrase, 0, CardHeight, SubliminalPalette.Default) is null
            && source.Render(Phrase, CardWidth, 0, SubliminalPalette.Default) is null;

        // A phrase far wider than the card: WPF measures with infinite width and lets it overflow
        // (SubliminalService.cs:778), so this must still produce a card rather than failing.
        var longPhrase = source.Render(new string('W', 200), CardWidth, CardHeight, SubliminalPalette.Default);

        if (frame is null)
        {
            return new SubliminalCardObservations(
                available, Rendered: false, 0, 0,
                CornersAreTheBackgroundColour: false,
                CarriesTheTextColour: false,
                CarriesTheOutlineColour: false,
                emptyRefused,
                SurvivedAPhraseLongerThanTheCard: longPhrase is null && !available);
        }

        return new SubliminalCardObservations(
            available,
            Rendered: true,
            frame.Width,
            frame.Height,
            CornersAreBackground(frame),
            CountColour(frame, SubliminalPalette.DefaultTextArgb) > 0,
            CountColour(frame, SubliminalPalette.DefaultOutlineArgb) > 0,
            emptyRefused,
            SurvivedAPhraseLongerThanTheCard: longPhrase is not null);
    }

    /// <summary>All four corners hold the background: the card is opaque edge to edge, which is what
    /// <c>SubBackgroundTransparent = false</c> ships as (<c>AppSettings.cs:1359</c>) and what stops
    /// the desktop showing through a surface with one uniform alpha.</summary>
    private static bool CornersAreBackground(OverlayFrame frame)
    {
        var background = SubliminalPalette.DefaultBackgroundArgb & 0x00FFFFFF;
        return frame.ColourAt(0, 0) == background
            && frame.ColourAt(frame.Width - 1, 0) == background
            && frame.ColourAt(0, frame.Height - 1) == background
            && frame.ColourAt(frame.Width - 1, frame.Height - 1) == background;
    }

    private static int CountColour(OverlayFrame frame, uint argb)
    {
        var wanted = argb & 0x00FFFFFF;
        var count = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (frame.ColourAt(x, y) == wanted)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
