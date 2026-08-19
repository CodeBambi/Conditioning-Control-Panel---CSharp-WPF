using CcpClient.Desktop.Overlay;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// Where a bounded, centred rectangle goes on the primary display, in physical pixels.
///
/// <para><b>Why this file exists, and it is SP-101's finding arriving a second time.</b> SP-110 gave
/// <c>LockCardEffect</c> a private <c>DefaultPlacement</c>; SP-111 gave
/// <see cref="VideoSurfacePresenter"/> its own private copy of the same twenty lines. Both walk
/// <see cref="OverlayDisplays.Enumerate"/>, both pick the primary display (falling back to index 0),
/// both take a fraction of it and both centre it. SP-112 was about to write the THIRD copy, which is
/// exactly the shape SP-101 refused for the effect template: a pattern that holds while three of its
/// parts are per-module copies is a pattern nobody is really sharing.</para>
///
/// <para><b>What is deliberately NOT shared: the fractions, and what an empty enumeration means.</b>
/// Each caller keeps its own <c>WidthFraction</c>/<c>HeightFraction</c> — those are per-module
/// divergences with their own D-records (D110 for the card, D123 for the video surface) — and each
/// caller decides what "no display at all" means for it, because the two answers are genuinely
/// different and both are correct. A video surface returns NULL and plays nothing (its module's dot
/// and arm result both read the capability's display answer); a card returns a minimum legal
/// rectangle so the request boundary stays a refusal rather than an exception. Collapsing those two
/// into one policy would be sharing the part that is not shared.</para>
///
/// <para>The display enumeration is the overlay capability's and is CONSUMED, never edited
/// (<c>Overlay/OverlayDisplays.cs</c>).</para>
/// </summary>
public static class PrimaryDisplayPlacement
{
    /// <summary>
    /// The primary display's bounds, or null when the operating system reports no display at all.
    /// Primary first, falling back to the first display the OS listed — which is what all three
    /// callers' own copies did.
    /// </summary>
    public static OverlayBounds? PrimaryBounds()
    {
        var displays = OverlayDisplays.Enumerate();
        if (displays.Count == 0)
        {
            return null;
        }

        var chosen = 0;
        for (var i = 0; i < displays.Count; i++)
        {
            if (displays[i].IsPrimary)
            {
                chosen = i;
                break;
            }
        }

        return displays[chosen].Bounds;
    }

    /// <summary>
    /// A rectangle of <paramref name="widthFraction"/> x <paramref name="heightFraction"/> of
    /// <paramref name="display"/>, centred on it, in the display's own physical pixels. Each side is
    /// floored at one pixel, because a zero-width rectangle is an exception at every request boundary
    /// in the port rather than a small window.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Centred(
        OverlayBounds display, double widthFraction, double heightFraction)
    {
        var width = Math.Max(1, (int)(display.Width * widthFraction));
        var height = Math.Max(1, (int)(display.Height * heightFraction));
        return (
            display.X + ((display.Width - width) / 2),
            display.Y + ((display.Height - height) / 2),
            width,
            height);
    }
}
