using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Window owners for do-not-disturb (WPF DoNotDisturbGuard: GetForegroundWindow, RunningWindowedProcesses)
/// from EWMH <c>_NET_ACTIVE_WINDOW</c> / <c>_NET_CLIENT_LIST</c> / <c>_NET_WM_PID</c>. XCB, not Xlib: a
/// window closing mid-read is a per-request error, not Xlib's process-ending default handler; XCB is
/// thread-safe. X11/XWayland windows only: a native Wayland one reads as "not privileged".
/// </summary>
internal static class X11Windows
{
    private const string LibXcb = "libxcb.so.1";

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenIterator { public IntPtr Data; public int Rem; public int Index; }

    [DllImport(LibXcb)] private static extern IntPtr xcb_connect(IntPtr display, out int screen);
    [DllImport(LibXcb)] private static extern int xcb_connection_has_error(IntPtr c);
    [DllImport(LibXcb)] private static extern IntPtr xcb_get_setup(IntPtr c);
    [DllImport(LibXcb)] private static extern ScreenIterator xcb_setup_roots_iterator(IntPtr setup);
    [DllImport(LibXcb)] private static extern uint xcb_intern_atom(IntPtr c, byte onlyIfExists, ushort len, string name);
    [DllImport(LibXcb)] private static extern IntPtr xcb_intern_atom_reply(IntPtr c, uint cookie, out IntPtr error);
    [DllImport(LibXcb)] private static extern uint xcb_get_property(IntPtr c, byte delete, uint window, uint property, uint type, uint offset, uint length);
    [DllImport(LibXcb)] private static extern IntPtr xcb_get_property_reply(IntPtr c, uint cookie, out IntPtr error);
    [DllImport(LibXcb)] private static extern IntPtr xcb_get_property_value(IntPtr reply);
    [DllImport(LibXcb)] private static extern int xcb_get_property_value_length(IntPtr reply);
    [DllImport("libc")] private static extern void free(IntPtr p);

    private static readonly object Gate = new();
    private static IntPtr _conn;
    private static uint _root, _active, _clients, _pid;
    private static bool _failed;

    /// <summary>Normalised process name owning the active window, or "" (no X, no EWMH, or Wayland-native).</summary>
    internal static string ForegroundProcess()
    {
        if (!Connect()) return "";
        var w = Read(_root, _active, 1);
        return w.Count == 0 || w[0] == 0 ? "" : NameOf(w[0]);
    }

    /// <summary>Process names of every managed top-level window, de-duplicated, sorted, without
    /// this app (WPF RunningWindowedProcesses).</summary>
    internal static List<string> RunningWindowedProcesses()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Connect()) return new List<string>();
        string self;
        try { self = DndProcessList.Normalize(Process.GetCurrentProcess().ProcessName); } catch { self = ""; }
        foreach (var w in Read(_root, _clients, 4096))
        {
            var name = NameOf(w);
            if (name.Length > 0 && name != self) names.Add(name);
        }
        return new List<string>(names);
    }

    private static string NameOf(uint window)
    {
        var pid = Read(window, _pid, 1);
        if (pid.Count == 0 || pid[0] == 0) return "";
        try { using var p = Process.GetProcessById((int)pid[0]); return DndProcessList.Normalize(p.ProcessName); }
        catch { return ""; }   // exited, or a pid from another host / namespace
    }

    private static bool Connect()
    {
        lock (Gate)
        {
            if (_conn != IntPtr.Zero) return true;
            if (_failed || !OperatingSystem.IsLinux()) return false;
            try
            {
                var c = xcb_connect(IntPtr.Zero, out _);
                if (c == IntPtr.Zero || xcb_connection_has_error(c) != 0)
                {
                    _failed = true;
                    Log.Information("[DND] no X connection: the foreground app cannot be read");
                    return false;
                }
                var it = xcb_setup_roots_iterator(xcb_get_setup(c));
                // ponytail: first screen's root only (DISPLAY=:N without .S, i.e. every desktop here).
                _root = (uint)Marshal.ReadInt32(it.Data);   // xcb_screen_t.root is the first field
                _active = Atom(c, "_NET_ACTIVE_WINDOW");
                _clients = Atom(c, "_NET_CLIENT_LIST");
                _pid = Atom(c, "_NET_WM_PID");
                _conn = c;
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _failed = true;
                Log.Information("[DND] libxcb unavailable ({Why})", ex.Message);
                return false;
            }
        }
    }

    private static uint Atom(IntPtr c, string name)
    {
        var reply = xcb_intern_atom_reply(c, xcb_intern_atom(c, 0, (ushort)name.Length, name), out var err);
        if (err != IntPtr.Zero) free(err);
        if (reply == IntPtr.Zero) return 0;
        uint atom = (uint)Marshal.ReadInt32(reply, 8);   // xcb_intern_atom_reply_t.atom
        free(reply);
        return atom;
    }

    /// <summary>A 32-bit-format property as uint values; empty on any error (gone window, no property).</summary>
    private static List<uint> Read(uint window, uint property, uint maxItems)
    {
        var values = new List<uint>();
        if (window == 0 || property == 0) return values;
        var reply = xcb_get_property_reply(_conn, xcb_get_property(_conn, 0, window, property, 0, 0, maxItems), out var err);
        if (err != IntPtr.Zero) free(err);
        if (reply == IntPtr.Zero) return values;
        try
        {
            if (Marshal.ReadByte(reply, 1) != 32) return values;   // xcb_get_property_reply_t.format
            int n = xcb_get_property_value_length(reply) / 4;
            var data = xcb_get_property_value(reply);
            for (int i = 0; i < n; i++) values.Add((uint)Marshal.ReadInt32(data, i * 4));
            return values;
        }
        finally { free(reply); }
    }
}
