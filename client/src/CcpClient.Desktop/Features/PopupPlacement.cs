using Avalonia;

namespace CcpClient.Desktop.Features;

/// <summary>
/// Owner-monitor working-area math for the feature popup (window-behavior-manifest §6
/// constraint 4: CenterOwner means the OWNER's monitor, never primary-by-default).
/// Pure functions, unit-tested without a display. <c>Screen.WorkingArea</c>/<c>Bounds</c>
/// arrive in PHYSICAL PIXELS (12.1.0 XML docs); <c>Screen.Scaling</c> converts to DIP.
/// </summary>
public static class PopupPlacement
{
    // WPF-parity constants, PENDING-OWNER (board open question: "what maximum popup height
    // fraction should become fixed acceptance constants" — NOT answered by this demonstrator).
    // WPF FeaturePopupWindow is a FIXED 520x640, min 420x360 (FeaturePopupWindow.xaml:5-6).
    public const double WpfParityHeightDip = 640.0;
    public const double WorkingAreaHeightFraction = 0.9;
    public const double DefaultWidthDip = 520.0;
    public const double MinWidthDip = 420.0;
    public const double MinHeightDip = 360.0;

    /// <summary>Popup height cap in DIP: the WPF fixed height, further capped to a fraction of the OWNER monitor's working area.</summary>
    public static double CapHeightDip(double workingAreaHeightPx, double scaling)
        => Math.Min(WpfParityHeightDip, WorkingAreaHeightFraction * workingAreaHeightPx / scaling);

    /// <summary>
    /// Fitted popup height: content desired height clamped into [MinHeight, cap] — short
    /// content stays compact, tall content caps inside the owner monitor's working area.
    /// </summary>
    public static double FitHeightDip(double desiredHeightDip, double workingAreaHeightPx, double scaling)
        => Math.Max(MinHeightDip, Math.Min(desiredHeightDip, CapHeightDip(workingAreaHeightPx, scaling)));

    /// <summary>Fitted popup width: WPF default clamped to the working area; below MinWidth only when the area itself is narrower.</summary>
    public static double FitWidthDip(double workingAreaWidthPx, double scaling)
    {
        var waDip = workingAreaWidthPx / scaling;
        return Math.Max(Math.Min(MinWidthDip, waDip), Math.Min(DefaultWidthDip, waDip));
    }

    /// <summary>
    /// Center the popup on the owner rect, clamped inside the owner screen's working area
    /// (a popup larger than the working area pins to the area origin). All physical pixels.
    /// </summary>
    public static PixelPoint CenteredClampedPosition(PixelRect ownerRect, PixelSize popupSize, PixelRect workingArea)
    {
        var maxX = Math.Max(workingArea.X, workingArea.Right - popupSize.Width);
        var maxY = Math.Max(workingArea.Y, workingArea.Bottom - popupSize.Height);
        var x = Math.Clamp(ownerRect.X + (ownerRect.Width - popupSize.Width) / 2, workingArea.X, maxX);
        var y = Math.Clamp(ownerRect.Y + (ownerRect.Height - popupSize.Height) / 2, workingArea.Y, maxY);
        return new PixelPoint(x, y);
    }
}
