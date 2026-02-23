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

    /// <summary>
    /// When true, suppresses system keys (Win, Alt+Tab, Alt+F4, Escape) for lockdown mode.
    /// </summary>
    public bool SuppressSystemKeys { get; set; }

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
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

            // Lockdown mode: suppress system/escape keys
            // NOTE: Ctrl+Alt+Del is kernel-level and cannot be hooked — intentional safety valve
            if (SuppressSystemKeys)
            {
                bool suppress = false;

                // Windows keys (LWin=0x5B, RWin=0x5C)
                if (vkCode == 0x5B || vkCode == 0x5C)
                    suppress = true;

                // Alt+anything (WM_SYSKEYDOWN = Alt held). Blocks Alt+Tab, Alt+F4, Alt+Esc, etc.
                if (wParam == (IntPtr)WM_SYSKEYDOWN)
                    suppress = true;

                // Alt keys themselves (LMenu=0xA4, RMenu=0xA5) — block the press entirely
                if (vkCode == 0xA4 || vkCode == 0xA5)
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
