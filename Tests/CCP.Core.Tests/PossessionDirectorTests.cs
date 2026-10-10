using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>HB13: the Core PossessionDirector over a fake host. The ladder follows the lockdown clock,
/// a pick books its victim and comes back on exit, panic brings everything back at once, tripwires
/// are throttled and never shake when photosafe, and nothing haunts with Possession switched off.</summary>
[Collection(SessionStatics.Name)]
public sealed class PossessionDirectorTests
{
    private sealed class FakeEffect : IPossessionEffect
    {
        public string Id { get; init; } = "breathe";
        public PossessionRung MinRung => PossessionRung.Settle;
        public PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
        public bool IsBig { get; init; }
        public bool UsesFlicker => false;
        public double Weight => 1;
        public TimeSpan HoldFor => TimeSpan.Zero;   // until exit
        public IReadOnlyList<PossessionRole> Roles { get; } = new[] { PossessionRole.Card };
        public bool IsLive { get; private set; }
        public int Applied;
        public readonly List<TimeSpan> Undone = new();
        public bool CanApply(PossessionContext ctx, PossessionTarget? target) => target != null;
        public Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            Applied++; IsLive = true;
            ctx.Name(Id, target?.DisplayName);
            return Task.CompletedTask;
        }
        public Task UndoAsync(TimeSpan duration) { Undone.Add(duration); IsLive = false; return Task.CompletedTask; }
    }

    private sealed class FakeScene : IPossessionScene
    {
        public string Id => "scene_fake";
        public PossessionRung MinRung => PossessionRung.Melt;
        public PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
        public bool IsBig => true;
        public bool UsesFlicker => false;
        public double Weight => 1;
        public TimeSpan HoldFor => TimeSpan.Zero;
        public IReadOnlyList<PossessionRole> Roles { get; } = Array.Empty<PossessionRole>();
        public int Beats { get; init; } = 2;
        public bool IsLive { get; private set; }
        public int Applied;
        public readonly List<PossessionRung> Rungs = new();
        public readonly List<TimeSpan> Undone = new();
        public bool CanApply(PossessionContext ctx, PossessionTarget? target) => target == null;
        public Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            Applied++; IsLive = true; Rungs.Add(ctx.Rung);
            return Task.CompletedTask;
        }
        public Task UndoAsync(TimeSpan duration) { Undone.Add(duration); IsLive = false; return Task.CompletedTask; }
    }

    [Fact]
    public void FromMeltAScenePlaysOnTheRoll_NeverBelowIt_NeverWhenItDoesNotFit_AndPanicEndsIt()
    {
        using var r = new Rig();
        var scene = new FakeScene();
        var wide = new FakeScene { Beats = 99 };       // can never fit the room
        r.Director.Scenes.Add(wide);
        r.Director.Scenes.Add(scene);
        r.Lockdown.Activate(TimeSpan.FromMinutes(20));

        for (double f = 0.09; f < 0.34; f += 0.01) r.TickAt(f);       // Settle and Drift: no scenes
        Assert.Equal(0, scene.Applied);

        for (double f = 0.36; f < 0.84 && scene.Applied == 0; f += 0.004) r.TickAt(f);
        Assert.Equal(1, scene.Applied);
        Assert.All(scene.Rungs, rung => Assert.True(rung >= PossessionRung.Melt));
        Assert.Equal(0, wide.Applied);
        Assert.True(r.Director.LiveEffectCount >= 1);

        r.Director.PanicStop();
        Assert.Equal(new[] { TimeSpan.Zero }, scene.Undone);          // a scene comes back like any ghost
        Assert.False(scene.IsLive);
        Assert.Equal(0, r.Director.LiveEffectCount);
    }

    [Fact]
    public void AReactiveAnswerNeedsARunningHaunt_IsThrottled_AndStaysQuietAfterAPanic()
    {
        using var r = new Rig();
        r.Director.RequestReactive("breathe", r.Card);          // no lockdown: nothing
        Assert.Equal(0, r.Effect.Applied);

        r.Lockdown.Activate(TimeSpan.FromMinutes(20));
        r.Director.RequestReactive("nosuch", r.Card);
        r.Director.RequestReactive("breathe", r.Card, PossessionRung.Collapse);   // the rung is not there yet
        Assert.Equal(0, r.Effect.Applied);

        r.Director.RequestReactive("breathe", r.Card);
        Assert.Equal(1, r.Effect.Applied);
        Assert.True(r.Card.IsLive);
        Assert.Equal(PossessionRung.Settle, r.Director.CurrentRung);              // an answer never climbs the ladder

        r.Director.PanicStop();
        Assert.False(r.Card.IsLive);
        r.Clock = r.Clock.AddSeconds(7);                        // past the throttle, inside the panic quiet
        r.Director.RequestReactive("breathe", r.Card);
        Assert.Equal(1, r.Effect.Applied);

        r.Lockdown.Deactivate();
        r.Director.RequestReactive("breathe", r.Card);          // the lockdown is over
        Assert.Equal(1, r.Effect.Applied);
    }

    private sealed class Rig : IDisposable
    {
        public readonly LockdownService Lockdown = new();
        public readonly PossessionDirector Director;
        public readonly FakeEffect Effect = new();
        public readonly PossessionTarget Card = new() { Element = new object(), Role = PossessionRole.Card, Key = "card", DisplayName = "the lockdown card" };
        public readonly List<double> Pulses = new();
        public readonly List<(double Amp, int Ms)> Shakes = new();
        public readonly List<(string Trigger, IReadOnlyDictionary<string, object>? Values)> Barks = new();
        public DateTime Clock = new(2026, 10, 10, 12, 0, 0);
        public bool Usable = true;

        private readonly ConditioningControlPanel.Models.AppSettings _s = CoreSettings.Current;
        private readonly (bool, int, bool, bool, bool, bool, bool, bool, bool) _saved;
        private readonly LockdownService? _prev = LockdownService.Current;
        private readonly Func<string, IReadOnlyDictionary<string, object>?, bool, bool>? _raise = CoreBark.RaiseProvider;

        public Rig(bool possession = true, int intensity = 1, bool photosafe = false, bool tripwires = true, bool big = false)
        {
            _saved = (_s.LockdownPossessionEnabled, _s.LockdownPossessionIntensity, _s.LockdownPhotosafe, _s.LockdownTripwiresEnabled,
                _s.LockdownForceStrictLock, _s.LockdownDisablePanicKey, _s.StrictLockEnabled, _s.PanicKeyEnabled, _s.LockdownDoseKeeperEnabled);
            (_s.LockdownPossessionEnabled, _s.LockdownPossessionIntensity, _s.LockdownPhotosafe, _s.LockdownTripwiresEnabled) = (possession, intensity, photosafe, tripwires);
            _s.LockdownForceStrictLock = _s.LockdownDisablePanicKey = _s.LockdownDoseKeeperEnabled = false;
            Effect = new FakeEffect { IsBig = big };
            CoreBark.RaiseProvider = (t, v, _) => { Barks.Add((t, v)); return true; };
            LockdownService.Current = Lockdown;
            Lockdown.UtcNow = () => Clock.ToUniversalTime();
            Director = new PossessionDirector(Lockdown, new IPossessionEffect[] { Effect }, new PossessionHost
            {
                Targets = () => new[] { Card },
                IsUsable = () => Usable,
                EdgePulse = Pulses.Add,
                Shake = (a, ms) => Shakes.Add((a, ms)),
            }, new Random(7)) { Now = () => Clock };
        }

        /// <summary>Move the clock to a fraction of a 20 minute lockdown and tick once.</summary>
        public void TickAt(double fraction)
        {
            Clock = new DateTime(2026, 10, 10, 12, 0, 0).AddMinutes(20 * fraction);
            Director.Tick(TimeSpan.FromMinutes(20 * (1 - fraction)));
        }

        public void Dispose()
        {
            if (Lockdown.IsActive) Lockdown.Deactivate();
            Director.Dispose();
            Lockdown.Dispose();
            LockdownService.Current = _prev;
            CoreBark.RaiseProvider = _raise;
            (_s.LockdownPossessionEnabled, _s.LockdownPossessionIntensity, _s.LockdownPhotosafe, _s.LockdownTripwiresEnabled,
                _s.LockdownForceStrictLock, _s.LockdownDisablePanicKey, _s.StrictLockEnabled, _s.PanicKeyEnabled, _s.LockdownDoseKeeperEnabled) = _saved;
        }
    }

    [Fact]
    public void TheLadderFollowsTheClock_PulsesAndBarksOncePerRung_AndIsCappedByIntensity()
    {
        using var r = new Rig(intensity: (int)PossessionIntensity.Gentle);
        var rungs = new List<PossessionRung>();
        r.Director.RungChanged += rungs.Add;
        r.Lockdown.Activate(TimeSpan.FromMinutes(20));
        Assert.True(r.Director.IsHaunting);
        Assert.Equal(PossessionRung.Settle, r.Director.CurrentRung);

        r.Usable = false;                 // the ladder climbs even when nothing may start
        r.TickAt(0.20);
        Assert.Equal(PossessionRung.Drift, r.Director.CurrentRung);
        r.TickAt(0.21);                   // same rung: nothing new
        r.TickAt(0.95);                   // Gentle caps at Melt
        Assert.Equal(PossessionRung.Melt, r.Director.CurrentRung);

        Assert.Equal(new[] { PossessionRung.Drift, PossessionRung.Melt }, rungs);
        Assert.Equal(new[] { 0.5, 0.65 }, r.Pulses.ConvertAll(p => Math.Round(p, 6)));   // 0.35 + 0.15 per rung
        Assert.Equal(2, r.Barks.FindAll(b => b.Trigger == PossessionBarkTriggers.RungChanged).Count);
        Assert.Equal(0, r.Effect.Applied);
    }

    [Fact]
    public void APickBooksItsVictim_NamesOnlyBigEffects_AndTheExitBringsItBack()
    {
        using var r = new Rig(big: true);
        r.Lockdown.Activate(TimeSpan.FromMinutes(20));

        r.Director.Tick(TimeSpan.FromMinutes(20));   // inside the first wait: the room settles first
        Assert.Equal(0, r.Effect.Applied);

        r.TickAt(0.09);                              // 108 s in: past every first delay
        Assert.Equal(1, r.Effect.Applied);
        Assert.True(r.Card.IsLive);
        Assert.Equal(1, r.Director.LiveEffectCount);
        var named = Assert.Single(r.Barks, b => b.Trigger == PossessionBarkTriggers.Effect);
        Assert.Equal("the lockdown card", named.Values!["target"]);

        r.Lockdown.Deactivate();                     // the reassembly exit
        Assert.False(r.Director.IsHaunting);
        Assert.False(r.Card.IsLive);
        Assert.Single(r.Effect.Undone);
        Assert.Equal(0, r.Director.LiveEffectCount);
        Assert.Equal(PossessionRung.Settle, r.Director.CurrentRung);
    }

    [Fact]
    public void PanicBringsEverythingBackAtOnce_AndTheRoomStaysQuietAfterIt()
    {
        using var r = new Rig();
        r.Lockdown.Activate(TimeSpan.FromMinutes(20));
        r.TickAt(0.09);
        Assert.Equal(1, r.Effect.Applied);

        r.Director.PanicStop();
        Assert.Equal(new[] { TimeSpan.Zero }, r.Effect.Undone);   // no animation: now
        Assert.False(r.Card.IsLive);
        Assert.Equal(0, r.Director.LiveEffectCount);

        r.Director.Tick(TimeSpan.FromMinutes(18));   // the same second: nothing twitches under the press
        Assert.Equal(1, r.Effect.Applied);

        r.Director.PanicStop();                      // safe twice
        Assert.Single(r.Effect.Undone);
    }

    [Fact]
    public void ATripwireIsThrottled_ScalesWithTheRepeat_AndNeverShakesWhenPhotosafe()
    {
        using (var r = new Rig())
        {
            r.Lockdown.Activate(TimeSpan.FromMinutes(20));
            r.Lockdown.NotifyEscapeAttempt(EscapeKinds.Stop);
            r.Lockdown.NotifyEscapeAttempt(EscapeKinds.Stop);     // inside 1.5 s: swallowed
            Assert.Equal(new[] { 0.5 }, r.Pulses);
            Assert.Empty(r.Shakes);

            r.Clock = r.Clock.AddSeconds(2);
            r.Lockdown.NotifyEscapeAttempt(EscapeKinds.Stop);     // the third report, a repeat
            Assert.Equal(new[] { 0.5, 0.8 }, r.Pulses);
            Assert.Equal((0.4, 250), Assert.Single(r.Shakes));
            Assert.Equal(2, r.Barks.FindAll(b => b.Trigger == PossessionBarkTriggers.Tripwire).Count);
        }
        using (var safe = new Rig(photosafe: true))
        {
            safe.Lockdown.Activate(TimeSpan.FromMinutes(20));
            safe.Lockdown.NotifyEscapeAttempt(EscapeKinds.Close);
            safe.Clock = safe.Clock.AddSeconds(2);
            safe.Lockdown.NotifyEscapeAttempt(EscapeKinds.Close);
            Assert.Equal(new[] { 0.5, 0.8 }, safe.Pulses);
            Assert.Empty(safe.Shakes);
        }
        using (var off = new Rig(tripwires: false))
        {
            off.Lockdown.Activate(TimeSpan.FromMinutes(20));
            off.Lockdown.NotifyEscapeAttempt(EscapeKinds.Close);
            Assert.Empty(off.Pulses);
        }
    }

    [Fact]
    public void WithPossessionOff_NothingHaunts_AndTheDosePulseIsSilent()
    {
        using (var off = new Rig(possession: false))
        {
            off.Lockdown.Activate(TimeSpan.FromMinutes(20));
            Assert.False(off.Director.IsHaunting);
            off.TickAt(0.5);
            off.Lockdown.NotifyEscapeAttempt(EscapeKinds.Close);
            off.Director.PulseEdges(0.8);
            Assert.Equal(PossessionRung.Settle, off.Director.CurrentRung);
            Assert.Equal(0, off.Effect.Applied);
            Assert.Empty(off.Pulses);
        }
        using var on = new Rig();
        on.Director.PulseEdges(0.8);                 // no lockdown: silent
        Assert.Empty(on.Pulses);
        on.Lockdown.Activate(TimeSpan.FromMinutes(20));
        on.Director.PulseEdges(5);                   // the Dose keeper's pulse, clamped
        Assert.Equal(new[] { 1.0 }, on.Pulses);
    }

    [Fact]
    public void ATimerRestartRewindsTheLadder_AndTheNextTickDoesNotAnnounceItAgain()
    {
        using var r = new Rig();
        r.Lockdown.Activate(TimeSpan.FromMinutes(20));
        r.TickAt(0.5);
        Assert.Equal(PossessionRung.Melt, r.Director.CurrentRung);
        r.Pulses.Clear(); r.Barks.Clear();

        r.Lockdown.RestartTimer("test");
        Assert.Equal(PossessionRung.Settle, r.Director.CurrentRung);
        Assert.Contains(0.6, r.Pulses);
        Assert.Single(r.Barks, b => b.Trigger == PossessionBarkTriggers.TimerRestarted);
        Assert.DoesNotContain(r.Barks, b => b.Trigger == PossessionBarkTriggers.RungChanged);
        Assert.Single(r.Director.AnnouncedRungs);
    }
}
