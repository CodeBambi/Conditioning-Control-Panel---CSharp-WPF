namespace CcpClient.Desktop.Input;

/// <summary>
/// Stable machine-readable reasons an input-capturing window refused
/// (<c>runtime-capability-contract.md</c> §1). Additive; every code lands with the consumer that
/// reads it.
///
/// <para><b>Each of these names a DIFFERENT link of the capture chain</b>, and that is the point of
/// having nine of them rather than one "input-unavailable": a card that is on screen and buried, a
/// card that is on screen and un-focused, and a card that never came up at all are three different
/// desktops, and a caller (and a bug report) must be able to tell them apart. (The prose said "six"
/// while the file held eight; corrected here rather than left further wrong.)</para>
/// </summary>
public static class InputReasonCodes
{
    /// <summary>No mechanism on this platform: nothing was attempted. The Linux/macOS branch, and
    /// the shape <c>ISecretStore</c>/<c>ITrayPresence</c>/<c>IOverlayPresence</c>/
    /// <c>IAudioPresence</c> all use for the same situation.</summary>
    public const string InputMechanismAbsent = "input-mechanism-absent";

    /// <summary>The window could not be created at all (class registration or
    /// <c>CreateWindowEx</c> failed).</summary>
    public const string InputWindowCreationFailed = "input-window-creation-failed";

    /// <summary>
    /// This process has no interactive window station or no display, so no window it creates can be
    /// put in front of anybody — a service in session 0, a non-interactive station, a session with
    /// no monitor. Read from the OS (<c>GetProcessWindowStation</c> +
    /// <c>GetUserObjectInformation(UOI_FLAGS)</c> and the OS's own monitor count), never inferred
    /// from a platform check.
    /// </summary>
    public const string InputNoInteractiveStation = "input-no-interactive-station";

    /// <summary>The OS placed the window but does not report it visible, at the requested rectangle,
    /// or above every ordinary window. A prompt nobody can see is not a prompt.</summary>
    public const string InputPromptNotOnScreen = "input-prompt-not-on-screen";

    /// <summary>
    /// <b>The refusal this capability exists for.</b> The window is on screen and the operating
    /// system did NOT give it the input: it is not the foreground window, or the foreground thread's
    /// keyboard focus is some other window, or the window manager routes a point inside it
    /// elsewhere. The foreground is lent by the OS and can be refused — measured: a plain
    /// <c>SetForegroundWindow</c> from a process that does not already own the foreground returns
    /// FALSE and no keystroke arrives (the packet plan.md §0, run 1).
    /// </summary>
    public const string InputNotCaptured = "input-not-captured";

    /// <summary>The OS holds no ink for the prompt's own client area, so the window is up, focused
    /// and blank — a question the user cannot read is one they cannot answer.</summary>
    public const string InputPromptNotInked = "input-prompt-not-inked";

    /// <summary>Asked to update or dismiss a presence that has nothing on screen.</summary>
    public const string InputNothingPrompted = "input-nothing-prompted";

    /// <summary>
    /// <b>This presence is already committed to a card, so a second one was not put over it.</b>
    ///
    /// <para>The input capability has TWO consumers over ONE shared instance
    /// (<c>Effects/BubbleCountEffect.cs:75</c>), and both already refuse their own firing while a
    /// card is up (<c>Effects/LockCardEffect.cs:351</c>, <c>Effects/BubbleCountEffect.cs:662</c>) —
    /// but the Lock Card tests on the paced CLOCK thread while Bubble Count asks from the SURFACE
    /// thread (<c>Effects/BubbleCountEffect.cs:630-633</c>), so neither can hold its own check
    /// across the other's <c>Prompt</c>. Without a refusal HERE the second prompt overwrote the
    /// shared <c>_content</c> and <c>_onKeystroke</c>: the first card's keystroke callback was gone
    /// and its question could never be answered. Upstream refuses the same collision at the same
    /// granularity — its interaction queue drops or defers an interaction that arrives while another
    /// is live (<c>Services/BubbleCountService.cs:169-186</c>), and with no queue configured its
    /// <c>ResolveBlockedCardAction</c> answers <c>DropNoQueue</c>
    /// (<c>Services/LockCard/LockCardService.cs:193-199</c>), which is this port's situation
    /// permanently.</para>
    ///
    /// <para><b>Named for its sibling <c>Video/VideoReasonCodes.cs</c>'s
    /// <c>video-already-playing</c>, and deliberately NOT built like it.</b> That guard tests
    /// <c>_clip is not null</c> under a lock and releases the lock
    /// (<c>Effects/VideoSurfacePresenter.cs:368-370</c>), then assigns <c>_clip</c> seventy lines
    /// later (<c>:439</c>) — it narrows its race rather than closing it. This one is staked with a
    /// single <c>Interlocked.CompareExchange</c>, so the test and the claim are one indivisible
    /// step.</para>
    ///
    /// <para>It means BUSY, never BROKEN: the claim is given back by every prompt that ends with no
    /// card, by a dismissal and by disposal, so a caller that sees this can ask again once the live
    /// card comes down.</para>
    /// </summary>
    public const string InputAlreadyPrompting = "input-already-prompting";

    /// <summary>The presence was disposed; its window is gone and it will never prompt again.</summary>
    public const string InputPresenceDisposed = "input-presence-disposed";
}
