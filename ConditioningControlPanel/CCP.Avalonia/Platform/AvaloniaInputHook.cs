using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Low-level input hook implementation.
/// On Windows it installs a WH_KEYBOARD_LL hook so panic keys and hotkeys work in the
/// Avalonia head. Mouse activity for idle detection is NOT a hook: the only consumer
/// (AvaloniaAutonomyService) needs an activity timestamp, so <see cref="MouseMoved"/> is
/// driven by a cheap GetLastInputInfo poll (WPF parity: ActivityTracker polls
/// GetLastInputInfo and never installs a mouse hook). The previous app-lifetime
/// WH_MOUSE_LL routed EVERY system-wide mouse move (up to 1000Hz on gaming mice) through
/// this process's UI thread purely to write a timestamp — desktop pointer latency held
/// hostage to our pump, and a busy pump risked the OS silently unhooking us.
/// On Linux/macOS/mobile it degrades gracefully to a no-op because global hooks require
/// platform-native interop that is not available in the shared Avalonia project.
/// </summary>
public sealed class AvaloniaInputHook : IInputHook
{
    // Poll cadence for GetLastInputInfo. Idle thresholds are minutes, so 2s is precise
    // enough and effectively free.
    private const int IdlePollMs = 2000;

    private readonly ILogger<AvaloniaInputHook>? _logger;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private HookProc? _keyboardProc;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private Timer? _idlePollTimer;
    private uint _lastInputTick;

    public AvaloniaInputHook(ILogger<AvaloniaInputHook>? logger = null)
    {
        _logger = logger;
        Start();
    }

    public event EventHandler<KeyboardHookEventArgs>? KeyPressed;

    /// <summary>
    /// User-activity signal (idle detection). Raised from a threadpool timer, at most once
    /// per poll interval, whenever GetLastInputInfo reports fresh input (mouse OR keyboard —
    /// both mean "not idle"). Coordinates are the current physical cursor position; consumers
    /// use this event as a timestamp, not a movement stream.
    /// </summary>
    public event EventHandler<MouseHookEventArgs>? MouseMoved;

    public bool CanSuppressKeys => false;

    public bool SuppressKey(int virtualKeyCode) => false;

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Unhook(ref _keyboardHook, "keyboard");
        }

        _keyboardProc = null;
        _idlePollTimer?.Dispose();
        _idlePollTimer = null;
    }

    public AvaloniaInputHook Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger?.LogInformation("Low-level input hooks are only supported on Windows in the Avalonia head.");
            return this;
        }

        InstallKeyboardHook();
        StartIdlePoller();
        return this;
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;

        try
        {
            _keyboardProc = KeyboardHookCallback;
            var moduleHandle = GetModuleHandle(null);
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger?.LogWarning("SetWindowsHookEx(WH_KEYBOARD_LL) failed with error {Error}", error);
                _keyboardProc = null;
            }
            else
            {
                _logger?.LogDebug("Low-level keyboard hook installed");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to install low-level keyboard hook");
            _keyboardProc = null;
        }
    }

    private void StartIdlePoller()
    {
        if (_idlePollTimer != null)
            return;

        _idlePollTimer = new Timer(_ => PollLastInput(), null, IdlePollMs, IdlePollMs);
        _logger?.LogDebug("Idle-detection poller started (GetLastInputInfo every {Ms}ms)", IdlePollMs);
    }

    private void PollLastInput()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return;
            if (info.dwTime == _lastInputTick) return; // no new input since last poll
            _lastInputTick = info.dwTime;

            GetCursorPos(out var pt);
            MouseMoved?.Invoke(this, new MouseHookEventArgs(pt.x, pt.y));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Idle poll failed");
        }
    }

    private void Unhook(ref IntPtr hook, string name)
    {
        if (hook == IntPtr.Zero)
            return;

        try
        {
            UnhookWindowsHookEx(hook);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to uninstall low-level {Name} hook", name);
        }

        hook = IntPtr.Zero;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            var msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var keyArgs = new KeyboardHookEventArgs((int)info.vkCode, false, false, false, false);
                try
                {
                    KeyPressed?.Invoke(this, keyArgs);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Exception in low-level keyboard hook handler");
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }
}
