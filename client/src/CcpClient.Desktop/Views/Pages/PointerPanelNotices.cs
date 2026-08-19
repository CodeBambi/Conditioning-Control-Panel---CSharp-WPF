using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// What the Bubble Pop panel says, as constants and pure functions.
///
/// <para>Same reason as <c>InputPanelNotices</c>, <c>AudioPanelNotices</c> and
/// <c>BubbleCountPanelNotices</c>: the sentences are the user-visible half of the port's honesty
/// rules, so they are pinned by facts rather than typed into XAML where a fact can only assert that
/// SOMETHING is there.</para>
///
/// <para><b>This row's sentences carry one thing no earlier row's do.</b> Every module before this
/// one did something TO the user; this one asks them to hit a moving target, so "the module is
/// running" and "the user can play it" are different states and the panel must be able to say the
/// second one is false while the first is true. That is what
/// <see cref="DescribeFieldState"/>'s routable clause exists for.</para>
/// </summary>
public static class PointerPanelNotices
{
    /// <summary>
    /// The leading notice: what this row PUTS ON THE DESKTOP, said before the dials. The Lock Card's
    /// panel leads with its interruption warning for the same reason, and this one has a different
    /// warning to give: these windows sit above everything and take clicks aimed at them.
    /// </summary>
    public const string InterruptionNotice =
        "Bubble Pop puts small always-on-top windows on your screen that float upward, and clicking one pops it. "
        + "They stay OUT of the way in the one respect that matters: they never take the keyboard or the "
        + "foreground from whatever you are doing, so typing, games and video calls keep the focus they had. "
        + "But they do cover the points they are drawn on, and a click aimed at a bubble goes to the bubble and "
        + "not to what is underneath it.";

    /// <summary>
    /// What is NOT here, in the user's own words. Upstream pays a pop with a sound, XP, an
    /// achievement, a haptic pulse and a Discord update (<c>Services/BubbleService.cs:941-968</c>);
    /// this port pays it with a pop.
    /// </summary>
    public const string ScopeNotice =
        "Ported: bubbles that spawn, float, wobble and pop when you click them, at the shipping app's own sizes, "
        + "speeds and spawn rate. NOT ported, and not silently: the pop SOUND; the XP, lucky roll, achievement "
        + "and haptic a pop pays upstream (this build has no progression system at all); Trigger Bubbles, where a "
        + "pop fires a flash, a spiral or a video; the whole Chaos Mode roguelite that shares the same engine; "
        + "stare-to-pop; the companion easter egg; and multi-monitor spawning — bubbles run on the primary "
        + "display only.";

    /// <summary>
    /// How this row is proved, said where the user reads it. Deliberately explicit about the
    /// stopping point: no automated step on any platform proves a HUMAN popped anything.
    /// </summary>
    public const string EvidenceNotice =
        "How this is checked: the operating system is asked, for every bubble, whether it is on screen at that "
        + "exact rectangle, whether it is above every ordinary window, whether a click at its own centre would be "
        + "routed to it, and whether the foreground window is still exactly what it was. A bubble that fails any "
        + "of those is reported rather than drawn and counted. What no check here can show is that YOU clicked "
        + "one: a human popping a bubble is a manual step and this build never claims it happened.";

    /// <summary>
    /// The live sentence. Every clause is a state the module can really be in, and the routable one
    /// is the state this row invented: a field on the desktop that nobody can hit.
    /// </summary>
    /// <param name="dot">What the row's dot is showing, so the sentence cannot disagree with it.</param>
    /// <param name="sessionRunning">Whether a session owns the module at all.</param>
    /// <param name="canReachAPointer">The OS's own answer about this process's window station.</param>
    /// <param name="targetsUp">How many bubbles are on the desktop.</param>
    /// <param name="routable">How many of those the OS routes a click to.</param>
    /// <param name="popped">Bubbles popped this run.</param>
    /// <param name="missed">Bubbles that floated away unhit.</param>
    public static string DescribeFieldState(
        EffectDotState dot,
        bool sessionRunning,
        bool canReachAPointer,
        int targetsUp,
        int routable,
        int popped,
        int missed)
    {
        if (dot == EffectDotState.Off)
        {
            return "Bubble Pop is off. Nothing will be put on your screen, session or no session.";
        }

        if (!canReachAPointer)
        {
            return "Bubble Pop is armed and this build cannot put a clickable window in front of you at all — the "
                + "operating system reports no interactive desktop for this process. No bubbles will appear.";
        }

        if (!sessionRunning)
        {
            return "Bubble Pop is armed and waiting for a session. Bubbles start floating when you press START.";
        }

        var tally = $" Popped {popped}, missed {missed} this run.";

        if (targetsUp == 0)
        {
            return "Bubble Pop is running. No bubble is on screen at this instant; the next one is on the spawn "
                + "timer." + tally;
        }

        if (routable == 0)
        {
            return $"Bubble Pop is running and you CANNOT hit it: {targetsUp} bubble(s) are on screen and the "
                + "window manager is routing clicks at none of them to this app — something is above them. The "
                + "dot is dark for exactly this." + tally;
        }

        return $"Bubble Pop is running: {routable} of {targetsUp} bubble(s) on screen are ones the operating "
            + "system says a click would reach." + tally;
    }

    /// <summary>
    /// The delivery line — evidence, and labelled as evidence.
    ///
    /// <para><b>Why it is worded as a count and never as a claim.</b> A field nobody has clicked has
    /// zero of both numbers, and that is the ordinary state of a healthy row. So this sentence
    /// reports what the OS has delivered so far and never says anything about what it WOULD deliver:
    /// that claim belongs to the routing answer, which the capability earns.</para>
    /// </summary>
    public static string DescribeDelivery(int presses, int activationsRefused)
    {
        if (presses == 0)
        {
            return "No click has been delivered to a bubble yet. That is what a field nobody has clicked looks "
                + "like, and it is not a fault.";
        }

        return $"The operating system has delivered {presses} mouse message(s) to bubble windows, and their "
            + $"window procedure has refused activation {activationsRefused} time(s) — which is how a bubble "
            + "takes a click without taking your foreground.";
    }

    /// <summary>The capability's own last answer, verbatim. Never re-worded: a bug report quotes
    /// this, and a paraphrase is the port's own opinion of what the OS said.</summary>
    public static string DescribeCapability(CapabilityState? placement) => placement switch
    {
        null => "The pointer capability has not been asked for anything yet, so it has said nothing.",
        CapabilityState.Available available => $"Pointer surface: AVAILABLE — {available.Detail}",
        CapabilityState.Degraded degraded =>
            $"Pointer surface: DEGRADED — {degraded.SurvivingSemantics}. {degraded.Reason.Code}: {degraded.Reason.Detail}",
        CapabilityState.Unavailable unavailable =>
            $"Pointer surface: UNAVAILABLE — {unavailable.Reason.Code}: {unavailable.Reason.Detail}",
        CapabilityState.PermissionRequired permission =>
            $"Pointer surface: PERMISSION REQUIRED — {permission.Reason.Code}: {permission.Reason.Detail}",
        CapabilityState.DependencyMissing missing =>
            $"Pointer surface: DEPENDENCY MISSING ({missing.Dependency}) — {missing.Reason.Code}: {missing.Reason.Detail}",
        CapabilityState.Faulted faulted =>
            $"Pointer surface: FAULTED — {faulted.Reason.Code}: {faulted.Reason.Detail}",
        _ => "Pointer surface: an outcome this panel does not know how to describe.",
    };

    /// <summary>The spawn-rate dial's own line, in the units upstream's dial uses
    /// (<c>Models/AppSettings.cs:2210</c>: spawns per MINUTE, not per hour).</summary>
    public static string DescribeSpawnRate(int perMinute)
    {
        var interval = BubblePopField.SpawnInterval(perMinute);
        return $"{perMinute} bubble(s) a minute — one every {interval.TotalSeconds:0.#} s, up to "
            + $"{BubblePopField.MaxConcurrent} on screen at once.";
    }

    /// <summary>The size dial's own line, in the pixels the user will actually see.</summary>
    public static string DescribeSize(int sizePercent) =>
        $"{sizePercent} % — {BubblePopField.SizeFor(BubblePopField.BaseMinSize, sizePercent)} to "
        + $"{BubblePopField.SizeFor(BubblePopField.BaseMaxSize - 1, sizePercent)} px across.";

    /// <summary>The speed dial's own line, with the race bound the whole capability rests on stated
    /// in the same breath — because the two are the same number.</summary>
    public static string DescribeSpeed(int speedBoostPercent)
    {
        var top = BubblePopField.MaxSpeed * (1 + (speedBoostPercent / 100.0));
        return $"+{speedBoostPercent} % — up to {top:0.#} px of travel every "
            + $"{BubblePopField.StepInterval.TotalMilliseconds:0} ms, which is well under the smallest bubble's "
            + $"own radius ({BubblePopField.MinSize / 2} px), so a bubble never moves off the point you aimed at "
            + "between one step and the next.";
    }
}
