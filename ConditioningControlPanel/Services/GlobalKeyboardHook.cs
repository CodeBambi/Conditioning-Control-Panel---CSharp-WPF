using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Global keyboard hook to capture key presses system-wide (even when minimized)
/// Supports lockdown mode to block escape keys (Alt+Tab, Alt+F4, Win keys, etc.)
/// </summary>
public class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // Virtual key codes for modifier detection
    private const int VK_MENU = 0x12;      // Alt
    private const int VK_CONTROL = 0x11;   // Ctrl
    private const int VK_SHIFT = 0x10;     // Shift
    private const int VK_LWIN = 0x5B;      // Left Windows
    private const int VK_RWIN = 0x5C;      // Right Windows

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private bool _isDisposed;
    private bool _lockdownMode = false;

    public event Action<Key>? KeyPressed;

    /// <summary>
    /// Whether lockdown mode is currently active (blocking escape keys)
    /// </summary>
    public bool IsLockdownActive => _lockdownMode;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    /// <summary>
    /// Enable lockdown mode - blocks Alt+Tab, Alt+F4, Win keys, Escape, etc.
    /// </summary>
    public void EnableLockdown()
    {
        _lockdownMode = true;
        App.Logger?.Warning("LOCKDOWN MODE ENABLED - Escape keys are now blocked");
    }

    /// <summary>
    /// Disable lockdown mode - allows all keys through
    /// </summary>
    public void DisableLockdown()
    {
        _lockdownMode = false;
        App.Logger?.Information("Lockdown mode disabled");
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        _hookId = SetHook(_proc);
        App.Logger?.Debug("Global keyboard hook started");
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        App.Logger?.Debug("Global keyboard hook stopped");
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
            GetModuleHandle(curModule.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            var key = KeyInterop.KeyFromVirtualKey(vkCode);

            // In lockdown mode, block escape keys
            if (_lockdownMode && ShouldBlockKey(key, vkCode))
            {
                App.Logger?.Debug("LOCKDOWN: Blocked key {Key}", key);
                return (IntPtr)1; // Block the key - don't pass to system
            }

            try
            {
                KeyPressed?.Invoke(key);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Error in keyboard hook callback: {Error}", ex.Message);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Determines if a key should be blocked in lockdown mode
    /// </summary>
    private bool ShouldBlockKey(Key key, int vkCode)
    {
        // Check modifier states
        bool altHeld = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        bool ctrlHeld = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        bool shiftHeld = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        bool winHeld = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
                       (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

        // Block Alt+Tab (window switching)
        if (altHeld && key == Key.Tab) return true;

        // Block Alt+F4 (close window)
        if (altHeld && key == Key.F4) return true;

        // Block Alt+Escape (switch window)
        if (altHeld && key == Key.Escape) return true;

        // Block Windows key combinations
        if (winHeld) return true;

        // Block Windows key itself
        if (key == Key.LWin || key == Key.RWin) return true;

        // Block Ctrl+Shift+Escape (Task Manager shortcut)
        if (ctrlHeld && shiftHeld && key == Key.Escape) return true;

        // Block Escape key entirely
        if (key == Key.Escape) return true;

        // Block Ctrl+Alt+Delete cannot be intercepted (Windows kernel protection)
        // But we can try to block other common escapes

        // Block Alt+Space (system menu)
        if (altHeld && key == Key.Space) return true;

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }

    #region Win32 API

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    #endregion
}
