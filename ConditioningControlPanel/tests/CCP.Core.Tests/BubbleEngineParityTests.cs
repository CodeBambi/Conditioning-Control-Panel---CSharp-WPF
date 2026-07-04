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
/// WPF-parity tests for the three S4b engine-internal fixes in <see cref="BubbleEngine"/>:
/// bound-enrage-stays-alive (S4b-1), treat-rot fires OnTreatExpired for the WPF treat set
/// (S4b-2), and the darter first-spank lesson hook (S4b-3). Each test exercises the REAL
/// engine path (BeginChaosMode + SpawnChaos* + TickOnceForTesting), not a stub.
/// </summary>
public class BubbleEngineParityTests
{
    // ============================ S4b-1: bound enrage enrages, does not detonate ============================

    [AvaloniaFact]
    public void S4b1_BoundEnrage_KeepsSurvivorAlive_HalvesFuse_ScalesSpeed_FiresOnlyOnBoundEnraged()
    {
        var engine = NewEngine();
        var onBoundEnraged = new List<ChaosBubbleSpec>();
        var onDetonate = new List<ChaosBubbleSpec>();
        engine.BeginChaosMode(
            onBenignPop: _ => { },
            onDefuse: (_, _, _) => { },
            onDetonate: s => onDetonate.Add(s),
            onBoundEnraged: s => onBoundEnraged.Add(s));

        // A bound pair: two live halves. Generous fuse/life, tiny resolve window so the enrage
        // fires within a handful of ticks. FloatUp keeps Vx stable (no bounces) so the ×1.4
        // scale is exactly assertable.
        var specA = BoundHalf("boundA");
        var specB = BoundHalf("boundB");
        engine.SpawnChaosBoundPair(specA, specB);
        // MaxSpawnsPerFrame=1, so each half materializes on its own tick.
        for (int i = 0; i < 6 && (engine.GetChaosBubble(specA.Id) == null || engine.GetChaosBubble(specB.Id) == null); i++)
            engine.TickOnceForTesting();
        Assert.NotNull(engine.GetChaosBubble(specA.Id));
        Assert.NotNull(engine.GetChaosBubble(specB.Id));

        // Resolve half A (instant defuse) → the mate B arms its resolve window.
        engine.PopBubble(specA.Id);
        var survivor = engine.GetChaosBubble(specB.Id);
        Assert.NotNull(survivor);
        Assert.True(survivor!.BoundHalfResolved);
        Assert.Equal(specB.BoundWindowMs, survivor.BoundResolveTimeRemainingMs);

        // Step until B's resolve window elapses and it enrages. Capture the pre-enrage fuse/Vx
        // on the tick that flips BoundEnraged so the halve/scale math is assertable.
        bool sawEnrage = false;
        double preFuse = 0;
        double preVx = 0;
        for (int i = 0; i < 60 && !sawEnrage; i++)
        {
            var b = engine.GetChaosBubble(specB.Id);
            if (b == null) break;
            if (!b.BoundEnraged)
            {
                preFuse = b.FuseRemainingMs;
                preVx = b.Vx;
            }
            engine.TickOnceForTesting();

            var b2 = engine.GetChaosBubble(specB.Id);
            if (b2 != null && b2.BoundEnraged && !sawEnrage)
            {
                sawEnrage = true;
                // STAYS ALIVE: not popped, still on the field.
                Assert.False(b2.IsPopping);
                Assert.False(b2.IsDetonated);
                Assert.NotNull(engine.GetChaosBubble(specB.Id));
                // ENRAGE math (WPF BubbleService.cs:2321-2335 Enrage()): drift ×1.4, fuse halved.
                Assert.True(Math.Abs(b2.Vx - preVx * ChaosTuning.BOUND_ENRAGE_SPEED_MULT) < 1e-6,
                    $"Vx {b2.Vx} should be pre-enrage {preVx} × {ChaosTuning.BOUND_ENRAGE_SPEED_MULT}");
                Assert.InRange(b2.FuseRemainingMs, preFuse * 0.5 - 30.0, preFuse * 0.5);
                Assert.True(b2.FuseRemainingMs >= 600.0, "fuse floor of 600ms");
            }
        }

        Assert.True(sawEnrage, "survivor never enraged");
        // OnBoundEnraged fired exactly once; OnDetonate did NOT fire for the survivor.
        Assert.Single(onBoundEnraged);
        Assert.DoesNotContain(onDetonate, s => s.Id == specB.Id);

        engine.EndChaosMode();
    }

    // ============================ S4b-2: treat rot fires OnTreatExpired for the WPF treat set ============================

    [AvaloniaFact]
    public void S4b2_TreatRot_FiresOnTreatExpired_ForOrdinaryAndGolden_NotHeartDroplet_TeaseFiresOnTeaseDenied()
    {
        var engine = NewEngine();
        var onTreatExpired = new List<ChaosBubbleSpec>();
        var onTeaseDenied = new List<ChaosBubbleSpec>();
        engine.BeginChaosMode(
            onBenignPop: _ => { },
            onDefuse: (_, _, _) => { },
            onDetonate: _ => { },
            onTeaseDenied: s => onTeaseDenied.Add(s),
            onTreatExpired: s => onTreatExpired.Add(s));

        // WPF _isTreat set (BubbleService.cs:2516): ordinary treats + golden + prism rot and fire
        // OnTreatExpired; heart/droplet/escort/tease/brittle do NOT (they never rot, or run their
        // own expiry). Ordinary treats and golden here; tease routes to OnTeaseDenied instead.
        var flash = Treat("flash");
        var golden = Treat("golden", isGolden: true);
        var heart = Treat("heart", isHeart: true);
        var droplet = Treat("droplet", isDroplet: true);
        var tease = Treat("tease", isTease: true);

        engine.SpawnChaosBubble(flash);
        engine.SpawnChaosBubble(golden);
        engine.SpawnChaosBubble(heart);
        engine.SpawnChaosBubble(droplet);
        engine.SpawnChaosBubble(tease);
        engine.TickOnceForTesting();   // materialize

        // TreatLifeMs=100ms (tease uses LifetimeMs=100ms) → rots in ~4 ticks. Step well past.
        for (int i = 0; i < 30; i++)
            engine.TickOnceForTesting();

        // Ordinary flash + golden rot → OnTreatExpired.
        Assert.Contains(onTreatExpired, s => s.Id == flash.Id);
        Assert.Contains(onTreatExpired, s => s.Id == golden.Id);
        // Heart & droplet never rot (WPF kindness pickups) → no OnTreatExpired.
        Assert.DoesNotContain(onTreatExpired, s => s.Id == heart.Id);
        Assert.DoesNotContain(onTreatExpired, s => s.Id == droplet.Id);
        // Tease runs its own expiry → OnTeaseDenied, NOT OnTreatExpired.
        Assert.DoesNotContain(onTreatExpired, s => s.Id == tease.Id);
        Assert.Contains(onTeaseDenied, s => s.Id == tease.Id);

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void S4b2_IsRottingTreat_MirrorsWpfIsTreatSet()
    {
        // Direct mirror of WPF BubbleService.cs:2516 _isTreat: ordinary/golden/prism rot;
        // live/darter/freeze/heart/droplet/escort/tease/brittle do not.
        Assert.True(EngineIsRottingTreat(Treat("flash")));
        Assert.True(EngineIsRottingTreat(Treat("golden", isGolden: true)));
        Assert.True(EngineIsRottingTreat(Treat("prism", isPrism: true)));
        Assert.False(EngineIsRottingTreat(BoundHalf("live")));          // IsLive
        Assert.False(EngineIsRottingTreat(Treat("dart", isDarter: true)));
        Assert.False(EngineIsRottingTreat(Treat("freeze", isFreeze: true)));
        Assert.False(EngineIsRottingTreat(Treat("heart", isHeart: true)));
        Assert.False(EngineIsRottingTreat(Treat("droplet", isDroplet: true)));
        Assert.False(EngineIsRottingTreat(Treat("escort", isEscort: true)));
        Assert.False(EngineIsRottingTreat(Treat("tease", isTease: true)));
        Assert.False(EngineIsRottingTreat(Treat("brittle", isBrittle: true)));
    }

    // ============================ S4b-3: darter first-spank lesson hook ============================

    [AvaloniaFact]
    public void S4b3_SpankerOn_DarterIsSpankedNotCaught_HookFiresOnceOnly()
    {
        // WPF BubbleService.cs:3706-3708: Spanker on ⇒ a darter pointer-down SMACKS (no catch);
        // the rabbit_caller hook fires on the FIRST smack only (:3789 `if (!_isSpanked)`).
        var engine = NewEngine();
        var onDarterSpanked = new List<(ChaosBubbleSpec Spec, bool Quick)>();
        engine.BeginChaosMode(
            onBenignPop: _ => { },
            onDefuse: (_, _, _) => { },
            onDetonate: _ => { },
            onDarterSpanked: (s, q) => onDarterSpanked.Add((s, q)));
        engine.Knobs.SpankerOn = true;

        var darter = MakeDarter();
        engine.SpawnChaosBubble(darter);
        engine.TickOnceForTesting();   // materialize at (500,500)

        var bubble = engine.GetChaosBubble(darter.Id);
        Assert.NotNull(bubble);
        Assert.False(bubble!.IsSpanked);

        var center = new Point((bubble.X + bubble.Size / 2.0) * bubble.Scaling,
                               (bubble.Y + bubble.Size / 2.0) * bubble.Scaling);

        // First pointer-down: hook fires once, latch sets, darter SURVIVES (never caught).
        Assert.True(engine.OnSharedHostLeftDown(center));
        Assert.Single(onDarterSpanked);
        Assert.True(bubble.IsSpanked);
        Assert.False(bubble.IsPopping);

        // Re-smack: consumed, but the hook does NOT re-fire and the darter still survives.
        Assert.True(engine.OnSharedHostLeftDown(center));
        Assert.Single(onDarterSpanked);
        Assert.False(bubble.IsPopping);

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void S4b3_SpankerOff_DarterIsCaught_HookNeverFires()
    {
        // Without the Spanker (the Avalonia default until the toy is ported), a darter
        // pointer-down is a CATCH — the spank hook must NOT fire, otherwise the
        // rabbit_caller lesson double-ticks per rabbit (catch + spank on the same click).
        var engine = NewEngine();
        var onDarterSpanked = new List<(ChaosBubbleSpec Spec, bool Quick)>();
        engine.BeginChaosMode(
            onBenignPop: _ => { },
            onDefuse: (_, _, _) => { },
            onDetonate: _ => { },
            onDarterSpanked: (s, q) => onDarterSpanked.Add((s, q)));
        Assert.False(engine.Knobs.SpankerOn); // WPF no-upgrade default

        var darter = MakeDarter();
        engine.SpawnChaosBubble(darter);
        engine.TickOnceForTesting();

        var bubble = engine.GetChaosBubble(darter.Id);
        Assert.NotNull(bubble);
        var center = new Point((bubble!.X + bubble.Size / 2.0) * bubble.Scaling,
                               (bubble.Y + bubble.Size / 2.0) * bubble.Scaling);

        Assert.True(engine.OnSharedHostLeftDown(center));
        Assert.Empty(onDarterSpanked);   // no spank on a catch
        Assert.True(bubble.IsPopping);   // the catch popped it
        Assert.False(bubble.IsSpanked);

        engine.EndChaosMode();
    }

    [AvaloniaFact]
    public void S4b3_Sweeper_BornSpanked_NeverCaught_HookNeverFires()
    {
        // WPF BubbleService.cs:3707-3708 + :3787: GG sweepers are NEVER catchable and are
        // born spanked, so a pointer-down re-smacks them without ever firing the lesson hook.
        var engine = NewEngine();
        var onDarterSpanked = new List<(ChaosBubbleSpec Spec, bool Quick)>();
        engine.BeginChaosMode(
            onBenignPop: _ => { },
            onDefuse: (_, _, _) => { },
            onDetonate: _ => { },
            onDarterSpanked: (s, q) => onDarterSpanked.Add((s, q)));

        var sweeper = MakeDarter(isSweeper: true);
        engine.SpawnChaosBubble(sweeper);
        engine.TickOnceForTesting();

        var bubble = engine.GetChaosBubble(sweeper.Id);
        Assert.NotNull(bubble);
        Assert.True(bubble!.IsSpanked); // born spanked

        var center = new Point((bubble.X + bubble.Size / 2.0) * bubble.Scaling,
                               (bubble.Y + bubble.Size / 2.0) * bubble.Scaling);

        Assert.True(engine.OnSharedHostLeftDown(center));
        Assert.Empty(onDarterSpanked);   // latch pre-set ⇒ hook never fires
        Assert.False(bubble.IsPopping);  // never caught

        engine.EndChaosMode();
    }

    private static ChaosBubbleSpec MakeDarter(bool isSweeper = false) => new()
    {
        VariantId = "darter",
        IsDarter = true,
        IsSweeper = isSweeper,
        SizePx = 80,
        Motion = ChaosMotion.RoamBounce,
        SpawnAtPxX = 500,
        SpawnAtPxY = 500,
        TelegraphMs = 0,
        DarterSpeed = 0,          // → DefaultDarterSpeed
        LifetimeMs = 20000,
        QuickWindowMs = 600,
    };

    // ============================ helpers ============================

    private static BubbleEngine NewEngine()
    {
        var screen = new ScreenInfo("test",
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            1.0);
        return new BubbleEngine(
            new FakeScreenProvider(screen),
            new FakeSettingsService(),
            new FakeBubbleRenderer(),
            new FakePointerState());
    }

    private static ChaosBubbleSpec BoundHalf(string variant) => new()
    {
        VariantId = variant,
        IsLive = true,
        IsBoundHalf = true,
        FuseMs = 6000,
        BoundWindowMs = 200,        // tiny window → enrages in ~8 ticks
        Motion = ChaosMotion.FloatUp,
        LifetimeMs = 20000,         // won't expire mid-test
        SizePx = 100,
        SpeedMult = 1.0,
    };

    private static ChaosBubbleSpec Treat(string variant,
        bool isGolden = false, bool isHeart = false, bool isDroplet = false,
        bool isTease = false, bool isPrism = false, bool isDarter = false,
        bool isFreeze = false, bool isEscort = false, bool isBrittle = false, bool isLive = false) => new()
    {
        VariantId = variant,
        PayloadKind = "Flash",
        SizePx = 80,
        Motion = ChaosMotion.FloatUp,
        // Treats rot at 100ms; tease uses LifetimeMs (its own expiry clock space).
        TreatLifeMs = isTease ? 0 : 100,
        LifetimeMs = isTease ? 100 : 8000,
        IsGolden = isGolden,
        IsHeart = isHeart,
        IsDroplet = isDroplet,
        IsTease = isTease,
        IsPrism = isPrism,
        IsDarter = isDarter,
        IsFreeze = isFreeze,
        IsEscort = isEscort,
        IsBrittle = isBrittle,
        IsLive = isLive,
    };

    // IsRottingTreat is private on the engine; reach it via a throwaway instance so the test
    // stays coupled to the engine's own definition (not a re-declaration of the WPF set).
    private static bool EngineIsRottingTreat(ChaosBubbleSpec spec)
    {
        // The helper is a pure static; invoke it through reflection on the engine type so a
        // future rename is caught at test time.
        var mi = typeof(BubbleEngine).GetMethod("IsRottingTreat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(mi);
        return (bool)mi!.Invoke(null, new object[] { spec })!;
    }

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
        // Pop invokes onComplete synchronously so popped bubbles leave _bubbles immediately
        // (matches the production pop→remove contract the engine relies on).
        public void Pop(BubbleState state, Action onComplete) => onComplete();
        public void Destroy(Guid id) { }
        public void SetLabel(Guid id, string label) { }
        public void SetFuse(Guid id, double fraction) { }
    }

    private sealed class FakePointerState : IPointerState
    {
        public System.Drawing.Point? GetCursorPosition() => null;
        public bool IsMouseButtonPressed(MouseButton button) => false;
    }
}
