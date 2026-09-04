using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Registers Win32 system-wide hotkeys via <c>RegisterHotKey</c> so a callback
    /// fires regardless of which application has keyboard focus. Used for the avatar
    /// chat shortcut (pop the input from any other window) and the camera start/stop
    /// shortcut.
    ///
    /// Multiple hotkeys are supported: each is keyed by a caller-supplied id (see the
    /// well-known ids below). They share a single message hook installed on the owner
    /// window; WM_HOTKEY is dispatched to the matching callback by id.
    ///
    /// The matching in-window <see cref="KeyBinding"/> stays in place as a fallback —
    /// if the OS rejects a combo (already taken by another app), the in-app path still
    /// works whenever one of the app's own windows has focus.
    /// </summary>
    public static class GlobalHotkeyService
    {
        // Well-known hotkey slot ids. Each must be unique within the owning window.
        public const int ChatHotkeyId = 0xB1B1;   // avatar chat input
        public const int CameraHotkeyId = 0xB1B2; // webcam tracker start/stop

        /// <summary>
        /// Gaze Quick Recal (one-dot drift correction). Deliberately NOT the same thing as
        /// <see cref="CameraHotkeyId"/>: that one STARTS/STOPS the tracker, this one corrects
        /// drift and leaves tracking exactly as it found it. See
        /// <c>MainWindow.ApplyGlobalQuickRecalHotkey</c>.
        /// </summary>
        public const int QuickRecalHotkeyId = 0xB1B3;

        /// <summary>
        /// EMI Desk's summon chord (default Ctrl+Alt+E). Toggles the widget: out, or away. See
        /// <c>Services.EmiDesk.EmiDeskService.ApplyHotkey</c>, which refuses a bare key and checks
        /// the same PanicPolicy hook clash the Quick Recal chord does.
        /// </summary>
        public const int EmiDeskHotkeyId = 0xB1B4;

        private const int WM_HOTKEY = 0x0312;

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

        // id -> callback for every currently-registered hotkey. The shared message
        // hook stays installed as long as this has any entries.
        private static readonly Dictionary<int, Action> _callbacks = new();

        // id -> "Ctrl+Alt+E", for the single summary line. Registration is spread across four
        // callers that each fire whenever their setting changes, so each one used to file its own
        // Information line and the log carried four near-identical lines per launch saying the
        // hotkeys were exactly where they were last launch. Now the per-hotkey detail is Debug and
        // one coalesced line names the whole set - and it only reappears when the set CHANGES.
        private static readonly SortedDictionary<int, string> _armed = new();
        private static string _lastLoggedSet = "";
        private static DispatcherTimer? _summaryTimer;

        /// <summary>
        /// Registers the given combo as a system-wide hotkey under <paramref name="id"/>.
        /// Replaces any prior registration for the same id. Returns true on success;
        /// false if the OS refused (typically because another app already grabbed the
        /// combo). Other registered hotkeys are left untouched on failure.
        /// </summary>
        public static bool Register(int id, Window owner, ModifierKeys modifiers, Key key, Action onPressed)
        {
            if (owner == null) return false;
            try
            {
                // Drop any prior registration for this id before re-arming.
                Unregister(id);

                var helper = new WindowInteropHelper(owner);
                helper.EnsureHandle();
                _ownerHwnd = helper.Handle;
                if (_ownerHwnd == IntPtr.Zero) return false;

                // Install the shared message hook once for the owner window.
                if (_source == null)
                {
                    _source = HwndSource.FromHwnd(_ownerHwnd);
                    if (_source == null) return false;
                    _hook = WndProc;
                    _source.AddHook(_hook);
                }

                uint fsMods = MOD_NOREPEAT;
                if ((modifiers & ModifierKeys.Alt) != 0) fsMods |= MOD_ALT;
                if ((modifiers & ModifierKeys.Control) != 0) fsMods |= MOD_CONTROL;
                if ((modifiers & ModifierKeys.Shift) != 0) fsMods |= MOD_SHIFT;
                if ((modifiers & ModifierKeys.Windows) != 0) fsMods |= MOD_WIN;

                uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

                bool ok = RegisterHotKey(_ownerHwnd, id, fsMods, vk);
                if (!ok)
                {
                    var err = Marshal.GetLastWin32Error();
                    App.Logger?.Information("GlobalHotkeyService: RegisterHotKey failed (Win32 err={Err}) for {Mods}+{Key} (id=0x{Id:X}) — combo may be taken by another app",
                        err, modifiers, key, id);
                    // Only tear down the slot that failed; other hotkeys stay armed.
                    Unregister(id);
                    return false;
                }

                _callbacks[id] = onPressed;
                _armed[id] = $"{modifiers}+{key}";
                App.Logger?.Debug("GlobalHotkeyService: registered {Mods}+{Key} as global hotkey (id=0x{Id:X})", modifiers, key, id);
                ScheduleSummary();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GlobalHotkeyService: registration threw (id=0x{Id:X})", id);
                Unregister(id);
                return false;
            }
        }

        /// <summary>
        /// Removes the registration for a single hotkey id. Safe to call repeatedly.
        /// When the last hotkey is removed the shared message hook is torn down too.
        /// Always called from <see cref="Register"/> before re-arming a slot.
        /// </summary>
        public static void Unregister(int id)
        {
            try
            {
                if (_ownerHwnd != IntPtr.Zero)
                {
                    UnregisterHotKey(_ownerHwnd, id);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GlobalHotkeyService: cleanup threw (id=0x{Id:X}): {Error}", id, ex.Message);
            }
            _callbacks.Remove(id);
            _armed.Remove(id);
            ScheduleSummary();
            if (_callbacks.Count == 0) TeardownHook();
        }

        /// <summary>
        /// Removes every registration and the shared message hook. Called from the
        /// owning window's Closing handler.
        /// </summary>
        public static void UnregisterAll()
        {
            try
            {
                if (_ownerHwnd != IntPtr.Zero)
                {
                    foreach (var id in new List<int>(_callbacks.Keys))
                        UnregisterHotKey(_ownerHwnd, id);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GlobalHotkeyService: cleanup-all threw: {Error}", ex.Message);
            }
            _callbacks.Clear();
            _armed.Clear();
            TeardownHook();
        }

        /// <summary>
        /// Coalesces a burst of Register/Unregister calls into ONE line. Registration happens once
        /// per hotkey at startup and again whenever the user edits a combo, so a short debounce
        /// turns "four lines, one per slot" into "one line, the whole set". Never throws; if there
        /// is no dispatcher (unit tests, teardown) the summary is simply skipped - it is a log
        /// line, not behaviour.
        /// </summary>
        private static void ScheduleSummary()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                if (_summaryTimer == null)
                {
                    _summaryTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(750),
                    };
                    _summaryTimer.Tick += (_, _) => { _summaryTimer?.Stop(); LogSummary(); };
                }
                _summaryTimer.Stop();
                _summaryTimer.Start();
            }
            catch { /* swallow: a diagnostic line must never be able to break hotkey arming */ }
        }

        private static void LogSummary()
        {
            try
            {
                // Only when the SET changed: re-applying the same combos (which several settings
                // handlers do on any settings save) must not re-file the line.
                var set = _armed.Count == 0 ? "(none)" : string.Join(", ", _armed.Values);
                if (set == _lastLoggedSet) return;
                _lastLoggedSet = set;
                App.Logger?.Information("GlobalHotkeyService: {Count} global hotkey(s) armed: {Hotkeys}",
                    _armed.Count, set);
            }
            catch { /* swallow: diagnostics only */ }
        }

        private static void TeardownHook()
        {
            try
            {
                if (_source != null && _hook != null)
                {
                    _source.RemoveHook(_hook);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GlobalHotkeyService: hook teardown threw: {Error}", ex.Message);
            }
            finally
            {
                _ownerHwnd = IntPtr.Zero;
                _source = null;
                _hook = null;
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_callbacks.TryGetValue(id, out var callback))
                {
                    try
                    {
                        callback?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "GlobalHotkeyService: hotkey callback threw (id=0x{Id:X})", id);
                    }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
    }
}
