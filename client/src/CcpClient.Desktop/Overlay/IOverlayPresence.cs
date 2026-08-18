using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Overlay;

/// <summary>
/// An overlay presence: a surface that sits above every ordinary window on the desktop and lets
/// the user's clicks go straight through it to whatever they were aiming at.
///
/// <para><b>The contract that makes this capability worth having.</b> Every operation returns a
/// typed <see cref="CapabilityState"/> (SP-006, <c>runtime-capability-contract.md</c> §1), and
/// <see cref="CapabilityState.Available"/> is returned ONLY after the OPERATING SYSTEM was asked
/// back and confirmed the effect: that the window exists and is visible, that it holds the
/// geometry that was requested, that a non-zero alpha is held for it so the compositor draws it,
/// that the OS's own z-order puts it above every ordinary window, and that the window manager's
/// hit test routes a point inside it the way the request asked. A backend that cannot do those
/// things returns <see cref="CapabilityState.Unavailable"/> with an
/// <see cref="OverlayReasonCodes"/> code. It never returns a success that means "the method
/// ran".</para>
///
/// <para><b>Why that paragraph is the whole packet.</b> The first port attempt's overlay seam is
/// <c>void Show(); void Hide(); void SetClickThrough(bool)</c>
/// (<c>ConditioningControlPanel/CCP.Core/Platform/IOverlaySurface.cs</c>). Not one member of it
/// can report a refusal, so an overlay that never appeared and an overlay that covered the screen
/// were the same call — and its cross-platform click-through implementation is a method body
/// containing only a comment (<c>CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:31-35</c>).
/// <c>docs/constitution.md</c> classes that tree as failure evidence largely because of this
/// subsystem.</para>
///
/// <para><b>What Available still cannot mean.</b> That anything is DRAWN — this packet puts no
/// content on the surface and wires it to no effect. That a human SEES it: composited pixels
/// depend on DWM, on exclusive-fullscreen and DirectX applications, on Magnifier, on RDP and
/// mirror drivers, and every OS query above can answer yes while a screen shows nothing. And that
/// a real pointer passes through: the hit test is the window manager's routing question, asked;
/// it is not delivered input. Those three are headed claims
/// (<c>client/docs/verification-harness.md</c>) and nothing here discharges them.</para>
///
/// <para><b>What a caller does with Unavailable.</b> Not draw. There is no lesser overlay to
/// degrade to — a surface that is invisible, buried, or swallowing clicks is worse for the user
/// than no surface at all, because it breaks the desktop while looking implemented. A caller that
/// sees Unavailable reports the effect's visual half as absent, which is exactly what the Flash
/// Images module already does in plain words (<c>wpf-surface-reachability.md</c> D47).</para>
///
/// <para><b>Thread affinity.</b> A backend owns a native window, and a native window belongs to
/// the thread that created it. Call <see cref="Present"/>/<see cref="SetClickThrough"/>/
/// <see cref="Withdraw"/>/<see cref="IDisposable.Dispose"/> on one thread — the UI thread in the
/// app.</para>
/// </summary>
public interface IOverlayPresence : IDisposable
{
    /// <summary>
    /// Puts the surface on screen at the requested bounds (or moves and re-tints an
    /// already-presented one). <see cref="CapabilityState.Available"/> means the OS confirmed all
    /// six properties named on this interface, not that a call returned.
    /// </summary>
    CapabilityState Present(OverlaySurfaceRequest request);

    /// <summary>
    /// Flips input routing on a presented surface — WPF re-applies the same flag on the same live
    /// hwnd every spawn rather than at window creation, because a recycled window changes polarity
    /// between spawns (<c>Services/Flash/FlashService.cs:3654-3673</c>).
    ///
    /// <para><see cref="CapabilityState.Available"/> means the window manager's hit test was asked
    /// afterwards and answers the new way. It never means the style write returned.</para>
    /// </summary>
    CapabilityState SetClickThrough(bool clickThrough);

    /// <summary>
    /// Takes the surface off the screen, keeping the window for the next
    /// <see cref="Present"/>. <see cref="CapabilityState.Available"/> means the OS confirmed the
    /// window is no longer visible and the hit test no longer routes to it.
    /// </summary>
    CapabilityState Withdraw();

    /// <summary>True only while a surface this presence put on screen is confirmed present.</summary>
    bool IsPresenting { get; }
}
