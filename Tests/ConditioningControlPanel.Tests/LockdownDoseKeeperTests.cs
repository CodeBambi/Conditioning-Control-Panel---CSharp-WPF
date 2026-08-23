using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Dose (Services/Haptics/LockdownDoseKeeper.cs) - the pure half: which features a round
/// conscripts, how many, and how fast the grace shrinks. The runtime half (engine start/stop,
/// SetWallFeature, recovery file) is WPF-bound and is exercised by the play-test, not here.
/// </summary>
public class LockdownDoseKeeperTests
{
    private static readonly string[] Starter = { "flash", "subliminal", "spiral", "pinkfilter", "bouncingtext", "bubbles" };
    private static readonly string[] Escalation = { "video" };

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(9, 4)]
    public void WantedFor_TwoThenOneMorePerRound_CapsAtFour(int round, int expected)
        => Assert.Equal(expected, LockdownDoseKeeper.WantedFor(round));

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 4)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(10, 2)]
    public void DoseGrace_ShrinksPerRound_FloorsAtTwoSeconds(int roundsSoFar, int expected)
        => Assert.Equal(expected, LockdownDoseKeeper.DoseGraceFor(roundsSoFar));

    [Fact]
    public void Round1_PicksTwoStarters_WhenTheUserHadNothingOn()
    {
        var picks = LockdownDoseKeeper.PickConscripts(1, Array.Empty<string>(), Starter, Escalation,
            Array.Empty<string>(), new Random(7));

        Assert.Equal(2, picks.Count);
        Assert.All(picks, k => Assert.Contains(k, Starter));
        Assert.Equal(picks.Count, picks.Distinct().Count());
    }

    [Fact]
    public void Round1_TurnsTheUsersOwnFeaturesBackOnFirst()
    {
        // They had flash + bubbles on at activation and switched both off: those come back before
        // anything else is invented for them.
        var picks = LockdownDoseKeeper.PickConscripts(1, new[] { "bubbles", "flash" }, Starter, Escalation,
            Array.Empty<string>(), new Random(3));

        Assert.Equal(2, picks.Count);
        Assert.Contains("flash", picks);
        Assert.Contains("bubbles", picks);
    }

    [Fact]
    public void NeverPicksWhatIsAlreadyOn()
    {
        var on = new[] { "flash", "subliminal" };
        var picks = LockdownDoseKeeper.PickConscripts(2, Array.Empty<string>(), Starter, Escalation, on, new Random(1));

        Assert.Equal(3, picks.Count);
        Assert.DoesNotContain("flash", picks);
        Assert.DoesNotContain("subliminal", picks);
    }

    [Fact]
    public void Round1_NeverReachesTheEscalationPool()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var picks = LockdownDoseKeeper.PickConscripts(1, Array.Empty<string>(), Starter, Escalation,
                Array.Empty<string>(), new Random(seed));
            Assert.DoesNotContain("video", picks);
        }
    }

    [Fact]
    public void Round2Plus_CanReachTheEscalationPool_WhenStartersRunOut()
    {
        // Everything in the starter pool is already on: the only thing left to add is video.
        var picks = LockdownDoseKeeper.PickConscripts(2, Array.Empty<string>(), Starter, Escalation, Starter, new Random(5));
        Assert.Equal(new[] { "video" }, picks);
    }

    [Fact]
    public void UnknownPreviouslyOnKeys_AreIgnored()
    {
        // A key the catalog does not know (a Tier 2 feature, a typo, a future flag) is never "picked".
        var picks = LockdownDoseKeeper.PickConscripts(1, new[] { "braindrain", "nope" }, Starter, Escalation,
            Array.Empty<string>(), new Random(2));
        Assert.DoesNotContain("braindrain", picks);
        Assert.DoesNotContain("nope", picks);
        Assert.Equal(2, picks.Count);
    }

    [Fact]
    public void NothingLeftToPick_ReturnsEmpty_NotAnException()
    {
        var all = Starter.Concat(Escalation).ToArray();
        var picks = LockdownDoseKeeper.PickConscripts(3, all, Starter, Escalation, all, new Random(0));
        Assert.Empty(picks);
    }

    [Fact]
    public void Deterministic_UnderTheSameSeed()
    {
        var a = LockdownDoseKeeper.PickConscripts(2, new[] { "spiral" }, Starter, Escalation, Array.Empty<string>(), new Random(42));
        var b = LockdownDoseKeeper.PickConscripts(2, new[] { "spiral" }, Starter, Escalation, Array.Empty<string>(), new Random(42));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Catalog_TierZeroIsTheStarterMix_AndEveryKeyIsAWallKey()
    {
        var tier0 = LockdownDoseKeeper.Catalog.Where(f => f.Tier == 0).Select(f => f.Key).ToArray();
        Assert.Equal(Starter.OrderBy(k => k), tier0.OrderBy(k => k));

        // Keys are the wall keys MainWindow.SetWallFeature switches on; a catalog entry it does not
        // know would be a conscription that flips nothing.
        var wallKeys = new HashSet<string> { "flash", "video", "subliminal", "spiral", "pinkfilter", "bubbles",
            "lockcard", "bubblecount", "bouncingtext", "mindwipe", "braindrain" };
        Assert.All(LockdownDoseKeeper.Catalog, f => Assert.Contains(f.Key, wallKeys));
    }
}
