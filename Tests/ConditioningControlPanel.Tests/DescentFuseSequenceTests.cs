using System;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE ZERO SHOW's clock, as arithmetic (CONTRACT-FUSE-0816 §2.3/§2.4).
///
/// <para>The show is the one part of the fuse that gets exactly one chance to be right: it plays at
/// a single instant, on a night the whole release is pointed at, and nobody is going to be watching
/// a debugger while it does. So the timing lives in pure tables — <see cref="DescentFuseTimeline"/>
/// and <see cref="DescentIgnitionTimeline"/> — with no WPF, no dispatcher and no
/// <c>DateTime.UtcNow</c> anywhere near them, and this file pins every beat.</para>
///
/// <para>What these tests cannot cover is what the picture LOOKS like. They cover when each beat
/// starts, that the catch-up is the same five beats compressed rather than a different animation,
/// and — the one that would be a silent regression — that reduced motion lands on a SNAPPED state
/// rather than on nothing at all.</para>
/// </summary>
public class DescentFuseSequenceTests
{
    // ------------------------------------------------------------ the live sequence

    /// <summary>
    /// The contract's five beats, in order, sampled comfortably inside each. Read top to bottom
    /// this is §2.3's sentence: freeze 1.5 → crack 1 → drain 1.7 → black 1 → bloom 2.5.
    /// </summary>
    [Theory]
    [InlineData(0.0, DescentFuseStage.Freeze)]
    [InlineData(1.4, DescentFuseStage.Freeze)]
    [InlineData(1.6, DescentFuseStage.Crack)]
    [InlineData(2.4, DescentFuseStage.Crack)]
    [InlineData(2.6, DescentFuseStage.Drain)]
    [InlineData(4.1, DescentFuseStage.Drain)]
    [InlineData(4.3, DescentFuseStage.Black)]
    [InlineData(5.1, DescentFuseStage.Black)]
    [InlineData(5.3, DescentFuseStage.Bloom)]
    [InlineData(7.6, DescentFuseStage.Bloom)]
    [InlineData(7.8, DescentFuseStage.Held)]
    [InlineData(600.0, DescentFuseStage.Held)]
    public void Live_WalksTheContractsBeats(double elapsed, DescentFuseStage expected)
    {
        var frame = DescentFuseTimeline.FrameAt(DescentShowKind.Live, elapsed, reducedMotion: false);
        Assert.Equal(expected, frame.Stage);
    }

    /// <summary>
    /// Progress is a real 0..1 inside every beat, because the drawing multiplies by it. A beat that
    /// reported 0 forever would render its first frame for its whole duration and nobody would see
    /// an exception.
    /// </summary>
    [Fact]
    public void Progress_RunsZeroToOneInsideEachBeat()
    {
        var start = DescentFuseTimeline.FrameAt(DescentShowKind.Live, 0.0, false);
        Assert.Equal(DescentFuseStage.Freeze, start.Stage);
        Assert.Equal(0.0, start.Progress, 3);

        var late = DescentFuseTimeline.FrameAt(DescentShowKind.Live, 1.4999, false);
        Assert.Equal(DescentFuseStage.Freeze, late.Stage);
        Assert.True(late.Progress > 0.99, $"expected the freeze to be nearly over, got {late.Progress}");

        var midCrack = DescentFuseTimeline.FrameAt(DescentShowKind.Live, 2.0, false);
        Assert.Equal(DescentFuseStage.Crack, midCrack.Stage);
        Assert.Equal(0.5, midCrack.Progress, 3);
    }

    /// <summary>
    /// A negative elapsed — a stopwatch read across a system clock adjustment — shows the first
    /// frame again rather than throwing in front of a fullscreen window.
    /// </summary>
    [Fact]
    public void NegativeElapsed_ShowsTheFirstFrame()
    {
        var frame = DescentFuseTimeline.FrameAt(DescentShowKind.Live, -12.0, false);
        Assert.Equal(DescentFuseStage.Freeze, frame.Stage);
        Assert.Equal(0.0, frame.Progress, 3);
    }

    // ------------------------------------------------------------ the catch-up

    /// <summary>
    /// §2.4's "condensed 6s crack" is SIX SECONDS, and it is the same five beats. A condensed
    /// version that dropped a beat would be a different animation wearing the same name.
    /// </summary>
    [Fact]
    public void CatchUp_IsSixSecondsOfTheSameFiveBeats()
    {
        Assert.Equal(6.0, DescentFuseTimeline.CatchUpSeconds, 3);

        var seen = new System.Collections.Generic.List<DescentFuseStage>();
        for (double t = 0; t < DescentFuseTimeline.CatchUpSeconds; t += 0.02)
        {
            var stage = DescentFuseTimeline.FrameAt(DescentShowKind.CatchUp, t, false).Stage;
            if (seen.Count == 0 || seen[^1] != stage) seen.Add(stage);
        }

        Assert.Equal(new[]
        {
            DescentFuseStage.Freeze,
            DescentFuseStage.Crack,
            DescentFuseStage.Drain,
            DescentFuseStage.Black,
            DescentFuseStage.Bloom,
        }, seen);

        // And it is finished (held) the moment its six seconds are up.
        Assert.Equal(DescentFuseStage.Held,
            DescentFuseTimeline.FrameAt(DescentShowKind.CatchUp, 6.01, false).Stage);
    }

    /// <summary>
    /// Every catch-up beat is the live beat times one scale factor. Pinning the ratio rather than
    /// the individual durations is what stops the two shows drifting apart when someone retimes
    /// the live one.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    [InlineData(6.5)]
    public void CatchUp_IsTheLiveShowScaled(double catchUpElapsed)
    {
        var scaled = DescentFuseTimeline.FrameAt(DescentShowKind.CatchUp, catchUpElapsed, false);
        var live = DescentFuseTimeline.FrameAt(
            DescentShowKind.Live, catchUpElapsed / DescentFuseTimeline.CatchUpScale, false);

        Assert.Equal(live.Stage, scaled.Stage);
        Assert.Equal(live.Progress, scaled.Progress, 3);
    }

    // ------------------------------------------------------------ reduced motion

    /// <summary>
    /// THE SNAPPED STATE (contract §0, the faucet PR #37 lesson). Reduced motion is one 1.5s
    /// crossfade that ENDS ON THE BLOOM and holds it — not a skipped show, and not a faster crack.
    /// </summary>
    [Theory]
    [InlineData(DescentShowKind.Live)]
    [InlineData(DescentShowKind.CatchUp)]
    public void ReducedMotion_IsOneCrossfadeIntoTheHeldBloom(DescentShowKind kind)
    {
        Assert.Equal(DescentFuseStage.Bloom,
            DescentFuseTimeline.FrameAt(kind, 0.0, reducedMotion: true).Stage);
        Assert.Equal(DescentFuseStage.Bloom,
            DescentFuseTimeline.FrameAt(kind, 1.4, reducedMotion: true).Stage);
        Assert.Equal(DescentFuseStage.Held,
            DescentFuseTimeline.FrameAt(kind, 1.6, reducedMotion: true).Stage);
        Assert.Equal(DescentFuseStage.Held,
            DescentFuseTimeline.FrameAt(kind, 90.0, reducedMotion: true).Stage);
    }

    /// <summary>
    /// A reduced-motion subject waits NO LONGER for the ceremony than anyone else. The handoff
    /// clock is <see cref="DescentFuseFrame.SinceBloom"/>, so it has to start at zero on the very
    /// first reduced frame — otherwise the forty-five second window would silently begin 7.7
    /// seconds late for exactly the people least able to sit through it.
    /// </summary>
    [Fact]
    public void ReducedMotion_StartsTheHandoffClockImmediately()
    {
        Assert.Equal(0.0, DescentFuseTimeline.FrameAt(DescentShowKind.Live, 0.0, true).SinceBloom, 3);
        Assert.Equal(4.0, DescentFuseTimeline.FrameAt(DescentShowKind.Live, 4.0, true).SinceBloom, 3);
        Assert.Equal(0.0, DescentFuseTimeline.BloomStartSeconds(DescentShowKind.Live, true), 3);
    }

    // ------------------------------------------------------------ the bloom clock

    /// <summary>
    /// <see cref="DescentFuseFrame.SinceBloom"/> is negative before the bloom and zero exactly at
    /// it. The handoff machine keys off that sign, and the keepsake flag keys off that instant.
    /// </summary>
    [Fact]
    public void SinceBloom_IsNegativeBeforeTheLightComesBack()
    {
        Assert.True(DescentFuseTimeline.FrameAt(DescentShowKind.Live, 0.0, false).SinceBloom < 0);
        Assert.True(DescentFuseTimeline.FrameAt(DescentShowKind.Live, 5.1, false).SinceBloom < 0);

        var bloomStart = DescentFuseTimeline.BloomStartSeconds(DescentShowKind.Live, false);
        Assert.Equal(5.2, bloomStart, 3);
        Assert.Equal(0.0, DescentFuseTimeline.FrameAt(DescentShowKind.Live, bloomStart, false).SinceBloom, 3);
        Assert.Equal(DescentFuseStage.Bloom,
            DescentFuseTimeline.FrameAt(DescentShowKind.Live, bloomStart, false).Stage);
    }

    /// <summary>The catch-up's bloom arrives proportionally earlier, from the same one factor.</summary>
    [Fact]
    public void CatchUp_BloomStartIsScaledToo()
    {
        var live = DescentFuseTimeline.BloomStartSeconds(DescentShowKind.Live, false);
        var catchUp = DescentFuseTimeline.BloomStartSeconds(DescentShowKind.CatchUp, false);
        Assert.Equal(live * DescentFuseTimeline.CatchUpScale, catchUp, 4);
        Assert.True(catchUp < live);
    }

    // ------------------------------------------------------------ the ignition

    /// <summary>
    /// The Year One spiral draws from the centre OUTWARD, once, and only after the lead beat. A
    /// draw fraction that started above zero would mean the spiral was already partly lit when the
    /// window opened, which is the one thing §2.4's "draws outward from center" rules out.
    /// </summary>
    [Fact]
    public void Ignition_DrawsOutwardAfterTheLead()
    {
        Assert.Equal(0.0, DescentIgnitionTimeline.DrawFraction(0.0, false), 4);
        Assert.Equal(0.0, DescentIgnitionTimeline.DrawFraction(DescentIgnitionTimeline.LeadSeconds, false), 4);
        Assert.Equal(0.5, DescentIgnitionTimeline.DrawFraction(
            DescentIgnitionTimeline.LeadSeconds + DescentIgnitionTimeline.DrawSeconds / 2, false), 4);
        Assert.Equal(1.0, DescentIgnitionTimeline.DrawFraction(
            DescentIgnitionTimeline.LeadSeconds + DescentIgnitionTimeline.DrawSeconds, false), 4);
        Assert.Equal(1.0, DescentIgnitionTimeline.DrawFraction(99.0, false), 4);
    }

    /// <summary>Reduced motion SNAPS to the finished spiral — lit, notches and all, on frame one.</summary>
    [Fact]
    public void Ignition_ReducedMotionSnapsToTheFinishedSpiral()
    {
        Assert.Equal(1.0, DescentIgnitionTimeline.DrawFraction(0.0, true), 4);
        foreach (var at in DescentIgnitionTimeline.NotchAt)
            Assert.Equal(1.0, DescentIgnitionTimeline.NotchGlow(0.0, at, true), 4);

        // "snap to the finished lit spiral, hold 3s, fade" — the line is up from the start,
        // because there is no draw to wait for.
        Assert.Equal(1.0, DescentIgnitionTimeline.LineOpacity(1.0, true), 4);
        Assert.Equal(DescentIgnitionTimeline.ReducedHoldSeconds,
            DescentIgnitionTimeline.FadeStartSeconds(true), 4);
    }

    /// <summary>
    /// A station pops only once the arm has PASSED it, and finishes popping shortly after. A notch
    /// lighting before the light reaches it would give the whole reveal away one beat early.
    /// </summary>
    [Fact]
    public void Ignition_NotchesPopOnlyOnceTheArmPassesThem()
    {
        const double at = 0.52;
        var reachedAt = DescentIgnitionTimeline.LeadSeconds + at * DescentIgnitionTimeline.DrawSeconds;

        Assert.Equal(0.0, DescentIgnitionTimeline.NotchGlow(reachedAt - 0.2, at, false), 4);
        Assert.Equal(0.0, DescentIgnitionTimeline.NotchGlow(reachedAt, at, false), 4);
        Assert.True(DescentIgnitionTimeline.NotchGlow(reachedAt + 0.15, at, false) > 0);
        Assert.Equal(1.0, DescentIgnitionTimeline.NotchGlow(
            reachedAt + DescentIgnitionTimeline.NotchPopSeconds + 0.05, at, false), 4);
    }

    /// <summary>Every station is on the arm and none of them is the very last frame.</summary>
    [Fact]
    public void Ignition_StationsSitOnTheArm()
    {
        Assert.NotEmpty(DescentIgnitionTimeline.NotchAt);
        foreach (var at in DescentIgnitionTimeline.NotchAt)
        {
            Assert.True(at > 0, $"a station at {at} would sit on the centre point");
            Assert.True(at < 1.0, $"a station at {at} would pop on the final frame");
        }
    }

    /// <summary>
    /// The line arrives with the hold and is gone before the fade finishes, both paths. It is the
    /// last thing anybody reads that night, so "on screen at all" is worth a test.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ignition_LineIsUpThroughTheHold(bool reduced)
    {
        var fadeStart = DescentIgnitionTimeline.FadeStartSeconds(reduced);

        Assert.Equal(0.0, DescentIgnitionTimeline.LineOpacity(fadeStart, reduced), 4);
        Assert.Equal(1.0, DescentIgnitionTimeline.LineOpacity(fadeStart - 0.1, reduced), 4);
        if (!reduced) Assert.Equal(0.0, DescentIgnitionTimeline.LineOpacity(0.2, reduced), 4);
    }

    /// <summary>
    /// The window is fully opaque until the fade, and fully gone by the end of it — so a show that
    /// finished would never be left on screen at 4% opacity swallowing clicks.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ignition_FadesFullyOutByItsEnd(bool reduced)
    {
        var total = DescentIgnitionTimeline.TotalSeconds(reduced);
        Assert.Equal(1.0, DescentIgnitionTimeline.ShowOpacity(0.0, reduced), 4);
        Assert.Equal(1.0, DescentIgnitionTimeline.ShowOpacity(
            DescentIgnitionTimeline.FadeStartSeconds(reduced), reduced), 4);
        Assert.True(DescentIgnitionTimeline.ShowOpacity(total - 0.1, reduced) < 0.2);
        Assert.Equal(0.0, DescentIgnitionTimeline.ShowOpacity(total, reduced), 4);
    }

    /// <summary>§2.4 says "~6s". Both paths land in the neighbourhood, and neither runs away.</summary>
    [Fact]
    public void Ignition_IsAboutSixSeconds()
    {
        Assert.Equal(6.4, DescentIgnitionTimeline.TotalSeconds(false), 3);
        Assert.Equal(4.0, DescentIgnitionTimeline.TotalSeconds(true), 3);
    }

    // ------------------------------------------------------------ the copy

    /// <summary>
    /// §0.6, on the surface where it matters most. Not one word the show says may smell like an
    /// offer — and this is the release's most-watched screen, so the check is worth having in a
    /// file that runs on every commit rather than in a reviewer's memory.
    /// </summary>
    [Fact]
    public void ShowCopy_CarriesNoOffer()
    {
        foreach (var line in new[] { DescentFuseCopy.ShowAwaits, DescentFuseCopy.IgnitionLine })
        {
            Assert.False(string.IsNullOrWhiteSpace(line));
            foreach (var banned in new[]
                     {
                         "tier", "patreon", "upgrade", "subscribe", "unlock", "$", "price",
                         "premium", "buy", "pack", "sale", "discount", "trial",
                     })
            {
                Assert.DoesNotContain(banned, line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
