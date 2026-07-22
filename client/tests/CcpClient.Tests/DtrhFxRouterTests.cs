using Avalonia.Media.Imaging;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-025 slice b3: the protocol upgrade — every b3-owned message dispatches to the REAL
/// effect seam (recording fakes, never the real backends); ordering + idempotency; bark +
/// b5 deferrals unchanged. SP-026 slice b4: the b4 messages are now Handled; the
/// run-boundary hygiene rides the SAME router entry, invoked FIRST inside the real
/// run-started/run-ended handlers (WPF :513/:259 order).
/// </summary>
public sealed class DtrhFxRouterTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _log = [];

    public DtrhFxRouterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dtrh-router-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sfx"));
        Directory.CreateDirectory(Path.Combine(_root, "voices"));
        Directory.CreateDirectory(Path.Combine(_root, "videos"));
        File.WriteAllBytes(Path.Combine(_root, "sfx", "Pop.mp3"), [1]);
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private (DtrhFxRouter router, DtrhNativeEffects fx, FakeAudio audio, FakeVideo video) Make()
    {
        var audio = new FakeAudio();
        var video = new FakeVideo();
        var fx = new DtrhNativeEffects(audio, video, new DtrhNativeEffectsOptions
        {
            SfxRoots = [Path.Combine(_root, "sfx")],
            VideoRoots = [Path.Combine(_root, "videos")],
            WhisperRoots = [Path.Combine(_root, "voices")],
        }, _log.Add);
        return (new DtrhFxRouter(fx, _log.Add), fx, audio, video);
    }

    private static DtrhProtocol.DtrhPageMessage Parse(string json) =>
        Assert.IsType<DtrhProtocol.DtrhPageParseResult.Parsed>(DtrhProtocol.ParsePageMessage(json)).Message;

    [Fact]
    public void B3Messages_Classify_Handled()
    {
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(DtrhProtocol.Classify(Parse("{\"type\":\"sfx\",\"name\":\"wave_clear\"}")));
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(DtrhProtocol.Classify(Parse("{\"type\":\"fire-payload\",\"kind\":\"video\"}")));
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(DtrhProtocol.Classify(Parse("{\"type\":\"freeze-state\",\"on\":true}")));
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(DtrhProtocol.Classify(Parse("{\"type\":\"vn-speaking\",\"on\":true}")));
    }

    [Fact]
    public void Bark_Deferral_Unchanged_B4AndB5NowHandled()
    {
        var bark = Assert.IsType<DtrhProtocol.DtrhDispatchClass.Deferred>(
            DtrhProtocol.Classify(Parse("{\"type\":\"bark\",\"event\":\"wave-cleared\"}")));
        Assert.Equal("voice-arbitration (quips row)", bark.Slice);
        // SP-026: the b4 messages upgraded Deferred → Handled (real meta/payout/loom effects).
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(
            DtrhProtocol.Classify(Parse("{\"type\":\"run-started\",\"difficulty\":\"Gentle\"}")));
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(
            DtrhProtocol.Classify(Parse("{\"type\":\"meta-command\",\"op\":\"add-gold\"}")));
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(
            DtrhProtocol.Classify(Parse("{\"type\":\"run-ended\",\"score\":1,\"durationSec\":1}")));
        // SP-027: the b5 messages upgraded Deferred → Handled (exit flow + watchdog).
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(
            DtrhProtocol.Classify(Parse("{\"type\":\"pong\",\"t\":1}")));
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(
            DtrhProtocol.Classify(Parse("{\"type\":\"exit-done\"}")));
    }

    [Fact]
    public void EveryUpgradedMessage_DispatchesToTheRealSeam()
    {
        var (router, fx, audio, video) = Make();

        router.Handle(Parse("{\"type\":\"sfx\",\"name\":\"Pop\",\"scale\":0.6}"));
        Assert.Single(audio.Players);

        router.Handle(Parse("{\"type\":\"vn-speaking\",\"on\":true}"));
        Assert.True(fx.VnSpeaking);

        router.Handle(Parse("{\"type\":\"freeze-state\",\"on\":true}"));
        Assert.True(fx.WorldFrozen);
        Assert.Equal([true], video.PauseCalls);

        router.Handle(Parse("{\"type\":\"freeze-state\",\"on\":false}"));
        Assert.Equal([true, false], video.PauseCalls);

        File.WriteAllBytes(Path.Combine(_root, "videos", "clip.mp4"), [1]);
        router.Handle(Parse("{\"type\":\"fire-payload\",\"kind\":\"video\",\"strength\":60}"));
        Assert.Single(video.Played);

        // Ordering: VN gate engages before a later sfx → the cue stands down.
        router.Handle(Parse("{\"type\":\"vn-speaking\",\"on\":true}"));
        router.Handle(Parse("{\"type\":\"sfx\",\"name\":\"Pop\",\"scale\":0.6}"));
        Assert.Single(audio.Players); // still 1 — gated, not queued
    }

    [Fact]
    public void RunBoundaryHygiene_RidesTheRealHandler()
    {
        var (router, fx, _, video) = Make();
        fx.SetVnSpeaking(true);
        fx.SetWorldFrozen(true);

        var msg = Parse("{\"type\":\"run-started\",\"difficulty\":\"Gentle\"}");
        Assert.True(router.TryRunBoundaryHygiene(msg));
        Assert.False(fx.WorldFrozen);
        Assert.False(fx.VnSpeaking);
        Assert.Equal([true, false], video.PauseCalls);
        // …and b4 (SP-026) the message itself is Handled — the SAME hygiene entry now
        // runs FIRST inside the real run-started/run-ended handlers (WPF :513/:259 order).
        Assert.IsType<DtrhProtocol.DtrhDispatchClass.Handled>(DtrhProtocol.Classify(msg));

        // run-ended rides too; unrelated messages never trigger the hygiene.
        fx.SetWorldFrozen(true);
        Assert.True(router.TryRunBoundaryHygiene(Parse("{\"type\":\"run-ended\",\"score\":1,\"durationSec\":1}")));
        Assert.False(fx.WorldFrozen);
        Assert.False(router.TryRunBoundaryHygiene(Parse("{\"type\":\"heartbeat\",\"t\":1}")));
    }

    [Fact]
    public void Router_Ignores_NonB3Messages()
    {
        var (router, _, audio, video) = Make();
        router.Handle(Parse("{\"type\":\"heartbeat\",\"t\":1}"));
        Assert.Empty(audio.Players);
        Assert.Empty(video.PauseCalls);
        Assert.Contains(_log, l => l.Contains("non-b3 message"));
    }

    // ---------- fakes (minimal — the effects-core suite covers the deep semantics) ----------

    private sealed class FakeAudio : IDtrhAudioBackend
    {
        public List<FakePlayer> Players { get; } = [];
        public bool TryInit(string? deviceName, out string? error) { error = null; return true; }
        public IDtrhAudioPlayer CreatePlayer(string path, float volume)
        {
            var p = new FakePlayer();
            Players.Add(p);
            return p;
        }

        public void Dispose() { }
    }

    private sealed class FakePlayer : IDtrhAudioPlayer
    {
        public event EventHandler? PlaybackEnded;
        public DtrhPlayerState State { get; set; } = DtrhPlayerState.Playing;
        public double PositionSec => 0;
        public void Play() { }
        public void Pause() => State = DtrhPlayerState.Paused;
        public void Stop() => State = DtrhPlayerState.Stopped;
        public void Dispose() { }
        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeVideo : IDtrhVideoBackend
    {
        public List<string> Played { get; } = [];
        public List<bool> PauseCalls { get; } = [];
        public long FrameCount => 0;
        public double PositionSec => 0;
        public WriteableBitmap? CurrentFrame => null;
#pragma warning disable CS0067 // the effects subscribes through the interface; this suite never raises
        public event EventHandler? FramePresented;
        public event EventHandler? PlaybackEnded;
        public event EventHandler? EncounteredError;
#pragma warning restore CS0067
        public bool TryPlay(string path) { Played.Add(path); return true; }
        public void SetPaused(bool paused) => PauseCalls.Add(paused);
        public void Stop() { }
    }
}
