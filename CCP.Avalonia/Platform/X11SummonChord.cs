using System;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The EMI Desk summon chord on X11 / XWayland, twin of WPF <c>GlobalHotkeyService</c> (RegisterHotKey):
/// an <c>XGrabKey</c> on the root, consumed like the Win32 hotkey; a combo another client holds fails
/// with BadAccess = "not armed". Reach: while an X11 client has focus. Own display; the listener
/// polls without <see cref="Gate"/> and drains under it, Arm/Disarm take it too (Xlib is not threaded).
/// </summary>
internal static class X11SummonChord
{
    private const string LibX11 = "libX11.so.6";
    private const int KeyPress = 2, MappingNotify = 34, BadAccess = 10, GrabModeAsync = 1, AnyModifier = 1 << 15;
    private const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;
    private const int XEventSize = 192, KeyEventKeycode = 84, ErrorEventCode = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd { public int Fd; public short Events; public short REvents; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern int XConnectionNumber(IntPtr display);
    [DllImport(LibX11)] private static extern int XPending(IntPtr display);
    [DllImport(LibX11)] private static extern int XNextEvent(IntPtr display, IntPtr ev);
    [DllImport(LibX11)] private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window, bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(LibX11)] private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window);
    [DllImport(LibX11)] private static extern int XSync(IntPtr display, bool discard);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);
    [DllImport(LibX11)] private static extern int XRefreshKeyboardMapping(IntPtr mappingEvent);
    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport("libc", SetLastError = true)] private static extern int poll([In, Out] PollFd[] fds, ulong nfds, int timeout);
    [DllImport("libc")] private static extern int pipe(int[] fds);
    [DllImport("libc")] private static extern nint read(int fd, byte[] buf, nint count);
    [DllImport("libc")] private static extern nint write(int fd, byte[] buf, nint count);
    private static readonly int[] _wake = { -1, -1 };   // self-pipe: Arm wakes the listener to drain what XSync queued

    private static readonly object Gate = new();
    private static IntPtr _display, _root;
    private static int _keycode;
    private static Action? _onPress;
    private static bool _badAccess;
    // Rooted for the process lifetime: Xlib keeps the pointer while it is installed.
    private static readonly XErrorHandler Handler = OnXError;
    private static IntPtr _previousHandler;

    /// <summary>Grab <paramref name="mods"/>+<paramref name="key"/> (a WPF/Avalonia Key name) and run
    /// <paramref name="onPress"/> on the listener thread for each press. Replaces any earlier grab.
    /// False when there is no X display, the key has no keycode, or another client holds the combo.</summary>
    internal static bool Arm(ChordMods mods, string key, Action onPress)
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            lock (Gate)
            {
                if (!EnsureDisplay()) return false;
                UngrabLocked();
                int code = XKeysymToKeycode(_display, X11PanicKey.KeysymOf(key));
                if (code == 0) { Log.Warning("[EmiDesk] chord key '{Key}' has no X keycode", key); return false; }
                uint mask = ((mods & ChordMods.Ctrl) != 0 ? ControlMask : 0) | ((mods & ChordMods.Alt) != 0 ? Mod1Mask : 0)
                          | ((mods & ChordMods.Shift) != 0 ? ShiftMask : 0) | ((mods & ChordMods.Win) != 0 ? Mod4Mask : 0);

                // BadAccess arrives asynchronously, so catch it with a handler around an XSync. The
                // handler is process-wide; it forwards every error that is not ours to the previous one.
                _badAccess = false;
                _previousHandler = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(Handler));
                // Caps Lock and Num Lock are modifiers to X: grab every combination of them too.
                foreach (var extra in new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask })
                    XGrabKey(_display, code, mask | extra, _root, false, GrabModeAsync, GrabModeAsync);
                XSync(_display, false);
                XSetErrorHandler(_previousHandler);

                if (_badAccess) { XUngrabKey(_display, code, AnyModifier, _root); XFlush(_display); return false; }
                _keycode = code;
                _onPress = onPress;
                // XSync read the socket, so anything it queued would sit until the next X traffic.
                if (_wake[1] >= 0) write(_wake[1], new byte[1], 1);
                return true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warning("[EmiDesk] X11 chord unavailable ({Why})", ex.Message);
            return false;
        }
    }

    /// <summary>Release the chord (feature switched off, chord refused). Safe to call when nothing is armed.</summary>
    internal static void Disarm()
    {
        lock (Gate)
        {
            if (_display == IntPtr.Zero) return;
            try { UngrabLocked(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chord ungrab failed"); }
        }
    }

    private static void UngrabLocked()
    {
        _onPress = null;
        if (_keycode == 0) return;
        XUngrabKey(_display, _keycode, AnyModifier, _root);
        XFlush(_display);
        _keycode = 0;
    }

    private static bool EnsureDisplay()
    {
        if (_display != IntPtr.Zero) return true;
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero) { Log.Warning("[EmiDesk] no X display: the summon chord cannot be armed"); return false; }
        _root = XDefaultRootWindow(_display);
        var fd = XConnectionNumber(_display);
        pipe(_wake);
        new Thread(() => Listen(fd)) { IsBackground = true, Name = "emi-chord-x11" }.Start();
        return true;
    }

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        if (display == _display)
        {
            if (Marshal.ReadByte(errorEvent, ErrorEventCode) == BadAccess) _badAccess = true;
            return 0;
        }
        return _previousHandler == IntPtr.Zero ? 0
            : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(_previousHandler)(display, errorEvent);
    }

    private static void Listen(int fd)
    {
        var ev = Marshal.AllocHGlobal(XEventSize);
        var pfd = new[] { new PollFd { Fd = fd, Events = 1 /* POLLIN */ }, new PollFd { Fd = _wake[0], Events = 1 } };
        var buf = new byte[16];
        while (true)
        {
            try
            {
                // Block without the lock on the X socket and the wake pipe; drain the queue on every wake.
                if (poll(pfd, _wake[0] >= 0 ? 2UL : 1UL, -1) < 0 && Marshal.GetLastWin32Error() != 4 /* EINTR */) Thread.Sleep(1000);
                if ((pfd[1].REvents & 1) != 0) read(_wake[0], buf, buf.Length);
                Action? fire = null;
                lock (Gate)
                {
                    while (XPending(_display) > 0)
                    {
                        XNextEvent(_display, ev);
                        int type = Marshal.ReadInt32(ev);
                        if (type == MappingNotify) XRefreshKeyboardMapping(ev);
                        else if (type == KeyPress && _keycode != 0 && Marshal.ReadInt32(ev, KeyEventKeycode) == _keycode)
                            fire = _onPress;
                    }
                }
                fire?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] chord listener event failed");
            }
        }
    }
}
