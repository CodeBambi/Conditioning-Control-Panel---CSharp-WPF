using System.Runtime.InteropServices;
using System.Text;

namespace CcpClient.Desktop.Glyph;

/// <summary>
/// This capability's own P/Invokes.
///
/// <para><b>Why they are re-declared rather than shared with <c>Overlay/**</c>.</b> Two reasons,
/// and the second is the load-bearing one. (1) <c>Overlay/**</c> is closed to this packet, so a
/// shared surface would mean editing it. (2) The two capabilities drive <b>mutually exclusive</b>
/// Win32 mechanisms on a window — <c>SetLayeredWindowAttributes</c> there,
/// <c>UpdateLayeredWindow</c> here — and calling either one on the other's window is the exact
/// hazard the overlay measured. Keeping the declarations apart means there is no import through which
/// this file could reach the overlay's call, and no import through which the overlay could reach
/// this one. <b>Note what is deliberately ABSENT below: there is no
/// <c>SetLayeredWindowAttributes</c> declaration anywhere in this capability.</b></para>
/// </summary>
internal static class Win32GlyphInterop
{
    internal const uint WsPopup = 0x80000000;
    internal const uint WsExLayered = 0x00080000;
    internal const uint WsExTransparent = 0x00000020;
    internal const uint WsExToolwindow = 0x00000080;
    internal const uint WsExNoactivate = 0x08000000;
    internal const uint WsExTopmost = 0x00000008;

    internal const int GwlExstyle = -20;
    internal const uint GwHwndnext = 2;

    internal const nint HwndTopmost = -1;
    internal const uint SwpNosize = 0x0001;
    internal const uint SwpNomove = 0x0002;
    internal const uint SwpNoactivate = 0x0010;
    internal const uint SwpShowwindow = 0x0040;

    internal const int SwHide = 0;

    /// <summary><c>AC_SRC_OVER</c>: the only blend operation Win32 defines.</summary>
    internal const byte AcSrcOver = 0x00;

    /// <summary><c>AC_SRC_ALPHA</c>: the source bitmap carries per-pixel alpha, premultiplied.</summary>
    internal const byte AcSrcAlpha = 0x01;

    /// <summary><c>ULW_ALPHA</c>: use the blend function.</summary>
    internal const uint UlwAlpha = 0x00000002;

    /// <summary>
    /// <c>PW_RENDERFULLCONTENT</c>. The flag that makes <c>PrintWindow</c> ask DWM for the window's
    /// real visual instead of sending <c>WM_PRINT</c> — which is the only way to see a layered
    /// window's composited surface, since it has no client-area painting at all. Measured: with
    /// flags 0 the same call returns an all-black bitmap for a ULW window, because nothing ever
    /// painted into its device context.
    /// </summary>
    internal const uint PwRenderfullcontent = 0x00000002;

    internal const int BiRgb = 0;
    internal const uint DibRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary><c>BLENDFUNCTION</c>. Four bytes, packed: the struct is byte-aligned in the SDK
    /// header and a default managed layout would pad it.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] bmiColors;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClassExW
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

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;

        public nint hIconSm;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WndClassExW windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    /// <summary>
    /// The per-pixel-alpha composite. Note that <c>SetLayeredWindowAttributes</c> is NOT declared in
    /// this file: it is the overlay's mechanism, it is mutually exclusive with this one, and
    /// calling it on a window that has been composited this way permanently disables further
    /// composites (measured: every later call returns FALSE with last-error 87).
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UpdateLayeredWindow(
        nint window, nint destinationDc, ref Point destinationPoint, ref Size size,
        nint sourceDc, ref Point sourcePoint, uint colourKey, ref BlendFunction blend, uint flags);

    /// <summary>
    /// The MOVE overload: every pointer but the destination point is NULL, which is what the
    /// documented "shape and visual context are not changing" form requires. Measured: passing a
    /// size with a null source DC instead returns FALSE with last-error 31, so the two forms are
    /// declared separately rather than papered over with nullable parameters.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "UpdateLayeredWindow")]
    internal static extern bool UpdateLayeredWindowPosition(
        nint window, nint destinationDc, ref Point destinationPoint, nint size,
        nint sourceDc, nint sourcePoint, uint colourKey, nint blend, uint flags);

    /// <summary>
    /// Reads back what the operating system holds for a window. With
    /// <see cref="PwRenderfullcontent"/> this is DWM's own copy of the layered surface — the only
    /// question a process can ask that a ghost window answers differently from a composited one.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

    /// <summary>
    /// The uniform-alpha read-back. Declared for ONE purpose: to REFUSE. A window that answers this
    /// call is in <c>SetLayeredWindowAttributes</c> mode and can never be composited per pixel
    /// again, so the surface reports <see cref="GlyphReasonCodes.GlyphUniformAlphaMode"/> instead of
    /// trying. It is a read, never a write; the write is the call this file does not declare.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetLayeredWindowAttributes(
        nint window, out uint colourKey, out byte alpha, out uint flags);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern nint GetTopWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(nint window, StringBuilder buffer, int capacity);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateDIBSection(
        nint deviceContext, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint deviceContext, nint gdiObject);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint gdiObject);
}
