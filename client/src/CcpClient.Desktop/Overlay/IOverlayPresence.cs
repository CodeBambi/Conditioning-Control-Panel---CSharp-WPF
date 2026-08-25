using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Overlay;

/// <summary>
/// An overlay presence: a surface that sits above every ordinary window on the desktop and lets
/// the user's clicks go straight through it to whatever they were aiming at.
///
/// <para><b>The contract that makes this capability worth having.</b> Every operation returns a
/// typed <see cref="CapabilityState"/> (<c>runtime-capability-contract.md</c> §1), and
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
/// <para><b>What Available still cannot mean.</b> That a human SEES it: composited pixels depend on
/// DWM, on exclusive-fullscreen and DirectX applications, on Magnifier, on RDP and mirror drivers,
/// and every OS query above can answer yes while a screen shows nothing. And that a real pointer
/// passes through: the hit test is the window manager's routing question, asked; it is not
/// delivered input. Both are headed claims (<c>client/docs/verification-harness.md</c>) and nothing
/// here discharges them.</para>
///
/// <para><b>Content.</b> <see cref="Paint"/> puts pixels on a presented surface, and its
/// <see cref="CapabilityState.Available"/> is earned the same way as every other: the operating
/// system is asked for the surface's content back and returns the frame. That is one step further
/// than the presence check went and one step short of a human seeing it.</para>
///
/// <para><b>What a caller does with Unavailable.</b> Not draw. There is no lesser overlay to
/// degrade to — a surface that is invisible, buried, or swallowing clicks is worse for the user
/// than no surface at all, because it breaks the desktop while looking implemented. A caller that
/// sees Unavailable reports the effect's visual half as absent, which is exactly what the Flash
/// Images module already does in plain words (<c>wpf-surface-reachability.md</c> D47 @ 7527243e7).</para>
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
    /// Puts pixels on a presented surface.
    ///
    /// <para><see cref="CapabilityState.Available"/> means the operating system was asked for the
    /// surface's content BACK afterwards and returned the frame. It never means the blit returned
    /// TRUE — "the draw call succeeded" is the same grade of evidence as the first attempt's
    /// <c>void Show()</c>, and a surface that is present, on top, and holding nothing is the
    /// failure this whole capability exists to make impossible.</para>
    ///
    /// <para><b>This is the content path, and it is deliberately NOT
    /// <see cref="Present"/>.</b> <see cref="Present"/> walks the OS's top-level z-order and asks
    /// the window manager's hit test in both polarities; that is right once per placement and
    /// wrong per frame. <see cref="Paint"/> touches no style, walks no z-order and asks no hit
    /// test. A caller shows a surface once and paints it; it never re-presents to change
    /// content.</para>
    ///
    /// <para>The frame's pixel size must equal the presented surface's size
    /// (<see cref="OverlayReasonCodes.OverlayFrameSizeMismatch"/> otherwise): this capability
    /// scales nothing, because a scaler here would silently decide the port's DPI policy on a
    /// surface whose coordinates are physical pixels (divergence D55).</para>
    ///
    /// <para><b>HOW MUCH of it is read back, and this is the one place the answer lives.</b> The
    /// whole surface, on the first paint after anything about the WINDOW changed — a
    /// <see cref="Present"/>, a <see cref="SetClickThrough"/>, a resize, a <see cref="Reassert"/>,
    /// or a comparison that failed. One band of it, sweeping, on a frame that differs from the one
    /// before it in NOTHING BUT CONTENT (<c>Overlay/Win32OverlayPresence.cs</c>
    /// <c>ContentBands</c>). What is unconditional is that the OS is asked and answers on EVERY
    /// frame: a surface that is on screen and does not hold what was drawn into it is still
    /// detected, and its caller still takes it down (<c>Effects/OverlaySurfaceSet.cs:344-352</c>).
    /// The reason is measured and not a preference — the full read-back is 4.6 ms of the UI thread
    /// per frame at 2880x1800, and a moving surface repaints tens of times a second.</para>
    /// </summary>
    CapabilityState Paint(OverlayFrame frame);

    /// <summary>
    /// Re-asserts the topmost band for a surface that is already on screen — WPF's
    /// <c>ForceTopmost</c> (<c>Services/Flash/FlashService.cs:3867</c>), which the shipping product
    /// drives on a roughly one-second cadence because the band is contested and an already-showing
    /// flash is otherwise briefly buried (<c>:206-243</c>).
    ///
    /// <para><b>It returns nothing, on purpose.</b> It is one <c>SetWindowPos</c> and it confirms
    /// NOTHING: it does not re-ask the z-order, the hit test or the alpha. Returning a
    /// <see cref="CapabilityState"/> here would be a claim with no round-trip behind it, which is
    /// the shape this interface exists to refuse. A caller that needs the fact calls
    /// <see cref="Present"/>, which earns it.</para>
    /// </summary>
    void Reassert();

    /// <summary>
    /// Takes the surface off the screen, keeping the window for the next
    /// <see cref="Present"/>. <see cref="CapabilityState.Available"/> means the OS confirmed the
    /// window is no longer visible and the hit test no longer routes to it.
    ///
    /// <para><b>The window is kept; the CONTENT is not.</b> Whatever a backend allocated to hold
    /// the last frame is given back here, not at <see cref="IDisposable.Dispose"/>. Callers pool
    /// presences and reuse them for the life of a session
    /// (<c>Effects/OverlaySurfaceSet.cs:238-258</c>), so "freed at Dispose" means "held until the
    /// session ends" — and a frame is as large as the surface, which at the image-scale dial's
    /// ceiling is the whole monitor. A caller that needs the same pixels again paints them again.</para>
    /// </summary>
    CapabilityState Withdraw();

    /// <summary>True only while a surface this presence put on screen is confirmed present.</summary>
    bool IsPresenting { get; }
}
