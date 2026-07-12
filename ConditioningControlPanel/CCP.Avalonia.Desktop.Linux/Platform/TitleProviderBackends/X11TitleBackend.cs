using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.TitleProviderBackends;

/// <summary>
/// X11 foreground-window-title backend: reads <c>_NET_ACTIVE_WINDOW</c> on the root window,
/// then the active window's <c>_NET_WM_NAME</c> (UTF-8, type-checked) with a <c>WM_NAME</c>
/// fallback (linux-foreground-title-contract.md §3.1-3.5). This is the FULL-FUNCTIONALITY
/// backend on a native X11 session (i3, XFCE, MATE, Cinnamon, KDE-Plasma-X11, GNOME-Xorg).
/// </summary>
/// <remarks>
/// <para><b>Connection &amp; threading (§3.1):</b> owns a DEDICATED <c>XOpenDisplay</c>
/// connection (never touches Avalonia's display). Xlib is not thread-safe and
/// <c>XInitThreads</c> cannot be guaranteed to run first next to Avalonia's own X11 use, so
/// ALL access is serialized behind <see cref="_xLock"/>. The awareness engine polls from
/// threadpool threads, possibly overlapping if a poll stalls — the lock keeps the single
/// connection touched by one thread at a time.</para>
///
/// <para><b>Mandatory error trap (§3.1):</b> the active window can be destroyed between
/// reading <c>_NET_ACTIVE_WINDOW</c> and reading its <c>_NET_WM_NAME</c> — a BadWindow race
/// that WILL eventually fire on a 1.5s ambient poll, and the DEFAULT Xlib error handler
/// TERMINATES THE PROCESS. Every read runs inside a scoped <see cref="XlibErrorTrap"/>
/// (Reset → reads → <c>XSync</c> → check); a trapped error returns <c>null</c> for that
/// poll. The handler is the process-global chained trap shared with the overlay backends
/// (it chains to Avalonia's own handler for foreign displays).</para>
///
/// <para><b>Wayland limitation (§3.5, do NOT delete):</b> under XWayland the root
/// <c>_NET_ACTIVE_WINDOW</c> is maintained by the compositor's XWM for X11 windows ONLY;
/// when a native Wayland window is focused it points at none. The selector therefore never
/// routes a Wayland/XWayland session here (§2.1). If it ever does, this backend reports
/// partial/stale data — it is correct-but-partial on Wayland by design. The
/// wlr-foreign-toplevel-management backend (native Wayland titles + activated state) is a
/// documented WAVE-3 gap.</para>
///
/// <para><b>Privacy (§1.3):</b> the title is memory-only input for activity classification.
/// This backend never logs title content — log lines carry backend status and error codes
/// only. No PID/process enumeration; the seam returns the title string only.</para>
/// </remarks>
internal sealed class X11TitleBackend : ILinuxTitleProviderBackend
{
    /// <summary>
    /// Maximum returned title length in CHARACTERS, for parity with the Windows 512-char
    /// bound (§1.2) and to bound the memory the privacy contract governs.
    /// </summary>
    private const int MaxTitleChars = 512;

    /// <summary>
    /// Property read length in 32-BIT UNITS (§3.3: long_length is in 32-bit multiples, not
    /// bytes). 128 units = 512 bytes of UTF-8, enough for the 512-char cap on typical text.
    /// </summary>
    private const long TitleLongLength = 128;

    private readonly ILogger? _logger;
    private readonly object _xLock = new();

    private IntPtr _display;
    private IntPtr _root;
    private IntPtr _atomNetActiveWindow;
    private IntPtr _atomNetWmName;
    private IntPtr _atomUtf8String;
    private IntPtr _atomWmName;
    private bool _disposed;

    public X11TitleBackend(ILogger? logger = null)
    {
        _logger = logger;
        OpenDisplayAndInternAtoms();
    }

    public string Name => "X11TitleBackend";

    /// <summary>
    /// True when a dedicated X display connection opened AND the window manager advertises
    /// EWMH (<c>_NET_ACTIVE_WINDOW</c> atom present). The selector uses this to decide
    /// reuse-vs-fallback; a non-EWMH desktop routes to <see cref="FallbackTitleBackend"/>
    /// (both return null, but the fallback's reason is more descriptive).
    /// </summary>
    public bool IsAvailable => _display != IntPtr.Zero && _atomNetActiveWindow != IntPtr.Zero;

    public string? GetForegroundWindowTitle()
    {
        if (_disposed) return null;
        // §3.2: if the WM does not implement EWMH there is no _NET_ACTIVE_WINDOW — return
        // null (awareness classifies Unknown). This is the bare-X / no-WM case.
        if (_display == IntPtr.Zero || _atomNetActiveWindow == IntPtr.Zero) return null;

        try
        {
            lock (_xLock)
            {
                if (_display == IntPtr.Zero) return null;

                // §3.1: one scoped error trap wraps the active-window read AND the title read.
                // The BadWindow race is between these two reads; XSync at the end delivers any
                // pending X error, and a non-zero code → null for this poll (no process death).
                // PER-DISPLAY overload: the legacy parameterless Reset()/LastErrorCode pair
                // targets the FIRST-registered display, which is whichever backend (overlay,
                // title, frame-source) happened to construct first in DI — reading it here
                // would miss this display's errors AND clear another backend's trap scope.
                XlibErrorTrap.Reset(_display);

                IntPtr activeWindow = ReadActiveWindowLocked();
                string? title = activeWindow == IntPtr.Zero
                    ? null // desktop focused / none focused / WM returned None (§3.2)
                    : ReadWindowTitleLocked(activeWindow);

                X11Interop.XSync(_display, false);
                int err = XlibErrorTrap.GetLastErrorCode(_display);
                if (err != 0)
                {
                    // §3.1: trapped error (e.g. BadWindow = 3 when the active window was
                    // destroyed mid-read). Null for this poll; the next poll retries cleanly.
                    _logger?.LogDebug(
                        "X11TitleBackend: trapped X error {ErrorCode} during title read; returning null for this poll",
                        err);
                    return null;
                }

                return Truncate(title);
            }
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogWarning("X11TitleBackend: libX11 not found: {Message}", ex.Message);
            return null;
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger?.LogWarning("X11TitleBackend: missing Xlib entry point: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11TitleBackend: GetForegroundWindowTitle faulted");
            return null;
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>. Caller owns the error-trap scope.</summary>
    private IntPtr ReadActiveWindowLocked()
    {
        // §3.2: read 1 item of type XA_WINDOW (33) from the root's _NET_ACTIVE_WINDOW.
        int status = X11Interop.XGetWindowProperty(
            _display, _root, _atomNetActiveWindow,
            0, 1, false, X11Interop.XA_WINDOW,
            out IntPtr type, out int format, out ulong nItems, out _, out IntPtr prop);
        // XFree whenever Xlib handed us a buffer — on Success with a TYPE MISMATCH Xlib
        // still allocates (nItems == 0, prop != NULL); the free must not be gated on nItems.
        if (prop == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            if (status != X11Interop.Success || nItems == 0)
            {
                return IntPtr.Zero;
            }

            // format 32 → each item is an 8-byte long on LP64; read as IntPtr.
            return Marshal.ReadIntPtr(prop);
        }
        finally
        {
            X11Interop.XFree(prop);
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>. Caller owns the error-trap scope.</summary>
    private string? ReadWindowTitleLocked(IntPtr window)
    {
        // §3.3: _NET_WM_NAME (UTF8_STRING) first; verify the returned type matches. Modern
        // toolkits all set this.
        string? title = ReadUtf8PropertyLocked(window, _atomNetWmName, _atomUtf8String);
        if (!string.IsNullOrEmpty(title)) return title;

        if (_atomWmName == IntPtr.Zero) return null;

        // §3.3: WM_NAME fallback (AnyPropertyType). PtrToStringAnsi decodes UTF-8 on Unix
        // .NET; genuine Latin-1/COMPOUND_TEXT titles may mojibake — acceptable degrade
        // (memory-only classification input, never persisted).
        return ReadAnsiPropertyLocked(window, _atomWmName);
    }

    /// <summary>Must be called under <see cref="_xLock"/>. Caller owns the error-trap scope.</summary>
    private string? ReadUtf8PropertyLocked(IntPtr window, IntPtr property, IntPtr expectedType)
    {
        if (property == IntPtr.Zero || expectedType == IntPtr.Zero) return null;

        int status = X11Interop.XGetWindowProperty(
            _display, window, property,
            0, TitleLongLength, false, expectedType,
            out IntPtr type, out _, out ulong nItems, out _, out IntPtr prop);
        // XFree whenever Xlib handed us a buffer — on Success with a TYPE MISMATCH (property
        // exists but is not UTF8_STRING) Xlib still allocates a buffer with nItems == 0;
        // gating the free on nItems leaked it once per 1.5s poll on such windows.
        if (prop == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (status != X11Interop.Success || nItems == 0) return null;
            if (type != expectedType) return null; // §3.3: verify returned type for the UTF-8 path
            return Marshal.PtrToStringUTF8(prop);
        }
        finally
        {
            X11Interop.XFree(prop);
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>. Caller owns the error-trap scope.</summary>
    private string? ReadAnsiPropertyLocked(IntPtr window, IntPtr property)
    {
        if (property == IntPtr.Zero) return null;

        int status = X11Interop.XGetWindowProperty(
            _display, window, property,
            0, TitleLongLength, false, X11Interop.AnyPropertyType,
            out _, out _, out ulong nItems, out _, out IntPtr prop);
        // XFree whenever Xlib handed us a buffer (see ReadUtf8PropertyLocked — never gate
        // the free on nItems).
        if (prop == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (status != X11Interop.Success || nItems == 0) return null;
            return Marshal.PtrToStringAnsi(prop);
        }
        finally
        {
            X11Interop.XFree(prop);
        }
    }

    /// <summary>§1.2/§3.3: cap the returned title at <see cref="MaxTitleChars"/> characters.</summary>
    private static string? Truncate(string? title) =>
        string.IsNullOrEmpty(title) || title!.Length <= MaxTitleChars
            ? title
            : title.Substring(0, MaxTitleChars);

    private void OpenDisplayAndInternAtoms()
    {
        try
        {
            lock (_xLock)
            {
                _display = X11Interop.XOpenDisplay(null);
                if (_display == IntPtr.Zero)
                {
                    _logger?.LogWarning("X11TitleBackend: cannot open X display");
                    return;
                }

                // Install the process-kill guard BEFORE any request that could error.
                XlibErrorTrap.RegisterDisplay(_display);
                _root = X11Interop.XDefaultRootWindow(_display);

                // §3.2: intern atoms ONCE at init. only_if_exists=true — EWMH atoms may not
                // exist under a non-EWMH WM (bare X, no openbox); _NET_ACTIVE_WINDOW absent
                // means no foreground-window support and IsAvailable stays false.
                _atomNetActiveWindow = X11Interop.XInternAtom(_display, "_NET_ACTIVE_WINDOW", true);
                _atomNetWmName = X11Interop.XInternAtom(_display, "_NET_WM_NAME", true);
                _atomUtf8String = X11Interop.XInternAtom(_display, "UTF8_STRING", true);
                _atomWmName = X11Interop.XInternAtom(_display, "WM_NAME", true);

                _logger?.LogDebug(
                    "X11TitleBackend: opened display (EWMH active: {Ewmh})",
                    _atomNetActiveWindow != IntPtr.Zero);
            }
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogWarning("X11TitleBackend: libX11 not found: {Message}", ex.Message);
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger?.LogWarning("X11TitleBackend: missing Xlib entry point: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11TitleBackend: failed to open display / intern atoms");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_xLock)
        {
            if (_display == IntPtr.Zero) return;

            XlibErrorTrap.UnregisterDisplay(_display);
            try
            {
                X11Interop.XCloseDisplay(_display);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "X11TitleBackend: XCloseDisplay failed");
            }

            _display = IntPtr.Zero;
            _root = IntPtr.Zero;
            _atomNetActiveWindow = IntPtr.Zero;
            _atomNetWmName = IntPtr.Zero;
            _atomUtf8String = IntPtr.Zero;
            _atomWmName = IntPtr.Zero;
        }
    }
}
