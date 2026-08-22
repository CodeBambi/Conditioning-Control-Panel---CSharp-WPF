namespace CcpClient.Desktop.Overlay;

/// <summary>
/// A rectangle on the desktop, in PHYSICAL pixels.
///
/// <para><b>Why physical pixels and not device-independent ones.</b> A Win32 top-level window's
/// coordinates are physical pixels, and this capability owns a Win32 top-level window with no
/// layout system above it. WPF's flash windows work in DIPs because WPF's window positions are
/// DIPs, so it converts each monitor's bounds by that monitor's own DPI scale
/// (<c>Services/Flash/FlashService.cs:2211-2223</c>, and it keeps the scale on
/// <c>MonitorInfo.DpiScale</c> at <c>:4141</c> precisely because the conversion has to be undone
/// later). There is nothing here to convert for: DPI becomes a real concern the moment content is
/// drawn, and nothing is drawn (divergence D55).</para>
/// </summary>
/// <param name="X">Left edge, in the virtual-screen coordinate space (may be negative on a
/// multi-monitor desktop where a display sits left of or above the primary).</param>
/// <param name="Y">Top edge, same space.</param>
/// <param name="Width">Width in pixels; must be positive.</param>
/// <param name="Height">Height in pixels; must be positive.</param>
public readonly record struct OverlayBounds(int X, int Y, int Width, int Height)
{
    /// <summary>The centre point, which is where the input hit test is asked.</summary>
    public (int X, int Y) Centre => (X + (Width / 2), Y + (Height / 2));

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>
/// What a caller wants on screen: where, how opaque, and whether clicks go through it.
///
/// <para><b>Why an invisible surface throws instead of being clamped or accepted.</b> Opacity zero
/// is the exact failure this capability exists to make impossible — a window that the OS agrees
/// exists, agrees is visible and agrees is on top, while the compositor draws nothing
/// (<see cref="OverlayReasonCodes.OverlayNotComposited"/>, measured on the first attempt's code).
/// Silently clamping it to something visible would put pixels on the user's screen that nobody
/// asked for; accepting it would ship the ghost. Neither is a capability STATE, because neither
/// describes the platform: it is a caller bug, so it is an exception at the boundary.</para>
/// </summary>
/// <param name="Bounds">Where the surface goes.</param>
/// <param name="Opacity">Uniform opacity in (0, 1]. Becomes the layered window's
/// <c>LWA_ALPHA</c> byte, which the OS is then asked to read back.</param>
/// <param name="ClickThrough">
/// True: the window manager must route a click inside the surface to whatever is underneath —
/// WPF's <c>WS_EX_TRANSPARENT</c> arm (<c>FlashService.cs:3668</c>).
/// False: the surface catches its own clicks — WPF's clickable-flash arm (<c>:3667</c>).
/// </param>
public sealed record OverlaySurfaceRequest(OverlayBounds Bounds, double Opacity, bool ClickThrough)
{
    /// <summary>The smallest alpha the OS will hold that is still "something is composited".</summary>
    public const byte MinimumAlpha = 1;

    public OverlayBounds Bounds { get; } = Validate(Bounds);

    public double Opacity { get; } = ValidateOpacity(Opacity);

    /// <summary>
    /// The requested opacity as the byte the OS is given and then asked for back. Rounded to
    /// nearest, floored at <see cref="MinimumAlpha"/> so a legal-but-tiny opacity can never
    /// round down into the invisible window this type refuses to construct.
    /// </summary>
    public byte Alpha
    {
        get
        {
            var scaled = (int)Math.Round(Opacity * 255.0, MidpointRounding.AwayFromZero);
            return (byte)Math.Clamp(scaled, MinimumAlpha, byte.MaxValue);
        }
    }

    private static OverlayBounds Validate(OverlayBounds bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Width, 0, nameof(bounds));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Height, 0, nameof(bounds));
        return bounds;
    }

    private static double ValidateOpacity(double opacity)
    {
        if (double.IsNaN(opacity))
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "overlay opacity must be a number in (0, 1]");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(opacity, 0.0, nameof(opacity));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opacity, 1.0, nameof(opacity));
        return opacity;
    }
}
