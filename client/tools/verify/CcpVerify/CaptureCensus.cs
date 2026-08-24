namespace CcpVerify;

/// <summary>
/// The distinct-colour census of one capture, and with it the NON-VACUITY precondition every
/// named check silently depends on. An image carrying a single colour cannot be evidence that
/// anything was drawn, so the harness refuses it by name instead of reporting a green it did
/// not earn.
///
/// <para>Why this exists: <c>capture-wslg.sh</c> probed the door's real geometry
/// (<c>175.0x44.0 DIP @ scale 1 @ screen 12,45</c>), wrote a correctly-sized BMP and printed
/// <c>CAPTURE PASS</c> over 7,700 pixels of <c>(0,0,0)</c> — in three display configurations
/// (WSLg RAIL, XWayland, a real <c>Xvfb :99</c>). The tier-3 named checks caught it downstream
/// (<c>rail-door-selected-border 0/525 pixels matched</c>); it was the capture step's own PASS
/// that lied.</para>
///
/// <para>"Entirely the background" needs no second rule: an image that is entirely ANY one
/// colour — black, the window ground, the wallpaper — has exactly one distinct colour, and the
/// message names that colour so the reader can tell which case they are looking at.</para>
///
/// <para>Alpha is deliberately not part of a colour's identity. A capture transport that varies
/// alpha while painting nothing must not be able to manufacture a second "colour" and buy
/// itself a pass.</para>
/// </summary>
public readonly record struct CaptureCensus(int DistinctColors, int Pixels, (byte R, byte G, byte B) OnlyColor)
{
    /// <summary>Fewer than two distinct colours: nothing in this image distinguishes anything.</summary>
    public bool IsVacuous => DistinctColors < 2;

    /// <summary>Always carries the COUNT — a refusal that does not say what it counted sends its reader nowhere.</summary>
    public override string ToString() =>
        IsVacuous
            ? $"{DistinctColors} distinct colour — all {Pixels} pixels are "
              + $"RGB({OnlyColor.R},{OnlyColor.G},{OnlyColor.B}) #{OnlyColor.R:X2}{OnlyColor.G:X2}{OnlyColor.B:X2}; "
              + "an image with no second colour is not evidence that anything was drawn"
            : $"{DistinctColors} distinct colours across {Pixels} pixels";

    public static CaptureCensus Of(DecodedImage image)
    {
        var pixels = image.Width * image.Height;
        var seen = new HashSet<int>();
        for (var i = 0; i < pixels; i++)
        {
            var o = i * 4;
            seen.Add((image.Bgra[o + 2] << 16) | (image.Bgra[o + 1] << 8) | image.Bgra[o]);
        }

        return new CaptureCensus(seen.Count, pixels, image.PixelAt(0, 0));
    }
}
