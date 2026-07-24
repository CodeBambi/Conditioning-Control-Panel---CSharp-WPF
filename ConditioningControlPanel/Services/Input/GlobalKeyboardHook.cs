using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Global keyboard hook to capture key presses system-wide (even when minimized)
/// </summary>
public class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;
    private bool _isDisposed;

    public event Action<Key>? KeyPressed;
    public event Action<Key, int>? KeyPressedWithVkCode;

    /// <summary>True while the WH_KEYBOARD_LL hook is actually installed. Lockdown
    /// checks this after arming suppression — SetWindowsHookEx can fail (hook quota,
    /// security software) and suppression on a hook that isn't there blocks nothing.</summary>
    public bool IsInstalled => _hookId != IntPtr.Zero;

    private bool _suppressSystemKeys;

    /// <summary>
    /// When true, suppresses system keys (Win, Alt+Tab, Alt+F4, Escape) for lockdown mode.
    /// Setting this true also installs the hook if it isn't running: the hook is only
    /// started at launch when the panic key or keyword triggers need it, so lockdown on a
    /// machine with both disabled would otherwise flip this flag on a hook that never
    /// existed and no key would actually be blocked. While true, Stop() is refused so a
    /// feature toggle can't uninstall the hook out from under an active lockdown.
    /// </summary>
    public bool SuppressSystemKeys
    {
        get => _suppressSystemKeys;
        set
        {
            _suppressSystemKeys = value;
            if (value) Start();
        }
    }

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        _hookId = SetHook(_proc);
        if (_hookId == IntPtr.Zero)
        {
            App.Logger?.Error("Global keyboard hook failed to install (win32 error {Code})",
                Marshal.GetLastWin32Error());
            VideoDiag.Log("PANIC", $"global keyboard hook FAILED to install (win32 {Marshal.GetLastWin32Error()}) - the panic key cannot work");
        }
        else
        {
            App.Logger?.Debug("Global keyboard hook started");
            // #616/#617/#621/#622/#623 context: this is a WH_KEYBOARD_LL hook, which Windows
            // delivers to the message loop of the INSTALLING thread — the WPF UI thread. Two
            // consequences the reports depend on: (1) while the UI thread is wedged the panic key
            // is physically undeliverable, and (2) Windows silently un-registers a low-level hook
            // whose callback exceeds LowLevelHooksTimeout (default 300ms), and nothing here ever
            // reinstalls it — so a single long stall can kill the panic key for the rest of the
            // session even after the app recovers. Recorded so a report's trace shows exactly when
            // the hook existed.
            VideoDiag.Log("PANIC", "global keyboard hook installed (WH_KEYBOARD_LL, delivered on the UI thread)");
        }
    }

    public void Stop()
    {
        if (_suppressSystemKeys)
        {
            App.Logger?.Debug("Global keyboard hook Stop() ignored - system key suppression (lockdown) active");
            return;
        }
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        App.Logger?.Debug("Global keyboard hook stopped");
        VideoDiag.Log("PANIC", "global keyboard hook uninstalled");
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

            // Lockdown mode: suppress system/escape keys
            // NOTE: Ctrl+Alt+Del is kernel-level and cannot be hooked — intentional safety valve
            if (SuppressSystemKeys)
            {
                bool suppress = false;

                // Windows keys (LWin=0x5B, RWin=0x5C)
                if (vkCode == 0x5B || vkCode == 0x5C)
                    suppress = true;

                // Alt-based app-switch/close combos ONLY (WM_SYSKEYDOWN = Alt held):
                // Alt+Tab (0x09), Alt+F4 (0x73), Alt+Esc (0x1B). We must NOT blanket-suppress
                // every WM_SYSKEYDOWN or every Alt press — that killed legitimate Alt shortcuts
                // (menu mnemonics, Alt+arrows, etc.) in every program while lockdown was on (#338).
                if (wParam == (IntPtr)WM_SYSKEYDOWN &&
                    (vkCode == 0x09 || vkCode == 0x73 || vkCode == 0x1B))
                    suppress = true;

                // Escape
                if (vkCode == 0x1B)
                    suppress = true;

                // Ctrl+Shift+Esc (direct Task Manager shortcut)
                if (vkCode == 0x1B && GetAsyncKeyState(0x11) < 0 && GetAsyncKeyState(0x10) < 0)
                    suppress = true;

                // Ctrl+Esc (opens Start menu)
                if (vkCode == 0x1B && GetAsyncKeyState(0x11) < 0)
                    suppress = true;

                if (suppress)
                    return (IntPtr)1;
            }

            try
            {
                KeyPressed?.Invoke(key);
                KeyPressedWithVkCode?.Invoke(key, vkCode);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Error in keyboard hook callback: {Error}", ex.Message);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _suppressSystemKeys = false; // allow Stop() during shutdown even mid-lockdown
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
