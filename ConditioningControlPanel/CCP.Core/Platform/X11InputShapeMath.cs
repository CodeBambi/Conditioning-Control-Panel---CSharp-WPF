using System;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Pure coordinate math for converting capture-region rectangles into X11
/// <c>XRectangle</c>-compatible values. XRectangle fields are <c>short</c> (x, y) and
/// <c>ushort</c> (width, height) — see docs/linux-overlay-contract.md §3.3: rects must be
/// clamped to ±32767 before narrowing because multi-monitor virtual desktops can exceed the
/// short range on exotic layouts. Kept in CCP.Core so the logic is unit-testable without X11.
/// </summary>
public static class X11InputShapeMath
{
    /// <summary>Minimum XRectangle coordinate (short.MinValue).</summary>
    public const int MinCoordinate = short.MinValue;

    /// <summary>Maximum XRectangle coordinate (short.MaxValue).</summary>
    public const int MaxCoordinate = short.MaxValue;

    /// <summary>Maximum XRectangle extent (ushort.MaxValue).</summary>
    public const int MaxExtent = ushort.MaxValue;

    /// <summary>
    /// Clamps a window-local physical-pixel rectangle into the XRectangle value domain.
    /// Non-finite inputs and non-positive sizes collapse to a zero-area rect (callers skip
    /// those). The right/bottom edges are clamped first so a rect that starts in range but
    /// extends past +32767 is clipped rather than wrapped.
    /// </summary>
    public static (int X, int Y, int Width, int Height) ClampRect(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return (0, 0, 0, 0);
        }

        if (width <= 0 || height <= 0)
        {
            return (0, 0, 0, 0);
        }

        long left = ClampCoord(Math.Round(x));
        long top = ClampCoord(Math.Round(y));
        long right = ClampCoord(Math.Round(x + width));
        long bottom = ClampCoord(Math.Round(y + height));

        long clampedWidth = Math.Clamp(right - left, 0, MaxExtent);
        long clampedHeight = Math.Clamp(bottom - top, 0, MaxExtent);

        return ((int)left, (int)top, (int)clampedWidth, (int)clampedHeight);
    }

    private static long ClampCoord(double value)
    {
        if (value < MinCoordinate) return MinCoordinate;
        if (value > MaxCoordinate) return MaxCoordinate;
        return (long)value;
    }
}
