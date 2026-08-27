using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1045 - a Deeper enhancement fired a flash (or subliminal) on its last tick and the visual
/// outlived the video, running the authored timeline segment's own duration on an empty screen.
///
/// The first attempt at the fix cleared the services' <c>_oneShotActive</c> latch. That is inert in
/// the common case: every arrival guard reads <c>!_isRunning &amp;&amp; !_oneShotActive</c>, so with
/// the ambient Flashes/Subliminals feature running (a Deeper enhancement bound to a mandatory video
/// inside a normal session) <c>_isRunning</c> is true and clearing the latch decides nothing.
///
/// Point-fired effects therefore carry the one-shot GENERATION they were dispatched under, and
/// cancelling retires that generation. These cover the pure decisions.
/// </summary>
public class OneShotGateTests
{
    // ---- IsRetired: does this dispatch still belong to a live one-shot? ----

    [Fact]
    public void AmbientWork_IsNeverRetired()
    {
        // The ambient scheduler's own flashes carry no generation. Cancelling a Deeper effect must
        // never take the user's own flash rhythm down with it, however many cancels have happened.
        Assert.False(OneShotGate.IsRetired(null, 0));
        Assert.False(OneShotGate.IsRetired(null, 7));
    }

    [Fact]
    public void CurrentGenerationDispatch_IsNotRetired()
    {
        Assert.False(OneShotGate.IsRetired(0, 0));
        Assert.False(OneShotGate.IsRetired(42, 42));
    }

    [Fact]
    public void DispatchFromBeforeTheCancel_IsRetired()
    {
        // The #1045 shape: the loader was handed the generation that was current at dispatch, the
        // engine stopped and bumped it, and the async void loader only now arrives.
        Assert.True(OneShotGate.IsRetired(0, 1));
        Assert.True(OneShotGate.IsRetired(3, 4));
    }

    [Fact]
    public void RetirementDoesNotDependOnTheAmbientScheduler()
    {
        // The whole point of the generation: the decision is the same whether or not the user has
        // the ambient feature running, which is what the latch could not manage.
        const int dispatched = 5;
        Assert.False(OneShotGate.IsRetired(dispatched, dispatched));
        Assert.True(OneShotGate.IsRetired(dispatched, dispatched + 1));
    }

    [Fact]
    public void SeveralCancelsInARow_KeepEarlierDispatchesRetired()
    {
        // Two enhancements stopped back to back must not resurrect the first one's flash.
        Assert.True(OneShotGate.IsRetired(1, 3));
        Assert.True(OneShotGate.IsRetired(2, 3));
        Assert.False(OneShotGate.IsRetired(3, 3));
    }

    // ---- ShouldBlankOnCancel: take the visible subliminal card down, or leave it? ----

    [Fact]
    public void AmbientStopped_BlanksWhateverIsOnScreen()
    {
        // Nothing but the one-shot can be up, so the caller does not need to know whose card it is.
        Assert.True(OneShotGate.ShouldBlankOnCancel(ambientRunning: false, visibleGeneration: null, retiredGeneration: 0));
        Assert.True(OneShotGate.ShouldBlankOnCancel(ambientRunning: false, visibleGeneration: 4, retiredGeneration: 9));
    }

    [Fact]
    public void AmbientRunning_BlanksTheCardTheCancelledOneShotPutUp()
    {
        // The regression #1045 describes: the Deeper card carries the segment duration and would
        // otherwise sit there after the video ended.
        Assert.True(OneShotGate.ShouldBlankOnCancel(ambientRunning: true, visibleGeneration: 2, retiredGeneration: 2));
    }

    [Fact]
    public void AmbientRunning_LeavesTheUsersOwnCardAlone()
    {
        // An ambient card carries no generation; blanking it would interrupt the user's own
        // subliminal rhythm for a Deeper effect that had already finished.
        Assert.False(OneShotGate.ShouldBlankOnCancel(ambientRunning: true, visibleGeneration: null, retiredGeneration: 2));
    }

    [Fact]
    public void AmbientRunning_LeavesANewerOneShotCardAlone()
    {
        // A second enhancement already put its own card up before the first one's Stop landed.
        Assert.False(OneShotGate.ShouldBlankOnCancel(ambientRunning: true, visibleGeneration: 3, retiredGeneration: 2));
    }
}
