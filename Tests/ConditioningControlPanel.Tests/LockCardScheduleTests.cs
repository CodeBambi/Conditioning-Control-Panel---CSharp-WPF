using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.LockCardService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #736 — the first lock card of a run used to be scheduled a whole inter-arrival interval out
/// (60/freq ±30%). At 1/hour that put the earliest possible card at minute 42, so the 30-minute
/// "Kept · Day 1 — The Vow" session could never produce one; because its task is non-optional, the
/// program was hard-blocked at day 1 forever. The first card is now an offset INTO the opening
/// interval, clamped to the session window when one is known.
/// </summary>
public class LockCardScheduleTests
{
    // A roll arbitrarily close to 1 — the worst case, and the one the old code got wrong.
    private const double WorstCaseRoll = 0.999999;

    [Fact]
    public void KeptDay1_CardLandsInsideTheSession()
    {
        // Kept Day 1: 30-minute session, lock cards deferred to minute 12, frequency 1/hour.
        // The deferred start sees 18 minutes of session left.
        const double windowLeft = 30 - 12;
        var delay = ComputeFirstCardDelayMinutes(perHour: 1, windowMinutes: windowLeft, roll: WorstCaseRoll);

        Assert.True(delay < windowLeft,
            $"first card at +{delay:F1}min must fall inside the {windowLeft}min remaining, or Day 1 can never complete");

        // And it lands early enough in the session to actually be typed out.
        Assert.True(12 + delay < 30);
    }

    [Fact]
    public void OldBehaviourWouldHaveMissed()
    {
        // Documents the regression: the previous scheme's *minimum* first interval at 1/hour.
        const double oldMinimumFirstInterval = 60.0 * 0.7;
        Assert.True(oldMinimumFirstInterval > 30 - 12,
            "sanity: the old floor really was outside a Kept Day 1 session");

        Assert.True(ComputeFirstCardDelayMinutes(1, 30 - 12, WorstCaseRoll) < oldMinimumFirstInterval);
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(18.0)]
    [InlineData(15.0)]
    [InlineData(5.0)]
    public void AnyKnownWindow_AlwaysLeavesRoomToComplete(double window)
    {
        var delay = ComputeFirstCardDelayMinutes(perHour: 1, windowMinutes: window, roll: WorstCaseRoll);

        Assert.True(delay < window);
        // The tail of the window stays free so the card is completable, not just spawnable.
        Assert.True(delay <= window * 0.8);
    }

    [Fact]
    public void OpenEndedRun_KeepsTheNominalRate()
    {
        // Dashboard use: no session window, so the first card is anywhere in the first hour.
        Assert.Equal(0.0, ComputeFirstCardDelayMinutes(1, null, 0.0));
        Assert.True(ComputeFirstCardDelayMinutes(1, null, WorstCaseRoll) < 60.0);
        Assert.True(ComputeFirstCardDelayMinutes(4, null, WorstCaseRoll) < 15.0);
    }

    [Fact]
    public void GenerousWindow_DoesNotStretchBeyondTheInterval()
    {
        // A long session must not push the first card later than the frequency implies.
        Assert.True(ComputeFirstCardDelayMinutes(perHour: 2, windowMinutes: 600, roll: WorstCaseRoll) < 30.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveFrequency_IsTreatedAsOnePerHour(int perHour)
    {
        var delay = ComputeFirstCardDelayMinutes(perHour, windowMinutes: null, roll: WorstCaseRoll);

        Assert.True(double.IsFinite(delay), "must not divide by zero into infinity");
        Assert.True(delay < 60.0);
    }

    [Fact]
    public void HigherFrequency_SchedulesSooner()
    {
        var slow = ComputeFirstCardDelayMinutes(1, null, 0.5);
        var fast = ComputeFirstCardDelayMinutes(6, null, 0.5);

        Assert.True(fast < slow);
    }
}
