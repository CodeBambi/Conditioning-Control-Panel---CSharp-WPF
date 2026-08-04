using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #683 (part 1) — animated GIFs/webps cut to ~4s and looped early. The decoder subsampled with
/// integer division (<c>step = frameCount / maxKeep</c>), so any clip with
/// maxKeep &lt;= frameCount &lt; 2*maxKeep got step==1: the loop's <c>frames.Count &lt; maxKeep</c>
/// guard stopped it at frame maxKeep-1 and the tail was thrown away. FlashService decodes with
/// maxFrames:60, so a 100-frame 66ms GIF kept frames 0..59 = 3.96s of a 6.6s loop — the exact
/// reported symptom. Wider clips truncated too (170 frames → floor step 2 → first 120 only).
///
/// Ceiling division fixes it: ceil(DecodeCount / Step) &lt;= MaxKeep, so the guard can never trim
/// the tail, and the decoder's <c>avgMs * step</c> frame delay keeps wall-clock duration correct.
/// </summary>
public class AnimatedWebpFrameStepTests
{
    private const int FlashMaxFrames = 60;   // FlashService.TryLoadAnimatedWebpFrames / LoadGifFrames
    private const double TypicalGifFrameMs = 66;   // ~15fps, the reported clip's cadence

    public static TheoryData<int, int> FrameCases() => new()
    {
        { 60, 60 },    // exactly at the cap
        { 61, 60 },    // one over — the worst floor-division case
        { 100, 60 },   // the reported regression (3.96s of 6.6s)
        { 119, 60 },   // top of the step==1 dead band
        { 120, 60 },   // exactly 2x the cap
        { 170, 60 },   // floor step 2 used to strand frames 120..169
        { 600, 60 },   // at the decode ceiling
    };

    [Theory]
    [MemberData(nameof(FrameCases))]
    public void KeptSet_NeverExceedsTheCap(int frameCount, int maxKeep)
    {
        var plan = AnimatedWebp.FramePlan.Create(frameCount, maxKeep);
        Assert.True(plan.KeptIndices().Count() <= maxKeep,
            $"{frameCount} frames @ cap {maxKeep} kept {plan.KeptIndices().Count()}");
    }

    [Theory]
    [MemberData(nameof(FrameCases))]
    public void KeptIndices_AreMonotonicAndStartAtZero(int frameCount, int maxKeep)
    {
        var kept = AnimatedWebp.FramePlan.Create(frameCount, maxKeep).KeptIndices().ToArray();
        Assert.NotEmpty(kept);
        Assert.Equal(0, kept[0]);
        for (int i = 1; i < kept.Length; i++)
            Assert.True(kept[i] > kept[i - 1], $"index {kept[i]} not after {kept[i - 1]}");
    }

    [Theory]
    [MemberData(nameof(FrameCases))]
    public void KeptSet_ReachesTheEndOfTheClip(int frameCount, int maxKeep)
    {
        var plan = AnimatedWebp.FramePlan.Create(frameCount, maxKeep);
        var kept = plan.KeptIndices().ToArray();

        // The guard must never fire early: the last kept frame's slot has to extend to (or past)
        // the end of the decoded range. Under the old floor step the last kept index was
        // maxKeep-1 with step 1, leaving the whole tail unreachable.
        int last = kept[^1];
        Assert.True(last + plan.Step >= plan.DecodeCount,
            $"{frameCount} frames @ cap {maxKeep}: last kept index {last} (step {plan.Step}) " +
            $"leaves frames {last + plan.Step}..{plan.DecodeCount - 1} unreachable");
        Assert.True(kept.Length * plan.Step >= plan.DecodeCount,
            $"{frameCount} frames @ cap {maxKeep}: {kept.Length} kept x step {plan.Step} " +
            $"covers only {kept.Length * plan.Step} of {plan.DecodeCount} frames");
    }

    [Theory]
    [MemberData(nameof(FrameCases))]
    public void PlaybackDuration_MatchesTheSourceClip(int frameCount, int maxKeep)
    {
        var plan = AnimatedWebp.FramePlan.Create(frameCount, maxKeep);
        int kept = plan.KeptIndices().Count();

        // DecodeFramesCore returns avgMs * step as the frame delay, and callers step the kept
        // frames at that cadence — so kept x (avgMs x step) is the loop's wall-clock length.
        double playedMs = kept * (TypicalGifFrameMs * plan.Step);
        double sourceMs = frameCount * TypicalGifFrameMs;
        double ratio = playedMs / sourceMs;

        Assert.True(ratio >= 0.95 && ratio <= 1.10,
            $"{frameCount} frames @ cap {maxKeep}: loop plays {playedMs / 1000:0.00}s of a " +
            $"{sourceMs / 1000:0.00}s clip (ratio {ratio:0.000})");
    }

    [Fact]
    public void ReportedCase_HundredFramesAt66ms_PlaysFullSixPointSixSeconds()
    {
        var plan = AnimatedWebp.FramePlan.Create(100, FlashMaxFrames);
        int kept = plan.KeptIndices().Count();
        double playedMs = kept * (TypicalGifFrameMs * plan.Step);

        Assert.Equal(2, plan.Step);          // was 1 (100 / 60 floored)
        Assert.Equal(50, kept);              // was 60 — a head slice of the clip
        Assert.Equal(6600.0, playedMs, 1);   // was 3960ms, the reported "cuts to ~4s and loops"
    }

    [Theory]
    [InlineData(61)]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(119)]
    public void DeadBand_BetweenCapAndTwiceCap_NoLongerStepsByOne(int frameCount)
    {
        // Every count in [maxKeep, 2*maxKeep) used to floor to step 1 and get truncated.
        var plan = AnimatedWebp.FramePlan.Create(frameCount, FlashMaxFrames);
        Assert.Equal(2, plan.Step);
        Assert.True(plan.KeptIndices().Count() <= FlashMaxFrames);
    }

    [Fact]
    public void ShortClips_AreKeptWhole()
    {
        var plan = AnimatedWebp.FramePlan.Create(24, FlashMaxFrames);
        Assert.Equal(1, plan.Step);
        Assert.Equal(Enumerable.Range(0, 24), plan.KeptIndices());
    }

    [Fact]
    public void PathologicalClip_StillBoundedByTheDecodeCeiling()
    {
        // Out of scope for #683: frameCount > 600 is still cut at the CPU decode ceiling, so the
        // loop is shorter than the source. What must hold is that the kept set spans everything
        // that IS decoded, and stays under the cap.
        var plan = AnimatedWebp.FramePlan.Create(1000, FlashMaxFrames);
        var kept = plan.KeptIndices().ToArray();

        Assert.Equal(AnimatedWebp.DECODE_CEILING, plan.DecodeCount);
        Assert.True(kept.Length <= FlashMaxFrames);
        Assert.True(kept[^1] + plan.Step >= plan.DecodeCount);
    }

    [Fact]
    public void DegenerateCap_DoesNotThrow()
    {
        // The old expression divided by maxKeep directly; a 0 cap would have thrown.
        var plan = AnimatedWebp.FramePlan.Create(100, 0);
        Assert.True(plan.Step >= 1);
        Assert.Single(plan.KeptIndices());
    }
}
