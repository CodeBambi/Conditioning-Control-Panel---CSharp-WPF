using System;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Input;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The panic key's global listener on Windows: WPF's WH_KEYBOARD_LL hook
/// (ConditioningControlPanel/Services/Input/GlobalKeyboardHook.cs), the twin of
/// <see cref="X11PanicKey"/> with the same Start(currentKey, onPress) entry point.
///
/// <para><b>Its own thread.</b> A low-level hook is delivered to the message loop of the thread that
/// installed it. WPF installed it on the UI thread, so a wedged UI thread could not receive the panic
/// key, and Windows silently drops a hook whose callback overruns LowLevelHooksTimeout (#616-#623,
/// #919b). Here a dedicated thread owns the hook and pumps nothing else, so the callback only reads
/// the key, posts, and returns. A timer on that thread reinstalls the hook every
/// <see cref="ReinstallEvery"/> (new hook first, then the old one off, so there is no gap): a hook
/// Windows dropped comes back without a restart.</para>
///
/// <para>Non-consuming, modifier-blind (PanicPolicy.HookBoundBaseKeys): the panic key always reaches
/// the focused app too. The only keys it ever eats are Lockdown's system keys while
/// <see cref="SuppressSystemKeys"/> is on. A held panic key is one press
/// (<see cref="PanicKeyHold"/>); every key's down and up are raised for the Leash and the
/// unfocused Esc door.</para>
/// </summary>
internal static class Win32PanicKey
{
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104,
        WM_SYSKEYUP = 0x0105, WM_TIMER = 0x0113, WM_QUIT = 0x0012, VK_CONTROL = 0x11;

    /// <summary>How often the listener thread re-registers the hook.</summary>
    internal static readonly TimeSpan ReinstallEvery = TimeSpan.FromSeconds(30);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private static readonly LowLevelKeyboardProc Proc = Callback;   // rooted: the hook outlives any local
    private static readonly PanicKeyHold HeldPanic = new();
    private static int _started;
    private static uint _threadId;
    private static IntPtr _hook;
    private static Func<string?> _currentKey = () => null;
    private static Action _onPress = () => { };
    private static volatile int _boundVk;
    private static volatile bool _suppress;

    /// <summary>True while the hook is installed: a panic press can actually arrive (LockCardWindow's
    /// strict-mode exit question, WPF PanicHook.IsInstalled).</summary>
    internal static bool IsListening => _hook != IntPtr.Zero;

    /// <summary>The virtual key the configured name resolved to on the last event (or Start); 0 = the
    /// key cannot fire.</summary>
    internal static int BoundVirtualKey => _boundVk;

    /// <summary>Every key-down / key-up the hook sees, on the listener thread, with the canonical key
    /// name (null for an unnamed code). Handlers must return at once (post, never work).</summary>
    internal static event Action<int, string?>? KeyDown, KeyUp;

    /// <summary>WPF hold-to-cut: true while this account is leashed (read on the listener thread).</summary>
    internal static Func<bool> Leashed = () => false;

    /// <summary>The panic key has been held five seconds while leashed (once per hold). Listener
    /// thread: post, never work.</summary>
    internal static event Action? LeashHoldDue;

    /// <summary>A swallowed repeat of a held panic key while leashed, with how long it has been held
    /// (the 5..1 ring and the tick). Listener thread: post, never work.</summary>
    internal static event Action<TimeSpan>? LeashHolding;

    /// <summary>The held panic key came up (the ring goes). Listener thread.</summary>
    internal static event Action? PanicReleased;

    /// <summary>A Lockdown system key was eaten (the Possession tripwire). Listener thread.</summary>
    internal static event Action? SystemKeyBlocked;

    /// <summary>Lockdown's Win / Alt+Tab / Alt+F4 / Ctrl+Esc block (WPF SuppressSystemKeys). Setting it
    /// true starts the hook if nothing started it (panic key off), as WPF does, so the block is real.</summary>
    internal static bool SuppressSystemKeys
    {
        get => _suppress;
        set
        {
            _suppress = value;
            if (value) EnsureHook();   // never rebinds: a failed first install keeps the panic binding
            if (value && !IsListening)
                Log.Warning("Lockdown: keyboard hook is not installed - Win/Alt-Tab will NOT be blocked this session");
        }
    }

    /// <summary>Starts the listener once. <paramref name="onPress"/> runs on the listener thread for
    /// every press of <paramref name="currentKey"/>() (read per event, so rebinds apply live); a held
    /// key's repeats are not presses. A later call (Lockdown started it first) only swaps the two in.
    /// False off Windows or when SetWindowsHookEx fails - the caller logs and relies on the tray.</summary>
    internal static bool Start(Func<string?> currentKey, Action onPress)
    {
        if (!OperatingSystem.IsWindows()) return false;
        Bind(currentKey, onPress);
        return EnsureHook();
    }

    private static bool EnsureHook()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (Interlocked.Exchange(ref _started, 1) == 1) return IsListening;

        var ready = new ManualResetEventSlim();   // not disposed: a slow thread may still Set it after the wait
        var thread = new Thread(() => Listen(ready)) { IsBackground = true, Name = "panic-key-win32" };
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        if (!IsListening)
        {
            Log.Warning("Panic key: WH_KEYBOARD_LL hook failed to install; the tray's Stop everything is the panic control");
            return false;
        }
        Log.Information("Panic key: listening through a WH_KEYBOARD_LL hook on its own thread");
        return true;
    }

    /// <summary>What a press reads and does, without installing anything (Start; tests).</summary>
    internal static void Bind(Func<string?> currentKey, Action onPress)
    {
        _currentKey = currentKey;
        _onPress = onPress;
        _boundVk = VirtualKeys.Of(currentKey());
    }

    /// <summary>Unhooks and ends the listener thread (self-checks; the app keeps it for its life).</summary>
    internal static void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    private static void Listen(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _hook = Install();
        ready.Set();
        if (_hook == IntPtr.Zero) { _started = 0; return; }
        SetTimer(IntPtr.Zero, UIntPtr.Zero, (uint)ReinstallEvery.TotalMilliseconds, IntPtr.Zero);
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message != WM_TIMER) continue;
            // Windows gives no signal when it drops a low-level hook, so re-register on a clock:
            // the new one first, then the old one off, so a key is never missed in between.
            var fresh = Install();
            if (fresh == IntPtr.Zero) continue;   // keep the old one; it may still be live
            var old = _hook;
            _hook = fresh;
            UnhookWindowsHookEx(old);
        }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _threadId = 0;
        Log.Information("Panic key: WH_KEYBOARD_LL hook removed");
    }

    private static IntPtr Install()
    {
        var h = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(null), 0);
        if (h == IntPtr.Zero)
            Log.Error("Panic key: SetWindowsHookEx failed (win32 error {Code})", Marshal.GetLastWin32Error());
        return h;
    }

    private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var msg = (int)wParam;
                var vk = Marshal.ReadInt32(lParam);
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    // Lockdown first, as WPF: a suppressed key never reaches the panic path or the events.
                    if (_suppress && SystemKeyBlock.Suppress(vk, msg == WM_SYSKEYDOWN, GetAsyncKeyState(VK_CONTROL) < 0))
                    {
                        try { SystemKeyBlocked?.Invoke(); } catch { /* never let the haunt break the block */ }
                        return (IntPtr)1;
                    }
                    OnDown(vk, DateTime.UtcNow);
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP)
                    OnUp(vk);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Panic key: hook callback failed");
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>One key-down on the listener thread (internal so a test can drive it without a hook).</summary>
    internal static void OnDown(int vk, DateTime nowUtc)
    {
        _boundVk = VirtualKeys.Of(_currentKey());
        // Panic first: no listener (keyword triggers, push-to-talk) may ever stand between a press and panic.
        if (vk != 0 && vk == _boundVk)
        {
            // WPF LeashHoldSwallows (DESK-5 + hold-to-cut): the first down is THE panic press, every
            // repeat is swallowed, leashed or not. The leashed flag only times the hold; it never
            // changes which down is a press, so the leash cannot delay or weaken panic.
            bool leashed;
            try { leashed = Leashed(); } catch { leashed = false; }
            var (repeat, due) = HeldPanic.Down(nowUtc, leashed);
            if (!repeat) _onPress();
            try
            {
                if (due) LeashHoldDue?.Invoke();
                else if (leashed && repeat && HeldPanic.HeldFor(nowUtc) is { } held) LeashHolding?.Invoke(held);
            }
            catch (Exception ex) { Log.Debug("Panic key: leash hold listener threw: {E}", ex.Message); }
        }
        try { KeyDown?.Invoke(vk, VirtualKeys.NameOf(vk)); }
        catch (Exception ex) { Log.Debug("Panic key: a key listener threw: {E}", ex.Message); }
        // She's Listening push-to-talk rides the same hook (WPF GlobalKeyboardHook), as on X11.
        if (X11PanicKey.PushToTalk is { } ptt && vk == VirtualKeys.Of(X11PanicKey.PushToTalkKey?.Invoke()) && vk != _boundVk)
            ptt();
    }

    /// <summary>One key-up on the listener thread.</summary>
    internal static void OnUp(int vk)
    {
        if (vk == _boundVk)
        {
            HeldPanic.Up();
            try { PanicReleased?.Invoke(); } catch { }
        }
        KeyUp?.Invoke(vk, VirtualKeys.NameOf(vk));
    }
}
