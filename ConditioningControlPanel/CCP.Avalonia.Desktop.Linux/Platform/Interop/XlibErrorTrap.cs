using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;

/// <summary>
/// Scoped, CHAINED Xlib error trap (linux-overlay-contract.md §3.1 — mandatory).
///
/// Why this exists: Xlib errors are NOT .NET exceptions. They invoke a process-global
/// error handler whose DEFAULT implementation calls exit(), killing the whole app.
/// Realistic non-exceptional errors on the overlay path: BadWindow when
/// XFixesSetWindowShapeRegion / XSendEvent races window destruction (monitor hot-unplug
/// tears down and recreates compositor windows — this WILL race eventually), BadValue /
/// BadMatch from malformed rectangles, BadRegion from a stale region id.
///
/// Design:
/// - XSetErrorHandler is PROCESS-GLOBAL (not per-Display). We install exactly once and
///   never uninstall (uninstalling would clobber any handler installed after ours).
/// - Errors for displays REGISTERED here (our own dedicated XOpenDisplay connections)
///   are swallowed: the error code is recorded and 0 is returned.
/// - Errors for any OTHER display are CHAINED to the previously installed handler
///   (contract §7.1 row 5: Avalonia's X11 backend may install its own handler for its
///   display — clobbering it without chaining would break Avalonia's error recovery).
///   If there was no previous handler (default handler => XSetErrorHandler returned
///   IntPtr.Zero), we return 0 rather than replicating the default's exit().
/// - The handler delegate is rooted in a static field for the process lifetime so the
///   native function pointer never dangles.
///
/// Usage pattern (caller holds its own per-display lock so trap scopes on that display
/// never interleave): Reset() → issue Xlib requests → XSync(display, false) →
/// LastErrorCode. The recorded code is a single shared slot, which is sufficient because
/// each owning backend serializes all access to its display behind one lock and there is
/// one such backend per process today; if a second owned display is ever added
/// concurrently, promote the slot to a per-display map.
/// </summary>
internal static class XlibErrorTrap
{
    private static readonly object Gate = new();
    private static bool _installed;

    // Rooted for the process lifetime — the native side holds this function pointer forever.
    private static X11Interop.XErrorHandlerDelegate? _handlerKeepAlive;
    private static IntPtr _previousHandler;

    // Snapshot-swapped under Gate; read lock-free (and allocation-free) inside the handler.
    private static volatile IntPtr[] _ownedDisplays = Array.Empty<IntPtr>();

    private static int _lastErrorCode;

    /// <summary>
    /// Registers a display connection we own. Installs the process-global handler on
    /// first use (chaining whatever handler was previously installed).
    /// </summary>
    public static void RegisterDisplay(IntPtr display)
    {
        if (display == IntPtr.Zero) return;

        lock (Gate)
        {
            if (!_installed)
            {
                _handlerKeepAlive = HandleError;
                _previousHandler = X11Interop.XSetErrorHandler(_handlerKeepAlive);
                _installed = true;
            }

            if (Array.IndexOf(_ownedDisplays, display) < 0)
            {
                var next = new IntPtr[_ownedDisplays.Length + 1];
                Array.Copy(_ownedDisplays, next, _ownedDisplays.Length);
                next[^1] = display;
                _ownedDisplays = next;
            }
        }
    }

    /// <summary>
    /// Unregisters a display (call before XCloseDisplay). The handler itself stays
    /// installed — it is process-global and must keep chaining for other displays.
    /// </summary>
    public static void UnregisterDisplay(IntPtr display)
    {
        if (display == IntPtr.Zero) return;

        lock (Gate)
        {
            int idx = Array.IndexOf(_ownedDisplays, display);
            if (idx < 0) return;

            var next = new IntPtr[_ownedDisplays.Length - 1];
            for (int i = 0, j = 0; i < _ownedDisplays.Length; i++)
            {
                if (i != idx) next[j++] = _ownedDisplays[i];
            }
            _ownedDisplays = next;
        }
    }

    /// <summary>Clears the recorded error code. Call at the start of a trap scope.</summary>
    public static void Reset() => System.Threading.Volatile.Write(ref _lastErrorCode, 0);

    /// <summary>
    /// The last X error code recorded for an owned display since <see cref="Reset"/>
    /// (0 = none). Read AFTER XSync so pending errors have been delivered.
    /// </summary>
    public static int LastErrorCode => System.Threading.Volatile.Read(ref _lastErrorCode);

    private static int HandleError(IntPtr display, ref X11Interop.XErrorEvent errorEvent)
    {
        var owned = _ownedDisplays;
        for (int i = 0; i < owned.Length; i++)
        {
            if (owned[i] == display)
            {
                System.Threading.Volatile.Write(ref _lastErrorCode, errorEvent.ErrorCode);
                return 0; // swallow — never let our display's errors reach a killing handler
            }
        }

        if (_previousHandler != IntPtr.Zero)
        {
            var previous = Marshal.GetDelegateForFunctionPointer<X11Interop.XErrorHandlerDelegate>(_previousHandler);
            return previous(display, ref errorEvent);
        }

        // No previous handler means the DEFAULT handler (exit()) was installed. Returning 0
        // here deliberately does NOT replicate the process kill — losing an error report is
        // strictly better than losing the process.
        return 0;
    }
}
