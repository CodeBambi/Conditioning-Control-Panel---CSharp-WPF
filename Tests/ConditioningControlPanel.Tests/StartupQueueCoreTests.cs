using System;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The rules behind the startup ladder and the quiet window.
///
/// <para>These exist because the behaviour they describe used to be spread across eight
/// hand-rolled 500 ms polls, each with its own idea of what "settled" meant, and the only way to
/// find out what a fresh install actually did was to install the app on a fresh machine. The
/// ordering, the dedupe, the quiet-window expiry and the two refusals below are now pure functions
/// with no WPF, no App and no clock of their own.</para>
/// </summary>
public class StartupQueueCoreTests
{
    private static readonly DateTime T0 = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static QuietInputs Calm(DateTime? firstLaunchUntil = null, DateTime? now = null) => new()
    {
        ModalUp = false,
        TutorialActive = false,
        SessionRunning = false,
        FirstLaunchUntilUtc = firstLaunchUntil,
        NowUtc = now ?? T0,
    };

    // ---------------------------------------------------------------- ordering

    [Fact]
    public void LowerPriorityNumberRunsFirst()
    {
        var q = new StartupQueueCore();
        q.Enqueue("mod-picker", 50);
        q.Enqueue("failed-update-report", 10);
        q.Enqueue("whats-new", 30);

        Assert.Equal("failed-update-report", q.Dequeue()!.Value.Key);
        Assert.Equal("whats-new", q.Dequeue()!.Value.Key);
        Assert.Equal("mod-picker", q.Dequeue()!.Value.Key);
        Assert.Null(q.Dequeue());
    }

    [Fact]
    public void TheRealLadderComesOutInThePlansOrder()
    {
        // The priority table from docs/first-run/REDESIGN-PLAN.md, enqueued in the order the
        // constructor and App.xaml.cs actually reach them, which is not the order they run.
        var q = new StartupQueueCore();
        q.Enqueue("update-available", 80);
        q.Enqueue("first-run-wizard", 20);
        q.Enqueue("deeper-enhance-nudge", 70);
        q.Enqueue("season-recap", 40);
        q.Enqueue("failed-update-report", 10);
        q.Enqueue("mod-picker", 50);
        q.Enqueue("whats-new", 30);

        var order = new string[7];
        for (int i = 0; i < order.Length; i++) order[i] = q.Dequeue()!.Value.Key;

        Assert.Equal(new[]
        {
            "failed-update-report",
            "first-run-wizard",
            "whats-new",
            "season-recap",
            "mod-picker",
            "deeper-enhance-nudge",
            "update-available",
        }, order);
    }

    [Fact]
    public void EqualPrioritiesRunFirstInFirstOut()
    {
        // Lane E's welcome-back sheet and the upgrader's What's New share priority 30, and only
        // one population ever queues both - but "whichever the dictionary felt like" is exactly
        // the non-determinism this class exists to delete.
        var q = new StartupQueueCore();
        q.Enqueue("welcome-back", 30);
        q.Enqueue("whats-new", 30);
        q.Enqueue("season-recap", 30);

        Assert.Equal("welcome-back", q.Dequeue()!.Value.Key);
        Assert.Equal("whats-new", q.Dequeue()!.Value.Key);
        Assert.Equal("season-recap", q.Dequeue()!.Value.Key);
    }

    [Fact]
    public void ArrivingLateWithALowerNumberStillCutsIn()
    {
        var q = new StartupQueueCore();
        q.Enqueue("mod-picker", 50);
        q.Enqueue("season-recap", 40);
        q.Enqueue("first-run-wizard", 20);

        Assert.Equal("first-run-wizard", q.Next()!.Value.Key);
    }

    [Fact]
    public void NextPeeksAndDoesNotConsume()
    {
        var q = new StartupQueueCore();
        q.Enqueue("whats-new", 30);

        Assert.Equal("whats-new", q.Next()!.Value.Key);
        Assert.Equal(1, q.Count);
        Assert.Equal("whats-new", q.Next()!.Value.Key);
    }

    [Fact]
    public void AnEmptyQueueHasNothingToOffer()
    {
        var q = new StartupQueueCore();
        Assert.True(q.IsEmpty);
        Assert.Null(q.Next());
        Assert.Null(q.Dequeue());
    }

    // ---------------------------------------------------------------- dedupe

    [Fact]
    public void TheSameKeyOnlyGetsOneTurn()
    {
        var q = new StartupQueueCore();
        Assert.True(q.Enqueue("whats-new", 30));
        Assert.False(q.Enqueue("whats-new", 30));
        Assert.False(q.Enqueue("whats-new", 10));   // nor by asking more politely

        Assert.Equal(1, q.Count);
        Assert.Equal(30, q.Dequeue()!.Value.Priority);
    }

    [Fact]
    public void DedupeIgnoresCase()
    {
        var q = new StartupQueueCore();
        Assert.True(q.Enqueue("Season-Recap", 40));
        Assert.False(q.Enqueue("season-recap", 40));
        Assert.True(q.Contains("SEASON-RECAP"));
    }

    [Fact]
    public void ABlankKeyIsRefused()
    {
        var q = new StartupQueueCore();
        Assert.False(q.Enqueue("", 10));
        Assert.False(q.Enqueue("   ", 10));
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void AKeyIsFreeAgainOnceItHasRun()
    {
        // The premium celebration can be re-offered by a later tier event in the same launch.
        var q = new StartupQueueCore();
        q.Enqueue("celebration", 60);
        q.Dequeue();

        Assert.True(q.Enqueue("celebration", 60));
    }

    [Fact]
    public void RemovingAQueuedSurfaceDropsIt()
    {
        var q = new StartupQueueCore();
        q.Enqueue("mod-picker", 50);
        q.Enqueue("whats-new", 30);

        Assert.True(q.Remove("whats-new"));
        Assert.False(q.Remove("whats-new"));
        Assert.Equal("mod-picker", q.Dequeue()!.Value.Key);
    }

    // ---------------------------------------------------------------- quiet window

    [Fact]
    public void NothingHappeningIsNotQuiet()
        => Assert.False(StartupQueueCore.IsQuiet(Calm()));

    [Fact]
    public void AModalMakesItQuiet()
        => Assert.True(StartupQueueCore.IsQuiet(Calm() with { ModalUp = true }));

    [Fact]
    public void ATourMakesItQuiet()
        => Assert.True(StartupQueueCore.IsQuiet(Calm() with { TutorialActive = true }));

    [Fact]
    public void ASessionMakesItQuiet()
        => Assert.True(StartupQueueCore.IsQuiet(Calm() with { SessionRunning = true }));

    [Fact]
    public void TheFirstLaunchWindowIsQuietUntilItRunsOut()
    {
        var until = T0.AddMinutes(10);

        Assert.True(StartupQueueCore.IsQuiet(Calm(until, T0)));
        Assert.True(StartupQueueCore.IsQuiet(Calm(until, T0.AddMinutes(9.9))));

        // Expiry is exclusive at the boundary: at the tick it ends, it has ended.
        Assert.False(StartupQueueCore.IsQuiet(Calm(until, until)));
        Assert.False(StartupQueueCore.IsQuiet(Calm(until, T0.AddMinutes(10.1))));
    }

    [Fact]
    public void NoFirstLaunchWindowIsNotAnInfiniteOne()
    {
        // Every upgrader takes this path: BeginFirstLaunchQuiet is only ever called by the far
        // side of the wizard, so a null here must read as "not quiet", never as "forever".
        Assert.False(StartupQueueCore.IsQuiet(Calm(firstLaunchUntil: null, now: T0)));
    }

    [Fact]
    public void AnExpiredWindowStillYieldsToARunningSession()
    {
        var expired = T0.AddMinutes(-1);
        Assert.True(StartupQueueCore.IsQuiet(Calm(expired, T0) with { SessionRunning = true }));
    }

    // ---------------------------------------------------------------- inbox vs present

    [Fact]
    public void APassiveSurfaceOpensWhenNothingIsQuiet()
        => Assert.False(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Passive, Calm()));

    [Fact]
    public void APassiveSurfaceGoesToTheInboxWhileQuiet()
    {
        Assert.True(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Passive, Calm() with { ModalUp = true }));
        Assert.True(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Passive, Calm() with { TutorialActive = true }));
        Assert.True(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Passive, Calm() with { SessionRunning = true }));
        Assert.True(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Passive, Calm(T0.AddMinutes(10), T0)));
    }

    [Fact]
    public void AModalIsNeverInboxed()
    {
        // A modal that quietly became a row nobody clicked is a modal that never ran. The ladder
        // already has a "wait, then give it up to the next launch" rule for that case.
        Assert.False(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Modal, Calm() with { ModalUp = true }));
        Assert.False(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Modal, Calm(T0.AddMinutes(10), T0)));
    }

    [Fact]
    public void TheAnnouncementParksOnAFirstLaunchAndPopsOnEveryOtherOne()
    {
        // The owner's headline complaint, as one assertion pair: "The Spiral is open" must not be
        // the fourth thing a brand-new user sees, and must still arrive normally for everyone else.
        var freshInstall = Calm(firstLaunchUntil: T0.AddMinutes(10), now: T0.AddMinutes(2));
        var normalLaunch = Calm();

        Assert.True(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Passive, freshInstall));
        Assert.False(StartupQueueCore.ShouldInbox(StartupSurfaceKind.Passive, normalLaunch));
    }

    // ---------------------------------------------------------------- modal clearance

    [Fact]
    public void AModalStartsWhenTheScreenIsFree()
        => Assert.True(StartupQueueCore.CanStartModal(modalUp: false, updateDialogActive: false, tutorialActive: false, windowReady: true));

    [Fact]
    public void NoModalWhileTheUpdateDialogIsUp()
    {
        // #481: the update dialog is Topmost, and a modal nested under it owns input that nobody
        // can reach - neither window can be dismissed.
        Assert.False(StartupQueueCore.CanStartModal(modalUp: false, updateDialogActive: true, tutorialActive: false, windowReady: true));
    }

    [Fact]
    public void NoModalWhileTheTutorialIsRunning()
    {
        // The spotlight measures live controls; a modal on top of it is the "picker opened
        // through the upgrade tour" bug from the 0812 build review.
        Assert.False(StartupQueueCore.CanStartModal(modalUp: false, updateDialogActive: false, tutorialActive: true, windowReady: true));
    }

    [Fact]
    public void NoModalBeforeTheWindowIsLoaded()
        => Assert.False(StartupQueueCore.CanStartModal(modalUp: false, updateDialogActive: false, tutorialActive: false, windowReady: false));

    [Fact]
    public void NoModalOnTopOfAnotherModal()
        => Assert.False(StartupQueueCore.CanStartModal(modalUp: true, updateDialogActive: false, tutorialActive: false, windowReady: true));

    [Fact]
    public void OneRefusalIsEnough()
    {
        // All four at once is the fresh-install-mid-update case; it must not accidentally pass by
        // some combination cancelling out.
        Assert.False(StartupQueueCore.CanStartModal(true, true, true, false));
    }

    // ---------------------------------------------------------------- ordering snapshot

    [Fact]
    public void DrainReportsTheRunOrderWithoutConsumingIt()
    {
        var q = new StartupQueueCore();
        q.Enqueue("update-available", 80);
        q.Enqueue("whats-new", 30);
        q.Enqueue("season-recap", 30);

        var ordered = q.Drain();

        Assert.Equal(new[] { "whats-new", "season-recap", "update-available" },
            new[] { ordered[0].Key, ordered[1].Key, ordered[2].Key });
        Assert.Equal(3, q.Count);
    }

    [Fact]
    public void ClearForgetsEverythingIncludingTheDedupeKeys()
    {
        var q = new StartupQueueCore();
        q.Enqueue("whats-new", 30);
        q.Clear();

        Assert.True(q.IsEmpty);
        Assert.False(q.Contains("whats-new"));
        Assert.True(q.Enqueue("whats-new", 30));
    }
}
