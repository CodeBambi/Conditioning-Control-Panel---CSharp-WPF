using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM effect dispatcher with the services behind a fake: the hero queue (Brake 2), the
/// 6 Hz ceiling across every recipe, cancel on suspend/close, and the symbol keys the page sends.
/// </summary>
public class BackRoomFxTests
{
    internal static readonly BackRoomMediaDeal Deal = new(7,
        new[]
        {
            new BackRoomGif("g0", "https://ccp.assets/images/a.gif", 480, 270, "pool"),
            new BackRoomGif("g1", "https://ccp.assets/images/b.gif", 480, 270, "pool"),
            new BackRoomGif("g2", "https://ccp.assets/images/c.gif", 480, 270, "pool"),
            new BackRoomGif("g3", "https://ccp.game/backroom/stations/slot/fallback/gif3.webp", 0, 0, "fallback"),
        },
        new[]
        {
            new BackRoomWord("s0", "Drop", "preset"), new BackRoomWord("s1", "Relax", "preset"),
            new BackRoomWord("s2", "Let Go", "preset"), new BackRoomWord("s3", "Sink", "preset"),
        });

    internal sealed class FakeScheduler : IFxScheduler
    {
        private readonly List<(long At, Action Run, Handle H)> _q = new();
        public long NowMs { get; private set; }

        internal sealed class Handle : IDisposable
        {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        public IDisposable After(int delayMs, Action action)
        {
            var h = new Handle();
            _q.Add((NowMs + delayMs, action, h));
            return h;
        }

        public void Advance(long ms)
        {
            long end = NowMs + ms;
            while (true)
            {
                var next = _q.Where(x => x.At <= end).OrderBy(x => x.At).FirstOrDefault();
                if (next.Run == null) break;
                _q.Remove(next);
                NowMs = Math.Max(NowMs, next.At);
                if (!next.H.Disposed) next.Run();
            }
            NowMs = end;
        }
    }

    internal sealed class RecordingSink : IBackRoomFxSink
    {
        private readonly FakeScheduler _clock;
        public readonly List<(long At, string Call)> Calls = new();
        public int Stops;
        public RecordingSink(FakeScheduler clock) => _clock = clock;
        private void Add(string c) => Calls.Add((_clock.NowMs, c));
        public void FlashBurst(int amount) => Add($"flash:{amount}");
        public void GifRain(int durationMs) => Add($"rain:{durationMs}");
        public void GlitchWash(int durationMs, double opacity) => Add($"glitch:{durationMs}");
        public void Subliminal(string text) => Add($"sub:{text}");
        public void Spiral(int durationMs, double level, bool still) => Add($"spiral:{durationMs}:{level}:{still}");
        public void BrainDrain(int durationMs, double level, bool melt) => Add($"drain:{durationMs}:{level}:{melt}");
        public void GifFull(BackRoomGif gif, int durationMs, bool still) => Add($"giffull:{gif.Key}:{durationMs}:{still}");
        public void StopAll() => Stops++;
    }

    private static (BackRoomFx Fx, FakeScheduler Clock, RecordingSink Sink) Make(
        BackRoomFxIntensity intensity = BackRoomFxIntensity.Normal, MotionLevel motion = MotionLevel.Full)
    {
        var clock = new FakeScheduler();
        var sink = new RecordingSink(clock);
        var fx = new BackRoomFx(sink, clock, () => new FxEnvironment(motion, intensity, FxGates.AllOn), new Random(5));
        return (fx, clock, sink);
    }

    private static BackRoomFxAck Fire(BackRoomFx fx, string id, params string[] symbols)
        => fx.Fire(id, "slot", symbols, Deal);

    // ---- hero gate -------------------------------------------------------------------------------

    [Fact]
    public void Gate_DuringAHero_OthersQueueUntilItEnds()
    {
        long now = 0;
        var gate = new FxHeroGate(() => now);
        gate.Admit("fx.jackpot", 2400);
        now = 400;
        Assert.Equal(new FxAdmission(FxAdmitKind.Queued, 2000), gate.Admit("fx.gif_storm", 0));
        Assert.Equal(new FxAdmission(FxAdmitKind.Queued, 2000), gate.Admit("fx.melt", 0));   // both start as the stage frees
        now = 2400;
        Assert.Equal(FxAdmitKind.Now, gate.Admit("fx.gif_burst", 0).Kind);
    }

    [Fact]
    public void Gate_SameId_Merges_RunningOrQueued()
    {
        long now = 0;
        var gate = new FxHeroGate(() => now);
        gate.Admit("fx.jackpot", 4000);
        now = 100;
        Assert.Equal(FxAdmitKind.Merged, gate.Admit("fx.jackpot", 4000).Kind);
        Assert.Equal(FxAdmitKind.Queued, gate.Admit("fx.sub_single", 0).Kind);
        Assert.Equal(FxAdmitKind.Merged, gate.Admit("fx.sub_single", 0).Kind);
    }

    [Fact]
    public void Gate_HeroesChain_AndAnythingWaitingOverFourSecondsIsBusy()
    {
        long now = 0;
        var gate = new FxHeroGate(() => now);
        gate.Admit("fx.jackpot", 4000);
        now = 500;
        Assert.Equal(new FxAdmission(FxAdmitKind.Queued, 3500), gate.Admit("fx.hero_b", 3000));   // opens 4000..7000
        Assert.Equal(FxAdmitKind.Busy, gate.Admit("fx.gif_storm", 0).Kind);                        // would wait 6500
        now = 3100;
        Assert.Equal(new FxAdmission(FxAdmitKind.Queued, 3900), gate.Admit("fx.gif_storm", 0));
    }

    // ---- dispatcher --------------------------------------------------------------------------------

    [Fact]
    public void JackpotNormal_SpiralFirst_ThenTheRestAtTheHeroEdge()
    {
        var (fx, clock, sink) = Make();
        var ack = Fire(fx, "fx.jackpot");
        Assert.Equal(new[] { "spiral-full", "flash-burst", "gif-rain", "glitch-bubbles", "sub-burst9" }, ack.Fired);
        clock.Advance(10_000);
        Assert.Equal((0L, "spiral:2400:1:False"), sink.Calls[0]);
        Assert.Contains((2400L, "flash:4"), sink.Calls);
        Assert.Contains((2400L, "rain:2000"), sink.Calls);
        Assert.Equal(9, sink.Calls.Count(c => c.Call.StartsWith("sub:")));
    }

    [Fact]
    public void QueuedBehindAHero_PlaysWhenTheWindowCloses()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.jackpot");
        clock.Advance(1000);
        var ack = Fire(fx, "fx.melt");
        Assert.Equal(new[] { "brain-drain-melt" }, ack.Fired);
        clock.Advance(10_000);
        Assert.Contains((2400L, "drain:6000:1:True"), sink.Calls);
    }

    [Fact]
    public void BusyFx_AcksEveryPrimitiveAsBusy_AndSchedulesNothing()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.jackpot");                       // hero 0..2400
        fx.Gate.Admit("fx.future_hero", 3000);        // a later station's hero chains to 2400..5400
        clock.Advance(100);
        int pending = fx.PendingCount;
        var ack = Fire(fx, "fx.gif_storm");           // would wait 5300 ms
        Assert.Empty(ack.Fired);
        Assert.Equal(new[] { "flash-burst", "gif-rain", "glitch-bubbles" }, ack.Skipped.Select(s => s.Prim));
        Assert.All(ack.Skipped, s => Assert.Equal(BackRoomFxSkipReason.Busy, s.Why));
        Assert.Equal(pending, fx.PendingCount);
    }

    [Fact]
    public void CancelAll_SilencesEverythingWaiting_AndStopsTheSink()
    {
        var (fx, clock, sink) = Make(BackRoomFxIntensity.Full);
        Fire(fx, "fx.jackpot");
        clock.Advance(100);
        Assert.True(fx.PendingCount > 0);
        fx.CancelAll();
        Assert.Equal(0, fx.PendingCount);
        Assert.Equal(1, sink.Stops);
        var callsAtCancel = sink.Calls.Count;
        clock.Advance(20_000);
        Assert.Equal(callsAtCancel, sink.Calls.Count);
        // and the stage is free again
        Assert.NotEmpty(Fire(fx, "fx.gif_burst").Fired);
        clock.Advance(10);
        Assert.Contains(sink.Calls, c => c.Call == "flash:3");
    }

    // ---- 6 Hz -------------------------------------------------------------------------------------

    public static IEnumerable<object[]> Combos() => BackRoomFxPlanTests.AllCombos();

    private static void AssertUnderSixHz(RecordingSink sink, string label)
    {
        foreach (var family in new[] { "sub:", "glitch:", "flash:" })
        {
            var onsets = sink.Calls.Where(c => c.Call.StartsWith(family)).Select(c => c.At).OrderBy(t => t).ToList();
            for (int i = 1; i < onsets.Count; i++)
                Assert.True(onsets[i] - onsets[i - 1] >= 1000 / 6.0, $"{label}: {family} onsets {onsets[i - 1]} and {onsets[i]} strobe over 6 Hz");
            for (int i = 0; i + 6 < onsets.Count; i++)
                Assert.True(onsets[i + 6] - onsets[i] >= 1000, $"{label}: more than 6 {family} onsets inside one second");
        }
    }

    [Theory]
    [MemberData(nameof(Combos))]
    public void NoRecipe_StrobesOverSixHz(string id, BackRoomFxIntensity i, MotionLevel m)
    {
        var (fx, clock, sink) = Make(i, m);
        Fire(fx, id, "sub0", "sub1", "sub2", "sub3", "gif0");
        clock.Advance(30_000);
        AssertUnderSixHz(sink, $"{id} {i} {m}");
    }

    [Fact]
    public void StackedFx_OnOneSpin_StayUnderSixHz_Together()
    {
        // One outcome can carry fx.sub_cascade plus fx.sub_single, and the next spin lands 800 ms on.
        var (fx, clock, sink) = Make(BackRoomFxIntensity.Full);
        Fire(fx, "fx.sub_cascade", "sub0", "sub1", "sub2");
        Fire(fx, "fx.sub_single", "sub0", "sub1", "sub2");
        Fire(fx, "fx.sub_pair", "sub3");
        Fire(fx, "fx.gif_storm");
        Fire(fx, "fx.gif_burst");
        clock.Advance(800);
        Fire(fx, "fx.sub_single", "sub3");
        Fire(fx, "fx.gif_storm");
        clock.Advance(30_000);
        AssertUnderSixHz(sink, "stacked");
    }

    // ---- symbols ------------------------------------------------------------------------------------

    [Fact]
    public void Symbols_ResolveOnlyThroughTheDeal()
    {
        var media = BackRoomFxPlan.ResolveSymbols(new[] { "sub3", "gif1", "s1", "g2", "spiral0", "emi3", "melt" }, Deal, new Random(3));
        Assert.Equal(new[] { "Sink", "Relax" }, media.Words);
        Assert.Equal(new[] { "g1", "g2" }, media.Gifs.Select(g => g.Key));
    }

    [Theory]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("https://evil.example/x.gif")]
    [InlineData("../../secrets.txt")]
    [InlineData("sub9")]
    [InlineData("gif7")]
    [InlineData("<b>BOLD</b>")]
    public void UnknownKeys_BecomeARandomDealtItem_NeverThePagesText(string hostile)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var media = BackRoomFxPlan.ResolveSymbols(new[] { hostile }, Deal, new Random(seed));
            Assert.Equal(1, media.Words.Count + media.Gifs.Count);
            Assert.All(media.Words, w => Assert.Contains(Deal.Words, d => d.Text == w));
            Assert.All(media.Gifs, g => Assert.Contains(g, Deal.Gifs));
        }
    }

    [Fact]
    public void GifFull_UsesTheNamedGif_ElseADealtOne()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.sub_cascade", "gif2");
        clock.Advance(5000);
        Assert.Contains(sink.Calls, c => c.Call == "giffull:g2:1500:False");
    }

    [Fact]
    public void EmptyDeal_SkipsMediaPrimitives_AsUnknown_WithoutThrowing()
    {
        var p = BackRoomFxPlan.Resolve("fx.sub_cascade", BackRoomFxIntensity.Normal, MotionLevel.Full, FxGates.AllOn,
            new[] { "sub0" }, new BackRoomMediaDeal(0, Array.Empty<BackRoomGif>(), Array.Empty<BackRoomWord>()), new Random(0));
        Assert.Empty(p.Fired);
        Assert.Contains(new BackRoomFxSkip("sub-burst9", BackRoomFxSkipReason.Unknown), p.Skipped);
        Assert.Contains(new BackRoomFxSkip("gif-full", BackRoomFxSkipReason.Unknown), p.Skipped);
    }

    [Fact]
    public void NullInputs_NeverThrow()
    {
        var (fx, _, _) = Make();
        var ack = fx.Fire(null!, "slot", null!, null!);
        Assert.Empty(ack.Fired);
        Assert.Single(ack.Skipped);
    }
}
