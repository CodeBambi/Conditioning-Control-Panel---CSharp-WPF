namespace CcpClient.Desktop.Glyph;

/// <summary>
/// A rectangle on the desktop, in PHYSICAL pixels — the same space and for the same reason as
/// <see cref="Overlay.OverlayBounds"/>: a Win32 top-level window's coordinates are physical pixels
/// and there is no layout system above this capability to convert for.
/// </summary>
/// <param name="X">Left edge in the virtual-screen coordinate space.</param>
/// <param name="Y">Top edge, same space.</param>
/// <param name="Width">Width in pixels; must be positive.</param>
/// <param name="Height">Height in pixels; must be positive.</param>
public readonly record struct GlyphBounds(int X, int Y, int Width, int Height)
{
    /// <summary>The centre point, which is where the input hit test is asked.</summary>
    public (int X, int Y) Centre => (X + (Width / 2), Y + (Height / 2));

    /// <summary>True when this rectangle shares any pixel with <paramref name="other"/>. Used by
    /// the surface's own occlusion arbitration, which decides who owns a sample point from the
    /// OS's z-order and these rectangles rather than assuming disjointness.</summary>
    public bool Intersects(GlyphBounds other) =>
        X < other.X + other.Width && X + Width > other.X
        && Y < other.Y + other.Height && Y + Height > other.Y;

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>
/// What a caller wants composited: where, at what uniform multiplier over the frame's own per-pixel
/// alpha, and whether clicks go through it.
///
/// <para><b>Why the opacity is a MULTIPLIER and not baked into the frame.</b> WPF's bouncing text
/// sets <c>Opacity</c> on the text element and leaves the glyph's own antialiased alpha alone
/// (<c>Services/Subliminal/BouncingTextService.cs:975</c>, <c>:983</c>); the two multiply. This
/// capability keeps that structure because it is also what keeps the surface honest: the frame's
/// fully-opaque ink stays at alpha 255 whatever the dial says, so the read-back that proves the
/// composite reached the operating system has a fixed anchor at every opacity. Measured:
/// <c>PrintWindow</c> reports the frame unchanged while <c>SourceConstantAlpha</c> is 128, i.e. the
/// dial lives in the compositor and not in the surface's stored bytes.</para>
///
/// <para><b>Why zero opacity throws.</b> Same reason as the overlay's
/// (<see cref="Overlay.OverlaySurfaceRequest"/>): a surface that the OS agrees exists, agrees is
/// visible and agrees is on top while compositing nothing is the exact failure this capability
/// exists to make impossible. It is a caller bug, so it is an exception at the boundary rather than
/// a platform state.</para>
/// </summary>
/// <param name="Bounds">Where the surface goes.</param>
/// <param name="Opacity">Uniform multiplier in (0, 1] over the frame's own per-pixel alpha.</param>
/// <param name="ClickThrough">True: the window manager must route a click inside the surface to
/// whatever is underneath. Bouncing glyphs are decoration and never catch a click, so the product
/// always asks for true; false exists because the SURFACE's own input fact is asked in both
/// polarities and a one-polarity capability could not be proven at all.</param>
public sealed record GlyphSurfaceRequest(GlyphBounds Bounds, double Opacity, bool ClickThrough)
{
    /// <summary>The smallest multiplier the OS will hold that is still "something is composited".</summary>
    public const byte MinimumConstantAlpha = 1;

    public GlyphBounds Bounds { get; } = Validate(Bounds);

    public double Opacity { get; } = ValidateOpacity(Opacity);

    /// <summary>
    /// The requested opacity as the <c>BLENDFUNCTION.SourceConstantAlpha</c> byte. Rounded to
    /// nearest, floored at <see cref="MinimumConstantAlpha"/> so a legal-but-tiny opacity can never
    /// round down into the invisible surface this type refuses to construct.
    /// </summary>
    public byte ConstantAlpha
    {
        get
        {
            var scaled = (int)Math.Round(Opacity * 255.0, MidpointRounding.AwayFromZero);
            return (byte)Math.Clamp(scaled, MinimumConstantAlpha, byte.MaxValue);
        }
    }

    private static GlyphBounds Validate(GlyphBounds bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Width, 0, nameof(bounds));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Height, 0, nameof(bounds));
        return bounds;
    }

    private static double ValidateOpacity(double opacity)
    {
        if (double.IsNaN(opacity))
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "glyph opacity must be a number in (0, 1]");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(opacity, 0.0, nameof(opacity));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opacity, 1.0, nameof(opacity));
        return opacity;
    }
}
