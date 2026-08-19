using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The Lock Card panel's sentences (SP-110), in their own file for the reason
/// <see cref="RampPanelNotices"/> and <see cref="AudioPanelNotices"/> are in theirs:
/// <c>StudioPage.axaml.cs</c> carries every landed module's rendered claims and does not scale, and
/// the extraction that fixes it is not this packet's risk to take. New text starts where the
/// extraction would put it.
///
/// <para><b>This panel says something no other panel on this page has had to say: that the module
/// will INTERRUPT the user.</b> Every other row draws, tints, paces or plays over whatever they are
/// doing; this one takes the keyboard away from it. That is the first thing the panel states, above
/// the dials, because a user is entitled to know it before they tick the box rather than the first
/// time a card lands mid-sentence in somebody else's chat window.</para>
///
/// <para><b>And its closing line quotes the operating system, not this page.</b> The capability's own
/// typed outcome is rendered verbatim, including the reason code's detail, for the same reason the
/// audio panel's does: "the card has the keyboard" is a claim about a resource this process does not
/// own and the OS is entitled to refuse.</para>
/// </summary>
public static class InputPanelNotices
{
    /// <summary>
    /// The warning that leads the panel. A constant rather than inline AXAML text so the same words
    /// can be pinned by a fact — a "tidy" that softened this into "shows a card" would be removing
    /// the one sentence that tells the user what they are agreeing to.
    /// </summary>
    public const string InterruptionNotice =
        "This is the only module that INTERRUPTS you. When a card is due it takes the keyboard from "
        + "whatever you are doing and waits until you type the phrase. Esc always closes a card in this "
        + "build — including a strict one — because the panic key that upstream lets strict mode rely on "
        + "does not exist here, and a window that takes your keyboard must always have a way out.";

    /// <summary>
    /// What is NOT ported, in the user's own words, rendered where they can read it before enabling
    /// anything. Same discipline as Brain Drain's visual-half notice: a missing half must be visible
    /// on a healthy run, not only when something breaks.
    /// </summary>
    public const string ScopeNotice =
        "Scope: one card on the primary display, typed. Upstream covers EVERY monitor with a fullscreen "
        + "cover whose mirrors echo your typing, and can be solved by SPEAKING the phrase into a "
        + "microphone; neither is in this build. The phrase list is edited in the module's own JSON file "
        + "rather than here.";

    /// <summary>
    /// The live-state line, off the row's own dot plus its counters.
    ///
    /// <para>The <see cref="EffectDotState.Armed"/> arms are several sentences rather than one, on
    /// the SP-105 finding this port keeps re-learning: a module that is Armed because no session is
    /// running and a module that is Armed DURING a session because the OS will not give it the
    /// keyboard are completely different situations, and telling the second to "start a session" —
    /// which they already did — is the exact message SP-105 had to split apart.</para>
    /// </summary>
    public static string DescribeCardState(
        EffectDotState dot,
        int cardCount,
        LockCardEvent? last,
        bool sessionRunning,
        bool canReachAUser,
        bool prompting,
        bool holdsTheInput,
        LockCardResolution lastResolution)
    {
        if (dot == EffectDotState.Off)
        {
            return "Switched off. No card will appear, session or no session.";
        }

        if (dot == EffectDotState.Armed && !sessionRunning)
        {
            return "Armed. No card appears until the session starts.";
        }

        if (dot == EffectDotState.Armed && !canReachAUser)
        {
            // The session IS running and this module can never ask anything. Never "start a session".
            return "Running, but it can never ask you anything: the operating system does not report a "
                + "desktop this process can put a window on. See below for what was asked and what came "
                + "back.";
        }

        if (dot == EffectDotState.Armed && prompting && !holdsTheInput)
        {
            // THE STATE THIS MODULE'S DOT EXISTS FOR. A card is up and the OS has the keyboard
            // somewhere else, so the user is not in fact being asked anything — measured as a real
            // state, not imagined (SP-110 plan.md §0).
            return "A card is on screen and the operating system has given the keyboard to another "
                + "window, so nothing you type reaches it. Click the card to give it the keyboard back.";
        }

        if (dot == EffectDotState.Armed)
        {
            return "This module is not scheduled right now, though the session is running. Switching it "
                + "off and on again re-arms it.";
        }

        var head = prompting
            ? "Asking you now: the card is on screen and the operating system is routing your keyboard to it."
            : "Running: the next card is on the clock.";

        if (cardCount == 0)
        {
            return head;
        }

        var ordinal = last is null ? string.Empty : $" The last one was #{last.Ordinal}.";
        var ending = lastResolution switch
        {
            LockCardResolution.Solved => " You typed the last one out in full.",
            LockCardResolution.Dismissed => " You closed the last one with Esc.",
            LockCardResolution.Withdrawn => " The last one was taken down when the session stopped.",
            LockCardResolution.Refused => " The last one was taken straight back down: the operating "
                + "system would not give it the keyboard.",
            _ => string.Empty,
        };

        return $"{head} {cardCount} card{(cardCount == 1 ? string.Empty : "s")} shown so far.{ordinal}{ending}";
    }

    /// <summary>What will be asked, and where it is edited. The port's standing answer to "where do I
    /// change these" — the same shape the flash panel uses for its images folder.</summary>
    public static string DescribePhrasePool(int phraseCount, IReadOnlyList<string> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        if (phraseCount == 0)
        {
            return $"No phrase is enabled, so no card will appear. Enable one in "
                + $"{LockCardPresetDocument.FileName} — it is picked up while the session runs, without "
                + "restarting.";
        }

        var listed = string.Join(", ", phrases.Take(5));
        var more = phrases.Count > 5 ? $", and {phrases.Count - 5} more" : string.Empty;
        return $"{phraseCount} phrase{(phraseCount == 1 ? string.Empty : "s")} in play: {listed}{more}. "
            + $"Edited in {LockCardPresetDocument.FileName}.";
    }

    /// <summary>How much typing a card will ask for, from the two dials that decide it.</summary>
    public static string DescribeDemand(int repeats, bool strict) =>
        strict
            ? $"Each card asks for the phrase {repeats} time{(repeats == 1 ? string.Empty : "s")}, and "
                + "refuses to be closed by the window manager (no Alt+F4). Esc still works."
            : $"Each card asks for the phrase {repeats} time{(repeats == 1 ? string.Empty : "s")}.";

    /// <summary>
    /// <b>The line that quotes the operating system.</b> It reports the capability's own last typed
    /// outcome — reason code detail and all — never a sentence this page made up about a platform.
    ///
    /// <para>Before anything has been asked it names the MECHANISM and says nothing has been
    /// attempted: a user must not have to wait for a card to find out whether this build can show one
    /// on their desktop, and "nothing has been asked yet" is a fact about this session rather than a
    /// claim about the machine.</para>
    /// </summary>
    public static string DescribeInputCapability(CapabilityState? prompt, InputCaptureObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var head = prompt switch
        {
            null => "A card is a window that takes the keyboard. Nothing has been asked of the operating "
                + "system yet.",
            CapabilityState.Available a => $"The operating system gave the card the keyboard: {a.Detail}",
            CapabilityState.Unavailable u => $"No card was shown: {u.Reason.Detail}",
            CapabilityState.DependencyMissing m => $"No card was shown: {m.Reason.Detail}",
            CapabilityState.Degraded d => $"Partly available: {d.SurvivingSemantics}. {d.Reason.Detail}",
            CapabilityState.PermissionRequired p => $"No card was shown: {p.Reason.Detail}",
            CapabilityState.Faulted f => $"No card was shown: {f.Reason.Detail}",
            // The hierarchy is closed (CapabilityState's ctor is private), so this arm is unreachable
            // today; it is this codebase's own convention for these switches and prints the state
            // rather than inventing a sentence.
            _ => prompt.ToString() ?? string.Empty,
        };

        if (!observation.Asked)
        {
            return head;
        }

        return $"{head} Last read-back: foreground={observation.IsForegroundWindow}, "
            + $"keyboard-focus={observation.SystemKeyboardFocusIsThisWindow}, "
            + $"{observation.KeystrokesSeen} keystroke(s) delivered to the card. That proves the OS routed "
            + "input here — it does NOT prove anybody typed them.";
    }
}
