using ConditioningControlPanel;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The 18+ gate, pure. It used to be a MessageBox in front of the first-run window - untestable by
/// definition, and the first of the fourteen modal stops a new PC walked through. It is now the
/// Welcome step's Enter button, and this is the whole of its rule.
///
/// <para>The stakes are asymmetric and worth naming: a false Proceed is an unverified user inside
/// an adult app, and a false DeclineAndShutDown is an app that will not start. Both verdicts are
/// covered below rather than only the happy one.</para>
/// </summary>
public class FirstRunGateTests
{
    [Fact]
    public void TickedAndEntered_Proceeds()
        => Assert.Equal(FirstRunOutcome.Proceed,
            FirstRunGate.Decide(ageChecked: true, enterPressed: true));

    [Fact]
    public void NeitherTickedNorEntered_ShutsDown()
        // The X, Esc, or the taskbar. Same answer the old MessageBox's default No gave.
        => Assert.Equal(FirstRunOutcome.DeclineAndShutDown,
            FirstRunGate.Decide(ageChecked: false, enterPressed: false));

    [Fact]
    public void TickedButNeverEntered_ShutsDown()
        // Read the sentence, ticked the box, closed the window. A tick is not consent to run: only
        // Enter writes HasAcceptedAgeVerification, so anything else must not be treated as if it
        // had. This is the case the MessageBox could not even express.
        => Assert.Equal(FirstRunOutcome.DeclineAndShutDown,
            FirstRunGate.Decide(ageChecked: true, enterPressed: false));

    [Fact]
    public void EnteredWithTheBoxSomehowClear_ShutsDown()
        // Not reachable through the UI (Enter is disabled until the box is ticked) and that is
        // exactly why it is asserted: if the button's IsEnabled binding is ever lost, the verdict
        // must still fail closed rather than inherit the bug.
        => Assert.Equal(FirstRunOutcome.DeclineAndShutDown,
            FirstRunGate.Decide(ageChecked: false, enterPressed: true));

    // ---- MustShutDown: the launch that never reached the gate ----
    //
    // Decide covers the wizard's two buttons. This covers everything that happens INSTEAD of them,
    // and it is the half that was missing. ShouldRunAndClaim spends Welcomed and
    // FirstRunClaimedThisLaunch before the window is constructed - deliberately, so the old age
    // MessageBox cannot fire on top of the wizard - and App.OnStartup's gate then stands down for
    // the rest of the launch. So a constructor that threw, a ShowDialog that threw (both caught and
    // swallowed in Run), or a ladder that gave up after five minutes each left an adult app running
    // with HasAcceptedAgeVerification false and nothing left in the process that would ever ask.

    [Fact]
    public void NoAcceptanceOnFile_MustShutDown()
        // The wizard never reached its gate. There is no second gate behind it.
        => Assert.True(FirstRunGate.MustShutDown(ageAccepted: false));

    [Fact]
    public void AcceptanceOnFile_KeepsRunning()
        // Not hypothetical: an earlier launch that was handed back (the ladder gave up, the wizard
        // was closed after Enter) leaves Welcomed false and HasAcceptedAgeVerification true, and
        // this launch is properly gated even though its wizard did not finish. Shutting that down
        // would be an app that refuses to start.
        => Assert.False(FirstRunGate.MustShutDown(ageAccepted: true));
}
