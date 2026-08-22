using CcpClient.Desktop.Features.Intake;
using Xunit;
using State = CcpClient.Desktop.Features.Intake.IntakePassService.IntakePassState;
using Reason = CcpClient.Desktop.Features.Intake.IntakePassService.IntakePassReason;

namespace CcpClient.Tests;

/// <summary>
/// The overturn round: the Graded Intake weekly-pass gate as a pure function.
///
/// <para>The packet first shipped this door UNGATED, on the reasoning that a local refusal would
/// be a claim about the install. That was wrong in the direction that matters. Unlimited retakes
/// are the patron privilege, stated outright upstream — <c>Premium</c> is "Patron. The pass
/// system does not apply - unlimited runs, no week, no door"
/// (<c>Services/Progression/IntakePassService.cs:13</c>) and "The intake is a premium Exclusive …
/// free accounts get ONE run per week … while retakes stay a reason to subscribe" (<c>:26-29</c>)
/// — so an ungated door hands every user paid content. These facts are the closure of that hole,
/// the same class that was closed at <c>DtrhGate</c> for an undefined tier.</para>
///
/// <para>The other half is that the refusal must be HONEST. WPF gives a free, signed-out user
/// <c>NeedsLogin</c> ("The pass is per-account, so there is nothing to hand out yet", <c>:15</c>;
/// branch at <c>:114</c>) and never <c>Spent</c>. The port has no account, so its answer is
/// "could not determine" — its own words, never the spent words.</para>
/// </summary>
public class IntakePassGateTests
{
    private const int AnyDays = 3;

    [Fact]
    public void PatronsProceed_AndSoDoesAReadFreeAccountWithAnUnspentWeek()
    {
        // WPF :140-146 — CanStartIntake = Premium || Available. Both grants, and ONLY these two.
        var premium = Assert.IsType<IntakePassDecision.Proceed>(
            IntakePassGate.Decide(State.Premium, Reason.PremiumEntitled, AnyDays));
        Assert.Equal(State.Premium, premium.State);

        var available = Assert.IsType<IntakePassDecision.Proceed>(
            IntakePassGate.Decide(State.Available, Reason.AvailableThisWeek, AnyDays));
        Assert.Equal(State.Available, available.State);
    }

    [Fact]
    public void TheGreenfieldDefault_IsUndeterminable_NotAGrant_AndNotASpentRefusal()
    {
        // THE BRANCH EVERY REAL USER OF THIS BUILD REACHES. IntakePassService's default seam
        // reports IsPremium=false and IsLoggedIn=null (not-applicable), so Evaluate() lands
        // Available/AvailableNoEntitlementProvider — a WEEK that was read, not a PASS that was
        // determined. The pass is per-account (:15) and there is no account.
        var decision = IntakePassGate.Decide(
            State.Available, Reason.AvailableNoEntitlementProvider, AnyDays);

        var undeterminable = Assert.IsType<IntakePassDecision.RefusedUndeterminable>(decision);
        Assert.Equal(Reason.AvailableNoEntitlementProvider, undeterminable.Reason);

        // It is a refusal — the over-grant is closed …
        Assert.IsNotType<IntakePassDecision.Proceed>(decision);

        // … and it does not wear either other refusal's clothes.
        Assert.DoesNotContain("You've taken your free Graded Intake", undeterminable.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("next pass unlocks", undeterminable.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sign in", undeterminable.Message, StringComparison.Ordinal);
        // It says what could not be told, and what would close it.
        Assert.Contains("could not determine your Graded Intake pass", undeterminable.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was decided about", undeterminable.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gap in the port", undeterminable.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Reason.SpentThisWeek)]
    [InlineData(Reason.SpentClockRollback)]
    public void ASpentWeek_RefusesWithWpfsVerbatimCopy(Reason reason)
    {
        // RefreshGradedIntakeGate writes the spent copy for the STATE and never asks why
        // (MainWindow/MainWindow.Lab.cs:414-427), so a rolled-back clock reads the same.
        var spent = Assert.IsType<IntakePassDecision.RefusedSpent>(
            IntakePassGate.Decide(State.Spent, reason, 4));

        Assert.Equal(4, spent.DaysUntilNextPass);
        Assert.Equal(
            "You've taken your free Graded Intake for this week. The next pass unlocks in 4 days. "
            + "Patrons retake it as often as they like.",
            spent.Message);
    }

    [Fact]
    public void OneDayLeft_UsesWpfsSecondKey_SoNobodyEverReadsUnlocksIn1Days()
    {
        // Two keys rather than one interpolation, and WPF says why: "unlocks in 1 days" is the
        // sort of thing that gets screenshotted (MainWindow/MainWindow.Lab.cs:416-418).
        var one = Assert.IsType<IntakePassDecision.RefusedSpent>(
            IntakePassGate.Decide(State.Spent, Reason.SpentThisWeek, 1));
        Assert.Equal(1, one.DaysUntilNextPass);
        Assert.Contains("unlocks tomorrow", one.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1 days", one.Message, StringComparison.Ordinal);

        // DaysUntilNextPass floors at 1 upstream (:81-86); a nonsense count cannot produce
        // "unlocks in 0 days" or a negative one here either.
        var floored = Assert.IsType<IntakePassDecision.RefusedSpent>(
            IntakePassGate.Decide(State.Spent, Reason.SpentThisWeek, 0));
        Assert.Equal(1, floored.DaysUntilNextPass);
        Assert.Contains("unlocks tomorrow", floored.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AThrownEvaluation_IsUndeterminable_NotSpent_WhichIsAnImprovementOnWpf()
    {
        // WPF fails CLOSED to Spent when State throws (:123-130) and then paints the spent copy,
        // telling a user their week is gone when in fact nothing was determined. The port refuses
        // just as hard — the over-grant stays closed — and says the true thing. §11 D33.
        var decision = IntakePassGate.Decide(State.Spent, Reason.SpentFailClosed, AnyDays);

        var undeterminable = Assert.IsType<IntakePassDecision.RefusedUndeterminable>(decision);
        Assert.Equal(Reason.SpentFailClosed, undeterminable.Reason);
        Assert.IsNotType<IntakePassDecision.Proceed>(decision);
        Assert.DoesNotContain("You've taken your free Graded Intake", undeterminable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedOut_RefusesWithWpfsPerAccountCopy_AndIsItsOwnBranch()
    {
        // Unreachable in this build (no login provider exists), implemented rather than collapsed
        // because the state is real upstream and collapsing it would be the boolean the
        // entitlement capability bans.
        var needsAccount = Assert.IsType<IntakePassDecision.RefusedNeedsAccount>(
            IntakePassGate.Decide(State.NeedsLogin, Reason.LoginRequired, AnyDays));

        Assert.Equal(
            "Your free Graded Intake is tied to your account. Sign in and this week's run is yours.",
            needsAccount.Message);
    }

    [Fact]
    public void EveryStateReasonPair_LandsOnExactlyOneDecision_AndOnlyTwoOfThemGrant()
    {
        // The closure guard, and the one that would have caught the original miss: run the WHOLE
        // cross product of the enums through the gate and count the grants. If a state or reason
        // is added later, this fails unless someone decides — explicitly — which side it is on.
        var states = Enum.GetValues<State>();
        var reasons = Enum.GetValues<Reason>();
        var granted = new List<(State, Reason)>();

        foreach (var state in states)
        {
            foreach (var reason in reasons)
            {
                var decision = IntakePassGate.Decide(state, reason, AnyDays);
                Assert.NotNull(decision);
                // Closed set: no pair may produce something the page cannot render.
                Assert.True(
                    decision is IntakePassDecision.Proceed
                        or IntakePassDecision.RefusedSpent
                        or IntakePassDecision.RefusedNeedsAccount
                        or IntakePassDecision.RefusedUndeterminable,
                    $"{state}/{reason} produced an unrenderable decision {decision.GetType().Name}");

                if (decision is IntakePassDecision.Proceed)
                {
                    granted.Add((state, reason));
                }
            }
        }

        Assert.Equal(
            new[] { (State.Premium, Reason.PremiumEntitled), (State.Available, Reason.AvailableThisWeek) },
            granted.ToArray());

        // Non-vacuous: the loop really did visit a full cross product.
        Assert.Equal(4, states.Length);
        Assert.Equal(7, reasons.Length);
    }

    [Fact]
    public void NoRefusalMessage_EverCarriesAnotherRefusalsSentence()
    {
        // The wording boundary, mechanically. The three refusals exist as three types so that
        // "could not tell" is never shown as "you are out of runs"; identical or overlapping copy
        // would defeat the types while leaving them green.
        var spent = (IntakePassDecision.RefusedSpent)IntakePassGate.Decide(State.Spent, Reason.SpentThisWeek, AnyDays);
        var needsAccount = (IntakePassDecision.RefusedNeedsAccount)IntakePassGate.Decide(State.NeedsLogin, Reason.LoginRequired, AnyDays);
        var undeterminable = (IntakePassDecision.RefusedUndeterminable)IntakePassGate.Decide(
            State.Available, Reason.AvailableNoEntitlementProvider, AnyDays);

        var messages = new[] { spent.Message, needsAccount.Message, undeterminable.Message };
        Assert.Equal(3, messages.Distinct(StringComparer.Ordinal).Count());

        // The spent claim ("you have used it") appears in exactly one message.
        Assert.Single(messages, m => m.Contains("You've taken your free Graded Intake", StringComparison.Ordinal));
        // The sign-in instruction appears in exactly one message.
        Assert.Single(messages, m => m.Contains("Sign in and this week's run is yours", StringComparison.Ordinal));
        // The port-gap disclosure appears in exactly one message, and it is the undeterminable one.
        var gapCarriers = messages.Where(m => m.Contains("gap in the port", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { undeterminable.Message }, gapCarriers);
    }

    [Fact]
    public void Classify_NamesTheBranchAndTheReason_AndNeverTheMessage()
    {
        // The log line's job is classification (the entitlement discipline). A message in a log is how
        // authored copy becomes an accidental telemetry field.
        Assert.Equal("proceed(premium)", IntakePassGate.Classify(
            IntakePassGate.Decide(State.Premium, Reason.PremiumEntitled, AnyDays)));
        Assert.Equal("refused(spent)", IntakePassGate.Classify(
            IntakePassGate.Decide(State.Spent, Reason.SpentThisWeek, AnyDays)));
        Assert.Equal("refused(needs-account)", IntakePassGate.Classify(
            IntakePassGate.Decide(State.NeedsLogin, Reason.LoginRequired, AnyDays)));
        Assert.Equal("refused(undeterminable:availablenoentitlementprovider)", IntakePassGate.Classify(
            IntakePassGate.Decide(State.Available, Reason.AvailableNoEntitlementProvider, AnyDays)));

        foreach (var state in Enum.GetValues<State>())
        {
            foreach (var reason in Enum.GetValues<Reason>())
            {
                var line = IntakePassGate.Classify(IntakePassGate.Decide(state, reason, AnyDays));
                Assert.DoesNotContain("Patrons retake", line, StringComparison.Ordinal);
                Assert.DoesNotContain("gap in the port", line, StringComparison.Ordinal);
                Assert.DoesNotContain("Sign in", line, StringComparison.Ordinal);
            }
        }
    }
}
