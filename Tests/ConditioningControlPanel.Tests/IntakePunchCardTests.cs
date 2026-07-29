using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The eight-hole intake punch card. Every test drives a service pointed at a throwaway temp file
/// (the ctor's test seam) - a punch card is weeks of a real user's time and a test run must never
/// be able to touch, let alone reset, the real one in %LOCALAPPDATA%.
///
/// The rule under test throughout: hole 1 is free, and holes 2-8 each need BOTH a completed intake
/// AND the session that intake drafted actually being run.
/// </summary>
public class IntakePunchCardTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public IntakePunchCardTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-punchcard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "intake_punchcard.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private IntakePunchCardService NewCard() => new(_path);

    private static Session SessionWithId(string id) => new() { Id = id };

    // ---- opening a card ----

    [Fact]
    public void NewCard_StartsWithOneHoleOnTheHouse()
    {
        var card = NewCard();
        Assert.Equal(1, card.PunchedCount);
        Assert.Equal(7, card.HolesRemaining);
        Assert.False(card.IsComplete);
        Assert.False(card.HasPendingStamp);
    }

    [Fact]
    public void NewCard_FirstHoleReadsAsPunched_RestEmpty()
    {
        var card = NewCard();
        Assert.Equal(PunchHoleView.Punched, card.HoleAt(0));
        Assert.Equal(PunchHoleView.Empty, card.HoleAt(1));
        Assert.Equal(PunchHoleView.Empty, card.HoleAt(IntakePunchCardService.TotalHoles - 1));
    }

    [Fact]
    public void HoleAt_OutOfRange_IsEmptyRatherThanThrowing()
    {
        var card = NewCard();
        Assert.Equal(PunchHoleView.Empty, card.HoleAt(-1));
        Assert.Equal(PunchHoleView.Empty, card.HoleAt(IntakePunchCardService.TotalHoles));
        Assert.Equal(PunchHoleView.Empty, card.HoleAt(9999));
    }

    // ---- the two-part stamp ----

    [Fact]
    public void CompletingAnIntake_DoesNotPunchOnItsOwn()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");

        // This is the whole design: the quiz alone earns nothing.
        Assert.Equal(1, card.PunchedCount);
        Assert.True(card.HasPendingStamp);
        Assert.Equal(PunchHoleView.Pending, card.HoleAt(1));
    }

    [Fact]
    public void RunningTheDraftedSession_LandsTheHole()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        card.NotifySessionCompleted(SessionWithId("session-a"));

        Assert.Equal(2, card.PunchedCount);
        Assert.False(card.HasPendingStamp);
        Assert.Equal(PunchHoleView.Punched, card.HoleAt(1));
    }

    [Fact]
    public void RunningAnUnrelatedSession_EarnsNothing()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        card.NotifySessionCompleted(SessionWithId("some-other-session"));

        Assert.Equal(1, card.PunchedCount);
        Assert.True(card.HasPendingStamp);
    }

    [Fact]
    public void RunningASessionWithNoIntakeBehindIt_EarnsNothing()
    {
        var card = NewCard();
        card.NotifySessionCompleted(SessionWithId("session-a"));
        Assert.Equal(1, card.PunchedCount);
    }

    [Fact]
    public void ReRunningTheSameSession_OnlyPunchesOnce()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        card.NotifySessionCompleted(SessionWithId("session-a"));
        card.NotifySessionCompleted(SessionWithId("session-a"));
        card.NotifySessionCompleted(SessionWithId("session-a"));

        Assert.Equal(2, card.PunchedCount);
    }

    [Fact]
    public void TheSameIntakeQueuedTwice_OnlyEarnsOneHole()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        card.NotifyIntakeCompleted("session-a");
        card.NotifySessionCompleted(SessionWithId("session-a"));

        Assert.Equal(2, card.PunchedCount);
        Assert.False(card.HasPendingStamp);
    }

    [Fact]
    public void NullOrBlankSessionId_IsIgnored()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted(null);
        card.NotifyIntakeCompleted("");
        card.NotifyIntakeCompleted("   ");
        Assert.False(card.HasPendingStamp);

        card.NotifySessionCompleted(null);
        Assert.Equal(1, card.PunchedCount);
    }

    // ---- the halfway threshold ----

    [Theory]
    [InlineData(0.0)]
    [InlineData(25.0)]
    [InlineData(49.9)]
    public void ProgressBelowTheThreshold_DoesNotPunch(double percent)
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        card.NotifySessionProgress(SessionWithId("session-a"), percent);

        Assert.Equal(1, card.PunchedCount);
        Assert.True(card.HasPendingStamp);
    }

    [Theory]
    [InlineData(50.0)]
    [InlineData(75.0)]
    [InlineData(100.0)]
    public void ProgressAtOrAboveTheThreshold_Punches(double percent)
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        card.NotifySessionProgress(SessionWithId("session-a"), percent);

        Assert.Equal(2, card.PunchedCount);
    }

    [Fact]
    public void ProgressTicksAreIdempotent()
    {
        // The hook fires on every engine tick; it must not punch once per second.
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        for (var p = 50.0; p <= 100.0; p += 1.0)
            card.NotifySessionProgress(SessionWithId("session-a"), p);

        Assert.Equal(2, card.PunchedCount);
    }

    // ---- several intakes in flight (the patron case) ----

    [Fact]
    public void SeveralIntakesThenSeveralSessions_EachEarnsItsOwnHole()
    {
        // A patron can run back-to-back intakes and work through the sessions afterwards. A
        // single pending slot would drop every draft but the newest.
        var card = NewCard();
        card.NotifyIntakeCompleted("a");
        card.NotifyIntakeCompleted("b");
        card.NotifyIntakeCompleted("c");

        card.NotifySessionCompleted(SessionWithId("b"));
        card.NotifySessionCompleted(SessionWithId("a"));
        card.NotifySessionCompleted(SessionWithId("c"));

        Assert.Equal(4, card.PunchedCount);   // 1 free + 3 earned
        Assert.False(card.HasPendingStamp);
    }

    // ---- filling the card ----

    [Fact]
    public void FillingEveryHole_CompletesOnceAndAwaitsThePrize()
    {
        var card = NewCard();
        var completedEvents = 0;
        card.PunchCardCompleted += (_, _) => completedEvents++;

        for (var i = 0; i < IntakePunchCardService.TotalHoles - 1; i++)
        {
            var id = $"session-{i}";
            card.NotifyIntakeCompleted(id);
            card.NotifySessionCompleted(SessionWithId(id));
        }

        Assert.Equal(IntakePunchCardService.TotalHoles, card.PunchedCount);
        Assert.True(card.IsComplete);
        Assert.Equal(1, completedEvents);
        Assert.True(card.PrizeAwaitingClaim);
        Assert.Equal(0, card.HolesRemaining);
    }

    [Fact]
    public void AFullCard_AcceptsNoFurtherWork()
    {
        var card = NewCard();
        for (var i = 0; i < IntakePunchCardService.TotalHoles - 1; i++)
        {
            var id = $"session-{i}";
            card.NotifyIntakeCompleted(id);
            card.NotifySessionCompleted(SessionWithId(id));
        }

        card.NotifyIntakeCompleted("one-too-many");
        card.NotifySessionCompleted(SessionWithId("one-too-many"));

        Assert.Equal(IntakePunchCardService.TotalHoles, card.PunchedCount);
        Assert.False(card.HasPendingStamp);
    }

    [Fact]
    public void ClaimingThePrize_HappensExactlyOnce()
    {
        var card = NewCard();
        for (var i = 0; i < IntakePunchCardService.TotalHoles - 1; i++)
        {
            var id = $"session-{i}";
            card.NotifyIntakeCompleted(id);
            card.NotifySessionCompleted(SessionWithId(id));
        }

        Assert.True(card.PrizeAwaitingClaim);
        card.MarkPrizeClaimed();
        var firstClaim = card.State.PrizeClaimedUtc;

        Assert.False(card.PrizeAwaitingClaim);
        card.MarkPrizeClaimed();
        Assert.Equal(firstClaim, card.State.PrizeClaimedUtc);
    }

    [Fact]
    public void PrizeCannotBeClaimedBeforeTheCardIsFull()
    {
        var card = NewCard();
        card.MarkPrizeClaimed();
        Assert.Null(card.State.PrizeClaimedUtc);
    }

    // ---- first-run detection (drives the gentler first launch) ----

    [Fact]
    public void HasEverCompletedIntake_IsFalseOnAFreshCard()
        => Assert.False(NewCard().HasEverCompletedIntake);

    [Fact]
    public void HasEverCompletedIntake_IsTrueOnceAnIntakeIsPending()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        Assert.True(card.HasEverCompletedIntake);
    }

    // ---- persistence ----

    [Fact]
    public void ProgressSurvivesAReload()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        card.NotifySessionCompleted(SessionWithId("session-a"));
        card.Save();
        card.Dispose();

        var reloaded = NewCard();
        Assert.Equal(2, reloaded.PunchedCount);
        // The free hole is only granted when no card exists - reloading must not re-grant it.
        Assert.False(reloaded.HasPendingStamp);
    }

    [Fact]
    public void APendingStampSurvivesAReload()
    {
        var card = NewCard();
        card.NotifyIntakeCompleted("session-a");
        card.Save();
        card.Dispose();

        var reloaded = NewCard();
        Assert.True(reloaded.HasPendingStamp);
        reloaded.NotifySessionCompleted(SessionWithId("session-a"));
        Assert.Equal(2, reloaded.PunchedCount);
    }

    [Fact]
    public void ACorruptStateFile_FallsBackToAFreshCardRatherThanThrowing()
    {
        File.WriteAllText(_path, "{ this is not json at all ");
        var card = NewCard();
        Assert.Equal(1, card.PunchedCount);
    }

    [Fact]
    public void AHalfWrittenTmpFile_IsRecovered()
    {
        // Crash between the .tmp write and the rename: the main file never appeared.
        var good = NewCard();
        good.NotifyIntakeCompleted("session-a");
        good.NotifySessionCompleted(SessionWithId("session-a"));
        good.Save();
        good.Dispose();

        File.Move(_path, _path + ".tmp");

        var recovered = NewCard();
        Assert.Equal(2, recovered.PunchedCount);
        Assert.True(File.Exists(_path));   // .tmp promoted back to the real file
    }

    [Fact]
    public void AnOverPunchedFile_IsClampedAndTreatedAsComplete()
    {
        File.WriteAllText(_path,
            "{\"SchemaVersion\":1,\"CardStartedUtc\":\"2026-01-01T00:00:00Z\",\"PunchedCount\":99}");

        var card = NewCard();
        Assert.Equal(IntakePunchCardService.TotalHoles, card.PunchedCount);
        Assert.True(card.IsComplete);
        // A full card with no completion stamp would leave PrizeAwaitingClaim stuck forever.
        Assert.NotNull(card.State.CompletedUtc);
    }
}
