using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Input;

/// <summary>A rectangle on the desktop in PHYSICAL pixels — a Win32 top-level window's own
/// coordinate space, for the same reason <c>Overlay.OverlayBounds</c> is physical.</summary>
public readonly record struct InputBounds(int X, int Y, int Width, int Height)
{
    /// <summary>The centre point, which is where the hit test is asked.</summary>
    public (int X, int Y) Centre => (X + (Width / 2), Y + (Height / 2));

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>
/// What the prompt window shows right now: the question, the progress through it, what the user has
/// produced so far, and the line that tells them how to get out.
///
/// <para>Deliberately a plain record and deliberately re-supplied on every repaint rather than
/// mutated: the presence owns a native window, and a window that reads caller state on a paint
/// message would be reading it from whichever thread the OS chose to invalidate on.</para>
/// </summary>
/// <param name="Question">The phrase the user must produce. Rendered verbatim.</param>
/// <param name="Progress">e.g. <c>2 of 3</c>. Upstream repaints the same counter on every card
/// (<c>Windows/LockCardWindow.xaml.cs</c>'s <c>UpdateProgressOnAllWindows</c>).</param>
/// <param name="Answer">What has been typed toward the current repeat, echoed back.</param>
/// <param name="Hint">The exit line. Upstream repaints this LIVE rather than snapshotting it,
/// because whether Esc closes the card can change under the user (<c>:639-647</c>).</param>
public sealed record InputPromptContent(string Question, string Progress, string Answer, string Hint);

/// <summary>What kind of keystroke the OS delivered to the prompt window.</summary>
public enum InputKeystrokeKind
{
    /// <summary>A character the OS's own keyboard translation produced.</summary>
    Character,

    /// <summary>Backspace. Upstream's box supports it, and it deliberately earns NO typing credit
    /// (its credit counter only ever counts growth — <c>LockCardWindow.xaml.cs:289-290</c>).</summary>
    Backspace,

    /// <summary>The user asked to leave (<c>VK_ESCAPE</c>). Whether that is honoured is the
    /// caller's decision, never this capability's (<c>:632</c>).</summary>
    Cancel,

    /// <summary>A key that produced no character — recorded so a delivery fact has something to
    /// count that cannot be confused with typing.</summary>
    Key,
}

/// <summary>One keystroke as the window procedure saw it.</summary>
/// <param name="Kind">Which of the four shapes above.</param>
/// <param name="Character">The character, for <see cref="InputKeystrokeKind.Character"/>.</param>
/// <param name="VirtualKey">The virtual-key code, for the non-character kinds.</param>
public readonly record struct InputKeystroke(InputKeystrokeKind Kind, char Character, int VirtualKey);

/// <summary>
/// What a caller wants asked: where the card goes, what it says, and who gets told when the user
/// presses something.
/// </summary>
/// <param name="Bounds">Where the card goes, in physical pixels.</param>
/// <param name="Content">What it shows when it first appears.</param>
/// <param name="OnKeystroke">
/// Raised from the window procedure, on the thread whose message loop delivered the key — the UI
/// thread in the product. It must not throw: the presence catches everything, because an exception
/// escaping a native callback is a process kill rather than a fault.
/// </param>
public sealed record InputPromptRequest(InputBounds Bounds, InputPromptContent Content, Action<InputKeystroke> OnKeystroke)
{
    public InputBounds Bounds { get; } = Validate(Bounds);

    private static InputBounds Validate(InputBounds bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Width, 0, nameof(bounds));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Height, 0, nameof(bounds));
        return bounds;
    }
}

/// <summary>
/// What the operating system says about this process's ability to put a window in front of a human
/// AT ALL — the necessary condition, asked before anything is shown.
///
/// <para><b>Why it is a separate observation from <see cref="InputCaptureObservation"/>.</b> "This
/// process could ask a question" and "this process is asking one and the OS routed the keyboard to
/// it" are different facts with different lifetimes, and a module's dot needs the first every time
/// it repaints while the second only exists while a card is up. Collapsing them would make a dot
/// that is dark whenever nothing is being asked, which is most of a session.</para>
/// </summary>
/// <param name="Asked">The read-back was attempted. False on a build with no mechanism — the Linux
/// case, and never dressed up as a zero.</param>
/// <param name="WindowStationVisible">The process's window station carries <c>WSF_VISIBLE</c>
/// (<c>GetUserObjectInformation(UOI_FLAGS)</c>). A service in session 0 does not, and every window
/// it creates is invisible to every human.</param>
/// <param name="DisplayCount">The OS's own monitor count (<c>SM_CMONITORS</c>).</param>
/// <param name="DesktopReachable">This thread has a desktop (<c>GetThreadDesktop</c>).</param>
public sealed record InputStationObservation(
    bool Asked,
    bool WindowStationVisible,
    int DisplayCount,
    bool DesktopReachable)
{
    /// <summary>Nothing was asked, because this build has no way to ask.</summary>
    public static InputStationObservation NotAsked { get; } = new(false, false, 0, false);

    /// <summary>The OS says a window this process creates can be put in front of somebody. It does
    /// NOT say any window took the input — only <see cref="InputCaptureObservation"/> can.</summary>
    public bool Confirmed => Asked && WindowStationVisible && DisplayCount >= 1 && DesktopReachable;
}

/// <summary>
/// What the operating system says about the prompt window RIGHT NOW. Every field is read back from
/// the platform; not one is remembered from a call this process made.
///
/// <para><b><see cref="SystemKeyboardFocusIsThisWindow"/> is the field this whole capability turns
/// on, and it is the one that is easiest to get subtly wrong.</b> It is
/// <c>GetGUIThreadInfo(<b>0</b>).hwndFocus</c> — the FOREGROUND thread's focus, which is the OS's
/// own statement of where the next keystroke goes.
/// <c>GetGUIThreadInfo(<i>ourThreadId</i>).hwndFocus</c> is a different question with a different
/// answer: it is the focus inside our own thread's input queue, and it is set for a window nobody
/// can type into. Measured (the packet plan.md §0, run 1): with the foreground held by another
/// application and every injected keystroke going elsewhere, the thread-local read still answered
/// "our window". A capability built on that read would be the same fake in reverse — an OS API,
/// correctly called, certifying nothing.</para>
/// </summary>
/// <param name="Asked">The read-back was attempted at all.</param>
/// <param name="Window">The hwnd asked about; 0 when there is none.</param>
/// <param name="WindowExists">The OS recognises the handle as a window.</param>
/// <param name="WindowVisible">The OS reports it visible.</param>
/// <param name="HeldBounds">The rectangle the OS holds for it.</param>
/// <param name="AboveEveryOrdinaryWindow">The OS's own top-level z-order walk puts it before the
/// first visible non-topmost window. The ORDERING, never the <c>WS_EX_TOPMOST</c> flag.</param>
/// <param name="IsForegroundWindow"><c>GetForegroundWindow()</c> is this window.</param>
/// <param name="SystemKeyboardFocusIsThisWindow">See the remarks above.</param>
/// <param name="HitTestWinner">What the window manager routes the card's centre point to.</param>
/// <param name="InkedPixels">How many sampled pixels of the card's own client area differ from its
/// painted background, read back out of the window's device context. Zero means the OS holds a
/// blank window.</param>
/// <param name="SampledPixels">How many were sampled — the ink check's own negative control, so a
/// read-back that degenerated into "sampled nothing" is visible.</param>
/// <param name="BackgroundHeld">
/// A control point in a margin the painter FILLS and never writes text into reads back exactly the
/// painted background colour. <b>Without this the ink count means nothing:</b> the card's window
/// class registers no background brush, so an UNPAINTED window's device context holds whatever the
/// OS left there — which differs from the background just as reliably as text does. The mutation
/// that deletes the paint call entirely survived a sweep that counted ink without this clause.
/// </param>
/// <param name="KeystrokesSeen">How many keys this window's own window procedure has received since
/// it was created. Not a claim that a human pressed any of them.</param>
public sealed record InputCaptureObservation(
    bool Asked,
    nint Window,
    bool WindowExists,
    bool WindowVisible,
    InputBounds HeldBounds,
    bool AboveEveryOrdinaryWindow,
    bool IsForegroundWindow,
    bool SystemKeyboardFocusIsThisWindow,
    nint HitTestWinner,
    int InkedPixels,
    int SampledPixels,
    bool BackgroundHeld,
    int KeystrokesSeen)
{
    /// <summary>Nothing was asked, because this build has no way to ask.</summary>
    public static InputCaptureObservation NotAsked { get; } =
        new(false, 0, false, false, default, false, false, false, 0, 0, 0, false, 0);

    /// <summary>The window manager routes the card's centre to the card. The non-zero test is not
    /// redundant: without it an observation about NO window reports that a hit test routes to it,
    /// because <c>0 == 0</c>.</summary>
    public bool HitTestRoutesHere => Window != 0 && HitTestWinner == Window;

    /// <summary>The OS holds a readable question in the card's client area: the painted background
    /// is really there AND pixels in the question band differ from it. A DIFFERENTIAL, for the
    /// reason on <see cref="BackgroundHeld"/>.</summary>
    public bool Inked => BackgroundHeld && InkedPixels > 0;

    /// <summary>
    /// The OS has given this window the input. This — and only this — is what
    /// <see cref="CapabilityState.Available"/> is allowed to rest on.
    /// </summary>
    public bool Confirmed =>
        Asked && WindowExists && WindowVisible && AboveEveryOrdinaryWindow
        && IsForegroundWindow && SystemKeyboardFocusIsThisWindow && HitTestRoutesHere;
}

/// <summary>
/// An input presence: this process's ability to put a window in front of the user that TAKES the
/// keyboard and answers — and, the part that makes it a capability rather than a wrapper, its
/// ability to ASK THE OPERATING SYSTEM whether that really happened.
///
/// <para><b>This is the exact inverse of the overlay capability, and the same standard applies.</b> That packet
/// proved a surface is click-THROUGH and its review recorded that "input passes through" is the
/// property most easily faked, because a window that does not exist satisfies it too. The mirror
/// hazard is sharper: <b>a test asserting that <c>WS_EX_TRANSPARENT</c> was not set is not evidence
/// that a click lands.</b> Neither is an attached handler, a returned <c>SetForegroundWindow</c>,
/// nor a thread-local focus read. So <see cref="CapabilityState.Available"/> here is downstream of
/// four things the OS said — the window is on screen and above every ordinary window, it IS the
/// foreground window, the FOREGROUND THREAD's keyboard focus is it, and the window manager's hit
/// test at a point inside it returns it — plus one thing the OS holds: ink in its own client
/// area.</para>
///
/// <para><b>The foreground is LENT, not owned, and that is why this capability is interesting.</b>
/// Measured on this machine before any of this was written: a plain <c>SetForegroundWindow</c> from
/// a process that does not already own the foreground returns FALSE, the OS keeps the keyboard
/// where it was, and every synthesised keystroke goes to the other application — while the window
/// is still visible, still topmost and still hit-testable. A caller that trusted its own call would
/// have shown the user a card nobody could answer and reported success.</para>
///
/// <para><b>What Available still cannot mean.</b> That a human pressed anything, that the card is
/// legible, that a physical keyboard reached it, or that the OS will still be routing input here a
/// millisecond later. The first is a manual gate; the second is
/// <c>presentation-verified</c>; the fourth is a property of a contested resource and the honest
/// answer is that this reports what the OS said when it was asked.</para>
///
/// <para><b>What a caller does with a refusal: take the card back down.</b> Not leave it up. A
/// fullscreen-adjacent window that holds no input and cannot be answered is strictly worse for the
/// user than no window at all, which is why upstream's own error path force-closes every card it
/// may have half-shown (<c>Services/LockCard/LockCardService.cs:308-313</c>).</para>
///
/// <para><b>Thread affinity.</b> A native window belongs to the thread that created it. Call
/// <see cref="Prompt"/>/<see cref="Update"/>/<see cref="Dismiss"/>/<see cref="IDisposable.Dispose"/>
/// on one thread — the UI thread in the app, because that is the thread with the message loop that
/// delivers the keystrokes.</para>
/// </summary>
public interface IInputPresence : IDisposable
{
    /// <summary>
    /// Put the card up, take the input, and then ASK THE OS whether it was really taken.
    /// <see cref="CapabilityState.Available"/> means the operating system reports this window as
    /// the foreground window, reports it as the foreground thread's keyboard focus, routes a point
    /// inside it to it, places it above every ordinary window, and holds ink for it.
    /// <see cref="CapabilityState.Degraded"/> means the input is held and the card is blank.
    /// Anything else is a typed refusal naming the link that failed.
    ///
    /// <para><b>ONE CARD AT A TIME, and the presence is what enforces it.</b> A prompt that arrives
    /// while this presence is already committed to one is refused with
    /// <see cref="InputReasonCodes.InputAlreadyPrompting"/> — the live card keeps its rectangle, its
    /// content and its keystroke callback, so the module that raised it can still be answered. This
    /// instance is SHARED (<c>Effects/BubbleCountEffect.cs:75</c>) and its consumers ask from
    /// different threads, so a caller-side "is one up?" test cannot hold across another caller's
    /// prompt; refusing here is what makes the rule true rather than customary.</para>
    /// </summary>
    CapabilityState Prompt(InputPromptRequest request);

    /// <summary>
    /// Repaint the card that is up — a new answer echo, a new progress counter, a new hint. Touches
    /// no style, asks for no foreground and walks no z-order: those are <see cref="Prompt"/>'s and
    /// are right once per card, not once per keystroke.
    /// </summary>
    CapabilityState Update(InputPromptContent content);

    /// <summary>
    /// Take the card off the screen. <see cref="CapabilityState.Available"/> means the OS confirmed
    /// it is no longer visible, no longer the foreground window, and that the window manager no
    /// longer routes its centre to it — a hidden window still eating input is the worst of both
    /// states.
    /// </summary>
    CapabilityState Dismiss();

    /// <summary>
    /// Ask the OS what it believes about the card, RIGHT NOW. A live round trip; never throws, and a
    /// build with no mechanism answers <see cref="InputCaptureObservation.NotAsked"/> rather than a
    /// shaped zero.
    /// </summary>
    InputCaptureObservation Observe();

    /// <summary>Ask the OS whether this process could put a window in front of anybody at all.</summary>
    InputStationObservation ObserveStation();

    /// <summary>
    /// Drain up to <paramref name="maxMessages"/> messages from this thread's queue, translating and
    /// dispatching each. Returns how many were dispatched.
    ///
    /// <para><b>The product does not call this</b> — the host's own loop (Avalonia's, on the UI
    /// thread) pumps the window. It exists for a caller that owns a window on a thread with no loop
    /// of its own, which is every fact in the suite, and it is bounded rather than blocking so it can
    /// never become a wall-clock wait.</para>
    /// </summary>
    int Pump(int maxMessages);

    /// <summary>
    /// A live, cheap read: does the OS report the card as the foreground window AND as the
    /// foreground thread's keyboard focus? This is what a row's dot is entitled to call "the user is
    /// really being asked". It is deliberately NOT a remembered answer — the foreground can be taken
    /// away by another process with this one doing nothing at all.
    /// </summary>
    bool HoldsTheInput { get; }

    /// <summary>
    /// A live, cheap read of the necessary condition: the OS says this process has an interactive,
    /// visible window station and at least one display. False on a build with no mechanism, which is
    /// what stops a module's dot from lighting where no question can ever be asked.
    /// </summary>
    bool CanReachAUser { get; }

    /// <summary>True only while a card this presence put up is confirmed on screen.</summary>
    bool IsPrompting { get; }

    /// <summary>The last state <see cref="Prompt"/> produced, or null before it was ever called.
    /// What a panel renders, so a user reads the OS's own answer rather than a sentence about a
    /// platform.</summary>
    CapabilityState? LastPrompt { get; }

    /// <summary>The most recent read-back this presence actually took. <b>A remembered ANSWER, never
    /// a remembered REQUEST.</b></summary>
    InputCaptureObservation LastObservation { get; }
}
