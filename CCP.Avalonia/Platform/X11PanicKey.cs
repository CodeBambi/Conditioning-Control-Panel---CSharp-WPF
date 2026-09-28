using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The panic key's global listener on X11 / XWayland: the twin of WPF's WH_KEYBOARD_LL hook
/// (ConditioningControlPanel/Services/Input/GlobalKeyboardHook.cs).
///
/// <para><b>XInput2 raw key events, not XGrabKey.</b> The WPF hook is modifier-blind and does NOT
/// consume the keystroke (PanicPolicy.HookBoundBaseKeys). XGrabKey would swallow the key from every
/// other X client, so with the default Escape nothing else could see Escape while CCP runs.
/// <c>XI_RawKeyPress</c> selected on the root window is delivered to us AND the focused client,
/// regardless of modifiers - exactly the hook's semantics. Reach is the same as a grab: while any
/// X11 client (XWayland) has focus. Native Wayland windows are the portal's job.</para>
///
/// <para>Own display connection, used only by the listener thread, so no Xlib call ever races
/// the overlay shim's display. The configured key is re-resolved on that thread whenever the
/// setting changes, so a rebind needs no restart.</para>
/// </summary>
internal static class X11PanicKey
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXi = "libXi.so.6";
    // Values and offsets from the compiler against X11/extensions/XInput2.h on x86_64.
    private const int GenericEvent = 35, XI_RawKeyPress = 13, XIAllMasterDevices = 1;
    private const int XEventSize = 192, CookieExtension = 32, CookieEvType = 36, CookieData = 48, RawDetail = 56;

    [StructLayout(LayoutKind.Sequential)]
    private struct XIEventMask { public int DeviceId; public int MaskLen; public IntPtr Mask; }

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern bool XQueryExtension(IntPtr display, string name, out int opcode, out int evBase, out int errBase);
    [DllImport(LibX11)] private static extern int XNextEvent(IntPtr display, IntPtr ev);
    [DllImport(LibX11)] private static extern bool XGetEventData(IntPtr display, IntPtr cookie);
    [DllImport(LibX11)] private static extern void XFreeEventData(IntPtr display, IntPtr cookie);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] internal static extern ulong XStringToKeysym(string name);
    [DllImport(LibX11)] private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);
    [DllImport(LibXi)] private static extern int XIQueryVersion(IntPtr display, ref int major, ref int minor);
    [DllImport(LibXi)] private static extern int XISelectEvents(IntPtr display, IntPtr window, ref XIEventMask mask, int count);

    private static int _started;

    /// <summary>True once the listener is up: a panic press can actually arrive (LockCardWindow's
    /// strict-mode exit question, WPF PanicHook.IsInstalled).</summary>
    internal static bool IsListening => Volatile.Read(ref _started) == 1;

    /// <summary>Starts the listener once. <paramref name="onPress"/> runs on the listener thread for
    /// every press of <paramref name="currentKey"/>() (read per event, so rebinds apply live).
    /// False when there is no X display or no XInput 2.2 - the caller logs and relies on the tray.</summary>
    internal static bool Start(Func<string?> currentKey, Action onPress)
    {
        if (!OperatingSystem.IsLinux() || Interlocked.Exchange(ref _started, 1) == 1) return false;
        IntPtr display;
        int opcode;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero || !XQueryExtension(display, "XInputExtension", out opcode, out _, out _))
                return Fail("no X display or no XInputExtension");
            int major = 2, minor = 2;
            if (XIQueryVersion(display, ref major, ref minor) != 0) return Fail("XInput 2.2 unavailable");

            var bits = Marshal.AllocHGlobal(4);   // bit 13 (XI_RawKeyPress) lives in the first 4 mask bytes
            Marshal.WriteInt32(bits, 1 << XI_RawKeyPress);
            var mask = new XIEventMask { DeviceId = XIAllMasterDevices, MaskLen = 4, Mask = bits };
            XISelectEvents(display, XDefaultRootWindow(display), ref mask, 1);
            XFlush(display);
            Marshal.FreeHGlobal(bits);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return Fail(ex.Message);
        }

        new Thread(() => Listen(display, opcode, currentKey, onPress)) { IsBackground = true, Name = "panic-key-x11" }.Start();
        Log.Information("Panic key: listening for XInput2 raw key presses (X11/XWayland focus)");
        return true;

        bool Fail(string why)
        {
            _started = 0;
            Log.Warning("Panic key: X11 listener unavailable ({Why}); the tray's Stop everything is the panic control", why);
            return false;
        }
    }

    private static void Listen(IntPtr display, int opcode, Func<string?> currentKey, Action onPress)
    {
        var ev = Marshal.AllocHGlobal(XEventSize);
        string? boundName = null;
        int boundCode = 0;
        while (true)
        {
            try
            {
                XNextEvent(display, ev);
                if (Marshal.ReadInt32(ev) != GenericEvent || Marshal.ReadInt32(ev, CookieExtension) != opcode) continue;
                if (!XGetEventData(display, ev)) continue;
                int keycode = -1;
                if (Marshal.ReadInt32(ev, CookieEvType) == XI_RawKeyPress)
                    keycode = Marshal.ReadInt32(Marshal.ReadIntPtr(ev, CookieData), RawDetail);
                XFreeEventData(display, ev);
                if (keycode < 0) continue;

                var name = currentKey();
                if (name != boundName)
                {
                    boundName = name;
                    var sym = KeysymOf(name);
                    boundCode = sym == 0 ? 0 : XKeysymToKeycode(display, sym);
                    if (boundCode == 0) Log.Warning("Panic key: '{Key}' has no X keycode; only the tray can panic", name);
                }
                if (boundCode != 0 && keycode == boundCode) onPress();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Panic key: listener event failed");
            }
        }
    }

    // WPF/Avalonia Key names (the setting stores Key.ToString()) whose X keysym name differs.
    // ponytail: common keys only; anything else is tried verbatim, and an unknown one logs a warning.
    private static readonly Dictionary<string, string> XNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = "space", ["Back"] = "BackSpace", ["Enter"] = "Return", ["PageUp"] = "Prior", ["PageDown"] = "Next",
        ["Scroll"] = "Scroll_Lock", ["Capital"] = "Caps_Lock", ["CapsLock"] = "Caps_Lock", ["NumLock"] = "Num_Lock",
        ["Snapshot"] = "Print", ["PrintScreen"] = "Print", ["Multiply"] = "KP_Multiply", ["Add"] = "KP_Add",
        ["Subtract"] = "KP_Subtract", ["Divide"] = "KP_Divide", ["Decimal"] = "KP_Decimal",
        ["OemTilde"] = "grave", ["Oem3"] = "grave", ["OemMinus"] = "minus", ["OemPlus"] = "equal", ["OemComma"] = "comma",
        ["OemPeriod"] = "period", ["OemQuestion"] = "slash", ["Oem2"] = "slash", ["OemSemicolon"] = "semicolon",
        ["Oem1"] = "semicolon", ["OemQuotes"] = "apostrophe", ["Oem7"] = "apostrophe", ["OemOpenBrackets"] = "bracketleft",
        ["Oem4"] = "bracketleft", ["OemCloseBrackets"] = "bracketright", ["Oem6"] = "bracketright",
        ["OemPipe"] = "backslash", ["Oem5"] = "backslash", ["LeftCtrl"] = "Control_L", ["RightCtrl"] = "Control_R",
        ["LeftShift"] = "Shift_L", ["RightShift"] = "Shift_R", ["LeftAlt"] = "Alt_L", ["RightAlt"] = "Alt_R",
    };

    /// <summary>The X keysym for a stored key name ("Escape", "F8", "A", "D1", "NumPad0", "OemTilde"); 0 when unknown.</summary>
    internal static ulong KeysymOf(string? name) => string.IsNullOrWhiteSpace(name) ? 0 : XStringToKeysym(XKeyName(name));

    /// <summary>The XKB keysym NAME for a stored key name - also the GlobalShortcuts portal's trigger syntax.</summary>
    internal static string XKeyName(string name)
    {
        name = name.Trim();
        if (XNames.TryGetValue(name, out var x)) return x;
        if (name.Length == 1 && char.IsLetter(name[0])) return name.ToLowerInvariant();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) return name[1..];
        if (name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length == 7) return "KP_" + name[6];
        return name;
    }
}
