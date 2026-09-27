using System;
using System.Linq;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The corner notices: folding repeats, the three-high stack, the timers, the corner.</summary>
public class FriendNoticeRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static FriendNotice N(NoticeKind kind, string friend, double atSeconds = 0, object? payload = null)
        => new(kind, friend, "name " + friend, T0.AddSeconds(atSeconds), FriendNoticeRules.LifetimeMs(kind), payload);

    [Fact]
    public void ARepeatFromTheSameFriendAndKindFoldsWithACount()
    {
        var s = new FriendNoticeStack();
        s.Add(N(NoticeKind.Poke, "a", 0, "first"));
        s.Add(N(NoticeKind.Poke, "a", 10, "second"));
        var (up, folded) = s.Add(N(NoticeKind.Poke, "a", 20, "third"));

        Assert.True(folded);
        Assert.Single(s.All);
        Assert.Equal(3, up.Count);
        Assert.Equal("third", up.Payload);
        Assert.Equal(" x3", FriendNoticeRules.CountSuffix(up.Count));
    }

    [Fact]
    public void ADifferentKindOrFriendOrALateRepeatDoesNotFold()
    {
        var s = new FriendNoticeStack();
        s.Add(N(NoticeKind.Poke, "a", 0));
        Assert.False(s.Add(N(NoticeKind.Invite, "a", 1)).Folded);
        Assert.False(s.Add(N(NoticeKind.Poke, "b", 2)).Folded);
        Assert.False(s.Add(N(NoticeKind.Poke, "a", 0 + FriendNoticeRules.CoalesceSeconds + 1)).Folded);
        Assert.Equal(4, s.All.Count);
    }

    [Fact]
    public void AFoldRefillsTheTimerAndMovesToTheTop()
    {
        var s = new FriendNoticeStack();
        var a = s.Add(N(NoticeKind.Poke, "a")).Notice;
        s.Add(N(NoticeKind.Poke, "b"));
        s.Tick(4000);
        Assert.True(a.LeftMs < a.LifetimeMs);

        s.Add(N(NoticeKind.Poke, "a", 5));
        Assert.Same(a, s.All[0]);
        Assert.Equal(a.LifetimeMs, a.LeftMs);
        Assert.Equal(1, a.Fraction);
    }

    [Fact]
    public void NewestIsOnTopAndOnlyThreeAreDrawn()
    {
        var s = new FriendNoticeStack();
        foreach (var id in new[] { "a", "b", "c", "d", "e" }) s.Add(N(NoticeKind.Poke, id));

        Assert.Equal(new[] { "e", "d", "c" }, s.Visible.Select(n => n.FriendId));
        Assert.Equal(2, s.Hidden);
    }

    [Fact]
    public void TheListIsCappedAndTheOldestGoes()
    {
        var s = new FriendNoticeStack();
        for (int i = 0; i < FriendNoticeRules.Cap + 3; i++) s.Add(N(NoticeKind.Poke, "f" + i));
        Assert.Equal(FriendNoticeRules.Cap, s.All.Count);
        Assert.DoesNotContain(s.All, n => n.FriendId == "f0");
    }

    [Fact]
    public void TimersRunOutAndHoverStandsThemStill()
    {
        var s = new FriendNoticeStack();
        var poke = s.Add(N(NoticeKind.Poke, "a")).Notice;

        s.Paused = true;
        Assert.Empty(s.Tick(60_000));
        Assert.Equal(1, poke.Fraction);

        s.Paused = false;
        s.Tick(3000);
        Assert.Equal(0.5, poke.Fraction, 3);
        var gone = s.Tick(3001);
        Assert.Same(poke, Assert.Single(gone));
        Assert.Empty(s.All);
    }

    [Fact]
    public void HiddenNoticesWaitWithAFullBar()
    {
        var s = new FriendNoticeStack();
        var oldest = s.Add(N(NoticeKind.Invite, "a")).Notice;
        foreach (var id in new[] { "b", "c", "d" }) s.Add(N(NoticeKind.Poke, id));

        var gone = s.Tick(6001);
        Assert.Equal(3, gone.Count);
        Assert.Equal(1, oldest.Fraction);
        Assert.Same(oldest, Assert.Single(s.Visible));
    }

    [Fact]
    public void AnswerableNoticesStayUpLongerThanAPoke()
    {
        Assert.True(FriendNoticeRules.LifetimeMs(NoticeKind.Invite) > FriendNoticeRules.LifetimeMs(NoticeKind.Poke));
        Assert.True(FriendNoticeRules.LifetimeMs(NoticeKind.Request) > FriendNoticeRules.LifetimeMs(NoticeKind.Poke));
        Assert.Equal(6000, FriendNoticeRules.LifetimeMs(NoticeKind.Poke));
    }

    [Fact]
    public void RemoveTakesOneDown()
    {
        var s = new FriendNoticeStack();
        var a = s.Add(N(NoticeKind.Poke, "a")).Notice;
        s.Add(N(NoticeKind.Poke, "b"));
        Assert.True(s.Remove(a));
        Assert.False(s.Remove(a));
        Assert.Single(s.All);
    }

    [Fact]
    public void TheStackSitsInTheBottomRightOfTheWorkArea()
    {
        // A 1920x1040 work area (taskbar under it), a 316x200 stack.
        var (left, top) = FriendNoticeRules.Place(0, 0, 1920, 1040, 316, 200);
        Assert.Equal(1920 - 316 - FriendNoticeRules.EdgeMargin, left);
        Assert.Equal(1040 - 200 - FriendNoticeRules.EdgeMargin, top);

        // A second monitor to the right, and a stack too big for it stays inside.
        var (l2, t2) = FriendNoticeRules.Place(1920, 0, 200, 150, 316, 200);
        Assert.Equal(1920, l2);
        Assert.Equal(0, t2);
    }

    [Fact]
    public void RelativeTimeReadsNowSecondsAndMinutes()
    {
        Assert.Equal(("friends_notice_now", 0), FriendNoticeRules.Ago(T0, T0.AddSeconds(2)));
        Assert.Equal(("friends_notice_seconds", 42), FriendNoticeRules.Ago(T0, T0.AddSeconds(42.7)));
        Assert.Equal(("friends_notice_minutes", 3), FriendNoticeRules.Ago(T0, T0.AddMinutes(3.5)));
        Assert.Equal("", FriendNoticeRules.CountSuffix(1));
    }
}
