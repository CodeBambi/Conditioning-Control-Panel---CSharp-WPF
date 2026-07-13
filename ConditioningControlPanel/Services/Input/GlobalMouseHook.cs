using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Global low-level mouse hook for the chaos Ripple verb (right-click anywhere on the live
/// desktop). The decision callback runs SYNCHRONOUSLY on the hook thread and returns whether
/// to swallow the click — so it must be cheap and touch only thread-safe snapshots (never
/// WPF dependency properties). Swallowed clicks never reach the app under the cursor;
/// unswallowed ones pass through untouched, keeping normal desktop right-clicks alive mid-run.
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelMouseProc _proc;
    private bool _isDisposed;
    // When a DOWN is swallowed, its matching UP must be swallowed too. An orphaned UP still
    // reaches the window under the cursor, and WPF surfaces wired to bare MouseLeftButtonUp
    // (the dashboard FeatureCards, preset/skill cards) treat it as a click - so popping a
    // hosted bubble over the dashboard also "clicked" the card beneath it. Right-clicks have
    // the same shape via context menus, which open on right-UP. Hook-thread only: every
    // low-level mouse message is delivered sequentially on this hook's message loop.
    private bool _swallowNextLeftUp;
    private bool _swallowNextRightUp;

    /// <summary>Right button pressed at this PHYSICAL-px screen point. Return true to swallow.</summary>
    public Func<Point, bool>? RightDown;

    /// <summary>Left button pressed at this PHYSICAL-px screen point. Return true to swallow.
    /// Used by the shared-host bubble field to pop a bubble (and swallow the click so it doesn't
    /// also land on whatever sits behind the click-through host); a miss returns false and passes
    /// the click through untouched. Same hook-thread contract as <see cref="RightDown"/>.</summary>
    public Func<Point, bool>? LeftDown;

    public GlobalMouseHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        App.Logger?.Debug("Global mouse hook started");
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        App.Logger?.Debug("Global mouse hook stopped");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            if (wParam == (IntPtr)WM_RBUTTONDOWN || wParam == (IntPtr)WM_LBUTTONDOWN)
            {
                try
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var pt = new Point(info.pt.X, info.pt.Y);
                    bool isRight = wParam == (IntPtr)WM_RBUTTONDOWN;
                    var cb = isRight ? RightDown : LeftDown;
                    if (cb?.Invoke(pt) == true)
                    {
                        if (isRight) _swallowNextRightUp = true;
                        else _swallowNextLeftUp = true;
                        return (IntPtr)1;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Mouse hook callback: {E}", ex.Message);
                }
            }
            else if (wParam == (IntPtr)WM_LBUTTONUP && _swallowNextLeftUp)
            {
                _swallowNextLeftUp = false;
                return (IntPtr)1;
            }
            else if (wParam == (IntPtr)WM_RBUTTONUP && _swallowNextRightUp)
            {
                _swallowNextRightUp = false;
                return (IntPtr)1;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion
}
