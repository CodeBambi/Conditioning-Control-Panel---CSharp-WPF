using CcpClient.Desktop.Audio;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The b3 native-effects owner (dtrh-admission.md §7 slice b3): everything the DTRH page
/// asks the host to do that is NOT rendered in-world — native SFX cues, the one-shot
/// whisper, the covering native video, and the world freeze that pauses video + voice.
/// WPF parity cites per member (READ-ONLY archaeology, SP-025 record Step 1).
///
/// Channel ownership (SP-017 selection, owner-ratified 2026-07-21):
///   SFX   — bounded pool, max <see cref="DtrhNativeEffectsOptions.MaxSfxVoices"/> simultaneous,
///           DROP-on-overflow (ChaosSfx.cs:91-107 parity: "a one-shot SFX played late is
///           worse than silence"); pool reclaims on the backend's real PlaybackEnded.
///   Voice — exclusive whisper channel, stop-replace newest-wins with a generation token
///           (F2 discipline: a stale player's end event must never clear the live one).
///   Video — one covering native video at a time (WPF mandatory video outcome), stop-replace.
/// Backends are seams (<see cref="IDtrhAudioBackend"/>/<see cref="IDtrhVideoBackend"/>) so
/// unit tests run against recording fakes — never the real SoundFlow/libvlc backends.
/// </summary>
public sealed class DtrhNativeEffects : IDisposable
{
    private readonly IDtrhAudioBackend _audio;
    private readonly IDtrhVideoBackend _video;
    private readonly DtrhNativeEffectsOptions _options;
    private readonly Action<string> _log;
    private readonly List<IDtrhAudioPlayer> _sfxPool = [];
    private readonly object _gate = new();
    private IDtrhAudioPlayer? _voice;
    private long _voiceGeneration;
    private bool _worldFrozen;
    private bool _vnSpeaking;
    private bool _tornDown;
    private int _videoActive;
    private readonly ISoundClock _clock;
    private IDisposable? _videoCapTimer;

    public DtrhNativeEffects(
        IDtrhAudioBackend audio,
        IDtrhVideoBackend video,
        DtrhNativeEffectsOptions options,
        Action<string> log,
        ISoundClock? clock = null)
    {
        _audio = audio;
        _video = video;
        _options = options;
        _log = log;
        // Injectable clock/timer seam (SP-043, additive-only): the segment-cap timer is the
        // one wall-clock element in this class; tests drive it deterministically. The
        // default is the REAL clock (SystemSoundClock = System.Threading.Timer) — product
        // behavior unchanged.
        _clock = clock ?? new SystemSoundClock();
        _video.PlaybackEnded += OnVideoEnded;
        _video.EncounteredError += OnVideoError;
    }

    /// <summary>A covering native video started (host mirrors WPF payload-state on=true,
    /// DtrhHostService.cs:744).</summary>
    public event EventHandler? VideoStarted;

    /// <summary>The covering video ended/capped/stopped (host mirrors payload-state on=false
    /// + focus reclaim, DtrhHostService.cs:764-775).</summary>
    public event EventHandler? VideoEnded;

    public bool WorldFrozen => _worldFrozen;

    public bool VnSpeaking => _vnSpeaking;

    public int ActiveSfxVoices { get { lock (_gate) return _sfxPool.Count; } }

    public IDtrhVideoBackend Video => _video;

    // ============================ SFX ============================

    /// <summary>sfx {name, scale} (DtrhHostService.cs:222-234): VN gate first (`:223` —
    /// stingers stay silent while she speaks), then resolution through the SP-051 audited
    /// chain table (<see cref="AuditedChains"/>) or the generic chain — see
    /// <see cref="ResolveSfxCue"/>. scale default 0.6 (`:226`).</summary>
    public void PlaySfx(string? name, double scale)
    {
        if (_vnSpeaking)
        {
            _log($"dtrh-fx: sfx '{name ?? "(null)"}' skipped — VN owns the mix");
            return;
        }

        var resolution = ResolveSfxCue(name, scale);
        if (resolution.Path is not { } path)
        {
            // Silent no-op parity (ChaosSfx.cs:75-93: absent asset = silence, logged).
            // SP-051: an audited cue whose chain misses the pool is a NAMED content gap —
            // typed + logged with its WPF chain cited, never an unrecorded drop.
            _log(resolution.GapNote is { } gap
                ? $"dtrh-fx: sfx '{name}' — {gap}; silent no-op"
                : $"dtrh-fx: sfx '{name ?? "(null)"}' unresolved — silent no-op (no candidate on disk)");
            return;
        }

        var effectiveScale = resolution.Scale;
        IDtrhAudioPlayer player;
        lock (_gate)
        {
            if (_sfxPool.Count >= _options.MaxSfxVoices)
            {
                // Drop-on-overflow (ChaosSfx.cs:91-107 parity; bound 8 = the packet decree,
                // SP-017 named limit 7's pending-owner value decided by SP-025).
                _log($"dtrh-fx: sfx pool full ({_options.MaxSfxVoices}) — dropping '{name}'");
                return;
            }

            player = _audio.CreatePlayer(path, Volume(effectiveScale));
            _sfxPool.Add(player);
        }

        player.PlaybackEnded += (_, _) =>
        {
            lock (_gate) _sfxPool.Remove(player);
            // Backend-event completion (SP-017 discipline: completion claims come from
            // backend-emitted events, never call returns).
            _log($"dtrh-fx: sfx '{name}' completed (backend PlaybackEnded, pool {ActiveSfxVoices}/{_options.MaxSfxVoices})");
            try { player.Dispose(); } catch { /* best-effort */ }
        };
        try
        {
            player.Play();
            _log($"dtrh-fx: sfx '{name}' playing ({DescribeMedia(path)}, vol {Volume(effectiveScale):0.00}, pool {ActiveSfxVoices}/{_options.MaxSfxVoices})");
        }
        catch (Exception ex)
        {
            lock (_gate) _sfxPool.Remove(player);
            try { player.Dispose(); } catch { /* best-effort */ }
            _log($"dtrh-fx: sfx '{name}' play failed ({ex.GetType().Name})");
        }
    }

    // ============================ SP-051 audited chain resolution ============================

    /// <summary>The complete audited WPF ChaosSfx cue→fallback-chain table (SP-051 map,
    /// spine-tasks/SP-051-chaossfx-chain-audit/record.md). Candidates are the chain member
    /// BASENAMES in override→fallback order (the greenfield pool is flat; WPF keeps them
    /// under Resources/sounds/ with chaos/ and bubbles/ prefixes — <see cref="ChaosSfxChain.WpfChain"/>
    /// keeps the WPF-relative spelling). Keyed by wire cue token, ordinal-exact
    /// (DtrhHostService.cs:260-261 parity). boon_reveal_rare/boon_reveal_common have no page
    /// wire today (WPF fires them overlay-side, ChaosOverlayWindow.xaml.cs:282-283,324) —
    /// they are table rows for future sfx consumers, resolvable through
    /// <see cref="ResolveSfxCue"/>, never invented page tokens. Every chain whose members
    /// are all absent from the pool is a NAMED CONTENT GAP (WPF sound-library content —
    /// a future content row), never an off-chain substitution.</summary>
    public static IReadOnlyDictionary<string, ChaosSfxChain> AuditedChains { get; } =
        new Dictionary<string, ChaosSfxChain>(StringComparer.Ordinal)
        {
            // ChaosSfx.cs:22 — helper ignores the page scale (DtrhHostService.cs:260).
            ["wave_clear"] = new(["wave_clear.mp3", "lvup.mp3"], 0.8,
                "chaos/wave_clear.mp3 → lvup.mp3", "ChaosSfx.cs:22"),
            // ChaosSfx.cs:25-30 — PlayBoonReveal(isRare: true).
            ["boon_reveal_rare"] = new(["dling.mp3", "chime1.mp3"], 0.6,
                "chaos/dling.mp3 → chime1.mp3", "ChaosSfx.cs:25-30"),
            // ChaosSfx.cs:25-30 — PlayBoonReveal(isRare: false).
            ["boon_reveal_common"] = new(["thud.mp3", "Pop2.mp3"], 0.65,
                "chaos/thud.mp3 → bubbles/Pop2.mp3", "ChaosSfx.cs:25-30"),
            // ChaosSfx.cs:33 helper is @0.7, but the DTRH page path rides the generic chain
            // at the PAGE scale (DtrhHostService.cs:262 → ChaosSfx.cs:47) — the SP-049
            // precedent: chain → chime2.mp3, page-supplied scale kept, test-pinned.
            ["boon_pick"] = new(["boon_pick.mp3", "chime2.mp3"], null,
                "chaos/boon_pick.mp3 → chime2.mp3", "ChaosSfx.cs:33 (+ page path DtrhHostService.cs:262)"),
            // ChaosSfx.cs:41 — helper ignores the page scale (DtrhHostService.cs:261).
            ["ripple_cast"] = new(["ripple_cast.mp3", "snap.mp3"], 0.6,
                "chaos/ripple_cast.mp3 → chaos/snap.mp3", "ChaosSfx.cs:41"),
            // ChaosSfx.cs:37 helper is @0.45, but the page path rides the generic chain at
            // the PAGE scale (DtrhHostService.cs:262 → ChaosSfx.cs:47) — a table row (not
            // the generic arm) so the gap log self-cites the named helper chain.
            ["ticktock"] = new(["ticktock.mp3"], null,
                "chaos/ticktock.mp3", "ChaosSfx.cs:37; page path ChaosSfx.cs:47"),
        };

    /// <summary>Resolve one cue against the sfx pool through the audited chains (SP-051):
    /// the table row when the token is listed, else the generic chain
    /// (<c>chaos/{name}.mp3</c> → flat {name}.mp3, page scale — ChaosSfx.cs:47). First
    /// candidate on disk wins (ChaosSfx.cs:62-79). Unresolved non-empty cue = typed
    /// <see cref="ChaosSfxResolution.GapNote"/> with its WPF chain cited.</summary>
    public ChaosSfxResolution ResolveSfxCue(string? name, double pageScale)
    {
        string[] candidates;
        double scale;
        string? chain;
        string? cite;
        ChaosSfxChain? audited = null;
        if (name is { Length: > 0 })
        {
            AuditedChains.TryGetValue(name, out audited);
        }

        if (audited is not null)
        {
            candidates = audited.Candidates;
            scale = audited.FixedScale ?? pageScale;
            chain = audited.WpfChain;
            cite = audited.Cite;
        }
        else
        {
            candidates = [name is { Length: > 0 } n ? n + ".mp3" : ""];
            scale = pageScale;
            chain = name is { Length: > 0 } gn ? $"chaos/{gn}.mp3" : null;
            cite = chain is null ? null : "ChaosSfx.cs:47";
        }

        var path = ResolveSfx(candidates);
        if (path is not null) return new ChaosSfxResolution(path, scale, null);
        // Two honest gap phrasings (SP-051 pre-completion consult): an AUDITED table row's
        // members are verified WPF sound-library content (a future content row); a GENERIC
        // cue's file may not exist even in the WPF library (page-sent detonate_thud/dive
        // don't) — claim only what was verified.
        return new ChaosSfxResolution(null, scale,
            chain is null ? null
                : audited is not null
                    ? $"named content gap (WPF chain {chain}, {cite} — WPF sound-library content, future content row)"
                    : $"named content gap (WPF chain {chain}, {cite} — no chain member in the payload sfx pool)");
    }

    /// <summary>Resolve the first candidate that exists in the sfx pool (payload
    /// assets/bubbles/sfx, overlay-first). Case-insensitive file match keeps page cue
    /// names (lower_snake) honest against the shipped mixed-case files on Linux.</summary>
    private string? ResolveSfx(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Length == 0) continue;
            foreach (var root in _options.SfxRoots)
            {
                var exact = Path.Combine(root, candidate);
                if (File.Exists(exact)) return exact;
                if (!Directory.Exists(root)) continue;
                var match = Directory.EnumerateFiles(root)
                    .FirstOrDefault(f => string.Equals(Path.GetFileName(f), candidate, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
            }
        }

        return null;
    }

    // ============================ fire-payload ============================

    /// <summary>fire-payload {kind, strength?, durationMult?} (DtrhHostService.cs:471-523):
    /// since the 2026-07 cutover only the two native-by-necessity kinds ride the bridge —
    /// video (covering window) and audio (one-shot whisper). Any other kind = page/host
    /// version mismatch, logged + ignored (`:505-510`). strength/durationMult are accepted
    /// and recorded NON-CONSUMED (WPF VideoPayload/AudioPayload never read them).</summary>
    public void FirePayload(string? kind, int? strength, double? durationMult)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            _log("dtrh-fx: fire-payload without kind — ignored");
            return;
        }

        if (kind.Equals("video", StringComparison.OrdinalIgnoreCase))
        {
            FireVideo();
        }
        else if (kind.Equals("audio", StringComparison.OrdinalIgnoreCase))
        {
            FireWhisper();
        }
        else
        {
            _log($"dtrh-fx: payload kind '{kind}' is in-world since the cutover — ignored (WPF parity)");
            return;
        }

        _log($"dtrh-fx: fire-payload {kind} (strength {Math.Clamp(strength ?? 60, 0, 100)}, durationMult {Math.Clamp(durationMult ?? 1.0, 0.1, 10.0):0.0} — accepted, non-consumed WPF parity)");
    }

    /// <summary>HARNESS-ONLY (--dtrh-fx-drive video-file step): play ONE NAMED pool file
    /// through the same covering-video path. The name resolves INSIDE the enumerated pool
    /// (never an arbitrary path — no traversal); unknown name = logged no-op. Exists so
    /// headed evidence can pin the staged real-media clip deterministically.</summary>
    public void FireVideoFromPool(string fileName)
    {
        var match = EnumerateVideoPool()
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            _log($"dtrh-fx: video-file '{fileName}' not in the media pool — harness no-op");
            return;
        }

        PlayVideoFile(match);
    }

    /// <summary>video payload (EffectPayload.cs:139-159): a covering video from the media
    /// pool; chaos caps the tape at SEGMENT_SEC=15 then it ends. Empty pool = silent no-op
    /// (TriggerVideo silentIfEmpty parity).</summary>
    private void FireVideo()
    {
        var pool = EnumerateVideoPool();
        if (pool.Count == 0)
        {
            _log("dtrh-fx: video payload — media pool empty, silent no-op");
            return;
        }

        PlayVideoFile(pool[Random.Shared.Next(pool.Count)]);
    }

    private void PlayVideoFile(string path)
    {
        if (!_video.TryPlay(path))
        {
            _log($"dtrh-fx: video payload — backend refused {DescribeMedia(path)}");
            return;
        }

        Interlocked.Exchange(ref _videoActive, 1);
        VideoStarted?.Invoke(this, EventArgs.Empty);
        _log($"dtrh-fx: video playing ({DescribeMedia(path)}, cap {_options.VideoSegmentCapSec:0}s)");
        _videoCapTimer?.Dispose();
        _videoCapTimer = _clock.Schedule(
            TimeSpan.FromSeconds(_options.VideoSegmentCapSec),
            () =>
            {
                _log("dtrh-fx: video segment cap reached — stopping");
                StopVideo();
            });
    }

    /// <summary>Media-description for logs (packet framing c, SP-018 V5 class): files
    /// under a <see cref="DtrhNativeEffectsOptions.PresenceOnlyRoots"/> root log
    /// presence+shape (bytes + extension class), NEVER a filename; everything else
    /// (payload/staged scope, SP-025) keeps the bare filename.</summary>
    private string DescribeMedia(string path)
    {
        foreach (var root in _options.PresenceOnlyRoots)
        {
            if (path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                long bytes;
                try { bytes = new FileInfo(path).Length; } catch { bytes = -1; }
                return $"user pool ({bytes} bytes, {Path.GetExtension(path).ToLowerInvariant()} class)";
            }
        }

        return Path.GetFileName(path);
    }

    /// <summary>Enumerate the native video pool: *.mp4/*.webm/*.m4v under the media roots,
    /// overlay-first by file name (the loopback §4 serving order mirrored host-side).</summary>
    private IReadOnlyList<string> EnumerateVideoPool()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Payload first, overlay second so overlay wins same-name shadowing.
        foreach (var root in _options.VideoRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var pattern in new[] { "*.mp4", "*.webm", "*.m4v" })
            {
                foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                {
                    byName[Path.GetFileName(file)] = file;
                }
            }
        }

        return byName.Values.ToList();
    }

    /// <summary>audio payload (EffectPayload.cs:183-196): a one-shot subliminal whisper on
    /// the exclusive voice channel. NOT gated by VN-speaking (WPF gates sfx + barks only;
    /// the fire-payload path has no VN check — mirrored exactly).</summary>
    private void FireWhisper()
    {
        var pool = EnumerateWhisperPool();
        if (pool.Count == 0)
        {
            _log("dtrh-fx: whisper payload — voice pool empty, silent no-op");
            return;
        }

        PlayWhisper(pool[Random.Shared.Next(pool.Count)]);
    }

    private IReadOnlyList<string> EnumerateWhisperPool()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _options.WhisperRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "sub_*.mp3", SearchOption.TopDirectoryOnly))
            {
                byName[Path.GetFileName(file)] = file;
            }
        }

        return byName.Values.ToList();
    }

    /// <summary>HARNESS-ONLY (--dtrh-fx-drive whisper-file step): play ONE NAMED pool
    /// whisper (e.g. a staged long clip) through the same voice channel — pool-resolved,
    /// never an arbitrary path.</summary>
    public void PlayWhisperFromPool(string fileName)
    {
        var match = EnumerateWhisperPool()
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            _log($"dtrh-fx: whisper-file '{fileName}' not in the voice pool — harness no-op");
            return;
        }

        PlayWhisper(match);
    }

    /// <summary>Voice channel: exclusive, stop-replace newest-wins, generation token (F2 —
    /// a stale player's PlaybackEnded must never clear the live whisper; SoundFlow does not
    /// fire end-on-stop but the token keeps the rule backend-independent).</summary>
    public void PlayWhisper(string path)
    {
        var generation = Interlocked.Increment(ref _voiceGeneration);
        var old = _voice;
        _voice = null;
        if (old is not null)
        {
            try { old.Stop(); } catch { /* best-effort */ }
            try { old.Dispose(); } catch { /* best-effort */ }
        }

        var player = _audio.CreatePlayer(path, Volume(1.0));
        player.PlaybackEnded += (_, _) =>
        {
            // Identity filter (F2): only the LIVE player may clear the channel.
            if (ReferenceEquals(player, _voice) && Interlocked.Read(ref _voiceGeneration) == generation)
            {
                _voice = null;
                _log("dtrh-fx: whisper completed (backend PlaybackEnded)");
            }

            try { player.Dispose(); } catch { /* best-effort */ }
        };
        _voice = player;
        player.Play();
        // Filename logging is PAYLOAD/OVERLAY-scope only (shipped + harness-staged clips).
        // b4's user/mod media admission must gate these lines to presence+shape (SP-018 V5 class).
        _log($"dtrh-fx: whisper playing ({DescribeMedia(path)})");
    }

    // ============================ VN mix gate ============================

    /// <summary>vn-speaking {on} (DtrhHostService.cs:218-221): while she speaks, the mix is
    /// hers — native SFX stand down. Never carried stale into a run/session
    /// (`:252` parity via <see cref="NotifyRunBoundary"/>).</summary>
    public void SetVnSpeaking(bool on)
    {
        if (_vnSpeaking == on) return;
        _vnSpeaking = on;
        _log($"dtrh-fx: vn-speaking {(on ? "on — SFX stand down" : "off — mix released")}");
    }

    // ============================ world freeze ============================

    /// <summary>freeze-state {on} (DtrhHostService.cs:671-698): the in-world Freeze bubble
    /// stops the field, so stop the REAL world with it — pause the covering video and the
    /// spoken whisper for the freeze window, resume both when it lifts. Idempotent dedup
    /// (`:675-677`); force-resumed at run boundaries and teardown so a clip can NEVER
    /// wedge paused (`:259`, `:513`, `:896`).</summary>
    public void SetWorldFrozen(bool on)
    {
        if (on == _worldFrozen) return;
        _worldFrozen = on;
        try
        {
            if (on)
            {
                _video.SetPaused(true);
                PauseVoice();
            }
            else
            {
                _video.SetPaused(false);
                ResumeVoice();
            }
        }
        catch (Exception ex)
        {
            _log($"dtrh-fx: freeze {(on ? "on" : "off")} apply failed ({ex.GetType().Name})");
        }

        _log($"dtrh-fx: world freeze {(on ? $"ON — native video + voice paused (video pos {_video.PositionSec:0.0}s, voice pos {(_voice?.PositionSec ?? -1):0.0}s, frames {_video.FrameCount})" : $"OFF — resumed (video pos {_video.PositionSec:0.0}s, voice pos {(_voice?.PositionSec ?? -1):0.0}s, frames {_video.FrameCount})")}");
    }

    private void PauseVoice()
    {
        if (_voice is { State: DtrhPlayerState.Playing } v) v.Pause();
    }

    private void ResumeVoice()
    {
        if (_voice is { State: DtrhPlayerState.Paused } v) v.Play();
    }

    // ============================ lifecycle ============================

    /// <summary>Run-boundary hygiene (WPF run-started `:252`/`:259` + run-ended `:513`
    /// parity): a stale freeze or stale VN duck must never bleed across a run boundary.
    /// Wired by the host dispatcher for run-started/run-ended in b3 (those messages stay
    /// classified Deferred(b4) — the hygiene rides alongside the typed deferral).</summary>
    public void NotifyRunBoundary()
    {
        SetVnSpeaking(false);
        SetWorldFrozen(false);
    }

    /// <summary>Session start (Launch `:71` parity): every host session begins unfrozen.</summary>
    public void NotifySessionStart()
    {
        _vnSpeaking = false;
        SetWorldFrozen(false);
    }

    /// <summary>Stop the covering video (segment cap, window close, teardown). Backend
    /// Stop runs off the UI thread inside the backend (libvlc vmem deadlock class). A
    /// stopped video raises VideoEnded too (WPF parity: the page's payload-state off +
    /// focus reclaim ride the video CLOSING, not only natural EndReached).</summary>
    public void StopVideo()
    {
        _videoCapTimer?.Dispose();
        _videoCapTimer = null;
        _video.Stop();
        if (Interlocked.Exchange(ref _videoActive, 0) == 1)
        {
            _log("dtrh-fx: video stopped — payload-state off");
            VideoEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Teardown (DisposeAll `:896` parity): NEVER leave a video or whisper wedged
    /// paused if the window dies mid-freeze — force-resume both, then stop + dispose.</summary>
    public void Teardown()
    {
        if (_tornDown) return;
        _tornDown = true;
        if (_worldFrozen)
        {
            _worldFrozen = false;
            try { _video.SetPaused(false); } catch { /* unwedge best-effort */ }
            try { ResumeVoice(); } catch { /* unwedge best-effort */ }
            _log("dtrh-fx: teardown mid-freeze — force-resumed video + voice (unwedge)");
        }

        StopVideo();
        var voice = _voice;
        _voice = null;
        if (voice is not null)
        {
            try { voice.Stop(); } catch { /* best-effort */ }
            try { voice.Dispose(); } catch { /* best-effort */ }
        }

        lock (_gate)
        {
            foreach (var p in _sfxPool)
            {
                try { p.Stop(); } catch { /* best-effort */ }
                try { p.Dispose(); } catch { /* best-effort */ }
            }

            _sfxPool.Clear();
        }

        _log("dtrh-fx: teardown complete");
    }

    public void Dispose()
    {
        Teardown();
        _video.PlaybackEnded -= OnVideoEnded;
        _video.EncounteredError -= OnVideoError;
    }

    private void OnVideoEnded(object? sender, EventArgs e)
    {
        _videoCapTimer?.Dispose();
        _videoCapTimer = null;
        Interlocked.Exchange(ref _videoActive, 0);
        _log($"dtrh-fx: video ended (backend EndReached, pos {_video.PositionSec:0.0}s, frames {_video.FrameCount})");
        VideoEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnVideoError(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref _videoActive, 0);
        _log("dtrh-fx: video backend error — closing the covering video");
        VideoEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>master × scale clamped 0..1 (ChaosSfx.cs:96-103 parity; master = the init
    /// settings.masterVolume, 0..100).</summary>
    private float Volume(double scale) =>
        (float)Math.Clamp(_options.MasterVolume / 100.0 * scale, 0.0, 1.0);
}

/// <summary>One audited WPF ChaosSfx fallback chain (SP-051). <see cref="Candidates"/> are
/// flat basenames in override→fallback order; <see cref="WpfChain"/> keeps the WPF-relative
/// spelling for gap logs. <see cref="FixedScale"/> null = the page-supplied scale passes
/// through (WPF generic path, ChaosSfx.cs:47); a value = the WPF helper's fixed scale.</summary>
public sealed record ChaosSfxChain(string[] Candidates, double? FixedScale, string WpfChain, string Cite);

/// <summary>The typed outcome of resolving one sfx cue (SP-051): either a pool
/// <see cref="Path"/> + effective <see cref="Scale"/>, or an unplayed cue whose
/// <see cref="GapNote"/> names the content gap with its WPF chain cited (null note = an
/// unlisted/empty cue — the plain silent no-op).</summary>
public readonly record struct ChaosSfxResolution(string? Path, double Scale, string? GapNote)
{
    public bool IsResolved => Path is not null;
}

/// <summary>Knobs + media roots for <see cref="DtrhNativeEffects"/>.</summary>
public sealed record DtrhNativeEffectsOptions
{
    /// <summary>SFX pool roots, overlay-first (payload assets/bubbles/sfx + overlay shadow).</summary>
    public required IReadOnlyList<string> SfxRoots { get; init; }

    /// <summary>Video pool roots (media trees enumerated for *.mp4/*.webm/*.m4v).</summary>
    public required IReadOnlyList<string> VideoRoots { get; init; }

    /// <summary>b4 media-logging rule (packet framing c; SP-018 V5 class): any resolved
    /// file under one of these roots logs PRESENCE+SHAPE ONLY (bytes + extension class) —
    /// never a filename. The user-media root lives here; payload/overlay roots keep names
    /// (SP-025's recorded payload/staged scope).</summary>
    public IReadOnlyList<string> PresenceOnlyRoots { get; init; } = [];

    /// <summary>Whisper pool roots (payload assets/bubbles/voices + overlay shadow).</summary>
    public required IReadOnlyList<string> WhisperRoots { get; init; }

    /// <summary>init settings.masterVolume (0..100; b2 currently sends 80).</summary>
    public int MasterVolume { get; init; } = 80;

    /// <summary>SFX bound — packet decree 8 (WPF ChaosSfx cap 6 recorded as incumbent parity).</summary>
    public int MaxSfxVoices { get; init; } = 8;

    /// <summary>Covering-video segment cap in seconds (EffectPayload SEGMENT_SEC=15 parity).</summary>
    public double VideoSegmentCapSec { get; init; } = 15;
}

/// <summary>Player states mirrored from the backend (SoundFlow PlaybackState names kept).</summary>
public enum DtrhPlayerState
{
    Playing,
    Paused,
    Stopped,
}

/// <summary>One audio player on a channel (SFX pool entry or the exclusive voice).</summary>
public interface IDtrhAudioPlayer : IDisposable
{
    /// <summary>Backend-emitted completion — NEVER fires on explicit Stop on SoundFlow
    /// (SP-017 A2 backend behavior fact); the effects layer still identity-filters (F2).</summary>
    event EventHandler? PlaybackEnded;

    DtrhPlayerState State { get; }

    double PositionSec { get; }

    void Play();

    void Pause();

    void Stop();
}

/// <summary>The audio backend seam (real = SoundFlow 1.4.1; tests = recording fake).</summary>
public interface IDtrhAudioBackend : IDisposable
{
    /// <summary>Initialise the playback device. F1 discipline (SP-017, process-fatal crash
    /// class): RE-ENUMERATE immediately before init, match the requested device by NAME,
    /// pass only a FRESH enumeration snapshot's DeviceInfo — never a stored one; persist
    /// the NAME, never the Id. null/missing name → default device.</summary>
    bool TryInit(string? deviceName, out string? error);

    /// <summary>Create (not yet playing) a player for a local audio file at a gain 0..1.</summary>
    IDtrhAudioPlayer CreatePlayer(string path, float volume);
}

/// <summary>The native video backend seam (real = libvlc vmem; tests = recording fake).</summary>
public interface IDtrhVideoBackend
{
    /// <summary>Frame-level decode/presentation proof: vmem display callback count.</summary>
    long FrameCount { get; }

    /// <summary>Decoder-reported position in seconds (freeze evidence: must not advance while paused).</summary>
    double PositionSec { get; }

    /// <summary>The current presented frame (UI-thread-swapped; null before the first frame).</summary>
    Avalonia.Media.Imaging.WriteableBitmap? CurrentFrame { get; }

    event EventHandler? FramePresented;

    event EventHandler? PlaybackEnded;

    event EventHandler? EncounteredError;

    /// <summary>Stop-replace: play a local video file. False = backend refused/unavailable.</summary>
    bool TryPlay(string path);

    /// <summary>Position-preserving pause/resume (VideoService.cs:249-265 SetPause parity).</summary>
    void SetPaused(bool paused);

    /// <summary>Stop playback (keeps the app-lifetime player; runs off the UI thread).</summary>
    void Stop();
}
