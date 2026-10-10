using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Progression;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// THE BANKER - the coalescing rules that stand between roughly thirty XP call sites and one
/// celebration. Everything worth testing here is a timing decision, and timing decisions are
/// exactly what cannot be verified by watching the app: "did that pot fly twice" and "did the
/// cooldown actually hold" both look like a normal session from the outside.
///
/// <para>The clock is a plain field the test moves by hand, which is the whole point of the
/// injected <c>Func&lt;double&gt;</c>: no sleeps, no flake, and a level-up landing 1ms before a
/// window expiry is as easy to write as one landing in the middle.</para>
/// </summary>
public class BankAccumulatorTests
{
    private double _now;
    private BankAccumulator New() { _now = 0; return new BankAccumulator(() => _now); }

    [Fact]
    public void NullClock_IsRefusedAtConstruction()
        => Assert.Throws<ArgumentNullException>(() => new BankAccumulator(null!));

    [Fact]
    public void AwardsInsideTheWindow_JoinOnePot_AndFlyOnce()
    {
        var bank = New();

        Assert.Null(bank.OnAward(10, "Flash", false));
        _now = 100; Assert.Null(bank.OnAward(20, "Flash", false));
        _now = 900; Assert.Null(bank.OnAward(30, "Mantra", false));

        // Still collecting: the window has not run out.
        _now = BankAccumulator.WindowMs - 1;
        Assert.Null(bank.Tick());

        _now = BankAccumulator.WindowMs;
        var flight = bank.Tick();
        Assert.NotNull(flight);
        Assert.Equal(60, flight!.XpSum, 6);
        Assert.Equal(BankFlightPlan.TokenCount(60), flight.TokenCount);

        // And the pot is gone - a second poll must not celebrate the same XP twice.
        _now = 10_000;
        Assert.Null(bank.Tick());
    }

    [Fact]
    public void NoPot_MeansNothingToPoll()
    {
        var bank = New();
        _now = 50_000;
        Assert.Null(bank.Tick());
        Assert.False(bank.HasOpenPot);
    }

    [Fact]
    public void CooldownDefersTheNextFlight_AndTheDeferredAwardsAllRide()
    {
        var bank = New();

        bank.OnAward(10, "Flash", false);
        _now = BankAccumulator.WindowMs;
        Assert.NotNull(bank.Tick());              // first flight leaves at 1500

        _now = 1600; bank.OnAward(5, "Bubble", false);
        _now = 1700; bank.OnAward(7, "Bubble", false);

        // Window expired at 3100, but the cooldown runs to 4500. Nothing may leave yet.
        _now = 3200; Assert.Null(bank.Tick());
        _now = 4499; Assert.Null(bank.Tick());

        _now = BankAccumulator.WindowMs + BankAccumulator.CooldownMs;   // 4500
        var second = bank.Tick();
        Assert.NotNull(second);
        Assert.Equal(12, second!.XpSum, 6);
    }

    [Fact]
    public void AwardsDuringCooldownKeepJoining_SoTheFlightGetsFullerNotMoreFrequent()
    {
        var bank = New();

        bank.OnAward(10, "Flash", false);
        _now = BankAccumulator.WindowMs;
        Assert.NotNull(bank.Tick());

        // A steady drip all through the cooldown.
        for (double t = 1600; t < 4500; t += 200)
        {
            _now = t;
            Assert.Null(bank.OnAward(1, "Flash", false));
        }

        _now = 4500;
        var flight = bank.Tick();
        Assert.NotNull(flight);
        Assert.Equal(15, flight!.XpSum, 6);       // 1600..4400 inclusive, every 200ms
    }

    [Fact]
    public void AnAwardArrivingAfterTheWindow_ShipsTheRipePotAndOpensTheNext()
    {
        var bank = New();

        bank.OnAward(40, "Session", false);

        _now = 4000;
        var flight = bank.OnAward(9, "Bubble", false);
        Assert.NotNull(flight);
        Assert.Equal(40, flight!.XpSum, 6);       // the arriving award did NOT join the pot it closed
        Assert.Equal("Session", flight.DominantSource);

        // ...and it opened the next one, which flies on its own window plus the new cooldown.
        _now = 7000;
        var next = bank.Tick();
        Assert.NotNull(next);
        Assert.Equal(9, next!.XpSum, 6);
    }

    [Fact]
    public void ALeveledUpAward_PoisonsThePot()
    {
        var bank = New();

        bank.OnAward(10, "Flash", false);
        _now = 200;
        Assert.Null(bank.OnAward(9_999, "Session", leveledUp: true));

        // House law, one burst per moment: CelebrateLevelUp owns this instant, so nothing of the
        // pot survives - not the earlier XP, and not the leveling award itself.
        Assert.False(bank.HasOpenPot);
        _now = 60_000;
        Assert.Null(bank.Tick());
    }

    [Fact]
    public void AfterALevelUp_TheNextPotIsNormal()
    {
        var bank = New();

        bank.OnAward(10, "Flash", false);
        _now = 200; bank.OnAward(500, "Session", leveledUp: true);

        _now = 3000; Assert.Null(bank.OnAward(25, "Bubble", false));
        _now = 4600;
        var flight = bank.Tick();
        Assert.NotNull(flight);
        Assert.Equal(25, flight!.XpSum, 6);
    }

    [Fact]
    public void Reset_DropsThePotSilently()
    {
        var bank = New();

        bank.OnAward(10, "Flash", false);
        _now = 200; bank.OnAward(10, "Flash", false);
        bank.Reset();

        Assert.False(bank.HasOpenPot);
        _now = 30_000;
        Assert.Null(bank.Tick());
    }

    [Fact]
    public void Reset_DoesNotBuyBackALaunchSlot()
    {
        var bank = New();

        bank.OnAward(10, "Flash", false);
        _now = BankAccumulator.WindowMs;
        Assert.NotNull(bank.Tick());              // launched at 1500

        _now = 1600; bank.OnAward(4, "Flash", false);
        bank.Reset();                             // window deactivated, pot dropped
        _now = 1700; bank.OnAward(4, "Flash", false);

        // The cooldown from the 1500 launch still governs; a reset is not a get-out.
        _now = 3300; Assert.Null(bank.Tick());
        _now = 4500; Assert.NotNull(bank.Tick());
    }

    [Fact]
    public void DominantSource_IsWhoeverPutTheMostIn()
    {
        var bank = New();

        bank.OnAward(10, "Flash", false);
        _now = 100; bank.OnAward(30, "Session", false);
        _now = 200; bank.OnAward(5, "Flash", false);

        _now = 2000;
        var flight = bank.Tick();
        Assert.NotNull(flight);
        Assert.Equal("Session", flight!.DominantSource);
        Assert.Equal(45, flight.XpSum, 6);
    }

    [Fact]
    public void DominantSource_TiesGoToWhoeverOpenedThePot()
    {
        var bank = New();

        bank.OnAward(10, "Bubble", false);
        _now = 100; bank.OnAward(10, "Mantra", false);

        _now = 2000;
        Assert.Equal("Bubble", bank.Tick()!.DominantSource);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void JunkAwards_DoNotEvenOpenAPot(double amount)
    {
        var bank = New();

        Assert.Null(bank.OnAward(amount, "Flash", false));
        Assert.False(bank.HasOpenPot);

        _now = 30_000;
        Assert.Null(bank.Tick());
    }

    [Fact]
    public void TokenCount_OnTheFlight_IsThePlansPriceForThePot()
    {
        var bank = New();

        bank.OnAward(400, "Session", false);
        _now = 2000;
        var flight = bank.Tick();

        Assert.NotNull(flight);
        Assert.Equal(BankFlightPlan.MaxTokens, flight!.TokenCount);
    }

    // ---- the guest list ----
    //
    // THE BANK is a celebration for FINISHING something. The gate is what makes the moment rare,
    // and rarity is what the louder flight was dialled against - so the list is asserted in both
    // directions rather than left as a comment on a switch.

    [Theory]
    [InlineData("Quest")]
    [InlineData("Session")]
    [InlineData("LockCard")]
    [InlineData("BubbleCount")]
    public void Completions_AreBankable(string source)
        => Assert.True(BankAccumulator.IsBankable(source));

    [Theory]
    [InlineData("Flash")]
    [InlineData("Video")]
    [InlineData("Subliminal")]
    [InlineData("Bubble")]
    [InlineData("BouncingText")]
    [InlineData("AvatarInteraction")]
    [InlineData("KeywordTrigger")]
    [InlineData("Mantra")]
    [InlineData("AttentionCheck")]
    [InlineData("Chaos")]
    [InlineData("Fyp")]
    [InlineData("Other")]
    public void Weather_IsNot(string source)
        => Assert.False(BankAccumulator.IsBankable(source));

    [Theory]
    [InlineData("")]
    [InlineData("quest")]
    [InlineData("SomethingNew")]
    public void AnUnknownSourceName_IsWeather(string source)
    {
        // This head passes source NAMES. A name nobody reviewed lands on the "weather" side, never
        // throws and never silently starts flying (the WPF enum sweep's job).
        Assert.False(BankAccumulator.IsBankable(source));
        Assert.False(BankAccumulator.IsBankable(null!));
    }
}
