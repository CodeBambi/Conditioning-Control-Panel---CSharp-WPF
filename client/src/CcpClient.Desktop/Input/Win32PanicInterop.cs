using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Input;

/// <summary>
/// The Windows mechanism for a system-wide emergency-stop chord: <c>RegisterHotKey</c> plus the
/// hidden owner window <c>WM_HOTKEY</c> is posted to. Windows-only; every caller guards on
/// <c>OperatingSystem.IsWindows()</c>, the convention <c>Tray/Win32TrayInterop.cs</c> and
/// <c>Overlay/Win32OverlayInterop.cs</c> already follow.
///
/// <para><b>Why a separate file rather than declarations on the class.</b> Every native window in
/// this tree is censused by re-deriving the site set from the shipping bytes
/// (<c>client/docs/window-behavior-manifest.md</c> §8.4), and that sweep recognises a creation site
/// by its <c>Type.CreateWindowExW(</c> shape. Declaring the import on the consuming class would put
/// a bare call in the tree that the census cannot classify — which it treats, correctly, as a hard
/// failure rather than a silent pass. So the port's one-interop-file-per-surface convention is not
/// decoration here: it is what keeps a new native window visible to the guard that counts them.</para>
///
/// <para><b>Why <c>RegisterHotKey</c> and not a keyboard hook</b> is a product decision and lives
/// with the product code, in <see cref="Win32PanicKey"/>.</para>
/// </summary>
internal static class Win32PanicInterop
{
    /// <summary>MOD_ALT.</summary>
    public const uint ModAlt = 0x0001;

    /// <summary>MOD_CONTROL.</summary>
    public const uint ModControl = 0x0002;

    /// <summary>MOD_NOREPEAT: one WM_HOTKEY per physical press, never an auto-repeat stream. A
    /// held-down emergency stop must not walk its own ladder to an exit.</summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>VK_ESCAPE — upstream's default panic key, kept as the chord's letter.</summary>
    public const uint VkEscape = 0x1B;

    /// <summary>The message the OS posts to the owner window when the chord is pressed.</summary>
    public const uint WmHotkey = 0x0312;

    /// <summary>WM_CLOSE — how <c>Dispose</c> asks the panic key's OWN thread to take its window
    /// down. A window belongs to the thread that created it, so no other thread may destroy it;
    /// posting is the only legal way to ask.</summary>
    public const uint WmClose = 0x0010;

    /// <summary>WM_DESTROY — the panic thread's cue to end its own message loop.</summary>
    public const uint WmDestroy = 0x0002;

    /// <summary>A real top-level popup, never shown. Deliberately not <c>HWND_MESSAGE</c>: a
    /// message-only window is outside the window manager, and this one is a hotkey target.</summary>
    public const uint WsPopup = 0x80000000;

    /// <summary>Off the taskbar and out of Alt+Tab. The window is never shown either, so this is
    /// belt and braces on a surface with no on-screen presence at all.</summary>
    public const uint WsExToolwindow = 0x00000080;

    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED — the one refusal worth naming to a reader, because
    /// it means another application on the machine holds the chord.</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WndClassExW wndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterClassW(string className, nint hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? moduleName);

    // ---------------------------------------------------------------------------------------
    //  The panic key's OWN message loop. Everything below exists so the chord is delivered on a
    //  thread of the panic key's own rather than on the UI thread, which is the thread a stalled
    //  app stops pumping — see Win32PanicKey's class documentation for the measurement.
    // ---------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    /// <summary>Blocks until a message arrives. Returns 0 on WM_QUIT and -1 on error, which is why
    /// the loop tests <c>&gt; 0</c> rather than truthiness.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetMessageW(out Msg message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DispatchMessageW(ref Msg message);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);
}
