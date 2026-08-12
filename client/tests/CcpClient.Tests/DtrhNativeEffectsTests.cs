using Avalonia.Media.Imaging;
using CcpClient.Desktop.Audio;
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

    private (DtrhNativeEffects fx, FakeAudio audio, FakeVideo video) Make(int maxSfx = 8, double capSec = 15, ManualClock? clock = null)
    {
        var audio = new FakeAudio();
        var video = new FakeVideo();
        // SP-043: every test in this class drives the segment-cap timer through an
        // injected ManualClock — no real System.Threading.Timer is ever armed here
        // (deterministic under parallel load; the 0.05s-cap + wall-clock-poll flake
        // class, SP-041 run-4 red, is closed structurally, never by wider windows).
        clock ??= new ManualClock();
        var fx = new DtrhNativeEffects(audio, video, new DtrhNativeEffectsOptions
        {
            SfxRoots = [Path.Combine(_root, "sfx")],
            VideoRoots = [Path.Combine(_root, "videos")],
            WhisperRoots = [Path.Combine(_root, "voices")],
            MasterVolume = 80,
            MaxSfxVoices = maxSfx,
            VideoSegmentCapSec = capSec,
        }, _log.Add, clock);
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
    public void Sfx_AuditedChains_ResolvePerChain_AndGenericResolution()
    {
        // SP-051: boon_reveal chains resolve per the WPF chain (ChaosSfx.cs:25-30) — the
        // dedicated drops (dling/thud) live in the WPF sound library, so the chain lands
        // on the fallback members that ARE in the payload pool, at the WPF fixed scales.
        var chime = TouchSfx("chime1.mp3");
        var pop2 = TouchSfx("Pop2.mp3");
        var pop = TouchSfx("Pop.mp3");
        var (fx, audio, _) = Make();

        fx.PlaySfx("boon_reveal_rare", 0.3);   // chain dling.mp3 → chime1.mp3 @0.6 (scale ignored)
        fx.PlaySfx("boon_reveal_common", 0.3); // chain thud.mp3 → Pop2.mp3 @0.65 (scale ignored)
        fx.PlaySfx("pop", 0.5);                // generic, case-insensitive file match (Linux honest)

        Assert.Equal(chime, audio.Players[0].Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(pop2, audio.Players[1].Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(pop, audio.Players[2].Path, StringComparer.OrdinalIgnoreCase);
        // volume = master(0.80) × scale clamped (ChaosSfx.cs:96-103)
        Assert.Equal(0.80f * 0.6f, audio.Players[0].Gain, 3);
        Assert.Equal(0.80f * 0.65f, audio.Players[1].Gain, 3);
        Assert.Equal(0.80f * 0.5f, audio.Players[2].Gain, 3);
    }

    [Fact]
    public void Sfx_FixedChainGaps_TypedAndRecorded()
    {
        // SP-051: wave_clear/ripple_cast chains (ChaosSfx.cs:22, :41) have NO member in the
        // payload pool — typed named content gaps with the WPF chain cited, never an
        // off-chain substitution. ticktock's page path rides the generic chain
        // (DtrhHostService.cs:262 → ChaosSfx.cs:47); also absent from the pool.
        var (fx, audio, _) = Make();

        fx.PlaySfx("wave_clear", 0.5);
        fx.PlaySfx("ripple_cast", 0.5);
        fx.PlaySfx("ticktock", 0.5);

        Assert.Empty(audio.Players);
        Assert.Contains(_log, l => l.Contains("wave_clear") && l.Contains("named content gap")
            && l.Contains("chaos/wave_clear.mp3 → lvup.mp3") && l.Contains("ChaosSfx.cs:22"));
        Assert.Contains(_log, l => l.Contains("ripple_cast") && l.Contains("named content gap")
            && l.Contains("chaos/ripple_cast.mp3 → chaos/snap.mp3") && l.Contains("ChaosSfx.cs:41"));
        Assert.Contains(_log, l => l.Contains("ticktock") && l.Contains("named content gap")
            && l.Contains("chaos/ticktock.mp3") && l.Contains("ChaosSfx.cs:47"));
    }

    /// <summary>SP-051: every page-sent cue riding the generic chain (record.md Tier B —
    /// grep of sfx('&lt;name&gt;') over the dtrh page JS + warren.js:246's unlock_card default)
    /// is a named content gap while the WPF chaos sound library is unported. detonate_thud
    /// and dive are silent in WPF too (absent from the WPF library) — the gap is still
    /// named, never an unrecorded drop.</summary>
    [Theory]
    [InlineData("collar_save")]
    [InlineData("countdown_tick")]
    [InlineData("defuse_hiss")]
    [InlineData("depth_change")]
    [InlineData("detonate_thud")]
    [InlineData("dive")]
    [InlineData("dvd_launch")]
    [InlineData("estim_zap")]
    [InlineData("fall_in")]
    [InlineData("focus_empty")]
    [InlineData("freeze_catch")]
    [InlineData("freeze_shatter")]
    [InlineData("freeze_trigger")]
    [InlineData("fx_drain")]
    [InlineData("glass_shatter")]
    [InlineData("golden_pop")]
    [InlineData("heartbeat")]
    [InlineData("rabbit_spawn")]
    [InlineData("resist_absorb")]
    [InlineData("reveal_chime")]
    [InlineData("sin_accept")]
    [InlineData("sink")]
    [InlineData("streak_milestone")]
    [InlineData("surface")]
    [InlineData("time_slow_in")]
    [InlineData("time_slow_out")]
    [InlineData("toy_denied")]
    [InlineData("toy_ready")]
    [InlineData("trigger")]
    [InlineData("tunnel_zone")]
    [InlineData("ui_click")]
    [InlineData("ui_deepen")]
    [InlineData("ui_denied")]
    [InlineData("ui_unlock")]
    [InlineData("unlock_card")]
    [InlineData("vibe_buzz")]
    public void Sfx_GenericPageCues_NamedGaps(string cue)
    {
        var (fx, audio, _) = Make();

        fx.PlaySfx(cue, 0.5);

        Assert.Empty(audio.Players);
        Assert.Contains(_log, l => l.Contains($"sfx '{cue}'") && l.Contains("named content gap")
            && l.Contains($"chaos/{cue}.mp3") && l.Contains("ChaosSfx.cs:47"));
    }

    [Fact]
    public void Sfx_ChainFallback_ResolvesWhenDedicatedAbsent()
    {
        // SP-051: when a chain's fallback member IS in the pool, the chain resolves to it.
        var lvup = TouchSfx("lvup.mp3");
        var chime = TouchSfx("chime1.mp3");
        var (fx, audio, _) = Make();

        fx.PlaySfx("wave_clear", 0.3);        // wave_clear.mp3 absent → lvup.mp3 @0.8 fixed
        fx.PlaySfx("boon_reveal_rare", 0.3);  // dling.mp3 absent → chime1.mp3 @0.6 fixed

        Assert.Equal(lvup, audio.Players[0].Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(chime, audio.Players[1].Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0.80f * 0.8f, audio.Players[0].Gain, 3);
        Assert.Equal(0.80f * 0.6f, audio.Players[1].Gain, 3);
    }

    [Fact]
    public void Sfx_ResolveSfxCue_TypedOutcomes()
    {
        // SP-051: the resolution entry point future sfx consumers use — typed Resolved vs
        // NamedGap, boon_reveal tokens included (table rows, no page wire today).
        TouchSfx("Pop2.mp3");
        var (fx, _, _) = Make();

        var resolved = fx.ResolveSfxCue("boon_reveal_common", 0.3);
        Assert.True(resolved.IsResolved);
        Assert.EndsWith("Pop2.mp3", resolved.Path);
        Assert.Equal(0.65, resolved.Scale);   // WPF fixed scale, page scale ignored
        Assert.Null(resolved.GapNote);

        var gap = fx.ResolveSfxCue("wave_clear", 0.3);
        Assert.False(gap.IsResolved);
        Assert.Contains("chaos/wave_clear.mp3 → lvup.mp3", gap.GapNote);
        Assert.Contains("ChaosSfx.cs:22", gap.GapNote);

        var genericGap = fx.ResolveSfxCue("golden_pop", 0.45);
        Assert.False(genericGap.IsResolved);
        Assert.Contains("chaos/golden_pop.mp3", genericGap.GapNote);
        Assert.Contains("ChaosSfx.cs:47", genericGap.GapNote);

        var empty = fx.ResolveSfxCue(null, 0.6);
        Assert.False(empty.IsResolved);
        Assert.Null(empty.GapNote);   // unlisted/empty cue — plain silent no-op, not a named gap
    }

    [Fact]
    public void Sfx_BoonPick_ChainFallsBackToChime2_KeepingPageScale()
    {
        // SP-049: the studio's save-success cue (loomStudio.js:209, scale 0.4). WPF's chain
        // (ChaosSfx.cs:33) is chaos/boon_pick.mp3 → chime2.mp3; the dedicated drop is not
        // in the DTRH payload pool, so the chain lands on chime2 — and unlike the other
        // chains the page-supplied scale passes through (WPF ChaosSfx.Play(name, scale)).
        var chime2 = TouchSfx("chime2.mp3");
        var (fx, audio, _) = Make();

        fx.PlaySfx("boon_pick", 0.4);

        Assert.Equal(chime2, audio.Players[0].Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0.80f * 0.4f, audio.Players[0].Gain, 3);
    }

    [Fact]
    public void Sfx_DedicatedFile_WinsOverFallback()
    {
        var dedicated = TouchSfx("wave_clear.mp3");
        TouchSfx("lvup.mp3");
        var (fx, audio, _) = Make();

        fx.PlaySfx("wave_clear", 0.6);
        Assert.Equal(dedicated, audio.Players[0].Path);
    }

    [Fact]
    public void Sfx_BoonReveal_DedicatedFile_WinsOverFallback()
    {
        // SP-051: a dedicated drop always wins its chain (ChaosSfx.cs:62-79 first-exists order).
        var dling = TouchSfx("dling.mp3");
        TouchSfx("chime1.mp3");
        var thud = TouchSfx("thud.mp3");
        TouchSfx("Pop2.mp3");
        var (fx, audio, _) = Make();

        fx.PlaySfx("boon_reveal_rare", 0.3);
        fx.PlaySfx("boon_reveal_common", 0.3);

        Assert.Equal(dling, audio.Players[0].Path);
        Assert.Equal(thud, audio.Players[1].Path);
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
        var clock = new ManualClock();
        // The REAL SEGMENT_SEC=15 parity value — the fake clock makes the cap instant to
        // reach, so the toy 0.05s cap (and its wall-clock poll) is gone for good.
        var (fx, _, video) = Make(capSec: 15, clock: clock);
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
        // Deterministic: the injected clock drives the cap — a wrongly-scheduled cap can
        // never fire inside this exact-15s advance window.
        Assert.Equal(0, video.StopCalls); // the cap is time-driven, never immediate
        clock.Advance(TimeSpan.FromSeconds(14.9));
        Assert.Equal(0, video.StopCalls); // and never early
        clock.Advance(TimeSpan.FromSeconds(0.1)); // the segment cap arrives
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

    /// <summary>Manual <see cref="ISoundClock"/> (SP-043; the SoundArbitrationTests.cs:551
    /// pattern): Schedule captures due+fire, Advance fires due timers in due order,
    /// Dispose cancels (the ISoundClock contract). Zero wall-clock.</summary>
    private sealed class ManualClock : ISoundClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            _timers.Add(entry);
            return new CancelHandle(entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            // Fire due timers in due order; timers scheduled by callbacks fire in the same pass.
            while (true)
            {
                var next = _timers
                    .Where(t => !t.Cancelled && t.Due <= UtcNow)
                    .OrderBy(t => t.Due)
                    .FirstOrDefault();
                if (next is null)
                {
                    return;
                }

                _timers.Remove(next);
                next.Fire();
            }
        }

        private sealed class CancelHandle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Cancelled = true;
        }
    }

    // ---------- b4 media-logging gate (SP-026) ----------

    [Fact]
    public void ActivePool_DeselectedUserVideo_NeverPlays_WhitelistOff_Plays()
    {
        // SP-055 (VideoService.cs:6640-6663 parity): the fire-payload video pool routes
        // through the ONE active-pool definition — a deselected user video is silently
        // out of the pool (the harness no-op line), payload files are unaffected.
        var userRoot = Path.Combine(_root, "usermedia");
        var userDir = Path.Combine(userRoot, "videos");
        Directory.CreateDirectory(userDir);
        File.WriteAllBytes(Path.Combine(userDir, "deselected-clip.mp4"), new byte[64]);
        TouchVideo("payload-clip.mp4");
        var disabled = DtrhUserMedia.BuildDisabledSet(["videos/deselected-clip.mp4"]);

        (DtrhNativeEffects fx, FakeVideo video) Make(bool useWhitelist)
        {
            var a = new FakeAudio();
            var v = new FakeVideo();
            var f = new DtrhNativeEffects(a, v, new DtrhNativeEffectsOptions
            {
                SfxRoots = [Path.Combine(_root, "sfx")],
                VideoRoots = [Path.Combine(_root, "videos"), userDir],
                WhisperRoots = [Path.Combine(_root, "voices")],
                UserMediaRoot = userRoot,
                DisabledAssets = disabled,
                UseAssetWhitelist = useWhitelist,
                MasterVolume = 80,
            }, _log.Add, new ManualClock());
            return (f, v);
        }

        var (fxOn, videoOn) = Make(useWhitelist: true);
        fxOn.FireVideoFromPool("deselected-clip.mp4");
        Assert.Empty(videoOn.Played); // deselected — the pool never yields it
        Assert.Contains(_log, l => l.Contains("not in the media pool"));
        fxOn.FireVideoFromPool("payload-clip.mp4");
        Assert.Single(videoOn.Played); // payload art is never deselectable
        fxOn.Dispose();

        var (fxOff, videoOff) = Make(useWhitelist: false);
        fxOff.FireVideoFromPool("deselected-clip.mp4");
        Assert.Single(videoOff.Played); // the flag gates the mechanism (AppSettings.cs:1637)
        fxOff.Dispose();
    }

    [Fact]
    public void MediaLogging_UserMediaRoot_PresenceShapeOnly_PayloadKeepsNames()
    {
        // Packet framing c (SP-018 V5 class): files under a PresenceOnlyRoots root log
        // bytes + extension class, NEVER a filename; payload/staged files keep names.
        var userDir = Path.Combine(_root, "usermedia", "videos");
        Directory.CreateDirectory(userDir);
        File.WriteAllBytes(Path.Combine(userDir, "secret-user-clip.mp4"), new byte[123]);
        TouchVideo("payload-clip.mp4");
        var audio = new FakeAudio();
        var video = new FakeVideo();
        var fx = new DtrhNativeEffects(audio, video, new DtrhNativeEffectsOptions
        {
            SfxRoots = [Path.Combine(_root, "sfx")],
            VideoRoots = [Path.Combine(_root, "videos"), userDir],
            WhisperRoots = [Path.Combine(_root, "voices")],
            PresenceOnlyRoots = [Path.Combine(_root, "usermedia")],
            MasterVolume = 80,
        }, _log.Add, new ManualClock());

        fx.FireVideoFromPool("secret-user-clip.mp4");
        Assert.Contains(_log, l => l.Contains("user pool (123 bytes, .mp4 class)"));
        Assert.DoesNotContain(_log, l => l.Contains("secret-user-clip"));

        fx.FireVideoFromPool("payload-clip.mp4");
        Assert.Contains(_log, l => l.Contains("payload-clip.mp4")); // payload/staged scope keeps names
        fx.Dispose();
    }

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
