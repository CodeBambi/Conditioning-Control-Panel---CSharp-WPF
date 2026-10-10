using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Deeper rule engine and its host in Core (WPF EnhancementEngine /
/// EnhancementHostService): timeline fires, band lifecycle, stop cleanup.</summary>
public class EnhancementEngineTests
{
    private sealed class FakeSource : IPlaybackTimeSource
    {
        public event Action<double>? PlaybackTimeChanged;
        public double Now, Duration = 60;
        public bool IsPlaying { get; set; } = true;
        public List<double> Seeks = new();
        public int Pauses;
        public double GetCurrentTimeSeconds() => Now;
        public double GetDurationSeconds() => Duration;
        public void Seek(double seconds) => Seeks.Add(seconds);
        public void Pause() => Pauses++;
        public void Play() { }
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
        public void Tick(double t) { Now = t; PlaybackTimeChanged?.Invoke(t); }
        public bool HasListeners => PlaybackTimeChanged != null;
    }

    private sealed class Sink : IActionDispatcher, IEnhancementRunCleanup
    {
        public List<EnhancementAction> Fired = new();
        public int Resets, FlashStops, SubliminalStops;
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        { Fired.Add(action); return Task.CompletedTask; }
        public void ResetOverlayBands() => Resets++;
        public void StopOneShotFlashes() => FlashStops++;
        public void StopOneShotSubliminals() => SubliminalStops++;
    }

    private static Enhancement TimedRule(double at, EnhancementAction action) => new()
    {
        MediaType = MediaTypes.Audio,
        MediaSource = "clip.mp3",
        Rules = { new EnhancementRule { Trigger = new TimeReachedTrigger { Time = at }, Action = action, Enabled = true } },
    };

    [Fact]
    public void ATimeReachedRuleFiresOnceWhenThePlayheadPassesIt()
    {
        var src = new FakeSource();
        var sink = new Sink();
        using var engine = new EnhancementEngine(TimedRule(5, new PauseAction()), src, sink);
        engine.Start();
        src.Tick(1);
        src.Tick(4.9);
        Assert.Empty(sink.Fired);
        src.Tick(5.1);
        src.Tick(5.2);
        Assert.Single(sink.Fired);
        Assert.IsType<PauseAction>(sink.Fired[0]);
    }

    [Fact]
    public void AScrubFarAheadSkipsThePointEntriesItJumpedOver()
    {
        var src = new FakeSource();
        var sink = new Sink();
        using var engine = new EnhancementEngine(TimedRule(5, new PauseAction()), src, sink);
        engine.Start();
        src.Tick(1);
        src.Tick(30);
        Assert.Empty(sink.Fired);
        src.Tick(2);      // seek back re-arms it
        src.Tick(4);
        src.Tick(5.05);
        Assert.Single(sink.Fired);
    }

    [Fact]
    public void AnOverlayBandStartsOnEntryStopsOnExitAndIsFlushedByStop()
    {
        var enh = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "clip.mp3" };
        enh.TimelineItems.Add(new TimelineItem
        {
            Kind = TimelineItemKind.Effect, EffectType = EffectTypes.Overlay,
            EffectOverlayKind = OverlayKinds.BrainDrain, Start = 2, Duration = 3,
        });
        var src = new FakeSource();
        var sink = new Sink();
        var engine = new EnhancementEngine(enh, src, sink);
        engine.Start();
        src.Tick(1);
        Assert.Empty(sink.Fired);
        src.Tick(2.5);
        Assert.Equal(EffectPhase.Start, Assert.IsType<TriggerEffectAction>(Assert.Single(sink.Fired)).Phase);
        src.Tick(5.5);
        Assert.Equal(EffectPhase.Stop, ((TriggerEffectAction)sink.Fired[^1]).Phase);

        src.Tick(2.5);    // back inside: a new Start, then Stop() must close it
        var before = sink.Fired.Count;
        engine.Stop();
        Assert.Equal(EffectPhase.Stop, ((TriggerEffectAction)sink.Fired[^1]).Phase);
        Assert.Equal(before + 1, sink.Fired.Count);
        Assert.Equal(1, sink.Resets);
        Assert.False(src.HasListeners);
        Assert.Equal(0, sink.FlashStops);   // no flash went out, so nobody else's flash is retired
    }

    [Fact]
    public void StopRetiresOnlyTheOneShotLayersThisRunUsed()
    {
        var src = new FakeSource();
        var sink = new Sink();
        var engine = new EnhancementEngine(
            TimedRule(1, new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 2000 }), src, sink);
        engine.Start();
        src.Tick(0.5);
        src.Tick(1.1);
        engine.Stop();
        Assert.Equal(1, sink.FlashStops);
        Assert.Equal(0, sink.SubliminalStops);
    }

    [Fact]
    public void TheHostBindsItsFactoryDispatcherAndUnbindStopsTheEngine()
    {
        var prev = EnhancementHostService.DispatcherFactory;
        var sink = new Sink();
        EnhancementHostService.DispatcherFactory = () => sink;
        try
        {
            using var host = new EnhancementHostService();
            var lines = new List<string>();
            host.ActionLogged += lines.Add;
            var src = new FakeSource();
            Assert.False(host.Bind(src));   // nothing loaded
            Assert.True(host.LoadFromMemory(TimedRule(1, new PauseAction()), "memory"));
            Assert.True(host.Bind(src));
            Assert.True(host.IsRunning);
            src.Tick(0.5);
            src.Tick(1.2);
            Assert.IsType<PauseAction>(Assert.Single(sink.Fired));
            Assert.Contains(lines, l => l.Contains("pause"));
            host.UnbindEngine();
            Assert.False(host.IsRunning);
            Assert.False(src.HasListeners);
            host.Unload();
            Assert.Null(host.LoadedEnhancement);
        }
        finally { EnhancementHostService.DispatcherFactory = prev; }
    }
}
