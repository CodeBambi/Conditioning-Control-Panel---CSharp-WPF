namespace CcpClient.Desktop.Overlay;

/// <summary>One display as the operating system describes it, in physical pixels.</summary>
/// <param name="Bounds">The whole display.</param>
/// <param name="WorkArea">The display minus the taskbar and any other appbar.</param>
/// <param name="IsPrimary">Whether the OS calls this the primary display.</param>
/// <param name="DeviceName">The OS's own name for it, e.g. <c>\\.\DISPLAY1</c>.</param>
public readonly record struct OverlayDisplay(
    OverlayBounds Bounds,
    OverlayBounds WorkArea,
    bool IsPrimary,
    string DeviceName);

/// <summary>
/// Where an overlay surface may be placed, asked of the operating system.
///
/// <para><b>Why this exists in an overlay packet.</b> The shipping product places its overlays per
/// monitor, not on the primary one: <c>Services/Flash/FlashService.cs:2204-2245</c> enumerates
/// every screen with its own DPI scale, and the dual-monitor video path creates one fullscreen
/// window per screen. A capability that could only cover the primary display would be a smaller
/// outcome wearing the same name, so the enumeration lands with the surface rather than after
/// it.</para>
///
/// <para><b>What it does not do.</b> Report DPI. WPF carries a per-monitor scale because its
/// window positions are device-independent units and it has to convert (<c>:4141</c>); a Win32
/// top-level window's coordinates are already physical pixels. DPI becomes a real concern the
/// moment content is drawn on the surface, and nothing is drawn (SP-099 divergence D55).</para>
/// </summary>
public static class OverlayDisplays
{
    /// <summary>
    /// Every display the OS reports, primary first. Empty off Windows — this build enumerates
    /// displays through USER32 and does not guess elsewhere.
    /// </summary>
    public static IReadOnlyList<OverlayDisplay> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var displays = new List<OverlayDisplay>();

        bool OnMonitor(nint monitor, nint deviceContext, ref Win32OverlayInterop.Rect rect, nint data)
        {
            var info = new Win32OverlayInterop.MonitorInfoExW
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32OverlayInterop.MonitorInfoExW>(),
            };

            if (Win32OverlayInterop.GetMonitorInfoW(monitor, ref info))
            {
                displays.Add(new OverlayDisplay(
                    ToBounds(info.rcMonitor),
                    ToBounds(info.rcWork),
                    (info.dwFlags & Win32OverlayInterop.MonitorinfofPrimary) != 0,
                    info.szDevice ?? string.Empty));
            }

            return true;
        }

        // The delegate is kept in a local for the duration of the call: EnumDisplayMonitors is
        // synchronous, but a collected delegate under a native call is an access violation rather
        // than an exception, so it is rooted deliberately.
        Win32OverlayInterop.MonitorEnumProc callback = OnMonitor;

        Win32OverlayInterop.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);

        displays.Sort(static (left, right) => right.IsPrimary.CompareTo(left.IsPrimary));
        return displays;
    }

    /// <summary>
    /// A centred rectangle on the primary display, which is where a caller with nothing more
    /// specific to say should put a surface. Returns null when the OS reports no display at all.
    /// </summary>
    public static OverlayBounds? CentredOnPrimary(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        var displays = Enumerate();
        if (displays.Count == 0)
        {
            return null;
        }

        var primary = displays[0].Bounds;
        return new OverlayBounds(
            primary.X + ((primary.Width - width) / 2),
            primary.Y + ((primary.Height - height) / 2),
            width,
            height);
    }

    private static OverlayBounds ToBounds(Win32OverlayInterop.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
