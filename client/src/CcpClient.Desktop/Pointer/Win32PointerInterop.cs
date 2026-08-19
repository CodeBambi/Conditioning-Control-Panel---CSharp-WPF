using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Pointer;

/// <summary>
/// The Windows mechanism for a clickable, non-activating top-level window. Windows-only; every
/// caller guards on <c>OperatingSystem.IsWindows()</c>, the convention
/// <c>Overlay/Win32OverlayInterop.cs</c> and <c>Input/Win32InputInterop.cs</c> already follow.
///
/// <para><b>The three declarations that make this file different from its two siblings.</b>
/// <c>Input/Win32InputInterop.cs:54-58</c> declares five message constants and NO mouse message at
/// all — SP-112's census, verified again here — and <c>Overlay/Win32OverlayInterop.cs</c> declares
/// none either, because a click-through surface's whole job is never to see one. This file declares
/// <see cref="WmLbuttondown"/>, <see cref="WmLbuttonup"/> and <see cref="WmMouseactivate"/>, and
/// those three are the capability.</para>
///
/// <para><b>The extended styles, and whose they are.</b> <see cref="WsExNoactivate"/> and
/// <see cref="WsExToolwindow"/> are exactly what upstream Bubble Pop puts on every bubble window —
/// <c>flags = exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c>
/// (<c>Services/BubbleService.cs:4887</c>, constants at <c>:4899-4900</c>). Upstream also sets
/// <see cref="WsExTransparent"/> on a bubble that must NOT be clickable, and its own comment is the
/// clearest statement of why this capability reads the style back rather than remembering it:
/// <i>"Non-clickable bubbles must be truly click-through at the Win32 level; WPF's
/// IsHitTestVisible alone doesn't prevent the window from eating clicks"</i> (<c>:4889-4890</c>),
/// and, for the recycled shell that kept the flag, <i>"a now-clickable bubble stuck click-through
/// (unpoppable)"</i> (<c>:4880-4884</c>).</para>
/// </summary>
internal static class Win32PointerInterop
{
    /// <summary>A real top-level window with no frame, caption or menu.</summary>
    public const uint WsPopup = 0x80000000;

    /// <summary>Off the taskbar and out of Alt+Tab — upstream's <c>HideFromAltTab</c>
    /// (<c>Services/BubbleService.cs:4877</c>).</summary>
    public const uint WsExToolwindow = 0x00000080;

    /// <summary>Set by the OS when a window is in the topmost band. Read back, never trusted on its
    /// own: the z-order walk is the ordering fact.</summary>
    public const uint WsExTopmost = 0x00000008;

    /// <summary><b>The flag this capability is built on.</b> A window carrying it is not activated
    /// by a click on it (<c>Services/BubbleService.cs:4899</c>).</summary>
    public const uint WsExNoactivate = 0x08000000;

    /// <summary>Click-through. The overlay capability's flag, and the one a pointer target must NOT
    /// have (<c>Services/BubbleService.cs:4900</c>, applied at <c>:4891-4892</c>).</summary>
    public const uint WsExTransparent = 0x00000020;

    public const int GwlExstyle = -20;

    public static readonly nint HwndTopmost = -1;

    public const uint SwpNosize = 0x0001;
    public const uint SwpNomove = 0x0002;
    public const uint SwpNoactivate = 0x0010;
    public const uint SwpShowwindow = 0x0040;

    public const int SwHide = 0;

    public const uint GwHwndnext = 2;

    public const int SmCmonitors = 80;

    public const uint WmPaint = 0x000F;
    public const uint WmClose = 0x0010;

    /// <summary><c>WM_MOUSEACTIVATE</c>. Answered <see cref="MaNoactivate"/>, which is the same
    /// answer and the same value upstream gives on its sibling surface
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1823-1824</c>, hook at <c>:1831-1839</c>).</summary>
    public const uint WmMouseactivate = 0x0021;

    public const uint WmLbuttondown = 0x0201;
    public const uint WmLbuttonup = 0x0202;

    /// <summary>"Do not activate the window, and do not discard the mouse message." The other
    /// discard-shaped answers exist and are wrong here: a bubble must SEE the click it refuses to
    /// be activated by.</summary>
    public const nint MaNoactivate = 3;

    public const uint PmRemove = 0x0001;

    /// <summary><c>UOI_FLAGS</c>.</summary>
    public const int UoiFlags = 1;

    /// <summary><c>WSF_VISIBLE</c>: this window station can show something to a human.</summary>
    public const int WsfVisible = 0x0001;

    public delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

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

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public Point pt;
        public uint lPrivate;
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
    public struct PaintStruct
    {
        public nint hdc;
        public int fErase;
        public Rect rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
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

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    public static extern nint GetTopWindow(nint parent);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassNameW(nint window, System.Text.StringBuilder buffer, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PeekMessageW(out Msg msg, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetThreadDesktop(uint threadId);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    public static extern bool GetUserObjectInformationW(
        nint handle, int index, out UserObjectFlags info, int length, out int needed);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? name);

    // ---- painting; GDI, for the same reason the overlay's and the card's content paths are -----

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint window, nint dc);

    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint window, out PaintStruct paint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(nint window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    public static extern int FillRect(nint dc, ref Rect rect, nint brush);

    [DllImport("gdi32.dll")]
    public static extern nint CreateSolidBrush(uint colour);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint dc, nint gdiObject);

    [DllImport("gdi32.dll")]
    public static extern bool Ellipse(nint dc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern nint GetStockObject(int index);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(nint dc, int x, int y);

    /// <summary><c>NULL_PEN</c>: the disc is filled and not outlined, so the fill colour is the only
    /// thing the ink read-back can find.</summary>
    public const int NullPen = 8;
}
