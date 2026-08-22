using System.Globalization;
using State = CcpClient.Desktop.Features.Intake.IntakePassService.IntakePassState;
using Reason = CcpClient.Desktop.Features.Intake.IntakePassService.IntakePassReason;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// What the Graded Intake door does about the weekly pass. A CLOSED set of four outcomes, one
/// per user-visible answer — never a boolean, for the entitlement reason: a bool collapses "you have
/// had your run" into "this build cannot tell", and those two must never be shown as each other.
/// </summary>
public abstract record IntakePassDecision
{
    private IntakePassDecision() { }

    /// <summary>Open the intake. WPF: <c>CanStartIntake = Premium || Available</c>
    /// (<c>Services/Progression/IntakePassService.cs:140-146</c>).</summary>
    public sealed record Proceed(State State) : IntakePassDecision;

    /// <summary>This account has already had its free run this week. WPF's own copy, verbatim
    /// (<c>en.json:24-26</c>), and the day count it interpolates.</summary>
    public sealed record RefusedSpent(string Message, int DaysUntilNextPass) : IntakePassDecision;

    /// <summary>The pass is per-account and there is no account signed in. WPF's own copy,
    /// verbatim (<c>en.json:21-23</c>). UNREACHABLE in this build — no login provider exists —
    /// and implemented rather than collapsed, because the state is real upstream.</summary>
    public sealed record RefusedNeedsAccount(string Message) : IntakePassDecision;

    /// <summary>The port could not determine the pass at all. Its own words, never the other
    /// two refusals' words.</summary>
    public sealed record RefusedUndeterminable(string Message, Reason Reason) : IntakePassDecision;
}

/// <summary>
/// The Graded Intake pass gate, as a pure function — the <see cref="Dtrh.DtrhGate"/> shape, and
/// the port's answer to WPF's own claim that there is "exactly one place that decides who gets
/// in and why" (<c>Services/Progression/IntakePassService.cs:8</c>).
///
/// <para><b>WHY A GATE EXISTS AT ALL, since this door first shipped without one.</b>
/// The intake is paid content and unlimited retakes are the patron privilege, stated outright
/// upstream: <c>Premium</c> is "Patron. The pass system does not apply - unlimited runs, no
/// week, no door" (<c>:13</c>), and the class header says "The intake is a premium Exclusive …
/// free accounts get ONE run per week … while retakes stay a reason to subscribe"
/// (<c>:26-29</c>). An ungated door hands every user the patron privilege. That is an
/// OVER-GRANT — the same direction as the undefined-tier hole closed at
/// <c>DtrhGate</c>, and the opposite of the under-grant §10 D24 records, which errs toward
/// refusing something WPF would allow.</para>
///
/// <para><b>AND THE HONEST REFUSAL IS "COULD NOT TELL", NOT "YOU ARE OUT OF RUNS".</b> WPF gives
/// a free, signed-out user <c>NeedsLogin</c> — "The pass is per-account, so there is nothing to
/// hand out yet" (<c>:15</c>, and the branch at <c>:115</c>) — and NOT <c>Spent</c>. The port has
/// no account of any kind, so it cannot determine the pass, and saying "you already ran this
/// week" would be a claim about the install that nobody could lift. That is the entitlement capability's
/// three-way distinction applied to a second surface: entitled / not entitled / could not tell,
/// with "could not tell" refusing out loud in its own words and never wearing the other
/// refusal's clothes. Divergence and close condition at wpf-surface-reachability.md §11 D26.</para>
/// </summary>
public static class IntakePassGate
{
    /// <summary>WPF's spent copy, <c>intake_gate_spent_body</c> (<c>en.json:25</c>), verbatim.
    /// <c>{0}</c> is the day count.</summary>
    public const string SpentMessageFormat =
        "You've taken your free Graded Intake for this week. The next pass unlocks in {0} days. "
        + "Patrons retake it as often as they like.";

    /// <summary>WPF's one-day variant, <c>intake_gate_spent_body_one_day</c> (<c>en.json:26</c>),
    /// verbatim. Two keys rather than one interpolation, because "unlocks in 1 days" is the sort
    /// of thing that gets screenshotted (<c>MainWindow/MainWindow.Lab.cs:416-418</c>).</summary>
    public const string SpentMessageOneDay =
        "You've taken your free Graded Intake for this week. The next pass unlocks tomorrow. "
        + "Patrons retake it as often as they like.";

    /// <summary>WPF's signed-out copy, <c>intake_gate_login_body</c> (<c>en.json:22</c>), verbatim.</summary>
    public const string NeedsAccountMessage =
        "Your free Graded Intake is tied to your account. Sign in and this week's run is yours.";

    /// <summary>
    /// The branch every real user of this build reaches, and it must read as a statement about
    /// the PORT. It never says the run was used, never says the person is not entitled, and it
    /// names what would close it — the §10 D21 discipline, applied to the intake.
    /// </summary>
    public const string UndeterminableMessage =
        "This build could not determine your Graded Intake pass, so it did not start a run. "
        + "The pass is per-account — patrons run the intake as often as they like, and a free "
        + "account gets one run a week — and this port has no account to read. "
        + "That is a gap in the port, not a finding about your access: nothing was decided about "
        + "you. It closes when the port has an entitlement authority for the intake.";

    /// <summary>
    /// Decide, from the pass state and the typed reason it landed on. BOTH are needed: the same
    /// <c>Available</c> state means "this week's run is waiting" when an authority actually
    /// answered and "nobody could tell" when none exists, and those are opposite answers.
    /// </summary>
    public static IntakePassDecision Decide(State state, Reason reason, int daysUntilNextPass)
    {
        // WPF :140-146 — CanStartIntake is Premium || Available. Premium is the patron's
        // "unlimited runs, no week, no door" (:13); Available is a free account whose week is
        // unspent AND whose account was actually READ (AvailableThisWeek).
        if (state == State.Premium && reason == Reason.PremiumEntitled)
        {
            return new IntakePassDecision.Proceed(state);
        }

        if (state == State.Available && reason == Reason.AvailableThisWeek)
        {
            return new IntakePassDecision.Proceed(state);
        }

        // The greenfield default seam: no entitlement provider, so IsLoggedIn is not-applicable
        // and IsPremium is merely "nobody said yes". The week key was read, but the week is not
        // the pass — the pass is per-account (:15). Undeterminable, never a grant.
        if (state == State.Available && reason == Reason.AvailableNoEntitlementProvider)
        {
            return new IntakePassDecision.RefusedUndeterminable(UndeterminableMessage, reason);
        }

        // Evaluation THREW. WPF fails closed to Spent and paints the spent copy
        // (:123-130, return at :129 -> MainWindow.Lab.cs:414-427), which tells a user their week is gone when in
        // fact nothing was determined. The port refuses just as hard and says the true thing — a
        // deliberate improvement, recorded at §11 D33 rather than smuggled in as parity.
        if (state == State.Spent && reason == Reason.SpentFailClosed)
        {
            return new IntakePassDecision.RefusedUndeterminable(UndeterminableMessage, reason);
        }

        if (state == State.Spent)
        {
            // SpentThisWeek and SpentClockRollback both render WPF's spent copy:
            // RefreshGradedIntakeGate writes it for the STATE and never asks why
            // (MainWindow.Lab.cs:414-427).
            var days = Math.Max(1, daysUntilNextPass);
            return new IntakePassDecision.RefusedSpent(
                days == 1
                    ? SpentMessageOneDay
                    : string.Format(CultureInfo.InvariantCulture, SpentMessageFormat, days),
                days);
        }

        if (state == State.NeedsLogin)
        {
            return new IntakePassDecision.RefusedNeedsAccount(NeedsAccountMessage);
        }

        // Every (state, reason) pair above is accounted for. Anything else is an enum that grew
        // without this gate growing with it, and the only safe answer to an outcome this function
        // does not understand is to refuse and say so — never to open paid content. This is the
        // same closure put on DtrhGate after the undefined-tier over-grant.
        return new IntakePassDecision.RefusedUndeterminable(UndeterminableMessage, reason);
    }

    /// <summary>Log-safe decision class. Never the message: the log's job is classification, and
    /// one vocabulary beats two (the entitlement-logging discipline).</summary>
    public static string Classify(IntakePassDecision decision) => decision switch
    {
        IntakePassDecision.Proceed proceed => "proceed(" + proceed.State.ToString().ToLowerInvariant() + ")",
        IntakePassDecision.RefusedSpent => "refused(spent)",
        IntakePassDecision.RefusedNeedsAccount => "refused(needs-account)",
        IntakePassDecision.RefusedUndeterminable undeterminable =>
            "refused(undeterminable:" + undeterminable.Reason.ToString().ToLowerInvariant() + ")",
        _ => "refused(unknown-decision)",
    };
}
