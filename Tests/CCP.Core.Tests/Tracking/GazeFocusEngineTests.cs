using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests.Tracking;

/// <summary>The WPF GazeFocusService rules in Core: scoring, the 600 ms dwell, the cooldown, the
/// stare-linger throttle and blink-to-pop.</summary>
public sealed class GazeFocusEngineTests
{
    private sealed class Fake(GazeTargetKind kind, double x, double y, double w, double h) : IGazeTarget
    {
        public GazeTargetKind Kind => kind;
        public object Key => this;
        public (double X, double Y, double W, double H) Bounds => (x, y, w, h);
        public double Progress; public int Activated, Boosts, LastBoostMs;
        public void SetDwellProgress(double t01) => Progress = t01;
        public void Activate() => Activated++;
        public void BoostLifetime(int extraMs) { Boosts++; LastBoostMs = extraMs; }
    }

    private static readonly GazeFocusOptions All = new(Bubbles: true, FlashPop: true, FlashLinger: true, LingerExtensionMs: 1500);
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Scoring_UsesEdgeDistance_AndTheWpfNumbers()
    {
        Assert.Equal(0, GazeFocusEngine.DistanceFromRectEdge((10, 10, 100, 50), 50, 30));
        Assert.Equal(5, GazeFocusEngine.DistanceFromRectEdge((10, 10, 100, 50), 5, 30), 6);
        Assert.Equal(5, GazeFocusEngine.DistanceFromRectEdge((0, 0, 10, 10), 13, 14), 6);
        Assert.Equal(1.0, GazeFocusEngine.GaussianScore(0, 160), 9);
        Assert.Equal(Math.Exp(-0.5), GazeFocusEngine.GaussianScore(160, 160), 9);
        Assert.Equal(600, GazeFocusEngine.DefaultDwellMs);
        Assert.Equal(250, GazeFocusEngine.CooldownMs);
    }

    /// <summary>WPF AdvanceFloatingTextDwell (hunt IB8, HB19): a mandatory video's attention target is clicked
    /// by a 600 ms dwell, never by a blink, counts no bubble pop, and loses a tie to a flash.</summary>
    [Fact]
    public void AVideoAttentionTarget_IsClickedByTheDwell_NeverByABlink()
    {
        var e = new GazeFocusEngine();
        var v = new Fake(GazeTargetKind.Video, 100, 100, 200, 60);
        var list = new List<IGazeTarget> { v };
        var none = new GazeFocusOptions(Bubbles: false, FlashPop: false, FlashLinger: false, LingerExtensionMs: 0);
        int pops = 0; e.GazePopped += () => pops++;
        e.GazeMoved(150, 120);
        Assert.Same(v, e.Tick(T0, list, none));             // listed = enabled: no bubble or flash switch needed
        Assert.False(e.Blink(T0.AddMilliseconds(10), list, none));
        Assert.Equal(0, v.Activated);
        var t1 = T0.AddMilliseconds(400);                   // past the blink's cooldown: a fresh dwell
        e.Tick(t1, list, none);
        e.Tick(t1.AddMilliseconds(599), list, none);
        Assert.Equal(0, v.Activated);
        e.Tick(t1.AddMilliseconds(600), list, none);
        Assert.Equal(1, v.Activated);
        Assert.Equal(0, pops);
        Assert.Null(e.Tick(t1.AddMilliseconds(700), list, none));   // cooldown

        var e2 = new GazeFocusEngine();
        var flash = new Fake(GazeTargetKind.Flash, 100, 100, 200, 60);
        e2.GazeMoved(150, 120);
        Assert.Same(flash, e2.Tick(T0, new List<IGazeTarget> { v, flash }, All));
    }

    [Fact]
    public void ABubbleFillsThenPops_AfterTheDwell_ThenCoolsDown()
    {
        var e = new GazeFocusEngine();
        var b = new Fake(GazeTargetKind.Bubble, 100, 100, 80, 80);
        var list = new List<IGazeTarget> { b };
        int pops = 0; e.GazePopped += () => pops++;
        Assert.Null(e.Tick(T0, list, All));                 // no gaze yet
        e.GazeMoved(140, 140);
        Assert.Same(b, e.Tick(T0, list, All));
        e.Tick(T0.AddMilliseconds(300), list, All);
        Assert.Equal(0.5, b.Progress, 6);
        Assert.Equal(0, b.Activated);
        e.Tick(T0.AddMilliseconds(600), list, All);
        Assert.Equal(1, b.Activated);
        Assert.Equal(1, pops);
        Assert.Null(e.Tick(T0.AddMilliseconds(700), list, All));    // cooldown
        Assert.NotNull(e.Tick(T0.AddMilliseconds(900), list, All)); // and a fresh dwell starts
        Assert.Equal(1, b.Activated);
    }

    /// <summary>WPF's glue, kept as shipped: the sticky bonus (0.20) is above the score threshold (0.05), so
    /// the target being dwelt on stays acquired wherever the eyes wander until another target outscores it,
    /// it goes away, or the face is lost.</summary>
    [Fact]
    public void TheDwellSticks_UntilTheTargetGoesOrTheFaceIsLost()
    {
        var e = new GazeFocusEngine();
        var b = new Fake(GazeTargetKind.Bubble, 100, 100, 80, 80);
        var list = new List<IGazeTarget> { b };
        var none = new List<IGazeTarget>();
        e.GazeMoved(3000, 3000);                              // far outside any sigma: never acquired
        Assert.Null(e.Tick(T0, list, All));
        e.GazeMoved(140, 140);
        e.Tick(T0.AddMilliseconds(33), list, All);
        e.GazeMoved(3000, 3000);
        Assert.Same(b, e.Tick(T0.AddMilliseconds(66), list, All));   // glued
        Assert.Null(e.Tick(T0.AddMilliseconds(100), none, All));     // the bubble left: fill cleared
        Assert.Equal(0, b.Progress);
        e.GazeMoved(140, 140);
        e.Tick(T0.AddMilliseconds(133), list, All);
        e.FaceLost();
        Assert.Null(e.Tick(T0.AddMilliseconds(166), list, All));
        Assert.Equal(0, b.Progress);
        e.FaceFound();
        e.Tick(T0.AddMilliseconds(500), list, All);
        e.Tick(T0.AddMilliseconds(1000), list, All);          // 500 ms since the new dwell: not yet
        Assert.Equal(0, b.Activated);
    }

    [Fact]
    public void AFlashOutranksABubble_Lingers_AndOnlyPopsWhenPopIsOn()
    {
        var e = new GazeFocusEngine();
        var flash = new Fake(GazeTargetKind.Flash, 0, 0, 200, 200);
        var bubble = new Fake(GazeTargetKind.Bubble, 150, 150, 80, 80);
        var list = new List<IGazeTarget> { bubble, flash };
        e.GazeMoved(170, 170);                                // inside both
        var lingerOnly = new GazeFocusOptions(true, FlashPop: false, FlashLinger: true, 1500);
        Assert.Same(flash, e.Tick(T0, list, lingerOnly));
        Assert.Equal(1, flash.Boosts);
        e.Tick(T0.AddMilliseconds(100), list, lingerOnly);    // throttled to one boost per 250 ms
        Assert.Equal(1, flash.Boosts);
        e.Tick(T0.AddMilliseconds(260), list, lingerOnly);
        Assert.Equal(2, flash.Boosts);
        Assert.Equal(1500, flash.LastBoostMs);
        e.Tick(T0.AddMilliseconds(900), list, lingerOnly);
        Assert.Equal(0, flash.Activated);                     // linger never pops
        e.Tick(T0.AddMilliseconds(930), list, All);
        Assert.Equal(1, flash.Activated);
        Assert.Equal(0, bubble.Activated);
    }

    [Fact]
    public void BubblesOff_AreNeverTargets_AndABlinkPopsAtOnce()
    {
        var e = new GazeFocusEngine();
        var b = new Fake(GazeTargetKind.Bubble, 100, 100, 80, 80);
        var list = new List<IGazeTarget> { b };
        e.GazeMoved(140, 140);
        var noBubbles = new GazeFocusOptions(false, true, true, 1500);
        Assert.Null(e.Tick(T0, list, noBubbles));
        Assert.False(e.Blink(T0, list, noBubbles));
        Assert.True(e.Blink(T0.AddMilliseconds(10), list, All));
        Assert.Equal(1, b.Activated);
        Assert.False(e.Blink(T0.AddMilliseconds(100), list, All));   // cooldown
    }

    [Fact]
    public void TheStickyBonus_KeepsTheCurrentTarget_WhenTwoAreEquallyNear()
    {
        var e = new GazeFocusEngine();
        var left = new Fake(GazeTargetKind.Bubble, 0, 0, 50, 50);
        var right = new Fake(GazeTargetKind.Bubble, 150, 0, 50, 50);
        var list = new List<IGazeTarget> { left, right };
        e.GazeMoved(60, 25);                                  // nearer the left one
        Assert.Same(left, e.Tick(T0, list, All));
        e.GazeMoved(105, 25);                                 // now slightly nearer the right one
        Assert.Same(left, e.Tick(T0.AddMilliseconds(33), list, All));
        e.GazeMoved(175, 25);                                 // inside the right one: it finally wins
        Assert.Same(right, e.Tick(T0.AddMilliseconds(66), list, All));
        Assert.Equal(0, left.Progress);
    }
}
