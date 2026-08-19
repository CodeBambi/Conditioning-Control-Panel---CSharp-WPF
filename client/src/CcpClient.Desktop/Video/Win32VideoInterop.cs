using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Video;

/// <summary>
/// The Windows video-surface mechanism: a layered, NON-ACTIVATING top-level window in the topmost
/// band, filled by GDI and then asked about. Windows-only; every caller guards on
/// <c>OperatingSystem.IsWindows()</c> — the convention <c>Tray/Win32TrayInterop.cs:7</c>,
/// <c>Overlay/Win32OverlayInterop.cs</c> and <c>Input/Win32InputInterop.cs</c> already follow.
///
/// <para><b>Why a raw hwnd and not an Avalonia window.</b> The same reason the overlay and the card
/// each own one: Avalonia can put a window on screen, and it exposes nothing that can answer "is the
/// operating system holding THIS picture on that surface right now". Owning the hwnd is what makes
/// the OS answerable, and answerability is the entire content of this capability — a surface showing
/// a frozen frame looks identical from inside the process to one playing a film.</para>
///
/// <para><b>The extended styles, and the two that are deliberate divergences.</b>
/// <see cref="WsExLayered"/> is set for the same reason the overlay sets it: it gives the OS a
/// uniform alpha to hold for the window and it is the shape a <c>CAPTUREBLT</c> screen read can
/// see, which is what makes the harness's composited-desktop leg possible at all.
/// <see cref="WsExNoactivate"/> is set — upstream's mandatory-video window ACTIVATES
/// (<c>Services/Video/VideoService.cs:2621</c>, <c>ShowActivated = withAudio</c>) — because a video
/// surface that took the foreground would fight Lock Card for it and could take it away from a
/// question the user is being asked. <see cref="WsExTransparent"/> is deliberately NOT set: this is
/// not a click-through overlay, and a video the pointer falls straight through would be a lie about
/// what is in front of the user.</para>
/// </summary>
internal static class Win32VideoInterop
{
    /// <summary>A real top-level window with no frame, caption or menu — WPF reaches the same shape
    /// with <c>WindowStyle.None</c> (<c>Services/Video/VideoService.cs:2618</c>).</summary>
    public const uint WsPopup = 0x80000000;

    /// <summary>Composited by the OS at one uniform alpha.</summary>
    public const uint WsExLayered = 0x00080000;

    /// <summary>Off the taskbar and out of Alt+Tab. Upstream's video window sets
    /// <c>ShowInTaskbar = false</c> (<c>VideoService.cs:2620</c>).</summary>
    public const uint WsExToolwindow = 0x00000080;

    /// <summary>Never takes the foreground. See the type remarks — this is a divergence and it is
    /// deliberate.</summary>
    public const uint WsExNoactivate = 0x08000000;

    /// <summary>Set by the OS when a window is in the topmost band. Read back, never trusted on its
    /// own — the z-order walk is the ordering fact.</summary>
    public const uint WsExTopmost = 0x00000008;

    public const int GwlExstyle = -20;

    public static readonly nint HwndTopmost = -1;

    public const uint SwpNoactivate = 0x0010;
    public const uint SwpShowwindow = 0x0040;
    public const uint SwpNosize = 0x0001;
    public const uint SwpNomove = 0x0002;

    public const int SwHide = 0;

    public const uint GwHwndnext = 2;

    public const int SmCmonitors = 80;

    /// <summary><c>LWA_ALPHA</c>: the window's uniform opacity. Upstream's video window is opaque
    /// (<c>Background = Brushes.Black</c>, <c>VideoService.cs:2622</c>), so this is always 255 —
    /// but it is SET rather than assumed, because a layered window with no attributes is not drawn
    /// at all.</summary>
    public const uint LwaAlpha = 0x00000002;

    /// <summary><c>UOI_FLAGS</c>: the window station's own flags, which carry
    /// <see cref="WsfVisible"/>.</summary>
    public const int UoiFlags = 1;

    /// <summary><c>WSF_VISIBLE</c>: this window station can show something to a human. A service in
    /// session 0 runs on one without it.</summary>
    public const int WsfVisible = 0x0001;

    /// <summary><c>SRCCOPY</c>.</summary>
    public const uint Srccopy = 0x00CC0020;

    /// <summary><c>DIB_RGB_COLORS</c>.</summary>
    public const uint DibRgbColors = 0;

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

    /// <summary><c>USEROBJECTFLAGS</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UserObjectFlags
    {
        public int fInherit;
        public int fReserved;
        public int dwFlags;
    }

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

    public delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

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

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    public static extern nint GetTopWindow(nint parent);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetProcessWindowStation();

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    public static extern bool GetUserObjectInformationW(
        nint handle, int index, out UserObjectFlags info, int length, out int needed);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? name);

    // ---- painting; GDI, for the same reason the overlay's content path is GDI ---------------

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint window, nint dc);

    [DllImport("user32.dll")]
    public static extern int FillRect(nint dc, ref Rect rect, nint brush);

    [DllImport("gdi32.dll")]
    public static extern nint CreateSolidBrush(uint colour);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(nint dc, int x, int y);

    [DllImport("gdi32.dll")]
    public static extern int StretchDIBits(
        nint dc, int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight,
        nint bits, ref BitmapInfoHeader info, uint usage, uint rop);

    [DllImport("gdi32.dll")]
    public static extern int SetStretchBltMode(nint dc, int mode);

    /// <summary><c>HALFTONE</c>: the best of GDI's stretch modes for a shrunk photograph. WPF's own
    /// video path scales in the compositor; GDI's default <c>COLORONCOLOR</c> drops rows, which
    /// makes a downscaled picture visibly wrong.</summary>
    public const int Halftone = 4;

    [DllImport("dwmapi.dll")]
    public static extern int DwmIsCompositionEnabled(out bool enabled);
}
