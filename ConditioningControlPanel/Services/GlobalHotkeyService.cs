using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Registers a single Win32 system-wide hotkey via <c>RegisterHotKey</c> so the
    /// callback fires regardless of which application has keyboard focus. Used for
    /// the avatar chat shortcut so the user can pop the input from any other window
    /// (browser, terminal, anything).
    ///
    /// The matching in-window <see cref="KeyBinding"/> stays in place as a fallback —
    /// if the OS rejects the combo (already taken by another app), the in-app path
    /// still works whenever one of the app's own windows has focus.
    /// </summary>
    public static class GlobalHotkeyService
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 0xB1B1; // arbitrary, just needs to be unique within owning window

        // Win32 modifier flags for RegisterHotKey
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private static IntPtr _ownerHwnd = IntPtr.Zero;
        private static HwndSource? _source;
        private static HwndSourceHook? _hook;
        private static Action? _callback;

        /// <summary>
        /// Registers the given combo as a system-wide hotkey. Replaces any prior
        /// registration owned by this service. Returns true on success; false if the
        /// OS refused (typically because another app already grabbed the combo).
        /// </summary>
        public static bool Register(Window owner, ModifierKeys modifiers, Key key, Action onPressed)
        {
            if (owner == null) return false;
            try
            {
                Unregister();

                var helper = new WindowInteropHelper(owner);
                helper.EnsureHandle();
                _ownerHwnd = helper.Handle;
                if (_ownerHwnd == IntPtr.Zero) return false;

                _source = HwndSource.FromHwnd(_ownerHwnd);
                if (_source == null) return false;

                _callback = onPressed;
                _hook = WndProc;
                _source.AddHook(_hook);

                uint fsMods = MOD_NOREPEAT;
                if ((modifiers & ModifierKeys.Alt) != 0) fsMods |= MOD_ALT;
                if ((modifiers & ModifierKeys.Control) != 0) fsMods |= MOD_CONTROL;
                if ((modifiers & ModifierKeys.Shift) != 0) fsMods |= MOD_SHIFT;
                if ((modifiers & ModifierKeys.Windows) != 0) fsMods |= MOD_WIN;

                uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

                bool ok = RegisterHotKey(_ownerHwnd, HotkeyId, fsMods, vk);
                if (!ok)
                {
                    var err = Marshal.GetLastWin32Error();
                    App.Logger?.Information("GlobalHotkeyService: RegisterHotKey failed (Win32 err={Err}) for {Mods}+{Key} — combo may be taken by another app",
                        err, modifiers, key);
                    // Tear down the hook since registration failed.
                    Unregister();
                    return false;
                }

                App.Logger?.Information("GlobalHotkeyService: registered {Mods}+{Key} as global hotkey", modifiers, key);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GlobalHotkeyService: registration threw");
                Unregister();
                return false;
            }
        }

        /// <summary>
        /// Removes the current registration and message hook. Safe to call repeatedly.
        /// Always called from <see cref="Register"/> before re-arming, and from the
        /// owning window's Closing handler.
        /// </summary>
        public static void Unregister()
        {
            try
            {
                if (_ownerHwnd != IntPtr.Zero)
                {
                    UnregisterHotKey(_ownerHwnd, HotkeyId);
                }
                if (_source != null && _hook != null)
                {
                    _source.RemoveHook(_hook);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GlobalHotkeyService: cleanup threw: {Error}", ex.Message);
            }
            finally
            {
                _ownerHwnd = IntPtr.Zero;
                _source = null;
                _hook = null;
                _callback = null;
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                try
                {
                    _callback?.Invoke();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "GlobalHotkeyService: hotkey callback threw");
                }
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
