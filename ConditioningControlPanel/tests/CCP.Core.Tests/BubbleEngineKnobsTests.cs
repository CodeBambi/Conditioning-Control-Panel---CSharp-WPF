using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// WPF-parity tests for the S4b-4 live run knobs (<see cref="ChaosRunKnobs"/>): the engine reads
/// every knob at its use site so upgrades/boons take effect mid-run, exactly like the live
/// lambdas WPF ChaosModeService passes into BeginChaosMode (WPF ChaosModeService.cs:361-381).
/// Each test exercises the REAL engine path (BeginChaosMode + SpawnChaosBubble +
/// TickOnceForTesting / OnSharedHostLeftDown), not a stub.
/// </summary>
public class BubbleEngineKnobsTests
{
    // ============================ knob defaults / Reset ============================

    [AvaloniaFact]
    public void Knobs_Defaults_MatchWpfNoUpgradeValues_AndResetRestoresThem()
    {
        // WPF no-upgrade lambda values (ChaosModeService.cs:363-386): chainReach 0 (off),
        // hitboxScale 1.0, bubbleOpacity 1.0, cursorPull 0, rabbitHoming false, spankerOn false,
        // spankGrow 1.0, liveMagnet false, rabbitTrailSec 0, electrifiedRabbits false.
        var knobs = new ChaosRunKnobs();
        AssertWpfDefaults(knobs);

        knobs.SpankerOn = true;
        knobs.ChainReachDip = 300;
        knobs.HitboxScale = 1.25;
        knobs.BubbleOpacity = 0.4;
        knobs.CursorPull = 2.5;
        knobs.RabbitHoming = true;
        knobs.SpankGrow = 1.6;
        knobs.LiveMagnet = true;
        knobs.RabbitTrailSec = 2.0;
        knobs.ElectrifiedRabbits = true;

        knobs.Reset();
        AssertWpfDefaults(knobs);
    }

    private static void AssertWpfDefaults(ChaosRunKnobs knobs)
    {
        Assert.False(knobs.SpankerOn);
        Assert.Equal(0.0, knobs.ChainReachDip);
        Assert.Equal(1.0, knobs.HitboxScale);
        Assert.Equal(1.0, knobs.BubbleOpacity);
        Assert.Equal(0.0, knobs.CursorPull);
        Assert.False(knobs.RabbitHoming);
        Assert.Equal(1.0, knobs.SpankGrow);
        Assert.False(knobs.LiveMagnet);
        Assert.Equal(0.0, knobs.RabbitTrailSec);
        Assert.False(knobs.ElectrifiedRabbits);
    }

    [AvaloniaFact]
    public void BeginChaosMode_ResetsKnobs_AndSeedsChainReachFromParam()
    {
        // WPF BeginChaosMode resets the sampled statics at run start (BubbleService.cs:1092-1093,
        // :1106); the chainReachDip param stays the back-compat seed for callers/fakes.
        var engine = NewEngine(out _);
        engine.Knobs.SpankerOn = true;
        engine.Knobs.HitboxScale = 1.9;

        BeginChaos(engine);
        Assert.False(engine.Knobs.SpankerOn);
        Assert.Equal(1.0, engine.Knobs.HitboxScale);
        Assert.Equal(120.0, engine.Knobs.ChainReachDip);   // DefaultChainReachDip seed

        engine.EndChaosMode();
        // WPF ClearChaos resets everything again (BubbleService.cs:1674-1687).
        Assert.Equal(0.0, engine.Knobs.ChainReachDip);
    }

    // ============================ hitboxScale / liveMagnet (spawn-sampled) ============================

    [AvaloniaFact]
    public void HitboxScale_EnlargesSharedHostHitDisc_AtSpawn()
    {
        // WPF Bubble ctor (BubbleService.cs:2539-2541): hitMult = Clamp(scale, 1.0, 2.0);
        // _hitSize = Max(size, Round(size*hitMult)); the shared-host click disc radius is
        // _hitSize/2 (WPF :2423 HitDiscPx). size 80, scale 1.5 → HitSize 120: a press 55px off
        // centre (outside the 40px nominal radius, inside the 60px scaled one) HITS.
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.HitboxScale = 1.5;

        var treat = Treat("flash", atX: 500, atY: 500);
        engine.SpawnChaosBubble(treat);
        engine.TickOnceForTesting();

        var bubble = engine.GetChaosBubble(treat.Id);
        Assert.NotNull(bubble);
        Assert.Equal(120.0, bubble!.HitSize);   // Max(80, Round(80*1.5))

        var press = new Point(bubble.X + bubble.Size / 2.0 + 55.0, bubble.Y + bubble.Size / 2.0);
        Assert.True(engine.OnSharedHostLeftDown(press), "55px off-centre press must hit the 60px scaled disc");
        Assert.True(bubble.IsPopping);

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void HitboxScale_One_KeepsNominalDisc_SamePressMisses()
    {
        var engine = NewEngine(out _);
        BeginChaos(engine);
        Assert.Equal(1.0, engine.Knobs.HitboxScale);   // WPF no-upgrade default

        var treat = Treat("flash", atX: 500, atY: 500);
        engine.SpawnChaosBubble(treat);
        engine.TickOnceForTesting();

        var bubble = engine.GetChaosBubble(treat.Id);
        Assert.NotNull(bubble);
        Assert.Equal(80.0, bubble!.HitSize);

        var press = new Point(bubble.X + bubble.Size / 2.0 + 55.0, bubble.Y + bubble.Size / 2.0);
        Assert.False(engine.OnSharedHostLeftDown(press), "55px off-centre press must miss the 40px nominal disc");
        Assert.False(bubble.IsPopping);

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void HitboxScale_ClampsAtTwo_AndSkipsPickups()
    {
        // WPF :2539 clamps the multiple to [1.0, 2.0]; pickups (golden here) are not "plain
        // effect bubbles" (WPF :2532-2538) and keep their natural hitbox.
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.HitboxScale = 5.0;

        var treat = Treat("flash", atX: 300, atY: 300);
        var golden = Treat("golden", atX: 800, atY: 300, isGolden: true);
        engine.SpawnChaosBubble(treat);
        engine.SpawnChaosBubble(golden);
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();   // MaxSpawnsPerFrame=1

        Assert.Equal(160.0, engine.GetChaosBubble(treat.Id)!.HitSize);   // 80 × clamp(5,1,2)
        Assert.Equal(80.0, engine.GetChaosBubble(golden.Id)!.HitSize);   // natural

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void LiveMagnet_WidensLiveHitboxOnly_WithSharedCeiling()
    {
        // WPF :2540: liveMagnet && IsLive → hitMult = Clamp(hitMult*1.4, 1.0, 2.0). Treats keep
        // the plain scale; the 2.0 ceiling is shared with the wand-enlarged hitbox.
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.LiveMagnet = true;

        var live = Live("live", atX: 300, atY: 300, sizePx: 100);
        var treat = Treat("flash", atX: 800, atY: 300);
        engine.SpawnChaosBubble(live);
        engine.SpawnChaosBubble(treat);
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();

        Assert.Equal(140.0, engine.GetChaosBubble(live.Id)!.HitSize);   // 100 × 1.4
        Assert.Equal(80.0, engine.GetChaosBubble(treat.Id)!.HitSize);   // magnet is live-only

        // Ceiling: scale 1.8 × 1.4 = 2.52 → clamps to 2.0.
        engine.Knobs.HitboxScale = 1.8;
        var live2 = Live("live2", atX: 300, atY: 700, sizePx: 100);
        engine.SpawnChaosBubble(live2);
        engine.TickOnceForTesting();
        Assert.Equal(200.0, engine.GetChaosBubble(live2.Id)!.HitSize);

        engine.EndChaosMode();
    }

    // ============================ bubbleOpacity (Blindfold, spawn-sampled) ============================

    [AvaloniaFact]
    public void BubbleOpacity_StampsPlainBubblesAtSpawn_PickupsStayVisible_ClampsAtPointTwo()
    {
        // WPF :2542: _baseOpacity = plainEffectBubble ? Clamp(opacityMult, 0.2, 1.0) : 1.0.
        // Hearts are rewards — always fully visible. The stamped BubbleState.Opacity is what the
        // Avalonia renderer forwards to the Skia BubbleLayer, so no compositor change is needed.
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.BubbleOpacity = 0.32;

        var treat = Treat("flash", atX: 300, atY: 300);
        var heart = Treat("heart", atX: 800, atY: 300, isHeart: true);
        engine.SpawnChaosBubble(treat);
        engine.SpawnChaosBubble(heart);
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();

        Assert.Equal(0.32, engine.GetChaosBubble(treat.Id)!.Opacity);
        Assert.Equal(1.0, engine.GetChaosBubble(heart.Id)!.Opacity);

        // Clamp floor (WPF :2542): 0.05 → 0.2.
        engine.Knobs.BubbleOpacity = 0.05;
        var treat2 = Treat("flash2", atX: 300, atY: 700);
        engine.SpawnChaosBubble(treat2);
        engine.TickOnceForTesting();
        Assert.Equal(0.2, engine.GetChaosBubble(treat2.Id)!.Opacity);

        engine.EndChaosMode();
    }

    // ============================ chainReach (live mid-run) ============================

    [AvaloniaFact]
    public void ChainReach_LiveMidRunChange_WidensNextPop_WithoutReBegin()
    {
        // The engine's chain pops pickups whose centre lies within ChainReachDip of the popped
        // bubble's centre. WPF re-invokes its chainReach lambda per pop (BubbleService.cs:1610),
        // so a mid-run change must affect the very next pop — no re-BeginChaosMode.
        var engine = NewEngine(out _);
        BeginChaos(engine);
        Assert.Equal(120.0, engine.Knobs.ChainReachDip);   // seed

        // Treat centre (540,540); golden centre (740,540) → 200 DIP apart.
        var treat1 = Treat("flash", atX: 500, atY: 500);
        var golden = Treat("golden", atX: 700, atY: 500, isGolden: true);
        engine.SpawnChaosBubble(treat1);
        engine.SpawnChaosBubble(golden);
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();

        engine.PopBubble(treat1.Id);
        Assert.False(engine.GetChaosBubble(golden.Id)!.IsPopping);   // 200 > 120: no chain
        Assert.NotNull(engine.GetChaosBubble(golden.Id));

        // Mid-run boon level-up: widen the reach, pop another treat at the same spot.
        engine.Knobs.ChainReachDip = 300.0;
        var treat2 = Treat("flash2", atX: 500, atY: 500);
        engine.SpawnChaosBubble(treat2);
        engine.TickOnceForTesting();
        engine.PopBubble(treat2.Id);

        Assert.Null(engine.GetChaosBubble(golden.Id));   // 200 <= 300: chained away

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void ChainReach_Zero_TurnsChainingOff()
    {
        // WPF no-boon: reachMult 0 → ChainPopNeighbors returns immediately (BubbleService.cs:1611).
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.ChainReachDip = 0.0;   // the service syncs 0 when ChainReactionReach <= 1

        var treat = Treat("flash", atX: 500, atY: 500);
        var golden = Treat("golden", atX: 510, atY: 510, isGolden: true);   // overlapping
        engine.SpawnChaosBubble(treat);
        engine.SpawnChaosBubble(golden);
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();

        engine.PopBubble(treat.Id);
        Assert.NotNull(engine.GetChaosBubble(golden.Id));   // no chain at all

        engine.EndChaosMode();
    }

    // ============================ cursorPull (The Pull / Cam Girl) ============================

    [AvaloniaFact]
    public void CursorPull_Positive_DriftsOrdinaryBubbleTowardCursor()
    {
        // WPF :3213-3226: pull > 0 steps the bubble pull×ts DIPs per 32ms frame toward the
        // cursor (30-DIP dead zone). Spawn-at-point bubbles have zero velocity, so ALL movement
        // here is the pull.
        var engine = NewEngine(out var pointer);
        BeginChaos(engine);
        pointer.Position = new System.Drawing.Point(940, 540);   // 400 DIP right of bubble centre

        var treat = Treat("flash", atX: 500, atY: 500);          // centre (540,540)
        engine.SpawnChaosBubble(treat);
        engine.TickOnceForTesting();
        var bubble = engine.GetChaosBubble(treat.Id)!;
        var x0 = bubble.X;
        var y0 = bubble.Y;

        engine.Knobs.CursorPull = 5.0;   // DIPs per WPF frame
        for (int i = 0; i < 10; i++) engine.TickOnceForTesting();

        // step/tick = pull × (dt/TickInterval) = 5 × timeScale(1) × FIELD_PACE(0.8) = 4 DIP.
        Assert.InRange(bubble.X - x0, 30.0, 50.0);   // ~40 DIP toward the cursor
        Assert.Equal(y0, bubble.Y, 3);               // pure +X pull, no Y drift

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void CursorPull_Negative_CamGirlFlee_PushesBubbleAwayInsideFleeRadius()
    {
        // WPF :3230-3245: pull < 0 repels only within FLEE_RADIUS 260 DIPs, fading with
        // distance, clamped on-screen.
        var engine = NewEngine(out var pointer);
        BeginChaos(engine);
        pointer.Position = new System.Drawing.Point(600, 540);   // 60 DIP right of bubble centre

        var treat = Treat("flash", atX: 500, atY: 500);          // centre (540,540)
        engine.SpawnChaosBubble(treat);
        engine.TickOnceForTesting();
        var bubble = engine.GetChaosBubble(treat.Id)!;
        var x0 = bubble.X;

        engine.Knobs.CursorPull = -2.0;   // Cam Girl flee
        for (int i = 0; i < 10; i++) engine.TickOnceForTesting();
        Assert.True(bubble.X < x0, "bubble must squirm AWAY from the nearby cursor");

        // Outside the 260-DIP flee radius the repulsion is silent.
        var far = Treat("far", atX: 500, atY: 100);              // centre (540,140), 400 from cursor
        engine.SpawnChaosBubble(far);
        engine.TickOnceForTesting();
        var farBubble = engine.GetChaosBubble(far.Id)!;
        var fx0 = farBubble.X;
        for (int i = 0; i < 10; i++) engine.TickOnceForTesting();
        Assert.Equal(fx0, farBubble.X, 3);

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void CursorPull_DoesNotMoveDarters()
    {
        // WPF: the pull block lives in the ordinary-motion branch; darters have their own
        // homing (:3023) and never read ChaosCursorPullNow for drift.
        var engine = NewEngine(out var pointer);
        BeginChaos(engine);
        pointer.Position = new System.Drawing.Point(1500, 540);

        var darter = Darter(atX: 500, atY: 500, telegraphMs: 60000);   // parked in telegraph
        engine.SpawnChaosBubble(darter);
        engine.TickOnceForTesting();
        var bubble = engine.GetChaosBubble(darter.Id)!;
        var x0 = bubble.X;

        engine.Knobs.CursorPull = 5.0;
        for (int i = 0; i < 10; i++) engine.TickOnceForTesting();
        Assert.Equal(x0, bubble.X, 3);   // telegraphing darter holds position; no pull leak

        engine.EndChaosMode();
    }

    // ============================ rabbitHoming (The Pull) ============================

    [AvaloniaFact]
    public void RabbitHoming_SteersDarterVelocityTowardCursor_TurnRateCapped()
    {
        // WPF :3023-3039: steer toward the cursor with maxTurn = 0.065 × Max(ts, 0.4) rad/frame,
        // preserving speed. Heading east with the cursor due north → Vy must turn negative by at
        // most the cap; speed unchanged.
        var engine = NewEngine(out var pointer);
        BeginChaos(engine);

        var darter = Darter(atX: 500, atY: 500, telegraphMs: 1);   // 1ms telegraph → done tick 1
        engine.SpawnChaosBubble(darter);
        engine.TickOnceForTesting();   // materialize + finish telegraph
        var bubble = engine.GetChaosBubble(darter.Id)!;
        Assert.True(bubble.TelegraphComplete);

        bubble.Vx = 360.0;   // heading due east at natural speed
        bubble.Vy = 0.0;
        pointer.Position = new System.Drawing.Point(
            (int)(bubble.X + bubble.Size / 2.0),
            (int)(bubble.Y + bubble.Size / 2.0) - 400);   // cursor due NORTH

        engine.Knobs.RabbitHoming = true;
        engine.TickOnceForTesting();

        Assert.True(bubble.Vy < 0, "velocity must rotate toward the cursor (north)");
        double speed = Math.Sqrt(bubble.Vx * bubble.Vx + bubble.Vy * bubble.Vy);
        Assert.Equal(360.0, speed, 3);   // turn, don't accelerate
        // Turn cap: 0.065 × Max(ts=0.8, 0.4) = 0.052 rad this tick.
        double turned = Math.Abs(Math.Atan2(bubble.Vy, bubble.Vx));
        Assert.InRange(turned, 0.05, 0.055);

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void RabbitHoming_Off_LeavesDarterHeadingAlone()
    {
        var engine = NewEngine(out var pointer);
        BeginChaos(engine);

        var darter = Darter(atX: 500, atY: 500, telegraphMs: 1);
        engine.SpawnChaosBubble(darter);
        engine.TickOnceForTesting();
        var bubble = engine.GetChaosBubble(darter.Id)!;

        bubble.Vx = 360.0;
        bubble.Vy = 0.0;
        pointer.Position = new System.Drawing.Point((int)bubble.X, (int)bubble.Y - 400);

        Assert.False(engine.Knobs.RabbitHoming);   // WPF no-upgrade default
        engine.TickOnceForTesting();
        Assert.Equal(0.0, bubble.Vy, 6);

        engine.EndChaosMode();
    }

    // ============================ spankGrow (The Spanker) ============================

    [AvaloniaFact]
    public void SpankGrow_FirstSmackStampsGrowthOnce_ResmackNeverRegrows()
    {
        // WPF Spank() :3794-3796: the swell happens ONCE on the first smack —
        // _spankGrowth = Max(1.0, ChaosSpankGrowNow); re-smacks only steer and hurry.
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.SpankerOn = true;
        engine.Knobs.SpankGrow = 1.6;

        var darter = Darter(atX: 500, atY: 500, telegraphMs: 60000);   // parked → stable press point
        engine.SpawnChaosBubble(darter);
        engine.TickOnceForTesting();
        var bubble = engine.GetChaosBubble(darter.Id)!;
        var press = new Point(bubble.X + bubble.Size / 2.0, bubble.Y + bubble.Size / 2.0);

        Assert.True(engine.OnSharedHostLeftDown(press));
        Assert.True(bubble.IsSpanked);
        Assert.Equal(1.6, bubble.SpankGrowth);
        Assert.Equal(1.6, bubble.Scale);   // the render swell (WPF _scale = _spankGrowth, :3001)

        // Burn past the 250ms smack cooldown (flat -32/tick, WPF :3073), raise the knob, re-smack.
        engine.Knobs.SpankGrow = 2.5;
        for (int i = 0; i < 9; i++) engine.TickOnceForTesting();
        Assert.True(engine.OnSharedHostLeftDown(press));
        Assert.Equal(1.6, bubble.SpankGrowth);   // no re-grow, no compounding

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void SpankGrow_BelowOne_ClampsToOne()
    {
        // WPF clamps at the sample (:489 Max(1.0, …)) and again at the stamp (:3796).
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.SpankerOn = true;
        engine.Knobs.SpankGrow = 0.5;

        var darter = Darter(atX: 500, atY: 500, telegraphMs: 60000);
        engine.SpawnChaosBubble(darter);
        engine.TickOnceForTesting();
        var bubble = engine.GetChaosBubble(darter.Id)!;
        var press = new Point(bubble.X + bubble.Size / 2.0, bubble.Y + bubble.Size / 2.0);

        Assert.True(engine.OnSharedHostLeftDown(press));
        Assert.Equal(1.0, bubble.SpankGrowth);

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void Smack_RedirectsAndHurries_CappedAtNaturalTimesTwoPointTwo()
    {
        // WPF Spank() :3777-3783: every (cooldown-gated) smack re-rolls the heading and stings
        // the pace +18%, capped at 2.2× the natural speed.
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.SpankerOn = true;

        var darter = Darter(atX: 500, atY: 500, telegraphMs: 60000);
        engine.SpawnChaosBubble(darter);
        engine.TickOnceForTesting();
        var bubble = engine.GetChaosBubble(darter.Id)!;
        var press = new Point(bubble.X + bubble.Size / 2.0, bubble.Y + bubble.Size / 2.0);

        // Spawn-at-point darters start with zero velocity → the first smack floors to natural
        // speed (WPF :3778), then +18% capped 2.2×. DarterSpeed=0 → DefaultDarterSpeed 360 DIP/s.
        Assert.True(engine.OnSharedHostLeftDown(press));
        double spd1 = Math.Sqrt(bubble.Vx * bubble.Vx + bubble.Vy * bubble.Vy);
        Assert.Equal(360.0 * 1.18, spd1, 3);

        // Smack through the cooldown many times: speed converges on the 2.2× cap.
        for (int s = 0; s < 12; s++)
        {
            for (int i = 0; i < 9; i++) engine.TickOnceForTesting();
            // The parked (telegraphing) darter never moves, so the press point stays valid.
            engine.OnSharedHostLeftDown(press);
        }
        double spdN = Math.Sqrt(bubble.Vx * bubble.Vx + bubble.Vy * bubble.Vy);
        Assert.Equal(360.0 * 2.2, spdN, 3);

        engine.EndChaosMode();
    }

    // ============================ spank sweep + electrifiedRabbits ============================

    [AvaloniaFact]
    public void SpankedRabbit_MowsOverlappingTreat_ElectrifiedArcsPopNeighbours()
    {
        // WPF SpankSweepFromDarter (:1564-1579): a spanked rabbit's body pops plain bubbles it
        // crosses; with Electrified Rabbits each victim discharges free arcs into up to 3
        // suitable neighbours within 620px (:1576, EStimBurstAt :407-441).
        var engine = NewEngine(out _);
        BeginChaos(engine);
        engine.Knobs.ElectrifiedRabbits = true;

        var sweeper = Darter(atX: 500, atY: 500, telegraphMs: 60000, isSweeper: true);   // born spanked
        var mowed = Treat("mowed", atX: 540, atY: 540);      // overlaps the sweeper's body box
        var arced = Treat("arced", atX: 900, atY: 500);      // ~362px from the victim: in arc range
        // One spawn materializes per tick — the sweeper goes LAST so both treats are already on
        // the field when its first sweep pass runs.
        engine.SpawnChaosBubble(mowed);
        engine.SpawnChaosBubble(arced);
        engine.SpawnChaosBubble(sweeper);
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();   // sweeper materializes; its sweep pass mows + discharges

        Assert.True(engine.GetChaosBubble(sweeper.Id)!.IsSpanked);
        engine.TickOnceForTesting();   // one more pass for good measure

        Assert.Null(engine.GetChaosBubble(mowed.Id));   // mowed by the body
        Assert.Null(engine.GetChaosBubble(arced.Id));   // popped by the free arc
        Assert.NotNull(engine.GetChaosBubble(sweeper.Id));   // the rabbit itself is unharmed

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void SpankedRabbit_WithoutElectrified_MowsButNeverArcs()
    {
        var engine = NewEngine(out _);
        BeginChaos(engine);
        Assert.False(engine.Knobs.ElectrifiedRabbits);   // WPF no-duo default

        var sweeper = Darter(atX: 500, atY: 500, telegraphMs: 60000, isSweeper: true);
        var mowed = Treat("mowed", atX: 540, atY: 540);
        var spared = Treat("spared", atX: 900, atY: 500);   // in would-be arc range, out of the body box
        engine.SpawnChaosBubble(mowed);
        engine.SpawnChaosBubble(spared);
        engine.SpawnChaosBubble(sweeper);   // last — both treats on the field before the first sweep
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();
        engine.TickOnceForTesting();

        Assert.Null(engine.GetChaosBubble(mowed.Id));       // body mow still works
        Assert.NotNull(engine.GetChaosBubble(spared.Id));   // no arcs without the duo

        engine.EndChaosMode();
    }

    // ============================ rabbitTrailSec wrappers ============================

    [AvaloniaFact]
    public void RabbitTrailSec_SetterAndGetter_AreThinKnobWrappers_WithWpfClamp()
    {
        // SetRabbitTrailSec/ChaosRabbitTrailSecNow stay as wrappers over the knob so existing
        // callers don't break; the WPF per-tick clamp Max(0, …) (BubbleService.cs:490) holds.
        var engine = NewEngine(out _);
        BeginChaos(engine);

        engine.SetRabbitTrailSec(2.0);
        Assert.Equal(2.0, engine.Knobs.RabbitTrailSec);
        Assert.Equal(2.0, engine.ChaosRabbitTrailSecNow);

        engine.SetRabbitTrailSec(-3.0);
        Assert.Equal(0.0, engine.ChaosRabbitTrailSecNow);

        engine.Knobs.RabbitTrailSec = 1.5;   // direct knob write (the service sync path)
        Assert.Equal(1.5, engine.ChaosRabbitTrailSecNow);

        engine.EndChaosMode();
    }

    // ============================ helpers ============================

    private static void BeginChaos(BubbleEngine engine)
    {
        engine.BeginChaosMode(
            onBenignPop: _ => { },
            onDefuse: (_, _, _) => { },
            onDetonate: _ => { });
    }

    private static BubbleEngine NewEngine(out MovablePointerState pointer)
    {
        var screen = new ScreenInfo("test",
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            1.0);
        pointer = new MovablePointerState();
        return new BubbleEngine(
            new FakeScreenProvider(screen),
            new FakeSettingsService(),
            new FakeBubbleRenderer(),
            pointer);
    }

    private static ChaosBubbleSpec Treat(string variant, double atX, double atY,
        bool isGolden = false, bool isHeart = false) => new()
    {
        VariantId = variant,
        PayloadKind = "Flash",
        SizePx = 80,
        Motion = ChaosMotion.FloatUp,
        SpawnAtPxX = atX,
        SpawnAtPxY = atY,     // spawn-at-point → zero velocity (ComputeChaosSpawn)
        TreatLifeMs = 60000,  // won't rot mid-test
        LifetimeMs = 60000,
        IsGolden = isGolden,
        IsHeart = isHeart,
    };

    private static ChaosBubbleSpec Live(string variant, double atX, double atY, double sizePx = 80) => new()
    {
        VariantId = variant,
        IsLive = true,
        FuseMs = 60000,
        SizePx = sizePx,
        Motion = ChaosMotion.FloatUp,
        SpawnAtPxX = atX,
        SpawnAtPxY = atY,
        LifetimeMs = 60000,
    };

    private static ChaosBubbleSpec Darter(double atX, double atY, int telegraphMs, bool isSweeper = false) => new()
    {
        VariantId = "darter",
        IsDarter = true,
        IsSweeper = isSweeper,
        SizePx = 80,
        Motion = ChaosMotion.RoamBounce,
        SpawnAtPxX = atX,
        SpawnAtPxY = atY,
        TelegraphMs = telegraphMs,
        DarterSpeed = 0,          // → DefaultDarterSpeed (360 DIP/s)
        DarterMaxBounces = 99,
        LifetimeMs = 600000,
        QuickWindowMs = 600,
    };

    private sealed class FakeScreenProvider : IScreenProvider
    {
        private readonly ScreenInfo _primary;
        public FakeScreenProvider(ScreenInfo primary) => _primary = primary;
        public IReadOnlyList<ScreenInfo> GetAllScreens() => new[] { _primary };
        public ScreenInfo? GetPrimaryScreen() => _primary;
        public event EventHandler? ScreensChanged { add { } remove { } }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private sealed class FakeBubbleRenderer : IBubbleRenderer
    {
        public void Create(BubbleState state) { }
        public void Move(BubbleState state) { }
        public void Pop(BubbleState state, Action onComplete) => onComplete();
        public void Destroy(Guid id) { }
        public void SetLabel(Guid id, string label) { }
        public void SetFuse(Guid id, double fraction) { }
    }

    /// <summary>Cursor-position fake the pull/homing tests move around (the engine's
    /// <see cref="IPointerState"/> seam; null position = no cursor sample).</summary>
    private sealed class MovablePointerState : IPointerState
    {
        public System.Drawing.Point? Position { get; set; }
        public System.Drawing.Point? GetCursorPosition() => Position;
        public bool IsMouseButtonPressed(MouseButton button) => false;
    }
}
