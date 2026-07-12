using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;

/// <summary>
/// Scoped, CHAINED Xlib error trap (linux-overlay-contract.md §3.1, and the mandatory trap
/// required by linux-framesource-contract.md §3.2).
///
/// Why this exists: Xlib errors are NOT .NET exceptions. They invoke a process-global
/// error handler whose DEFAULT implementation calls exit(), killing the whole app.
/// Realistic non-exceptional errors on the overlay path: BadWindow when
/// XFixesSetWindowShapeRegion / XSendEvent races window destruction (monitor hot-unplug
/// tears down and recreates compositor windows — this WILL race eventually), BadValue /
/// BadMatch from malformed rectangles, BadRegion from a stale region id. On the frame-source
/// path: BadMatch from XGetImage when the capture rect is not fully inside the root drawable
/// (monitor hot-unplug/resize between the ScreenInfo snapshot and capture — contract §3.2).
///
/// Design:
/// - XSetErrorHandler is PROCESS-GLOBAL (not per-Display). We install exactly once and
///   never uninstall (uninstalling would clobber any handler installed after ours).
/// - Errors for displays REGISTERED here (our own dedicated XOpenDisplay connections) are
///   swallowed: the error code is recorded and 0 is returned.
/// - Errors for any OTHER display are CHAINED to the previously installed handler
///   (contract §7.1 row 5: Avalonia's X11 backend may install its own handler for its
///   display — clobbering it without chaining would break Avalonia's error recovery).
///   If there was no previous handler (default handler => XSetErrorHandler returned
///   IntPtr.Zero), we return 0 rather than replicating the default's exit().
/// - The handler delegate is rooted in a static field for the process lifetime so the
///   native function pointer never dangles.
///
/// Error codes are recorded PER-DISPLAY. This was promoted from a single shared slot when
/// the frame-source backend registered a SECOND owned display alongside the overlay
/// backend's display (the prior single-slot comment read: "if a second owned display is
/// ever added concurrently, promote the slot to a per-display map"). Per-display codes
/// remove the misattribution race when the two backends' trap scopes interleave on their
/// separate connections (overlay traps on the compositor/UI thread; frame-source traps on
/// the capture thread).
///
/// Usage pattern (caller holds its own per-display lock so trap scopes on that display
/// never interleave): Reset(display) → issue Xlib requests → XSync(display, false) →
/// GetLastErrorCode(display). The legacy parameterless Reset()/LastErrorCode pair (used by
/// the overlay backend) targets the FIRST-registered display, which is the overlay's
/// connection at app startup.
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

    // Per-display recorded error codes. ConcurrentDictionary keeps the handler path lock-free
    // (taking a monitor lock inside an Xlib error handler risks deadlock). Writes are rare
    // (only on errors); reads happen once per trap scope after XSync.
    private static readonly ConcurrentDictionary<IntPtr, int> _codesByDisplay = new();

    // First display registered, targeted by the legacy parameterless Reset()/LastErrorCode
    // API (the overlay backend). The overlay registers at app startup, before any frame
    // source; frame-source backends use the explicit per-display overloads.
    private static IntPtr _legacyDisplay;

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

            // First registration wins — the overlay backend registers first at app startup,
            // so the parameterless Reset()/LastErrorCode pair keeps targeting its display.
            if (_legacyDisplay == IntPtr.Zero)
            {
                _legacyDisplay = display;
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

            _codesByDisplay.TryRemove(display, out _);

            // If the legacy display is going away, repoint the parameterless API at the next
            // remaining owned display (or zero, making the legacy calls no-ops). Keeps the
            // overlay's parameterless API valid as long as ANY owned display is live.
            if (display == _legacyDisplay)
            {
                _legacyDisplay = _ownedDisplays.Length > 0 ? _ownedDisplays[0] : IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Clears the recorded error code for <paramref name="display"/>. Call at the start of a
    /// trap scope (per-display overload — used by the frame-source backend).
    /// </summary>
    public static void Reset(IntPtr display)
    {
        if (display == IntPtr.Zero) return;
        _codesByDisplay[display] = 0;
    }

    /// <summary>
    /// The last X error code recorded for <paramref name="display"/> since the matching
    /// <see cref="Reset(IntPtr)"/> (0 = none). Read AFTER XSync so pending errors have been
    /// delivered (per-display overload — used by the frame-source backend).
    /// </summary>
    public static int GetLastErrorCode(IntPtr display)
        => display != IntPtr.Zero && _codesByDisplay.TryGetValue(display, out int code) ? code : 0;

    /// <summary>
    /// Clears the recorded error code for the first-registered (legacy) display. Legacy
    /// parameterless API — used by the overlay backend; prefer the per-display overload.
    /// </summary>
    public static void Reset() => Reset(System.Threading.Volatile.Read(ref _legacyDisplay));

    /// <summary>
    /// The last X error code recorded for the first-registered (legacy) display since
    /// <see cref="Reset()"/> (0 = none). Read AFTER XSync. Legacy parameterless API — used
    /// by the overlay backend; prefer the per-display overload.
    /// </summary>
    public static int LastErrorCode => GetLastErrorCode(System.Threading.Volatile.Read(ref _legacyDisplay));

    private static int HandleError(IntPtr display, ref X11Interop.XErrorEvent errorEvent)
    {
        var owned = _ownedDisplays;
        for (int i = 0; i < owned.Length; i++)
        {
            if (owned[i] == display)
            {
                // Per-display: record against the connection that actually errored.
                _codesByDisplay[display] = errorEvent.ErrorCode;
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
