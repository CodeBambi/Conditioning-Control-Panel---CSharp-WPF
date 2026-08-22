using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Overlay;

/// <summary>
/// The Windows overlay mechanism: a layered, click-through, no-activate, no-Alt-Tab top-level
/// window, placed with <c>SetWindowPos(HWND_TOPMOST)</c>. Windows-only; every caller guards on
/// <c>OperatingSystem.IsWindows()</c> (the convention <c>Tray/Win32TrayInterop.cs:7</c> and
/// <c>Features/Chaos/ChaosTunnelWin32.cs:8</c> already follow).
///
/// <para><b>Why this mechanism and not an Avalonia window.</b> The shipping product's overlay is
/// exactly this set of extended styles on a real hwnd — <c>WS_EX_LAYERED | WS_EX_NOACTIVATE</c>
/// plus <c>WS_EX_TRANSPARENT</c> for click-through (<c>Services/Flash/FlashService.cs:3666</c>,
/// <c>:3667-3668</c>), <c>WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c> to stay out of Alt+Tab
/// (<c>:3826</c>), and <c>SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE)</c> to stay on top
/// (<c>:3867</c>). Avalonia's <c>Window.Topmost</c> reaches the same <c>SetWindowPos</c> but
/// exposes nothing that can answer "is it really on top and really click-through", and its
/// click-through has no cross-platform surface at all. The first attempt subclassed
/// <c>Avalonia.Controls.Window</c> for its overlay
/// (<c>CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:14</c>), which is why the only overlay
/// verification that tree has is a harness that asks a human what they saw
/// (<c>tests/CCP.Avalonia.Desktop.Windows.Smoke/VisibleOverlayVerification.cs</c>) and exits 0
/// regardless. Owning the hwnd is what makes the OS answerable.</para>
/// </summary>
internal static class Win32OverlayInterop
{
    // Window styles. WS_POPUP: a real top-level window with no frame, no caption, no menu -
    // WPF reaches the same shape with WindowStyle.None + AllowsTransparency (FlashService.cs:3613-3614).
    public const uint WsPopup = 0x80000000;

    /// <summary>Composited by the OS from an alpha the window supplies. Without an alpha it is
    /// composited from nothing (OverlayReasonCodes.OverlayNotComposited).</summary>
    public const uint WsExLayered = 0x00080000;

    /// <summary>The click-through flag. Documented under "Layered Windows": with it set the
    /// window's shape is ignored for hit testing and mouse events go to the windows underneath.</summary>
    public const uint WsExTransparent = 0x00000020;

    /// <summary>Out of Alt+Tab and off the taskbar (WPF: FlashService.cs:3826, and
    /// ShowInTaskbar = false at :3616).</summary>
    public const uint WsExToolwindow = 0x00000080;

    /// <summary>Never take the foreground when shown (WPF: FlashService.cs:3666, :3826, and
    /// ShowActivated = false at :3617).</summary>
    public const uint WsExNoactivate = 0x08000000;

    /// <summary>Set by the OS when a window is in the topmost band. Read back, never trusted on
    /// its own - the z-order walk is the ordering fact.</summary>
    public const uint WsExTopmost = 0x00000008;

    public const int GwlExstyle = -20;

    public const uint LwaAlpha = 0x00000002;

    public static readonly nint HwndTopmost = -1;

    public const uint SwpNosize = 0x0001;
    public const uint SwpNomove = 0x0002;
    public const uint SwpNoactivate = 0x0010;
    public const uint SwpShowwindow = 0x0040;

    public const int SwHide = 0;

    public const uint GwHwndnext = 2;

    /// <summary>The window is asked to repaint. Serviced from the retained frame so an overlay
    /// that the OS invalidates does not go blank.</summary>
    public const uint WmPaint = 0x000F;

    /// <summary>Plain copy. The only raster op this capability uses: a frame replaces the
    /// surface, it is never blended into it (blending is the window's uniform LWA_ALPHA).</summary>
    public const int Srccopy = 0x00CC0020;

    /// <summary>Uncompressed, no palette.</summary>
    public const int BiRgb = 0;

    public const uint DibRgbColors = 0;

    public const int SmCmonitors = 80;
    public const int SmCxscreen = 0;
    public const int SmCyscreen = 1;

    public const uint MonitorinfofPrimary = 0x00000001;

    public delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    public delegate bool MonitorEnumProc(nint monitor, nint dc, ref Rect rect, nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassExW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>A 32bpp top-down <c>BI_RGB</c> DIB header. The <c>biHeight</c> a caller supplies is
    /// NEGATIVE: a top-down section means the frame's first row is the surface's first row, so no
    /// row flip is needed anywhere between an image decoder and the screen.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    /// <summary>Header plus the three colour masks the struct reserves; unused at
    /// <c>BI_RGB</c> but part of the layout the API reads.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public uint bmiColors0;
        public uint bmiColors1;
        public uint bmiColors2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaintStruct
    {
        public nint hdc;
        public int fErase;
        public Rect rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoExW
    {
        public uint cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WndClassExW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetLayeredWindowAttributes(nint window, out uint colorKey, out byte alpha, out uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    public static extern nint GetTopWindow(nint parent);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint window, uint command);

    /// <summary>GW_OWNER: a window owned by a non-topmost owner cannot hold the topmost band.</summary>
    public const uint GwOwner = 4;

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassNameW(nint window, System.Text.StringBuilder buffer, int max);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoExW info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? name);

    // ---- the content mechanism ----------------------------------------------------
    // GDI, deliberately: the surface is composited from a uniform LWA_ALPHA, which is exactly the
    // mode in which ordinary painting into the window's own DC reaches the screen. The alternative
    // (UpdateLayeredWindow) is mutually exclusive with SetLayeredWindowAttributes and would take
    // GetLayeredWindowAttributes — the ghost check this capability is built around — away.

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint window, nint dc);

    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint window, out PaintStruct paint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(nint window, ref PaintStruct paint);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint dc, nint gdiObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern nint CreateDIBSection(
        nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool BitBlt(
        nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int rop);
}
