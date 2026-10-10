using System;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Linux typed-text source for keyword triggers (WPF: the WH_KEYBOARD_LL hook; the Windows port
/// rides <see cref="Win32PanicKey"/>'s hook). X11 allows passive observation: XInput 2.1 raw key
/// presses selected on the root window are delivered to any client, with no grab, so nothing the user
/// types is intercepted or delayed and <see cref="X11PanicKey"/>'s own grab is untouched.
///
/// <para><b>Limits.</b> X11 and XWayland windows only: a Wayland compositor shows no client another
/// client's keys, by design, so text typed into a native Wayland window is never seen (there is no
/// portal for it). 64-bit only (the event layout below is LP64). No dead-key or input-method
/// composition: a composed character arrives as its base keys.</para>
///
/// <para><b>Privacy</b>, as WPF: a key becomes one character handed to the in-memory matcher. Nothing
/// is logged, stored or sent from here. The thread and its X connection exist only while the keyword
/// master switch is on (<see cref="Sync"/>); off means no thread, no connection, no selection.</para>
/// </summary>
internal static class X11KeyListener
{
    internal enum Key { Char, Clear, Backspace, Space }

    private const string LibX11 = "libX11.so.6";
    private const string LibXi = "libXi.so.6";

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern bool XQueryExtension(IntPtr display, string name, out int opcode, out int firstEvent, out int firstError);
    [DllImport(LibX11)] private static extern int XPending(IntPtr display);
    [DllImport(LibX11)] private static extern int XNextEvent(IntPtr display, IntPtr ev);
    [DllImport(LibX11)] private static extern bool XGetEventData(IntPtr display, IntPtr cookie);
    [DllImport(LibX11)] private static extern void XFreeEventData(IntPtr display, IntPtr cookie);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] private static extern int XkbGetState(IntPtr display, uint deviceSpec, IntPtr state);
    [DllImport(LibX11)] private static extern nuint XkbKeycodeToKeysym(IntPtr display, byte keycode, int group, int level);
    [DllImport(LibXi)] private static extern int XIQueryVersion(IntPtr display, ref int major, ref int minor);
    [DllImport(LibXi)] private static extern int XISelectEvents(IntPtr display, IntPtr window, ref XIEventMask masks, int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct XIEventMask { public int DeviceId; public int MaskLen; public IntPtr Mask; }

    private const int GenericEvent = 35, XI_RawKeyPress = 13, XIAllMasterDevices = 1;
    private const uint XkbUseCoreKbd = 0x0100;
    private const int ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 0x10, Mod4Mask = 0x40, Mod5Mask = 0x80;
    // XGenericEventCookie / XIRawEvent on LP64.
    private const int OffType = 0, OffExtension = 32, OffEvType = 36, OffData = 48, OffRawDetail = 56;

    private static readonly object Gate = new();
    private static Thread? _thread;
    private static int _gen;   // each thread owns one generation; a stop or restart retires it

    /// <summary>Set once by the test assembly's module initializer: no test may listen to the real keyboard.</summary>
    internal static bool Disabled;

    internal static bool IsRunning { get { lock (Gate) return _thread != null; } }

    /// <summary>Start when <paramref name="want"/> and not running; stop when not wanted. Linux only.</summary>
    internal static void Sync(bool want, Action<Key, char> sink)
    {
        if (!OperatingSystem.IsLinux() || Disabled) return;
        if (want) Start(sink); else Stop();
    }

    private static void Start(Action<Key, char> sink)
    {
        lock (Gate)
        {
            if (_thread != null) return;
            if (IntPtr.Size != 8 || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return;
            var gen = Interlocked.Increment(ref _gen);
            _thread = new Thread(() => Run(sink, gen)) { IsBackground = true, Name = "x11-keyword-keys" };
            _thread.Start();
        }
    }

    internal static void Stop()
    {
        lock (Gate)
        {
            Interlocked.Increment(ref _gen);
            _thread = null;   // the loop sees its generation retired within one sleep and closes its own connection
        }
    }

    private static void Run(Action<Key, char> sink, int gen)
    {
        var display = IntPtr.Zero;
        IntPtr ev = IntPtr.Zero, state = IntPtr.Zero, mask = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return;
            if (!XQueryExtension(display, "XInputExtension", out var opcode, out _, out _)) return;
            int major = 2, minor = 1;   // 2.1: raw events arrive even while another client holds a grab
            if (XIQueryVersion(display, ref major, ref minor) != 0) return;

            mask = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(mask, 0);
            Marshal.WriteByte(mask, XI_RawKeyPress >> 3, (byte)(1 << (XI_RawKeyPress & 7)));
            var m = new XIEventMask { DeviceId = XIAllMasterDevices, MaskLen = 4, Mask = mask };
            if (XISelectEvents(display, XDefaultRootWindow(display), ref m, 1) != 0) return;
            XFlush(display);

            ev = Marshal.AllocHGlobal(192);      // sizeof(XEvent) on LP64
            state = Marshal.AllocHGlobal(32);    // XkbStateRec is 18 bytes
            Log.Information("Keyword triggers: X11 key listener started");
            while (Volatile.Read(ref _gen) == gen)
            {
                if (XPending(display) <= 0) { Thread.Sleep(20); continue; }
                XNextEvent(display, ev);
                if (Marshal.ReadInt32(ev, OffType) != GenericEvent || Marshal.ReadInt32(ev, OffExtension) != opcode) continue;
                if (Marshal.ReadInt32(ev, OffEvType) != XI_RawKeyPress) continue;
                if (!XGetEventData(display, ev)) continue;
                int keycode;
                try { keycode = Marshal.ReadInt32(Marshal.ReadIntPtr(ev, OffData), OffRawDetail); }
                finally { XFreeEventData(display, ev); }
                if (keycode is < 8 or > 255) continue;
                if (XkbGetState(display, XkbUseCoreKbd, state) != 0) continue;
                int group = Marshal.ReadByte(state, 0), mods = Marshal.ReadByte(state, 6);
                if (Translate(mods, level => (uint)XkbKeycodeToKeysym(display, (byte)keycode, group, level)) is { } hit)
                {
                    try { sink(hit.Key, hit.Char); } catch { /* the sink posts; never into this loop */ }
                }
            }
        }
        catch (Exception ex) { Log.Debug("X11 key listener ended: {Error}", ex.Message); }   // no libXi: no typed triggers
        finally
        {
            if (ev != IntPtr.Zero) Marshal.FreeHGlobal(ev);
            if (state != IntPtr.Zero) Marshal.FreeHGlobal(state);
            if (mask != IntPtr.Zero) Marshal.FreeHGlobal(mask);
            if (display != IntPtr.Zero) { try { XCloseDisplay(display); } catch { } }
            lock (Gate) { if (Volatile.Read(ref _gen) == gen) _thread = null; }
        }
    }

    /// <summary>Modifier state + a keysym lookup (by shift level) to one buffer key. A Ctrl, Alt or
    /// Super chord is not text. Pure, so a test covers it without X.</summary>
    internal static (Key Key, char Char)? Translate(int mods, Func<int, uint> keysymAtLevel)
    {
        if ((mods & (ControlMask | Mod1Mask | Mod4Mask)) != 0) return null;
        var shift = (mods & ShiftMask) != 0;
        var level = (shift ? 1 : 0) + ((mods & Mod5Mask) != 0 ? 2 : 0);   // Mod5 = AltGr
        var ks = keysymAtLevel(level);
        if (ks == 0 && level != 0) ks = keysymAtLevel(0);
        // The number pad is a KEYPAD-type key: NumLock (Mod2 on every stock layout) picks level 1 and
        // Shift undoes it. Without this the pad's digits arrive as KP_End, KP_Down... and are dropped.
        if (ks is >= 0xFF80 and <= 0xFFBD && (mods & Mod2Mask) != 0)
        {
            var pad = keysymAtLevel(shift ? 0 : 1);
            if (pad != 0) ks = pad;
        }
        switch (ks)
        {
            case 0xFF0D or 0xFF8D or 0xFF09 or 0xFF1B: return (Key.Clear, '\0');   // Return, KP_Enter, Tab, Escape
            case 0xFF08: return (Key.Backspace, '\0');
            case 0x20 or 0xFF80: return (Key.Space, ' ');
        }
        char c;
        if (ks is > 0x20 and <= 0x7E or >= 0xA0 and <= 0xFF) c = (char)ks;               // Latin-1 keysyms are the code points
        else if (ks is >= 0xFFB0 and <= 0xFFB9) c = (char)('0' + (ks - 0xFFB0));          // keypad digits
        else if (ks is >= 0x01000100 and <= 0x0100FFFF) c = (char)(ks - 0x01000000);      // Unicode keysyms (BMP)
        else return null;
        if ((mods & LockMask) != 0 && char.IsLetter(c))
            c = shift ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
        return char.IsControl(c) ? null : (Key.Char, c);
    }
}
