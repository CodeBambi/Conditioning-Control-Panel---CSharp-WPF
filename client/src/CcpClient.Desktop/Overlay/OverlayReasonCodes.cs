namespace CcpClient.Desktop.Overlay;

/// <summary>
/// Stable machine-readable reason codes for the overlay capability (runtime-capability-contract
/// §1: "codes are additive; new codes land with their consumer row"). They live beside their
/// consumer for the same reason <see cref="Tray.TrayReasonCodes"/> does: a feature-local code
/// needs no edit to a shared file.
///
/// <para><b>Why there are so many of them.</b> The first port attempt had exactly one outcome for
/// an overlay — nothing — because every operation returned <c>void</c>
/// (<c>CCP.Core/Platform/IOverlaySurface.cs</c>). A surface fails in ways that are invisible from
/// inside the process and completely unlike one another: it can exist and be invisible, exist and
/// be buried, exist and swallow every click. Those need different codes, because a caller and a
/// bug report both need to tell them apart.</para>
/// </summary>
public static class OverlayReasonCodes
{
    /// <summary>
    /// No overlay backend in this build can drive this platform, so nothing was attempted.
    /// Never means "this platform cannot have an overlay"; it means this build has not
    /// implemented and verified one, and the detail names the route and the manual gate.
    /// </summary>
    public const string OverlayMechanismAbsent = "overlay-mechanism-absent";

    /// <summary>The window class could not be registered, or <c>CreateWindowEx</c> refused. The
    /// detail carries the failing call and the Win32 last-error.</summary>
    public const string OverlayWindowCreationFailed = "overlay-window-creation-failed";

    /// <summary>
    /// The window exists but the OS holds no non-zero alpha for it, so the compositor draws
    /// nothing: a window that is present, on top, and invisible.
    ///
    /// <para>This is the code that catches the first attempt's exact defect.
    /// <c>CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45</c> sets
    /// <c>WS_EX_LAYERED</c> and never calls <c>SetLayeredWindowAttributes</c>. Measured on this
    /// machine before this file was written (the packet record.md): such a window reports
    /// <c>IsWindowVisible = TRUE</c> while <c>GetLayeredWindowAttributes</c> returns FALSE.</para>
    /// </summary>
    public const string OverlayNotComposited = "overlay-not-composited";

    /// <summary>The window exists and is visible, but the OS's own top-level z-order does not put
    /// it above every ordinary window. An overlay nobody can see is not an overlay.</summary>
    public const string OverlayNotOnTop = "overlay-not-on-top";

    /// <summary>
    /// Click-through was asked for, and the window manager's hit test still routes a point inside
    /// the surface to the surface. Clicks are being swallowed: the desktop underneath is broken
    /// while the overlay looks implemented.
    /// </summary>
    public const string OverlayInputNotPassingThrough = "overlay-input-not-passing-through";

    /// <summary>
    /// The mirror case, and the one that keeps the click-through claim honest: the surface was
    /// asked to CATCH input (or was momentarily made opaque to prove it could) and the hit test
    /// does not route the point to it. Without this, "the point does not route to us" would be
    /// satisfied just as well by a window that is not there at all.
    /// </summary>
    public const string OverlayInputNotReceived = "overlay-input-not-received";

    /// <summary>The OS holds a different rectangle than the one that was asked for.</summary>
    public const string OverlayGeometryRefused = "overlay-geometry-refused";

    /// <summary>The extended-style read-back is missing a bit that was written. The window is not
    /// the window that was asked for, whatever the write calls returned.</summary>
    public const string OverlayStyleRefused = "overlay-style-refused";

    /// <summary>The window exists but the OS does not report it visible.</summary>
    public const string OverlayNotVisible = "overlay-not-visible";

    /// <summary>Showing the surface moved the foreground window. An overlay that takes focus
    /// interrupts whatever the user was typing into; WPF's flash windows carry
    /// <c>WS_EX_NOACTIVATE</c> and <c>ShowActivated = false</c> to prevent exactly that
    /// (<c>Services/Flash/FlashService.cs:3617</c>, <c>:3666</c>, <c>:3826</c>).</summary>
    public const string OverlayStoleFocus = "overlay-stole-focus";

    /// <summary>An operation that needs a presented surface was asked while none is presented.
    /// Reporting success here would be a claim for work that did not happen.</summary>
    public const string OverlayNothingPresented = "overlay-nothing-presented";

    /// <summary>
    /// The surface was asked to leave the screen and the OS still reports it visible, or its hit
    /// test still routes to it. Symmetric to the placement checks on purpose: a withdraw that is
    /// only reported is how an overlay outlives the effect that owned it.
    /// </summary>
    public const string OverlayWithdrawRefused = "overlay-withdraw-refused";

    /// <summary>The presence was disposed; its window is gone and it will never present another.
    /// Truthful terminal state, never a silent no-op.</summary>
    public const string OverlayPresenceDisposed = "overlay-presence-disposed";

    /// <summary>
    /// The OS enumerated no display, so there is no rectangle a surface could legally be placed
    /// on. Distinct from <see cref="OverlayMechanismAbsent"/> on purpose: this build may have a
    /// perfectly good backend and simply have nowhere to put a window (a session with no attached
    /// display), and a caller's response to the two is different.
    /// </summary>
    public const string OverlayNoDisplay = "overlay-no-display";

    /// <summary>
    /// A frame was offered whose pixel size is not the presented surface's size. Stretching it
    /// would put a rectangle on the user's screen that nobody asked for and would hide a stale
    /// frame behind a plausible picture; refusing names the mismatch instead.
    /// </summary>
    public const string OverlayFrameSizeMismatch = "overlay-frame-size-mismatch";

    /// <summary>
    /// The drawing mechanism itself refused: no device context, no DIB section, or the blit
    /// returned FALSE. The detail carries the failing call and the Win32 last-error.
    /// </summary>
    public const string OverlayPaintRefused = "overlay-paint-refused";

    /// <summary>
    /// The blit reported success and the OS does NOT hold the frame's pixels in the surface
    /// afterwards. This is the draw-side twin of
    /// <see cref="OverlayNotComposited"/>: without it, "the surface was painted" would mean
    /// "BitBlt returned TRUE", which is the same grade of evidence as the first attempt's
    /// <c>void Show()</c>.
    /// </summary>
    public const string OverlayContentNotHeld = "overlay-content-not-held";
}
