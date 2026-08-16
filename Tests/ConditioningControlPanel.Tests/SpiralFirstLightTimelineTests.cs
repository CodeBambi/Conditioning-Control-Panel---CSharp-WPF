using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE FIRST LIGHT's clock (CONTRACT-FUSE-0816 §2.4, owner ruling 2026-08-16) — the reveal that
/// plays inside the map window the one time an account is ever shown the spiral.
///
/// <para><b>Why this is worth a test file.</b> It is the one animation in the app that plays ONCE
/// per account on a night that cannot be repeated, so the half of it that can be silently wrong —
/// its arithmetic — is pure, has no WPF in it, and is pinned here. Three properties carry the
/// feature: it lasts the three-to-four seconds the ruling asked for, reduced motion gets a SNAPPED
/// state rather than a missing one, and the HOLD point exists so the window can wait for a descent
/// block inside a bloom instead of in front of an empty rectangle.</para>
/// </summary>
public class SpiralFirstLightTimelineTests
{
    // ---------------------------------------------------------------- the shape

    /// <summary>
    /// "Go crazy with those 3-4 seconds." The full sequence is inside that window — long enough to
    /// be a reveal, short enough that nobody is waiting it out.
    /// </summary>
    [Fact]
    public void TheFullRevealRunsThreeToFourSeconds()
    {
        var total = SpiralFirstLightTimeline.TotalSeconds(reducedMotion: false);

        Assert.InRange(total, 3.0, 4.0);
    }

    /// <summary>Reduced motion is a single crossfade, and a short one: the contract's snapped
    /// state, not a shortened version of the show.</summary>
    [Fact]
    public void ReducedMotionIsOneShortCrossfade()
    {
        Assert.Equal(SpiralFirstLightTimeline.ReducedCrossfadeSeconds,
                     SpiralFirstLightTimeline.TotalSeconds(reducedMotion: true), 3);
        Assert.True(SpiralFirstLightTimeline.TotalSeconds(true) < SpiralFirstLightTimeline.TotalSeconds(false));
    }

    // ---------------------------------------------------------------- the draw

    /// <summary>The arm starts dark, draws steadily, and finishes exactly once — never past 1.</summary>
    [Fact]
    public void TheArmDrawsFromNothingToWhole()
    {
        Assert.Equal(0.0, SpiralFirstLightTimeline.DrawFraction(0.0, false));
        Assert.Equal(0.0, SpiralFirstLightTimeline.DrawFraction(SpiralFirstLightTimeline.DrawStartSeconds, false));

        var mid = SpiralFirstLightTimeline.DrawFraction(
            SpiralFirstLightTimeline.DrawStartSeconds + SpiralFirstLightTimeline.DrawSeconds / 2, false);
        Assert.InRange(mid, 0.45, 0.55);

        Assert.Equal(1.0, SpiralFirstLightTimeline.DrawFraction(SpiralFirstLightTimeline.BloomStartSeconds, false));
        Assert.Equal(1.0, SpiralFirstLightTimeline.DrawFraction(99.0, false));
    }

    /// <summary>
    /// REDUCED MOTION GETS A FINISHED SPIRAL, from the first frame — present rather than missing
    /// (contract §0, the faucet PR #37 lesson). Same for every station dot: a finished spiral is
    /// finished, stations and all.
    /// </summary>
    [Fact]
    public void ReducedMotionSnapsToTheFinishedFigure()
    {
        Assert.Equal(1.0, SpiralFirstLightTimeline.DrawFraction(0.0, reducedMotion: true));
        Assert.Equal(0.0, SpiralFirstLightTimeline.SwirlAngle(0.4, reducedMotion: true));
        Assert.Equal(0.0, SpiralFirstLightTimeline.EmberOpacity(1.5, reducedMotion: true));

        foreach (var at in SpiralFirstLightTimeline.DotAt)
            Assert.Equal(1.0, SpiralFirstLightTimeline.DotGlow(0.0, at, reducedMotion: true));
    }

    /// <summary>
    /// A station dot is dark until the arm's head reaches it, then pops. The last dot must light
    /// BEFORE the arm finishes — a dot popping on the final frame reads as a glitch, which is the
    /// same rule the ignition's notches are spaced under.
    /// </summary>
    [Fact]
    public void StationDotsPopAfterTheHeadPassesAndBeforeTheArmEnds()
    {
        var first = SpiralFirstLightTimeline.DotAt[0];
        var last = SpiralFirstLightTimeline.DotAt[^1];

        Assert.Equal(0.0, SpiralFirstLightTimeline.DotGlow(SpiralFirstLightTimeline.DrawStartSeconds, first, false));

        // Just after the head has passed the first station, it has begun but not finished.
        var justPast = SpiralFirstLightTimeline.DrawStartSeconds
                       + SpiralFirstLightTimeline.DrawSeconds * (first + 0.01);
        Assert.InRange(SpiralFirstLightTimeline.DotGlow(justPast, first, false), 0.0001, 0.9999);

        // Every dot is fully lit by the time the bloom starts.
        foreach (var at in SpiralFirstLightTimeline.DotAt)
            Assert.Equal(1.0, SpiralFirstLightTimeline.DotGlow(SpiralFirstLightTimeline.BloomStartSeconds, at, false));

        Assert.True(last < 1.0, "the last station must sit inside the arm, not on its final frame");
    }

    // ---------------------------------------------------------------- the hold

    /// <summary>
    /// THE HOLD IS THE BLOCK RACE'S ANSWER. The window advances this clock only up to the hand-off
    /// and waits there for the descent block, so a slow sync is spent inside a held bloom. At the
    /// hold point the figure is finished, the bloom is full and the layer is still completely
    /// opaque — which is what makes the wait invisible.
    /// </summary>
    [Fact]
    public void AtTheHoldPointTheFigureIsFinishedAndTheLayerIsStillWhole()
    {
        var hold = SpiralFirstLightTimeline.HandoffStartSeconds(reducedMotion: false);

        Assert.Equal(1.0, SpiralFirstLightTimeline.DrawFraction(hold, false));
        Assert.Equal(1.0, SpiralFirstLightTimeline.BloomOpacity(hold, false));
        Assert.Equal(1.0, SpiralFirstLightTimeline.LayerOpacity(hold, false));
        Assert.False(SpiralFirstLightTimeline.IsComplete(hold, false));
    }

    /// <summary>
    /// The layer is what hands the window to the embed — a WebView2 paints over WPF whatever the
    /// z-order says, so the reveal fades to nothing over the window's own ground and the browser is
    /// shown at the end of it. It must reach exactly zero, and only at the end.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLayerLeavesOnlyAtTheEnd(bool reduced)
    {
        var total = SpiralFirstLightTimeline.TotalSeconds(reduced);
        var start = SpiralFirstLightTimeline.HandoffStartSeconds(reduced);

        Assert.Equal(1.0, SpiralFirstLightTimeline.LayerOpacity(start, reduced));
        Assert.InRange(SpiralFirstLightTimeline.LayerOpacity((start + total) / 2, reduced), 0.0001, 0.9999);
        Assert.Equal(0.0, SpiralFirstLightTimeline.LayerOpacity(total, reduced));
        Assert.Equal(0.0, SpiralFirstLightTimeline.LayerOpacity(total + 5, reduced));

        Assert.True(SpiralFirstLightTimeline.IsComplete(total, reduced));
    }

    // ---------------------------------------------------------------- the edges

    /// <summary>
    /// Embers arrive during the draw and are gone by the time the bloom finishes: a spark still
    /// climbing over a full-screen bloom reads as a dead pixel rather than as an ember.
    /// </summary>
    [Fact]
    public void EmbersRiseDuringTheDrawAndAreGoneByTheBloomsEnd()
    {
        Assert.Equal(0.0, SpiralFirstLightTimeline.EmberOpacity(SpiralFirstLightTimeline.EmberStartSeconds, false));
        Assert.True(SpiralFirstLightTimeline.EmberOpacity(SpiralFirstLightTimeline.EmberStartSeconds + 0.6, false) > 0.9);
        Assert.Equal(0.0, SpiralFirstLightTimeline.EmberOpacity(
            SpiralFirstLightTimeline.BloomStartSeconds + SpiralFirstLightTimeline.BloomSeconds, false));
    }

    /// <summary>
    /// A Stopwatch read across a system clock adjustment should show the first frame again, not
    /// throw or return garbage in the one window this account will ever see this animation in.
    /// </summary>
    [Fact]
    public void NegativesAndNaNReadAsTheFirstFrame()
    {
        Assert.Equal(0.0, SpiralFirstLightTimeline.DrawFraction(-4.0, false));
        Assert.Equal(0.0, SpiralFirstLightTimeline.DrawFraction(double.NaN, false));
        Assert.Equal(0.0, SpiralFirstLightTimeline.BloomOpacity(double.NaN, false));
        Assert.Equal(1.0, SpiralFirstLightTimeline.LayerOpacity(-1.0, false));
        Assert.Equal(0.0, SpiralFirstLightTimeline.SwirlAngle(-1.0, false));
        Assert.False(SpiralFirstLightTimeline.IsComplete(double.NaN, false));
    }

    /// <summary>The whole figure turns while it draws and has stopped turning by the bloom — a
    /// spiral that is still rotating under a bloom reads as a loading spinner.</summary>
    [Fact]
    public void TheFigureTurnsWhileItDrawsAndStopsAtTheBloom()
    {
        var early = SpiralFirstLightTimeline.SwirlAngle(0.6, false);
        var late = SpiralFirstLightTimeline.SwirlAngle(SpiralFirstLightTimeline.BloomStartSeconds - 0.2, false);
        var atBloom = SpiralFirstLightTimeline.SwirlAngle(SpiralFirstLightTimeline.BloomStartSeconds, false);

        Assert.True(early > 0);
        Assert.True(late > early);
        Assert.Equal(atBloom, SpiralFirstLightTimeline.SwirlAngle(SpiralFirstLightTimeline.BloomStartSeconds + 2, false), 6);
    }
}
