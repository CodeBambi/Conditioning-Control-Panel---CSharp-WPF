using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Pointer;

/// <summary>
/// A rectangle on the desktop, in PHYSICAL pixels — the same coordinate space and the same reason
/// as <c>Overlay/OverlaySurfaceRequest.cs</c>'s <c>OverlayBounds</c>: this capability owns Win32
/// top-level windows and a Win32 top-level window's coordinates are physical pixels.
/// </summary>
public readonly record struct PointerBounds(int X, int Y, int Width, int Height)
{
    /// <summary>The centre point. This is where the routing question is asked, and it is also the
    /// furthest point inside the rectangle from every edge — which is what makes the one-step
    /// staleness bound in <see cref="IPointerSurface"/> arithmetic rather than a hope.</summary>
    public (int X, int Y) Centre => (X + (Width / 2), Y + (Height / 2));

    /// <summary>Half the shorter side: the largest displacement that can be applied to this
    /// rectangle while its own centre stays inside it.</summary>
    public int Radius => Math.Min(Width, Height) / 2;

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>What a caller wants on the desktop: a rectangle that catches a click and takes
/// nothing.</summary>
/// <param name="Bounds">Where the target goes. Both extents must be positive.</param>
/// <param name="Fill">The background the whole client area is filled with, 0x00RRGGBB. The ink
/// read-back is a DIFFERENTIAL against this exact value, so it is not decoration: without a fill
/// this process controls, "these pixels are not the background" is satisfied by an unpainted
/// window whose device context holds whatever the OS last left in it (SP-110's M-t).</param>
/// <param name="Ink">The colour the disc is drawn in. Must differ from <paramref name="Fill"/>, or
/// the content check could never distinguish a painted target from a blank one.</param>
public sealed record PointerTargetRequest(PointerBounds Bounds, uint Fill, uint Ink)
{
    /// <summary>The smallest target this capability will place, per side. Upstream's own floor for
    /// a bubble is 60 DIP and its stated reason is the one that matters here — <i>"a bubble is a
    /// MOVING click target, and below roughly this it stops being fair to ask someone to hit it"</i>
    /// (<c>Services/BubbleSizing.cs:70</c>). A capability that placed a four-pixel target would
    /// satisfy every OS question in this file and be unhittable.</summary>
    public const int MinimumSide = 60;

    public PointerBounds Bounds { get; } = Validate(Bounds);

    public uint Ink { get; } = ValidateInk(Fill, Ink);

    private static PointerBounds Validate(PointerBounds bounds)
    {
        if (bounds.Width < MinimumSide || bounds.Height < MinimumSide)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds), bounds,
                $"a pointer target is at least {MinimumSide}x{MinimumSide} physical pixels; "
                + "anything smaller routes and cannot be hit");
        }

        return bounds;
    }

    private static uint ValidateInk(uint fill, uint ink)
    {
        if ((fill & 0x00FFFFFF) == (ink & 0x00FFFFFF))
        {
            throw new ArgumentException(
                "the ink colour must differ from the fill colour, or the content read-back could "
                + "never tell a painted target from a blank window", nameof(ink));
        }

        return ink;
    }
}

/// <summary>
/// A press the OPERATING SYSTEM routed to one target's own window procedure.
/// </summary>
/// <param name="Target">Which target's window the message arrived at. <b>The OS chose this, not
/// this process:</b> each target is its own top-level window, so the window manager's own hit test
/// at the instant of the click decides which procedure runs. That is the whole reason for the
/// per-target window and it is what removes upstream's snapshot race from the product
/// (<c>Services/BubbleService.cs</c> primer §4c/§4e — the hosted path decides in user space from a
/// disc snapshot rebuilt once per UI tick).</param>
/// <param name="Kind">Which half of the click this is.</param>
/// <param name="X">Client-relative x, from the message's own lParam.</param>
/// <param name="Y">Client-relative y, same.</param>
public readonly record struct PointerPress(int Target, PointerPressKind Kind, int X, int Y);

/// <summary>The two halves of a click, kept apart because they are different facts: a button that
/// went down and never came up is a drag, a wedge, or an injection that half-arrived.</summary>
public enum PointerPressKind
{
    /// <summary><c>WM_LBUTTONDOWN</c>.</summary>
    Down,

    /// <summary><c>WM_LBUTTONUP</c>.</summary>
    Up,
}

/// <summary>
/// What the OS says about this process's ability to put a pointer target on a desktop at all,
/// asked BEFORE any window is created. Same shape and same purpose as
/// <c>Input/IInputPresence.cs</c>'s station observation: a process with no interactive window
/// station can create an hwnd that satisfies every other check and reaches nobody.
/// </summary>
/// <param name="Asked">False means nothing was attempted, so nothing below means anything.</param>
/// <param name="WindowStationVisible">The process window station carries <c>WSF_VISIBLE</c>.</param>
/// <param name="DisplayCount"><c>SM_CMONITORS</c>. Zero displays means no desktop to aim at.</param>
/// <param name="DesktopReachable">This thread has a desktop.</param>
public readonly record struct PointerStationObservation(
    bool Asked,
    bool WindowStationVisible,
    int DisplayCount,
    bool DesktopReachable)
{
    /// <summary>Nothing was asked. The only value this type has on a non-Windows host.</summary>
    public static PointerStationObservation NotAsked => new(false, false, 0, false);

    /// <summary>Every necessary condition for a pointer target to reach a human.</summary>
    public bool Confirmed => Asked && WindowStationVisible && DisplayCount >= 1 && DesktopReachable;
}

/// <summary>
/// Everything the operating system said about ONE target, at one moment.
///
/// <para>Every member is an answer to a question asked of the OS after the fact, never a value this
/// process remembered writing.</para>
/// </summary>
/// <param name="Asked">False means no question was put to the OS.</param>
/// <param name="Window">The hwnd the answers are about, so a fact can re-ask independently.</param>
/// <param name="WindowExists"><c>IsWindow</c>.</param>
/// <param name="WindowVisible"><c>IsWindowVisible</c>.</param>
/// <param name="HeldBounds">The rectangle the OS holds, which is compared against the one asked
/// for. A target the OS moved is a target whose routing answer is about somewhere else.</param>
/// <param name="NonActivatingStyleHeld">The OS holds <c>WS_EX_NOACTIVATE</c> for this window —
/// upstream Bubble Pop's own mechanism (<c>Services/BubbleService.cs:4887</c>, constant
/// <c>:4899</c>), read back out of the OS rather than remembered.</param>
/// <param name="ClickThroughStyleHeld">The OS holds <c>WS_EX_TRANSPARENT</c>. For a target that is
/// supposed to CATCH clicks this must be false, and upstream says why in its own words: <i>"WPF's
/// IsHitTestVisible alone doesn't prevent the window from eating clicks"</i> and its converse —
/// a recycled shell that kept the flag is <i>"a now-clickable bubble stuck click-through
/// (unpoppable)"</i> (<c>:4880-4892</c>).</param>
/// <param name="AboveEveryOrdinaryWindow">The OS's own top-level z-order walk. Not "above every
/// window": another topmost window legitimately contests the band, which is SP-099's wording and
/// SP-099's reason.</param>
/// <param name="HitTestWinner">What <c>WindowFromPoint</c> at <paramref name="HitTestPoint"/>
/// returned.</param>
/// <param name="HitTestPoint">Where it was asked. Carried so a reader can tell WHICH point an
/// answer is about — the answer is a function of a position, and the position moves.</param>
/// <param name="ForegroundBefore"><c>GetForegroundWindow()</c> sampled before the operation.</param>
/// <param name="ForegroundAfter">The same read after it. <b>These two are the non-activation
/// claim.</b></param>
/// <param name="BackgroundHeld">A control point in a margin the painter fills and never draws on
/// read back EXACTLY the requested fill. Without this leg, "these pixels are not the background" is
/// satisfied by a window nothing ever painted (SP-110's M-t, and the same remedy).</param>
/// <param name="InkedPixels">How many sampled pixels in the disc band differ from the fill.</param>
/// <param name="SampledPixels">How many were sampled, so a zero can be told from an unread band.</param>
public readonly record struct PointerTargetObservation(
    bool Asked,
    nint Window,
    bool WindowExists,
    bool WindowVisible,
    PointerBounds HeldBounds,
    bool NonActivatingStyleHeld,
    bool ClickThroughStyleHeld,
    bool AboveEveryOrdinaryWindow,
    nint HitTestWinner,
    (int X, int Y) HitTestPoint,
    nint ForegroundBefore,
    nint ForegroundAfter,
    bool BackgroundHeld,
    int InkedPixels,
    int SampledPixels)
{
    /// <summary>Nothing was asked.</summary>
    public static PointerTargetObservation NotAsked => new(
        false, 0, false, false, default, false, false, false, 0, (0, 0), 0, 0, false, 0, 0);

    /// <summary>The window manager routes a click at this target's own point TO this target.
    /// <b>The exact inverse of SP-099</b>, asked per target.</summary>
    public bool HitTestRoutesHere => Window != 0 && HitTestWinner == Window;

    /// <summary>
    /// The foreground did not move across the operation AND is not this window.
    ///
    /// <para><b>Both halves are load-bearing and they are different claims.</b> "Not us" alone is
    /// satisfied by a target that stole the foreground and lost it again a microsecond later;
    /// "unchanged" alone is satisfied by a target that was already the foreground when it was asked
    /// for. Together they say: whatever owned the desktop before this call still owns it, and it is
    /// not us.</para>
    /// </summary>
    public bool ForegroundUndisturbed =>
        ForegroundBefore == ForegroundAfter && ForegroundAfter != Window;

    /// <summary>The OS holds ink for this target, differentially.</summary>
    public bool Inked => BackgroundHeld && InkedPixels > 0;

    /// <summary>
    /// Everything the OS confirmed about a target that is really on the desktop and really
    /// clickable. <b>This — and only this — is what <see cref="CapabilityState.Available"/> may
    /// rest on.</b>
    ///
    /// <para><see cref="Inked"/> is deliberately NOT a conjunct, exactly as it is not one for the
    /// input capability: an un-inked target is <see cref="CapabilityState.Degraded"/> — it holds
    /// everything it claimed about routing and one non-fatal check failed — and a caller decides
    /// whether a blank clickable rectangle is worth keeping.</para>
    /// </summary>
    public bool Confirmed =>
        Asked && Window != 0 && WindowExists && WindowVisible
        && NonActivatingStyleHeld && !ClickThroughStyleHeld
        && AboveEveryOrdinaryWindow && HitTestRoutesHere && ForegroundUndisturbed;
}

/// <summary>
/// A pointer surface: this process's ability to put MANY rectangles on the desktop that CATCH a
/// click and take nothing — no foreground, no focus, no activation — plus its ability to ask the
/// operating system whether that is really so.
///
/// <para><b>This is the third thing this port has had to prove about a window, and it is the
/// hardest.</b> SP-099 proved a surface is click-THROUGH and its review recorded that as the
/// property most easily faked: a window that does not exist satisfies it too. SP-110 proved the
/// inverse for a keyboard-taking window and found that <c>GetGUIThreadInfo(<i>ourThread</i>)</c>
/// LIES — it is thread-local and answered "our window has focus" while every injected keystroke
/// went to another application. <b>This capability must prove a click LANDS on a window that must
/// NOT take the foreground</b>, so it cannot lean on either of the two facts SP-110's chain leaned
/// on. Its whole evidence base is the window manager's routing answer plus the foreground
/// <i>staying where it was</i>.</para>
///
/// <para><b>What <see cref="CapabilityState.Available"/> means here, precisely.</b> The OS was
/// asked back and confirmed, per target: the window exists and is visible, it holds the exact
/// rectangle requested, it carries <c>WS_EX_NOACTIVATE</c> and NOT <c>WS_EX_TRANSPARENT</c>, the
/// OS's own z-order walk puts it above every ordinary window, <c>WindowFromPoint</c> at its own
/// centre returns IT, and <c>GetForegroundWindow()</c> is the same window before and after and is
/// not this one. It never means a call returned.</para>
///
/// <para><b>What Available still cannot mean, and this is where the chain stops.</b> That a click
/// was DELIVERED. A delivery fact requires a click, and this capability must never synthesise one:
/// a product that pressed the mouse to check it could receive presses would be clicking on whatever
/// the user actually had under the cursor. So delivery is measured by the HARNESS with
/// <c>SendInput</c> (<c>client/docs/verification-harness.md</c>, pointer evidence class) and the
/// product's claim stops one step short of it. It also cannot mean the window will never activate:
/// <c>WM_MOUSEACTIVATE</c> only exists when a click really happens, so
/// <see cref="MouseActivateRefusals"/> is evidence a fact can read and never an input to
/// <see cref="CapabilityState.Available"/>.</para>
///
/// <para><b>Keyed from birth, and that is deliberate.</b> Every operation names a target. SP-112
/// §2.3 F5 found the video and input capabilities are SINGLE-TENANT — neither knows whose clip or
/// whose card is up — and that every guard against a second consumer had to be written in the
/// module. SP-109's audio presence solved it with per-module keyed slots. A pointer capability has
/// MANY live targets by construction, so it takes the keyed answer at design time rather than at
/// the second consumer.</para>
///
/// <para><b>Thread affinity.</b> A native window belongs to the thread that created it. Call
/// <see cref="Open"/>/<see cref="Move"/>/<see cref="Close"/>/<see cref="Pump"/>/
/// <see cref="IDisposable.Dispose"/> on one thread.</para>
/// </summary>
public interface IPointerSurface : IDisposable
{
    /// <summary>
    /// Whether this process could put a pointer target in front of anybody at all — the window
    /// station question, asked of the OS, before any window exists.
    /// </summary>
    bool CanReachAPointer { get; }

    /// <summary>The station reading behind <see cref="CanReachAPointer"/>, so a caller can report
    /// WHICH necessary condition failed rather than a bare false.</summary>
    PointerStationObservation ObserveStation();

    /// <summary>How many targets this surface currently holds open.</summary>
    int OpenTargets { get; }

    /// <summary>
    /// The most recent <see cref="Open"/>/<see cref="Move"/> outcome, verbatim, or null before
    /// anything was attempted. A caller that must tell "the target was refused" from "the target
    /// ended" reads this rather than re-deriving it.
    /// </summary>
    CapabilityState? LastPlacement { get; }

    /// <summary>
    /// How many <c>WM_MOUSEACTIVATE</c> messages this surface's windows have answered
    /// <c>MA_NOACTIVATE</c>.
    ///
    /// <para><b>Evidence, never a claim.</b> A window that has never been clicked has answered
    /// none, so this can never be a conjunct of <see cref="CapabilityState.Available"/> — see the
    /// interface remarks. It exists because the answer given at the instant of a real click is the
    /// only observation that shows the procedure refusing activation with a click in its hand;
    /// upstream answers the same message with the same value on its sibling surface
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1823-1824</c>, hook at <c>:1831-1839</c>).</para>
    /// </summary>
    int MouseActivateRefusals { get; }

    /// <summary>How many <c>WM_MOUSEACTIVATE</c> messages arrived in total. Equal to
    /// <see cref="MouseActivateRefusals"/> on every healthy path; the pair exists so a procedure
    /// that started answering something else is visible rather than silent.</summary>
    int MouseActivateQueries { get; }

    /// <summary>
    /// Put a target on the desktop and say what the operating system answered about it.
    ///
    /// <para>The returned handle is this surface's key for the target and is never reused inside one
    /// surface's life. It is non-zero even when the state is a refusal <b>only</b> if a window was
    /// really created; a refusal that never got that far returns 0 and the caller has nothing to
    /// close.</para>
    /// </summary>
    CapabilityState Open(PointerTargetRequest request, out int target);

    /// <summary>
    /// Move an open target, and re-ask the OS everything <see cref="Open"/> asked.
    ///
    /// <para><b>The move seam is why this is a capability and not a second consumer of
    /// <c>Input/**</c>.</b> That presence owns one window and places it once
    /// (<c>Win32InputPresence.cs:206</c> is its only <c>SetWindowPos</c>); a pointer target moves
    /// every frame, and the routing answer moves with it. The reposition carries
    /// <c>SWP_NOACTIVATE</c>, which is upstream's own flag on the same operation
    /// (<c>Services/BubbleService.cs:4807</c>).</para>
    ///
    /// <para><b>The staleness this leaves, bounded.</b> The answer returned here is about the
    /// rectangle as it is NOW; a click arriving later is routed by the window manager over the
    /// position the OS holds THEN. The product never hit-tests and then acts on the answer — the
    /// arbiter at click time is the OS — so the residue is only that a caller's belief about
    /// routing can be one move old. With upstream's own constants that is at most
    /// <c>sqrt(12² + 4.5²) = 12.81</c> DIP of travel per 30 ms step (<c>BubbleService.cs:53</c>,
    /// <c>:2825</c>, <c>:2831-2836</c>, <c>:3458-3464</c>, <c>:3496</c>) against a smallest legal
    /// radius of 30 DIP (<c>Services/BubbleSizing.cs:70</c>), so the target's own centre never
    /// leaves the target in one step.</para>
    /// </summary>
    CapabilityState Move(int target, PointerBounds bounds);

    /// <summary>
    /// Take a target off the desktop. <see cref="CapabilityState.Available"/> means the OS confirmed
    /// the window is no longer visible, the hit test at its old point no longer answers it, and the
    /// foreground still did not move.
    /// </summary>
    CapabilityState Close(int target);

    /// <summary>Everything the OS says about one target right now, re-asked. A caller that needs the
    /// fact after some time has passed asks again; nothing here is cached.</summary>
    PointerTargetObservation Observe(int target);

    /// <summary>
    /// Drain and dispatch up to <paramref name="max"/> of this thread's messages, so presses reach
    /// the callback. Bounded iteration, never a wall-clock wait.
    /// </summary>
    /// <returns>How many messages were dispatched.</returns>
    int Pump(int max);

    /// <summary>Where presses go. Set once by the owner; a press that arrives with no listener is
    /// counted and dropped, never queued.</summary>
    Action<PointerPress>? OnPress { get; set; }

    /// <summary>How many presses the OS delivered to any of this surface's windows. Diagnostic and
    /// evidence; never an input to a claim.</summary>
    int PressesSeen { get; }
}
