using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Input;

/// <summary>
/// The Windows input-capture mechanism: an ordinary activatable top-level window, placed topmost,
/// handed the foreground, and then asked about. Windows-only; every caller guards on
/// <c>OperatingSystem.IsWindows()</c> — the convention <c>Tray/Win32TrayInterop.cs:7</c>,
/// <c>Overlay/Win32OverlayInterop.cs</c> and <c>Features/Chaos/ChaosTunnelWin32.cs:8</c> already
/// follow.
///
/// <para><b>Why a raw hwnd and not an Avalonia window.</b> The same reason the overlay owns one:
/// Avalonia can put a window on screen and can even ask for activation, but it exposes nothing that
/// can answer "is the operating system routing the keyboard here". Owning the hwnd is what makes
/// the OS answerable — and answerability is the entire content of this capability, because a card
/// that is up and un-focused looks identical from inside the process to one the user is typing
/// into.</para>
///
/// <para><b>The deliberately absent extended styles.</b> This window must NOT carry
/// <c>WS_EX_NOACTIVATE</c> (it exists to activate) and must NOT carry <c>WS_EX_TRANSPARENT</c> (it
/// exists to catch input). Those two are exactly what the overlay sets
/// (<c>Overlay/Win32OverlayInterop.cs</c>), which is the whole difference between the two
/// capabilities expressed in two flags.</para>
/// </summary>
internal static class Win32InputInterop
{
    /// <summary>A real top-level window with no frame, caption or menu — WPF reaches the same shape
    /// with <c>WindowStyle.None</c> (<c>Windows/LockCardWindow.xaml:6</c>).</summary>
    public const uint WsPopup = 0x80000000;

    /// <summary>Off the taskbar and out of Alt+Tab. Upstream's card is a single fullscreen cover per
    /// monitor and does not want a taskbar button either.</summary>
    public const uint WsExToolwindow = 0x00000080;

    /// <summary>Set by the OS when a window is in the topmost band. Read back, never trusted on its
    /// own — the z-order walk is the ordering fact.</summary>
    public const uint WsExTopmost = 0x00000008;

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
    public const uint WmKeydown = 0x0100;
    public const uint WmSyskeydown = 0x0104;
    public const uint WmChar = 0x0102;
    public const uint WmClose = 0x0010;

    public const uint PmRemove = 0x0001;

    public const int VkBack = 0x08;
    public const int VkEscape = 0x1B;

    /// <summary><c>UOI_FLAGS</c>: the window station's own flags, which carry
    /// <see cref="WsfVisible"/>.</summary>
    public const int UoiFlags = 1;

    /// <summary><c>WSF_VISIBLE</c>: this window station can show something to a human. A service in
    /// session 0 runs on one without it.</summary>
    public const int WsfVisible = 0x0001;

    /// <summary>Opaque background fill. <c>TRANSPARENT</c> is 1; the card paints its own
    /// background first, so text is drawn transparently over it.</summary>
    public const int BkModeTransparent = 1;

    public const uint DtCenter = 0x00000001;
    public const uint DtVcenter = 0x00000004;
    public const uint DtSinglelineFormat = 0x00000020;
    public const uint DtWordbreak = 0x00000010;
    public const uint DtEndEllipsis = 0x00008000;

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

    /// <summary>
    /// <c>GUITHREADINFO</c>. Asked with thread id <b>0</b>, which the API documents as "the
    /// foreground thread" — the system-wide focus, not this thread's.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public uint cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public Rect rcCaret;
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
    public static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    public static extern nint SetActiveWindow(nint window);

    [DllImport("user32.dll")]
    public static extern nint SetFocus(nint window);

    /// <summary>
    /// The escalation's one non-obvious call. Attaching this thread's input queue to the current
    /// foreground thread's is what makes <see cref="SetForegroundWindow"/> succeed from a process
    /// that does not already own the foreground — measured, and measured to be NECESSARY (SP-110
    /// plan.md §0: plain <c>SetForegroundWindow</c> returned FALSE, the attach-then-ask returned
    /// TRUE). Always detached immediately; never held across anything.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AttachThreadInput(uint attach, uint attachTo, bool attaching);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

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

    // ---- painting; GDI, for the same reason the overlay's content path is GDI ---------------

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(nint dc, string text, int count, ref Rect rect, uint format);

    [DllImport("gdi32.dll")]
    public static extern nint CreateSolidBrush(uint colour);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(nint dc, int mode);

    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(nint dc, uint colour);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(nint dc, int x, int y);
}
