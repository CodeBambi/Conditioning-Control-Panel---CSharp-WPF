using System.Runtime.InteropServices;

namespace CcpClient.Tests;

/// <summary>
/// SP-093's independent effect instrument: it asks the Windows SHELL whether a notification-area
/// icon exists, without asking the product.
///
/// <para><b>Why it re-declares every P/Invoke instead of using the product's interop.</b> This is
/// the packet's named trap — a tray capability is trivial to fake because every failure mode is
/// invisible. A test that measured placement through the same code that performs it could be
/// fooled by one edit to that code. These declarations are deliberately a second, independent
/// copy, so "the product says it placed an icon" and "the shell holds an icon" are two different
/// facts produced by two different code paths.</para>
///
/// <para><b>Why NIM_MODIFY is a valid oracle.</b> <c>Shell_NotifyIcon(NIM_MODIFY)</c> succeeds
/// only for an <c>(hWnd, uID)</c> pair the notification area actually holds. Measured on this
/// machine (Windows 11) before any of this was written, with both negative controls holding:
/// FALSE before NIM_ADD, TRUE after it, FALSE for a never-added id, TRUE for NIM_DELETE itself,
/// FALSE after the delete. <see cref="RunNegativeControl"/> re-runs the never-added leg on every
/// suite run, so an oracle that degenerated into "always true" fails the suite instead of
/// silently certifying everything.</para>
///
/// <para>Every native path is guarded by a platform check HERE, in the helper, so the fact bodies
/// stay free of predicates and none of their assertions can be silenced.</para>
/// </summary>
internal static class TrayShellProbe
{
    private const uint NimModify = 0x00000001;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolwindow = 0x00000080;
    private const uint PmRemove = 0x0001;
    private const int IdiApplication = 32512;

    /// <summary>An id this probe never adds anywhere — the oracle's negative control.</summary>
    internal const uint NeverAddedIconId = 4242;

    /// <summary>Hard ceiling on the pump loop. A message queue that never drains is a defect, and
    /// this bounds it without a wall-clock wait (none is permitted in this suite).</summary>
    private const int MaxPumpIterations = 512;

    internal static bool WindowsHost => OperatingSystem.IsWindows();

    /// <summary>
    /// Does this machine have a notification area at all? <c>Shell_TrayWnd</c> is the shell's
    /// taskbar window; without it there is nowhere for an icon to go, and the honest expectation
    /// flips. This is a property of the machine and the session, established by the test rather
    /// than taken from the product.
    /// </summary>
    internal static bool MachineHasNotificationArea =>
        WindowsHost && FindWindowW("Shell_TrayWnd", null) != 0;

    /// <summary>The shell's own answer to "do you hold this icon".</summary>
    internal static bool ShellHoldsIcon(nint ownerWindow, uint iconId)
    {
        if (!WindowsHost || ownerWindow == 0)
        {
            return false;
        }

        var data = BuildProbeData(ownerWindow, iconId);
        return Shell_NotifyIconW(NimModify, ref data);
    }

    internal static bool WindowStillExists(nint window) => WindowsHost && window != 0 && IsWindow(window);

    /// <summary>
    /// Posts the exact notification the shell posts when the user left-clicks the icon, then
    /// drains the owner window's queue so its WndProc runs on this thread. Proves the ROUTING
    /// (shell notification to application event); it does not prove a human click, which needs a
    /// headed run.
    /// </summary>
    internal static void PostSyntheticLeftClick(nint ownerWindow, uint callbackMessage, uint iconId)
    {
        if (!WindowsHost || ownerWindow == 0)
        {
            return;
        }

        const uint wmLbuttonup = 0x0202;
        PostMessageW(ownerWindow, callbackMessage, (nint)iconId, (nint)wmLbuttonup);
        DrainQueue(ownerWindow);
    }

    private static void DrainQueue(nint window)
    {
        for (var i = 0; i < MaxPumpIterations; i++)
        {
            if (!PeekMessageW(out var msg, window, 0, 0, PmRemove))
            {
                return;
            }

            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    /// <summary>
    /// The instrument's self-check: a scratch owner window, an id that was never added, and the
    /// oracle asked about it. Also proves this probe's own window teardown really destroys a
    /// window, so <see cref="WindowStillExists"/> is known to be able to say "no".
    /// </summary>
    internal static NegativeControl RunNegativeControl()
    {
        if (!WindowsHost)
        {
            return new NegativeControl(false, false, false, false);
        }

        var scratch = ScratchOwnerWindow.Create();
        var handle = scratch.Handle;
        var created = handle != 0;
        var holdsNeverAdded = ShellHoldsIcon(handle, NeverAddedIconId);
        scratch.Dispose();

        return new NegativeControl(
            MachineHasNotificationArea: MachineHasNotificationArea,
            OwnerWindowCreated: created,
            ShellHoldsNeverAddedIcon: holdsNeverAdded,
            OwnerWindowExistsAfterCleanup: WindowStillExists(handle));
    }

    private static NotifyIconDataW BuildProbeData(nint ownerWindow, uint iconId) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconDataW>(),
        hWnd = ownerWindow,
        uID = iconId,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = 0x8000 + 1,
        hIcon = LoadIconW(0, IdiApplication),
        szTip = "ccp tray effect probe",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <param name="MachineHasNotificationArea">Whether this session has a shell taskbar.</param>
    /// <param name="OwnerWindowCreated">Whether the scratch owner window came up.</param>
    /// <param name="ShellHoldsNeverAddedIcon">Must be false, or the oracle is broken.</param>
    /// <param name="OwnerWindowExistsAfterCleanup">Must be false, or teardown is broken.</param>
    internal sealed record NegativeControl(
        bool MachineHasNotificationArea,
        bool OwnerWindowCreated,
        bool ShellHoldsNeverAddedIcon,
        bool OwnerWindowExistsAfterCleanup);

    /// <summary>A throwaway hidden top-level window this probe owns, for oracle self-checks.</summary>
    internal sealed class ScratchOwnerWindow : IDisposable
    {
        private readonly string _className;
        private readonly WndProc _proc;
        private readonly nint _module;
        private bool _disposed;

        private ScratchOwnerWindow(string className, WndProc proc, nint module, nint handle)
        {
            _className = className;
            _proc = proc;
            _module = module;
            Handle = handle;
        }

        internal nint Handle { get; private set; }

        internal static ScratchOwnerWindow Create()
        {
            var className = "CcpTrayProbeOwner." + Guid.NewGuid().ToString("N");
            WndProc proc = DefWindowProcW;
            var module = GetModuleHandleW(null);
            var cls = new WndClassExW
            {
                cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                hInstance = module,
                lpszClassName = className,
            };

            var handle = RegisterClassExW(ref cls) == 0
                ? 0
                : CreateWindowExW(WsExToolwindow, className, "ccp tray probe owner", WsPopup,
                    0, 0, 0, 0, 0, 0, module, 0);
            return new ScratchOwnerWindow(className, proc, module, handle);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!WindowsHost)
            {
                return;
            }

            if (Handle != 0)
            {
                DestroyWindow(Handle);
                Handle = 0;
            }

            UnregisterClassW(_className, _module);
            GC.KeepAlive(_proc);
        }
    }

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassExW
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconDataW
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassExW cls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIconW(nint instance, nint name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out Msg msg, nint hWnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessageW(ref Msg msg);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconDataW data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
}
