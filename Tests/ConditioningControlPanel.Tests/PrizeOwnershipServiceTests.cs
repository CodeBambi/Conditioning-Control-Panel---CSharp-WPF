using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Back Room prize ownership seam: the DEBUG override parser, the snapshot rules the server
/// lane will lean on (another account's snapshot, a stale revision, an unchanged repeat) and the
/// grant id spelling. The account source and the event marshal are injected, so nothing here
/// reads the process environment, needs DEBUG, or depends on a WPF dispatcher.
/// </summary>
public class PrizeOwnershipServiceTests
{
    private const string Me = "uid-me";
    private string? _account = Me;
    private readonly List<OwnershipChangedEventArgs> _events = new();

    private OwnershipService New(string? overrideSpec = null)
    {
        var svc = new OwnershipService(() => _account, overrideSpec, invoke => invoke());
        svc.OwnershipChanged += (_, e) => _events.Add(e);
        return svc;
    }

    // ---- override parsing ----

    [Fact]
    public void Override_ExactIds_AreTrimmedAndMatchOnlyThemselves()
    {
        var p = OwnershipService.ParseOverride("  fx.jackpot_remix , rt.original.03 ");
        Assert.Equal(new[] { "fx.jackpot_remix", "rt.original.03" }, p);
        Assert.True(OwnershipService.MatchesOverride(p, PrizeGrants.JackpotRemix));
        Assert.True(OwnershipService.MatchesOverride(p, PrizeGrants.RacingTrack(3)));
        Assert.False(OwnershipService.MatchesOverride(p, PrizeGrants.RacingTrack(4)));
        Assert.False(OwnershipService.MatchesOverride(p, "fx.jackpot_remix.extra"));
    }

    [Fact]
    public void Override_PrefixWildcard_MatchesTheFamilyOnly()
    {
        var p = OwnershipService.ParseOverride("fx.*");
        var matched = PrizeGrants.All.Where(id => OwnershipService.MatchesOverride(p, id)).ToList();
        Assert.Equal(5, matched.Count);
        Assert.All(matched, id => Assert.StartsWith("fx.", id));
        Assert.False(OwnershipService.MatchesOverride(p, "fxx.bubble.rain"));

        var rt = OwnershipService.ParseOverride("rt.original.*");
        Assert.Equal(11, PrizeGrants.All.Count(id => OwnershipService.MatchesOverride(rt, id)));
    }

    [Fact]
    public void Override_Star_MatchesEverything()
    {
        var p = OwnershipService.ParseOverride("*");
        Assert.All(PrizeGrants.All, id => Assert.True(OwnershipService.MatchesOverride(p, id)));
        Assert.True(OwnershipService.MatchesOverride(p, PrizeGrants.DiscordHighRoller));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",, ,")]
    [InlineData(".*")]
    [InlineData("fx*")]
    [InlineData("**")]
    [InlineData("*.fx")]
    [InlineData("fx..*")]
    [InlineData("fx.*.rain")]
    [InlineData("fx. jackpot")]
    public void Override_JunkEntries_AreDropped(string? spec)
        => Assert.Empty(OwnershipService.ParseOverride(spec));

    [Fact]
    public void Override_JunkNextToGoodEntries_KeepsTheGoodOnes_Deduplicated()
        => Assert.Equal(new[] { "fx.bubble.rain", "rt.*" },
            OwnershipService.ParseOverride("fx*, fx.bubble.rain,,rt.*, fx.bubble.rain"));

    [Fact]
    public void Override_GrantsWithoutAnySnapshot_ButIsNotListedInGrants()
    {
        var svc = New("fx.*");
        Assert.True(svc.IsGranted(PrizeGrants.BubbleSpiralIn));
        Assert.False(svc.IsGranted(PrizeGrants.RacingTrack(0)));
        Assert.Empty(svc.Grants);

        _account = null; // the override is a desk-test switch, not an account grant
        Assert.True(svc.IsGranted(PrizeGrants.BubbleSpiralIn));
    }

    // ---- snapshot rules ----

    [Fact]
    public void Snapshot_ForTheSignedInAccount_GrantsAndRaises()
    {
        var svc = New();
        svc.ApplySnapshot(Me, 3, new[] { PrizeGrants.JackpotRemix, " rt.original.10 ", "", null! });

        Assert.True(svc.IsGranted(PrizeGrants.JackpotRemix));
        Assert.True(svc.IsGranted(PrizeGrants.RacingTrack(10)));
        Assert.Equal(2, svc.Grants.Count);
        Assert.Equal(3, svc.Revision);
        var e = Assert.Single(_events);
        Assert.Equal(new[] { PrizeGrants.JackpotRemix, "rt.original.10" }, e.Added.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(e.Removed);
        Assert.Equal(3, e.Revision);
    }

    [Fact]
    public void Snapshot_ForAnotherAccount_IsIgnored()
    {
        var svc = New();
        svc.ApplySnapshot("uid-someone-else", 9, new[] { PrizeGrants.JackpotRemix });
        Assert.False(svc.IsGranted(PrizeGrants.JackpotRemix));

        _account = null; // signed out: nothing is anyone's
        svc.ApplySnapshot(Me, 9, new[] { PrizeGrants.JackpotRemix });
        Assert.False(svc.IsGranted(PrizeGrants.JackpotRemix));

        Assert.Empty(_events);
        Assert.Equal(0, svc.Revision);
    }

    [Fact]
    public void Snapshot_WithALowerRevision_IsIgnored()
    {
        var svc = New();
        svc.ApplySnapshot(Me, 5, new[] { PrizeGrants.FlashPendulum });
        svc.ApplySnapshot(Me, 4, new[] { PrizeGrants.BubbleRain });

        Assert.True(svc.IsGranted(PrizeGrants.FlashPendulum));
        Assert.False(svc.IsGranted(PrizeGrants.BubbleRain));
        Assert.Equal(5, svc.Revision);
        Assert.Single(_events);
    }

    [Fact]
    public void Snapshot_EqualRevisionIdenticalSet_IsANoOp()
    {
        var svc = New();
        svc.ApplySnapshot(Me, 7, new[] { PrizeGrants.FlashDriftBounce, PrizeGrants.BubbleRain });
        svc.ApplySnapshot(Me, 7, new[] { PrizeGrants.BubbleRain, PrizeGrants.FlashDriftBounce });

        Assert.Single(_events);
        Assert.Equal(7, svc.Revision);
    }

    [Fact]
    public void Snapshot_HigherRevision_ReportsAddedAndRemoved()
    {
        var svc = New();
        svc.ApplySnapshot(Me, 1, new[] { PrizeGrants.FlashDriftBounce, PrizeGrants.BubbleRain });
        svc.ApplySnapshot(Me, 2, new[] { PrizeGrants.BubbleRain, PrizeGrants.BubbleSpiralIn });

        Assert.Equal(2, _events.Count);
        Assert.Equal(new[] { PrizeGrants.BubbleSpiralIn }, _events[1].Added);
        Assert.Equal(new[] { PrizeGrants.FlashDriftBounce }, _events[1].Removed);
        Assert.False(svc.IsGranted(PrizeGrants.FlashDriftBounce));
    }

    [Fact]
    public void AccountSwitch_WithoutClear_OldGrantsStopCounting_AndTheNewAccountIsNotStale()
    {
        var svc = New();
        svc.ApplySnapshot(Me, 50, new[] { PrizeGrants.JackpotRemix });

        _account = "uid-other";
        Assert.False(svc.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Empty(svc.Grants);

        svc.ApplySnapshot("uid-other", 2, new[] { PrizeGrants.BubbleRain });
        Assert.True(svc.IsGranted(PrizeGrants.BubbleRain));
        Assert.False(svc.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Equal(2, svc.Revision);
    }

    [Fact]
    public void Clear_RaisesWithEverythingRemoved_AndResetsTheRevision()
    {
        var svc = New();
        svc.ApplySnapshot(Me, 12, new[] { PrizeGrants.JackpotRemix, PrizeGrants.RacingTrack(1) });
        svc.Clear();

        Assert.Equal(2, _events.Count);
        var e = _events[1];
        Assert.Empty(e.Added);
        Assert.Equal(2, e.Removed.Count);
        Assert.Equal(0, e.Revision);
        Assert.False(svc.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Equal(0, svc.Revision);

        // A fresh snapshot at a low revision is welcome again after a clear.
        svc.ApplySnapshot(Me, 1, new[] { PrizeGrants.RacingTrack(1) });
        Assert.True(svc.IsGranted(PrizeGrants.RacingTrack(1)));
    }

    [Fact]
    public void Clear_WithNothingHeld_RaisesNothing()
    {
        var svc = New();
        svc.Clear();
        Assert.Empty(_events);
    }

    // ---- grant ids ----

    [Theory]
    [InlineData(0, "rt.original.00")]
    [InlineData(7, "rt.original.07")]
    [InlineData(10, "rt.original.10")]
    public void RacingTrack_IsTwoDigits(int track, string expected)
        => Assert.Equal(expected, PrizeGrants.RacingTrack(track));

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(100)]
    public void RacingTrack_OutOfRange_Throws(int track)
        => Assert.Throws<ArgumentOutOfRangeException>(() => PrizeGrants.RacingTrack(track));

    [Fact]
    public void All_IsExactlyTheSixteenClientGrants()
    {
        Assert.Equal(new[]
        {
            "fx.jackpot_remix", "fx.flash.drift_bounce", "fx.flash.pendulum", "fx.bubble.rain", "fx.bubble.spiral_in",
            "rt.original.00", "rt.original.01", "rt.original.02", "rt.original.03", "rt.original.04", "rt.original.05",
            "rt.original.06", "rt.original.07", "rt.original.08", "rt.original.09", "rt.original.10",
        }, PrizeGrants.All);
        Assert.DoesNotContain(PrizeGrants.DiscordHighRoller, PrizeGrants.All);
        Assert.Equal("discord.high_roller", PrizeGrants.DiscordHighRoller);
    }
}
