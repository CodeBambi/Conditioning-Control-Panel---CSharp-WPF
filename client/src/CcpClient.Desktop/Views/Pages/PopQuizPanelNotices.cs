using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The Pop Quiz panel's sentences, in their own file for the same reason
/// <see cref="InputPanelNotices"/>, <see cref="BubbleCountPanelNotices"/> and
/// <see cref="VideoPanelNotices"/> are in theirs: a fact can pin the exact words, and a "tidy" that
/// softens the one sentence telling the user what they are agreeing to has to red something.
///
/// <para><b>This panel says one thing no other panel on the page has had to say: the answer is never
/// wrong.</b> Upstream's own header states it — <i>"All answers are 'correct' — pure positive
/// reinforcement"</i> (<c>Services/Quiz/PopQuizService.cs:12</c>) — and a user who assumes the
/// opposite is being asked to agree to something this module does not do. There is no score, no
/// streak and no failure state anywhere in the three upstream files.</para>
///
/// <para><b>And its closing line quotes the operating system, not this page</b>, exactly as the Lock
/// Card's and Bubble Count's do: "the card has the keyboard" is a claim about a resource this
/// process does not own and the OS is entitled to refuse.</para>
/// </summary>
public static class PopQuizPanelNotices
{
    /// <summary>
    /// The warning that leads the panel, above the dials. Same discipline as the Lock Card's and
    /// Bubble Count's: this module takes the keyboard, and a user is entitled to know that before
    /// they tick the box rather than the first time a question lands in the middle of somebody
    /// else's chat window.
    /// </summary>
    public const string InterruptionNotice =
        "This module INTERRUPTS you. When a question is due it takes the keyboard from whatever you are "
        + "doing and puts a card in front of you with four answers on it. Press 1-4 to answer or Esc to "
        + "skip. EVERY ANSWER IS CORRECT — there is no score, no streak and no wrong one; what your "
        + "choice decides is which reply comes back.";

    /// <summary>
    /// What is NOT ported, in the user's own words, rendered where they read it before enabling
    /// anything. Same discipline as the Brain Drain and Mandatory Video half-row notices: a missing
    /// half must be visible on a healthy run, not only when something breaks.
    /// </summary>
    public const string ScopeNotice =
        "Scope: one card on the primary display, answered with the number keys, silent, and worth no XP "
        + "here. Upstream plays a chime on each question, lets you CLICK the four answers, and banks 25 XP "
        + "for answering — this build ships no sounds, asks through the keyboard so a question and a lock "
        + "card can never stack, and holds no progression record open while a session runs. The questions "
        + "are built into the app upstream and here; there is no list to edit.";

    /// <summary>
    /// Why there is no Test button, said where a user of the shipping app would go looking for one.
    /// Upstream's is <c>BtnTestPopQuiz</c> (<c>Views/Tabs/GradedIntakeTabView.xaml:295-299</c>),
    /// wired to <c>App.PopQuiz?.TestPopQuiz()</c> (<c>MainWindow/MainWindow.Lab.cs:646-649</c>),
    /// which is <c>ShowPopQuiz(isTest: true)</c> (<c>Services/Quiz/PopQuizService.cs:258-261</c>) —
    /// a question NOW, off the schedule, session or no session. It is absent rather than stubbed: a
    /// button that did nothing would be worse than no button.
    /// </summary>
    public const string NoTestButtonNotice =
        "There is no Test button here yet. The shipping app has one that puts a question up immediately; "
        + "this build can only ask on the schedule above, so raise the rate and start a session to meet one.";

    /// <summary>
    /// The live-state line, off the row's own dot plus its counters. The
    /// <see cref="EffectDotState.Armed"/> arms are separate sentences for the reason every module
    /// since the first has kept: "armed because no session is running" and "armed DURING a session
    /// because the OS will not give the card the keyboard" are completely different situations, and
    /// telling the second to start a session they already started is the message that had to be
    /// split apart.
    /// </summary>
    public static string DescribeQuizState(
        EffectDotState dot,
        int quizCount,
        PopQuizEvent? last,
        bool sessionRunning,
        bool canReachAUser,
        bool asking,
        int answeredCount,
        int skippedCount,
        PopQuizResolution lastResolution)
    {
        if (dot == EffectDotState.Off)
        {
            return "Switched off. No question will come up, session or no session.";
        }

        if (dot == EffectDotState.Armed && !sessionRunning)
        {
            return "Armed. No question comes up until the session starts.";
        }

        if (dot == EffectDotState.Armed && !canReachAUser)
        {
            return "Running, but it could never ask you anything: the operating system does not report a "
                + "desktop this process can put a window on. See below for what was asked and what came "
                + "back.";
        }

        if (dot == EffectDotState.Armed && asking)
        {
            // THE DEMAND HALF OF THE DOT (its sixth meaning), reached through this row.
            return "A question is on screen and the operating system has given the keyboard to another "
                + "window, so nothing you press reaches it. Click the card to give it the keyboard back.";
        }

        if (dot == EffectDotState.Armed)
        {
            return "This module is not scheduled right now, though the session is running. Switching it off "
                + "and on again re-arms it.";
        }

        var head = asking
            ? "Asking you now: the card is on screen and the operating system is routing your keyboard to it."
            : "Running: the next question is on the clock.";

        if (quizCount == 0)
        {
            return head;
        }

        var ordinal = last is null ? string.Empty : $" The last one was #{last.Ordinal}.";
        var ending = lastResolution switch
        {
            PopQuizResolution.Answered => " You answered the last one.",
            PopQuizResolution.Skipped => " You skipped the last one with Esc.",
            PopQuizResolution.Withdrawn => " The last one was taken down when the session stopped.",
            PopQuizResolution.Refused =>
                " The last question was taken straight back down: the operating system would not give it "
                + "the keyboard.",
            _ => string.Empty,
        };

        return $"{head} {quizCount} question{(quizCount == 1 ? string.Empty : "s")} asked so far, "
            + $"{answeredCount} answered and {skippedCount} skipped.{ordinal}{ending}";
    }

    /// <summary>
    /// The frequency dial's value label — upstream's own units and its own string, verbatim:
    /// <c>$"{val}/session hr"</c> (<c>MainWindow/MainWindow.Lab.cs:643</c>), which is what the
    /// shipping app writes beside this slider. "Session hour" rather than "hour" because the
    /// schedule only runs while a session does.
    /// </summary>
    public static string DescribeFrequency(int perHour) => $"{perHour}/session hr";

    /// <summary>
    /// The pool line. Upstream's questions are a <c>static readonly</c> array in the service with no
    /// settings key, no editor and no mod hook (<c>Services/Quiz/PopQuizService.cs:23-100</c>) —
    /// unlike the Lock Card's phrases, which really are persisted and editable. So this line REPORTS
    /// rather than pointing at a folder: there is nothing for the user to put anywhere.
    /// </summary>
    public static string DescribePool(int questionCount, int optionsPerQuestion) =>
        $"{questionCount} built-in questions, {optionsPerQuestion} answers each, dealt in a shuffled order "
        + "so the same answer is never in the same place twice. They are part of the app, not a folder you "
        + "can add to.";

    /// <summary>
    /// The XP line. <b>It reports the absence rather than hiding it</b>, because upstream really does
    /// pay twenty-five for an answer (<c>Windows/PopQuizWindow.xaml.cs:161</c>,
    /// <c>AddXP(25, XPSource.Other)</c>) and a user of the shipping app will look for it. The module
    /// banks the moment it is handed a ledger; this build hands it none, and says why.
    /// </summary>
    public static string DescribeXp(bool banksXp, XpGrant? lastGrant)
    {
        if (!banksXp)
        {
            return "No XP here. The shipping app pays 25 XP for answering; this build opens its "
                + "progression record only while one of the big windows is open, so a question answered "
                + "during a session banks nothing and the card makes no XP claim.";
        }

        return lastGrant switch
        {
            null => "25 XP an answer, once you have answered one.",
            { Banked: true } granted =>
                $"Banked {granted.Amount:0.##} XP for the last answer — level {granted.LevelAfter}.",
            { } refused => $"The last answer's 25 XP did not bank: {refused.Reason}",
        };
    }

    /// <summary>
    /// <b>The line that quotes the operating system.</b> The capability's own last typed outcome,
    /// rendered verbatim including its reason detail — never a sentence this page made up about a
    /// platform. Same shape as the Lock Card's, because it is the same shared capability.
    ///
    /// <para>The <c>Degraded</c> arm matters more here than anywhere else on the page: a card that
    /// holds the keyboard and carries no ink is a question the user can neither read nor answer their
    /// way out of, and the module takes it straight back down rather than leaving it up
    /// (<see cref="PopQuizEffect"/>'s <c>Deliver</c>). The wording says that happened rather than
    /// reporting a half-working card.</para>
    /// </summary>
    public static string DescribeInputCapability(CapabilityState? prompt, InputCaptureObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var head = prompt switch
        {
            null => "A question is a window that takes the keyboard. Nothing has been asked of the "
                + "operating system yet.",
            CapabilityState.Available a => $"The operating system gave the card the keyboard: {a.Detail}",
            CapabilityState.Unavailable u => $"No question was shown: {u.Reason.Detail}",
            CapabilityState.DependencyMissing m => $"No question was shown: {m.Reason.Detail}",
            CapabilityState.Degraded d =>
                $"The question was taken straight back down. Partly available: {d.SurvivingSemantics}. "
                + $"{d.Reason.Detail}",
            CapabilityState.PermissionRequired p => $"No question was shown: {p.Reason.Detail}",
            CapabilityState.Faulted f => $"No question was shown: {f.Reason.Detail}",
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
            + "input here — it does NOT prove anybody pressed them.";
    }
}
