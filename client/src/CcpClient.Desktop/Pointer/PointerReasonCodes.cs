namespace CcpClient.Desktop.Pointer;

/// <summary>
/// Stable machine-readable reason codes for the pointer capability
/// (<c>runtime-capability-contract.md</c> §1: codes are additive and land with their consumer).
/// They live beside their consumer for the same reason <c>Overlay/OverlayReasonCodes.cs</c> and
/// <c>Input/InputReasonCodes.cs</c> do.
///
/// <para><b>Why a clickable target needs its own vocabulary.</b> The overlay's failures are about a
/// surface nobody can see; the input presence's are about a question nobody is being asked. A
/// pointer target fails in a third way that neither names: it can be present, visible, on top and
/// perfectly composited while the window manager routes every click somewhere else — a target the
/// user can see and cannot hit. And it can fail in the opposite direction, by TAKING the foreground
/// it was placed specifically not to take, which is a failure that looks like success from inside
/// the process.</para>
/// </summary>
public static class PointerReasonCodes
{
    /// <summary>No pointer backend in this build can drive this platform, so nothing was attempted.
    /// Never "this platform cannot have clickable targets"; the detail names the route and the
    /// manual gate.</summary>
    public const string PointerMechanismAbsent = "pointer-mechanism-absent";

    /// <summary>This process has no interactive window station, no display, or no desktop, so a
    /// target would be an hwnd that satisfies every other check and reaches nobody.</summary>
    public const string PointerNoInteractiveStation = "pointer-no-interactive-station";

    /// <summary>The window class could not be registered, or <c>CreateWindowEx</c> refused. The
    /// detail carries the failing call and the Win32 last-error.</summary>
    public const string PointerWindowCreationFailed = "pointer-window-creation-failed";

    /// <summary>The target is not on screen where it was asked for: it does not exist, is not
    /// visible, or the OS holds a different rectangle than the one requested.</summary>
    public const string PointerTargetNotPlaced = "pointer-target-not-placed";

    /// <summary>
    /// The window manager's hit test at the target's own point does NOT return the target.
    ///
    /// <para>This is the code the whole capability exists for. It is the exact inverse of
    /// <c>OverlayReasonCodes.OverlayInputNotPassingThrough</c>: there, a click that failed to pass
    /// through broke the desktop underneath; here, a click that fails to arrive leaves a target the
    /// user can see and cannot hit, which upstream met in its own code and named in its own words —
    /// <i>"a now-clickable bubble stuck click-through (unpoppable)"</i>
    /// (<c>Services/BubbleService.cs:4880-4884</c>).</para>
    /// </summary>
    public const string PointerTargetNotRoutable = "pointer-target-not-routable";

    /// <summary>
    /// The OS does not hold <c>WS_EX_NOACTIVATE</c> for the target, or holds
    /// <c>WS_EX_TRANSPARENT</c> for it. Either one alone makes the target the wrong window: the
    /// first can steal the user's foreground on a click, the second swallows the click into the
    /// desktop.
    /// </summary>
    public const string PointerTargetStyleWrong = "pointer-target-style-wrong";

    /// <summary>
    /// The foreground window CHANGED across a pointer operation, or became the target.
    ///
    /// <para><b>The failure this capability was built to make impossible.</b> A bubble that takes
    /// the foreground has stolen the user's typing, their game, their video call — silently, and
    /// from inside a process that would otherwise report success. Upstream's whole non-activation
    /// apparatus (<c>ShowActivated = false</c> at <c>:2158</c>, <c>WS_EX_NOACTIVATE</c> at
    /// <c>:4887</c>, <c>SWP_NOACTIVATE</c> at <c>:4785</c> and <c>:4807</c>) exists to prevent
    /// exactly this, and none of it is a check.</para>
    /// </summary>
    public const string PointerTargetTookTheForeground = "pointer-target-took-the-foreground";

    /// <summary>The window exists and is visible and the OS's z-order does not put it above every
    /// ordinary window. A target under somebody else's window is a target nobody can hit.</summary>
    public const string PointerTargetNotOnTop = "pointer-target-not-on-top";

    /// <summary>Everything about routing held and the client area came back blank — the target is
    /// clickable and invisible. A <see cref="Capabilities.CapabilityState.Degraded"/> cause, never
    /// an <see cref="Capabilities.CapabilityState.Unavailable"/> one: the capability still holds
    /// everything it claimed and one non-fatal check failed.</summary>
    public const string PointerTargetBlank = "pointer-target-blank";

    /// <summary>A handle that this surface does not hold. Distinct from every failure above because
    /// it is a CALLER fault reported in type rather than a platform state.</summary>
    public const string PointerTargetUnknown = "pointer-target-unknown";

    /// <summary>The surface was disposed; its windows are gone and it will never place anything
    /// again.</summary>
    public const string PointerSurfaceDisposed = "pointer-surface-disposed";

    /// <summary>The surface already holds as many targets as it will place at once. A refusal
    /// rather than a silent drop, because a caller that keeps opening targets and never notices
    /// they stopped appearing is the failure this whole contract exists to forbid. The cap and its
    /// upstream provenance are on <c>Win32PointerSurface.MaxConcurrentTargets</c>.</summary>
    public const string PointerTargetLimitReached = "pointer-target-limit-reached";
}
