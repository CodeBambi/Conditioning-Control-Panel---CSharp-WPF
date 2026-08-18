using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Tray;

/// <summary>
/// The Windows notification-area surface: <c>Shell_NotifyIconW</c> plus the hidden owner window
/// it needs. Windows-only; every caller guards on <c>OperatingSystem.IsWindows()</c> (the same
/// convention as <c>Features/Chaos/ChaosTunnelWin32.cs:8</c>).
///
/// <para><b>Why this mechanism.</b> It is the one the shipping product uses
/// (<c>Services/Notifications/TrayIconService.cs:19,42</c> via WinForms <c>NotifyIcon</c>) and
/// the one Avalonia 12.1.1 itself uses on Windows — its <c>Avalonia.Win32.TrayIconImpl</c> and
/// <c>Avalonia.Win32.Interop.NOTIFYICONDATA</c> are the same call, verified in the packaged
/// assembly's metadata. It is reached directly here because Avalonia's public tray surface
/// cannot answer whether an icon exists (see <see cref="Win32TrayPresence"/>).</para>
/// </summary>
internal static class Win32TrayInterop
{
    // Shell_NotifyIcon messages.
    public const uint NimAdd = 0x00000000;
    public const uint NimModify = 0x00000001;
    public const uint NimDelete = 0x00000002;

    // NOTIFYICONDATA flags.
    public const uint NifMessage = 0x00000001;
    public const uint NifIcon = 0x00000002;
    public const uint NifTip = 0x00000004;

    // Window styles for the hidden owner: a real TOP-LEVEL popup that is never shown.
    // Deliberately NOT an HWND_MESSAGE child — a message-only window cannot receive the
    // "TaskbarCreated" broadcast, which is the only signal that an Explorer restart wiped
    // the icon and it must be re-added.
    public const uint WsPopup = 0x80000000;
    public const uint WsExToolwindow = 0x00000080;

    // The icon's callback message. WM_APP + 1: private to this window class, so it can never
    // collide with a system message.
    public const uint TrayCallbackMessage = 0x8000 + 1;

    public const uint WmLbuttonup = 0x0202;
    public const uint WmLbuttondblclk = 0x0203;

    public const int IdiApplication = 32512;

    /// <summary>Icon id inside the owner window. Unique per (hWnd, uID) pair, and each presence
    /// owns its own window, so a constant is correct.</summary>
    public const uint TrayIconId = 1;

    public delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

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

    /// <summary>
    /// The full (Vista+) NOTIFYICONDATAW. <c>Marshal.SizeOf</c> of this layout is 976 bytes on
    /// x64, which the shell accepts as the current version — measured on Windows 11 before this
    /// backend was written, together with the NIM_MODIFY existence semantics it relies on.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconDataW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WndClassExW cls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadIconW(nint instance, nint name);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessageW(string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Shell_NotifyIconW(uint message, ref NotifyIconDataW data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconExW(string file, int index, out nint large, out nint small, uint count);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? name);
}
