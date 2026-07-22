using Avalonia.Media.Imaging;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-025 slice b3: native effects core — SFX pool bounds/drop-on-overflow, resolution
/// chains, VN mix gate, voice stop-replace + generation token, freeze idempotency +
/// run-boundary/teardown unwedge invariants, fire-payload video/whisper outcomes. All
/// against RECORDING FAKES — never the real SoundFlow/libvlc backends (packet Step 3).
/// WPF parity cites per test (SP-025 record Step 1 archaeology).
/// </summary>
public sealed class DtrhNativeEffectsTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _log = [];

    public DtrhNativeEffectsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dtrh-fx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sfx"));
        Directory.CreateDirectory(Path.Combine(_root, "voices"));
        Directory.CreateDirectory(Path.Combine(_root, "videos"));
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private string TouchSfx(string name) { var p = Path.Combine(_root, "sfx", name); File.WriteAllBytes(p, [1]); return p; }

    private string TouchVoice(string name) { var p = Path.Combine(_root, "voices", name); File.WriteAllBytes(p, [1]); return p; }

    private string TouchVideo(string name) { var p = Path.Combine(_root, "videos", name); File.WriteAllBytes(p, [1]); return p; }

    private (DtrhNativeEffects fx, FakeAudio audio, FakeVideo video) Make(int maxSfx = 8, double capSec = 15)
    {
        var audio = new FakeAudio();
        var video = new FakeVideo();
        var fx = new DtrhNativeEffects(audio, video, new DtrhNativeEffectsOptions
        {
            SfxRoots = [Path.Combine(_root, "sfx")],
            VideoRoots = [Path.Combine(_root, "videos")],
            WhisperRoots = [Path.Combine(_root, "voices")],
            MasterVolume = 80,
            MaxSfxVoices = maxSfx,
            VideoSegmentCapSec = capSec,
        }, _log.Add);
        return (fx, audio, video);
    }

    // ---------- SFX pool ----------

    [Fact]
    public void Sfx_PoolBounded_DropOnOverflow()
    {
        TouchSfx("Pop.mp3");
        var (fx, audio, _) = Make(maxSfx: 2);

        fx.PlaySfx("Pop", 0.6);
        fx.PlaySfx("Pop", 0.6);
        fx.PlaySfx("Pop", 0.6); // overflow → dropped, never queued (ChaosSfx.cs:91-107 parity)

        Assert.Equal(2, audio.Players.Count);
        Assert.Equal(2, fx.ActiveSfxVoices);
        Assert.Contains(_log, l => l.Contains("pool full (2)") && l.Contains("dropping"));
    }

    [Fact]
    public void Sfx_PoolReclaims_OnRealPlaybackEnded()
    {
        TouchSfx("Pop.mp3");
        var (fx, audio, _) = Make(maxSfx: 1);

        fx.PlaySfx("Pop", 0.6);
        fx.PlaySfx("Pop", 0.6); // dropped
        Assert.Single(audio.Players);

        audio.Players[0].RaiseEnded(); // backend completion event reclaims the slot
        Assert.Equal(0, fx.ActiveSfxVoices);
        Assert.True(audio.Players[0].Disposed);

        fx.PlaySfx("Pop", 0.6);
        Assert.Equal(2, audio.Players.Count);
    }

    [Fact]
    public void Sfx_SpecialCases_AndGenericResolution()
    {
        var chime = TouchSfx("chime1.mp3");
        var pop2 = TouchSfx("Pop2.mp3");
        var pop = TouchSfx("Pop.mp3");
        var (fx, audio, _) = Make();

        fx.PlaySfx("wave_clear", 0.3);   // chain wave_clear.mp3 → chime1.mp3 @0.8 (scale ignored)
        fx.PlaySfx("ripple_cast", 0.3);  // chain ripple_cast.mp3 → Pop2.mp3 @0.6
        fx.PlaySfx("pop", 0.5);          // generic, case-insensitive file match (Linux honest)

        Assert.Equal(chime, audio.Players[0].Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(pop2, audio.Players[1].Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(pop, audio.Players[2].Path, StringComparer.OrdinalIgnoreCase);
        // volume = master(0.80) × scale clamped (ChaosSfx.cs:96-103)
        Assert.Equal(0.80f * 0.8f, audio.Players[0].Gain, 3);
        Assert.Equal(0.80f * 0.6f, audio.Players[1].Gain, 3);
        Assert.Equal(0.80f * 0.5f, audio.Players[2].Gain, 3);
    }

    [Fact]
    public void Sfx_DedicatedFile_WinsOverFallback()
    {
        var dedicated = TouchSfx("wave_clear.mp3");
        TouchSfx("chime1.mp3");
        var (fx, audio, _) = Make();

        fx.PlaySfx("wave_clear", 0.6);
        Assert.Equal(dedicated, audio.Players[0].Path);
    }

    [Fact]
    public void Sfx_Unresolved_SilentNoOp_Logged()
    {
        var (fx, audio, _) = Make();
        fx.PlaySfx("no_such_cue", 0.6);
        fx.PlaySfx(null, 0.6);
        Assert.Empty(audio.Players);
        Assert.Contains(_log, l => l.Contains("no_such_cue") && l.Contains("silent no-op"));
    }

    // ---------- VN mix gate ----------

    [Fact]
    public void VnSpeaking_Transitions_Idempotent()
    {
        // The host-side state machine the in-page tint path touches (§3.2 decision: the
        // tinted VN portrait renders page-side; vn-speaking is the host's only signal).
        var (fx, _, _) = Make();
        fx.SetVnSpeaking(true);
        fx.SetVnSpeaking(true);
        fx.SetVnSpeaking(false);
        fx.SetVnSpeaking(false);
        Assert.False(fx.VnSpeaking);
        Assert.Equal(1, _log.Count(l => l.Contains("vn-speaking on")));
        Assert.Equal(1, _log.Count(l => l.Contains("vn-speaking off")));
    }

    [Fact]
    public void VnSpeaking_Gates_Sfx_ButNotWhisper()
    {
        TouchSfx("Pop.mp3");
        TouchVoice("sub_one.mp3");
        var (fx, audio, _) = Make();

        fx.SetVnSpeaking(true);
        fx.PlaySfx("Pop", 0.6);            // gated (DtrhHostService.cs:223)
        fx.FirePayload("audio", 60, 1.0);  // NOT gated (WPF fire-payload path has no VN check)
        fx.SetVnSpeaking(false);
        fx.PlaySfx("Pop", 0.6);            // released

        Assert.Equal(2, audio.Players.Count); // whisper + the post-release sfx
        Assert.Contains(_log, l => l.Contains("VN owns the mix"));
    }

    // ---------- voice channel ----------

    [Fact]
    public void Whisper_StopReplace_GenerationToken()
    {
        var (fx, audio, _) = Make();
        fx.PlayWhisper("a.mp3");
        fx.PlayWhisper("b.mp3"); // newest-wins stop-replace

        Assert.Equal(2, audio.Players.Count);
        Assert.True(audio.Players[0].Stopped);
        Assert.True(audio.Players[0].Disposed);

        // F2: the stale player's end event must NOT clear the live channel.
        audio.Players[0].RaiseEnded();
        Assert.DoesNotContain(_log, l => l.Contains("whisper completed"));

        audio.Players[1].RaiseEnded(); // the live player's completion clears
        Assert.Contains(_log, l => l.Contains("whisper completed (backend PlaybackEnded)"));
    }

    // ---------- freeze ----------

    [Fact]
    public void Freeze_IdempotentDedup_VideoAndVoice()
    {
        var (fx, audio, video) = Make();
        fx.PlayWhisper("a.mp3");
        audio.Players[0].State = DtrhPlayerState.Playing;

        fx.SetWorldFrozen(true);
        fx.SetWorldFrozen(true); // dedup (DtrhHostService.cs:675-677)
        Assert.Equal([true], video.PauseCalls);
        Assert.True(audio.Players[0].Paused);

        fx.SetWorldFrozen(false);
        fx.SetWorldFrozen(false);
        Assert.Equal([true, false], video.PauseCalls);
        Assert.Equal(2, audio.Players[0].PlayCalls); // Play (start) + Play (resume from pause)
    }

    [Fact]
    public void Freeze_PauseOnlyWhenPlaying_ResumeOnlyWhenPaused()
    {
        var (fx, audio, _) = Make();
        fx.PlayWhisper("a.mp3");
        audio.Players[0].State = DtrhPlayerState.Stopped; // Speech.cs:1651-1669 parity

        fx.SetWorldFrozen(true);
        Assert.False(audio.Players[0].Paused);

        audio.Players[0].State = DtrhPlayerState.Playing;
        fx.SetWorldFrozen(false);
        fx.SetWorldFrozen(true);
        Assert.True(audio.Players[0].Paused);
    }

    [Fact]
    public void RunBoundary_ClearsStaleFreezeAndVnDuck()
    {
        var (fx, _, video) = Make();
        fx.SetVnSpeaking(true);
        fx.SetWorldFrozen(true);

        fx.NotifyRunBoundary(); // run-started :252/:259 + run-ended :513 parity

        Assert.False(fx.VnSpeaking);
        Assert.False(fx.WorldFrozen);
        Assert.Equal([true, false], video.PauseCalls);
    }

    [Fact]
    public void Teardown_MidFreeze_Unwedges_ThenStops()
    {
        var (fx, audio, video) = Make();
        fx.PlayWhisper("a.mp3");
        audio.Players[0].State = DtrhPlayerState.Playing;
        fx.SetWorldFrozen(true);

        fx.Teardown(); // DisposeAll :896 parity — never leave a clip wedged paused

        Assert.False(fx.WorldFrozen);
        Assert.Equal([true, false], video.PauseCalls); // force-resumed BEFORE stop
        Assert.Equal(1, video.StopCalls);
        Assert.True(audio.Players[0].Stopped);
        Assert.True(audio.Players[0].Disposed);
        Assert.Contains(_log, l => l.Contains("unwedge"));

        fx.Teardown(); // idempotent
        Assert.Equal(1, video.StopCalls);
    }

    // ---------- fire-payload ----------

    [Fact]
    public void FirePayload_Video_PlaysFromPool_RaisesStarted_CapsAtSegment()
    {
        var clip = TouchVideo("clip.mp4");
        var (fx, _, video) = Make(capSec: 0.05);
        var started = 0;
        var ended = 0;
        fx.VideoStarted += (_, _) => started++;
        fx.VideoEnded += (_, _) => ended++;

        fx.FirePayload("video", 60, 1.0); // strength/durationMult accepted, non-consumed

        Assert.Equal(1, started);
        Assert.Equal(clip, Assert.Single(video.Played));
        Assert.Contains(_log, l => l.Contains("non-consumed"));

        // SEGMENT_SEC parity: the cap stops the tape (EffectPayload.cs:148-153), and the
        // stop raises VideoEnded (payload-state off rides the video CLOSING, WPF parity).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (video.StopCalls == 0 && DateTime.UtcNow < deadline) Thread.Sleep(20);
        Assert.Equal(1, video.StopCalls);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void FirePayload_Video_EmptyPool_SilentNoOp()
    {
        var (fx, _, video) = Make();
        fx.FirePayload("video", 60, 1.0);
        Assert.Empty(video.Played);
        Assert.Contains(_log, l => l.Contains("media pool empty"));
    }

    [Fact]
    public void FirePayload_UnknownKind_LoggedAndIgnored()
    {
        var (fx, audio, video) = Make();
        fx.FirePayload("flash", 60, 1.0); // in-world since the cutover (:505-510)
        fx.FirePayload(null, null, null);
        Assert.Empty(audio.Players);
        Assert.Empty(video.Played);
        Assert.Contains(_log, l => l.Contains("in-world since the cutover"));
    }

    [Fact]
    public void VideoBackend_EndAndError_RaiseVideoEnded()
    {
        TouchVideo("clip.webm");
        var (fx, _, video) = Make();
        var ended = 0;
        fx.VideoEnded += (_, _) => ended++;

        fx.FirePayload("video", 60, 1.0);
        video.RaiseEnded();
        Assert.Equal(1, ended);

        fx.FirePayload("video", 60, 1.0);
        video.RaiseError();
        Assert.Equal(2, ended);
    }

    // ---------- fakes ----------

    private sealed class FakeAudio : IDtrhAudioBackend
    {
        public List<FakePlayer> Players { get; } = [];

        public bool TryInit(string? deviceName, out string? error)
        {
            error = null;
            return true;
        }

        public IDtrhAudioPlayer CreatePlayer(string path, float volume)
        {
            var p = new FakePlayer(path, volume);
            Players.Add(p);
            return p;
        }

        public void Dispose() { }
    }

    private sealed class FakePlayer : IDtrhAudioPlayer
    {
        public FakePlayer(string path, float gain) { Path = path; Gain = gain; }

        public string Path { get; }
        public float Gain { get; }
        public DtrhPlayerState State { get; set; } = DtrhPlayerState.Stopped;
        public int PlayCalls { get; private set; }
        public bool Paused { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }

        public event EventHandler? PlaybackEnded;

        public DtrhPlayerState StateSnapshot => State;
        DtrhPlayerState IDtrhAudioPlayer.State => State;
        public double PositionSec => 0;

        public void Play()
        {
            PlayCalls++;
            if (Paused) { Paused = false; State = DtrhPlayerState.Playing; return; }
            State = DtrhPlayerState.Playing;
        }

        public void Pause() { Paused = true; State = DtrhPlayerState.Paused; }

        public void Stop() { Stopped = true; State = DtrhPlayerState.Stopped; }

        public void Dispose() => Disposed = true;

        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeVideo : IDtrhVideoBackend
    {
        public List<string> Played { get; } = [];
        public List<bool> PauseCalls { get; } = [];
        public int StopCalls { get; private set; }

        public long FrameCount => 0;
        public double PositionSec => 0;
        public WriteableBitmap? CurrentFrame => null;

        public event EventHandler? FramePresented;
        public event EventHandler? PlaybackEnded;
        public event EventHandler? EncounteredError;

        public bool TryPlay(string path) { Played.Add(path); return true; }
        public void SetPaused(bool paused) => PauseCalls.Add(paused);
        public void Stop() => StopCalls++;

        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        public void RaiseError() => EncounteredError?.Invoke(this, EventArgs.Empty);
        public void RaiseFrame() => FramePresented?.Invoke(this, EventArgs.Empty);
    }
}
