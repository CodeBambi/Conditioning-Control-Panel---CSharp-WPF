namespace CcpClient.Desktop.Audio;

/// <summary>The arbitration channels (SP-017 §6 channel ownership — ONE generic player REJECTED).</summary>
public enum SoundChannel
{
    /// <summary>Companion voice: exclusive, stop-replace newest-wins + generation token.</summary>
    Voice,

    /// <summary>Whisper/subliminal: exclusive, stop-replace, real-event busy flag.</summary>
    Whisper,

    /// <summary>One-shot effects: bounded pool, drop-on-overflow, fire-and-forget.</summary>
    Sfx,
}

/// <summary>Why a request was dropped (typed — drops are never silent, SP-017 discipline).</summary>
public enum SoundDropReason
{
    /// <summary>SFX pool at capacity — "a one-shot SFX played late is worse than silence" (ChaosSfx.cs:91-107).</summary>
    PoolOverflow,

    /// <summary>Queued item exceeded its caller-supplied freshness window before starting.</summary>
    Stale,

    /// <summary>Queued item discarded by a priority preemption (WPF GigglePriority clear-all, AvatarTubeWindow.Speech.cs:319-360).</summary>
    Preempted,

    /// <summary>Arbitration torn down / panic-reset before the item started.</summary>
    Disposed,
}

/// <summary>Typed outcomes for every arbitration request — nothing is silently dropped.</summary>
public abstract record SoundOutcome
{
    private SoundOutcome() { }

    /// <summary>Playback device is up on the named endpoint (a fresh enumeration snapshot's NAME).</summary>
    public sealed record Ready(string DeviceName) : SoundOutcome;

    /// <summary>Playback started on a channel; Generation identifies the active player (completion events are generation-filtered).</summary>
    public sealed record Started(SoundChannel Channel, long Generation) : SoundOutcome;

    /// <summary>Voice request queued behind the active line (Depth after enqueue).</summary>
    public sealed record Queued(SoundChannel Channel, int Depth) : SoundOutcome;

    /// <summary>Request dropped for a typed reason (logged at the drop site).</summary>
    public sealed record Dropped(SoundChannel Channel, SoundDropReason Reason) : SoundOutcome;

    /// <summary>Audio unavailable (no endpoints / not initialised / torn down) — WPF "audio disabled for the session" parity (AudioService.cs:129-131).</summary>
    public sealed record Unavailable(string Reason) : SoundOutcome;

    /// <summary>Backend failure (typed error, logged).</summary>
    public sealed record Failed(string Error) : SoundOutcome;
}

/// <summary>Result of a duck acquisition (WPF Duck/Undock symmetry, AudioService.cs:766-906).</summary>
public sealed record DuckAttempt(bool Held, IDuckHandle? Handle, string? Error);

/// <summary>One reference-counted duck hold. Dispose releases; stale-generation releases are ignored (WPF :892-898).</summary>
public interface IDuckHandle : IDisposable
{
    /// <summary>The duck generation this handle belongs to.</summary>
    long Generation { get; }
}

/// <summary>Tuning surface for <see cref="SoundArbitration"/> (every default WPF-cited or packet-decreed).</summary>
public sealed class SoundArbitrationOptions
{
    /// <summary>SFX pool bound = 8 (SP-025 packet decree; WPF ChaosSfx cap-6 cited, ChaosSfx.cs:91-107).</summary>
    public int MaxSfxVoices { get; init; } = 8;

    /// <summary>Duck watchdog: force-unduck after this hold (WPF DuckWatchdogMs 300_000, AudioService.cs:39).</summary>
    public TimeSpan DuckWatchdog { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Default gap between the end of one voice line and the start of the next queued line (WPF MinSpeechDelaySeconds 2.0, AvatarTubeWindow.Speech.cs:112-119).</summary>
    public TimeSpan VoicePacingDelay { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// The APP-WIDE sound arbitration core (SP-029 slice q1) on the SP-017-selected SoundFlow
/// backend. Channel ownership (spike §6, owner-ratified 2026-07-21):
/// voice = exclusive stop-replace + generation token; whisper = exclusive with real-event
/// busy; SFX = bounded pool (8), drop-on-overflow typed. Plus ordinary/priority voice
/// queueing with caller-supplied freshness, reference-counted ducking (machinery against
/// the <see cref="IAudioDuckSink"/> seam), the device re-probe discipline, and panic cleanup.
///
/// WPF parity anchors (READ-ONLY evidence, SP-029 record.md Step 1): stop-replace +
/// identity filter (AvatarTubeWindow.Speech.cs:473,:1594,:1623-1632); priority = clear-all
/// + play-now (:319-360); pacing 2.0 s floor (:112-119,148-164); WPF has NO ms-age queue
/// expiry — freshness here is mechanism-only, policy values are q2's; whisper busy replaces
/// the WPF duration estimate (AudioService.cs:750-758) with the real completion signal the
/// spike proved (A5); ducking refcount/watchdog/ForceUnduck (AudioService.cs:766-1033);
/// device fallback chain (:86-361); SFX drop-on-overflow (ChaosSfx.cs:91-107).
///
/// DTRH boundary: Features/Dtrh/DtrhNativeEffects + SoundFlowDtrhAudio stay the DTRH-local
/// owners (separate engine/device; miniaudio devices coexist) — NOT routed through this
/// core in q1 (packet decree; future row may refactor).
/// </summary>
public sealed class SoundArbitration : IDisposable
{
    private readonly object _gate = new();
    private readonly IAudioBackend _backend;
    private readonly IAudioDuckSink _duckSink;
    private readonly ISoundClock _clock;
    private readonly SoundArbitrationOptions _options;
    private readonly Action<string> _log;

    private bool _initialized;
    private bool _audioDisabledForSession;
    private string? _preferredDeviceName;
    private string? _activeDeviceName;

    // Voice channel (exclusive stop-replace + generation token).
    private IAudioPlayer? _voice;
    private long _voiceGeneration;
    private readonly Queue<VoiceRequest> _voiceQueue = new();
    private DateTimeOffset _lastVoiceEndUtc;
    private bool _voiceStartScheduled;
    private IDisposable? _pacingTimer;

    // Whisper channel (exclusive stop-replace + real-event busy).
    private IAudioPlayer? _whisper;
    private long _whisperGeneration;
    private bool _whisperBusy;

    // SFX pool (bounded, drop-on-overflow).
    private readonly List<IAudioPlayer> _sfxPool = [];

    // Ducking (reference-counted + generation + watchdog).
    private int _duckCount;
    private long _duckGeneration;
    private bool _duckApplied;
    private IDisposable? _duckWatchdog;

    private bool _tornDown;

    public SoundArbitration(
        IAudioBackend backend,
        IAudioDuckSink duckSink,
        ISoundClock clock,
        SoundArbitrationOptions options,
        Action<string> log)
    {
        _backend = backend;
        _duckSink = duckSink;
        _clock = clock;
        _options = options;
        _log = log;
    }

    private sealed record VoiceRequest(string Path, float Gain, TimeSpan? Freshness, TimeSpan Pacing, DateTimeOffset EnqueuedUtc);

    // ---------- events (raised OUTSIDE the gate) ----------

    /// <summary>Current-generation voice line ended NATURALLY (backend event, generation-filtered — interruption never surfaces here).</summary>
    public event Action<long>? VoiceCompleted;

    /// <summary>Whisper busy flag changed (set at play; cleared ONLY by the real completion event, stop-replace failure, or panic — spike A5 shape).</summary>
    public event Action<bool>? WhisperBusyChanged;

    // ---------- state ----------

    /// <summary>Whisper channel busy (real-event driven — replaces the WPF duration estimate).</summary>
    public bool WhisperBusy { get { lock (_gate) { return _whisperBusy; } } }

    /// <summary>Active SFX voices in the bounded pool.</summary>
    public int ActiveSfxVoices { get { lock (_gate) { return _sfxPool.Count; } } }

    /// <summary>Queued voice lines.</summary>
    public int QueuedVoiceCount { get { lock (_gate) { return _voiceQueue.Count; } } }

    /// <summary>
    /// A voice line is actively playing (SP-032 q2 addition — the anti-stale gate's
    /// "speaking" state, WPF BarkService.cs:1358-1365; outcome-mapping: WPF's IsSpeaking is
    /// bubble-visible and greenfield has no bubble, so voice-player-active is the analog).
    /// </summary>
    public bool VoiceActive { get { lock (_gate) { return _voice is not null; } } }

    /// <summary>Outstanding duck holds.</summary>
    public int DuckCount { get { lock (_gate) { return _duckCount; } } }

    /// <summary>Audio disabled for the session (no endpoints / init failure — WPF :129-131 parity).</summary>
    public bool AudioDisabledForSession { get { lock (_gate) { return _audioDisabledForSession; } } }

    /// <summary>The endpoint the device is up on (fresh-snapshot NAME; null before init).</summary>
    public string? ActiveDeviceName { get { lock (_gate) { return _activeDeviceName; } } }

    // ---------- device layer (F1 re-probe discipline) ----------

    /// <summary>Fresh render-endpoint NAMEs (re-enumerated per call — names, never Ids; session facts only).</summary>
    public IReadOnlyList<string> EnumerateDevices() => _backend.EnumerateDevices();

    /// <summary>
    /// Bring the playback device up with the re-probe discipline (spike F1 + named limit 9's
    /// mechanism): the requested NAME is matched against a FRESH enumeration; absent → typed
    /// fallback to default (WPF AudioService.cs:292-293); zero endpoints → audio disabled for
    /// the session (WPF :129-131); the backend itself re-enumerates again immediately before
    /// native init and passes only the fresh snapshot's DeviceInfo.
    /// </summary>
    public SoundOutcome Initialize(string? preferredDeviceName)
    {
        lock (_gate)
        {
            if (_tornDown)
            {
                return new SoundOutcome.Unavailable("arbitration torn down");
            }
        }

        var devices = _backend.EnumerateDevices();
        _log($"sound: {devices.Count} render endpoint(s): {string.Join(" | ", devices)}");
        if (devices.Count == 0)
        {
            lock (_gate) { _audioDisabledForSession = true; }
            _log("sound: no render endpoints — audio disabled for the session (AudioService.cs:129-131 parity)");
            return new SoundOutcome.Unavailable("no render endpoints — audio disabled for session");
        }

        string? effective = null;
        if (!string.IsNullOrEmpty(preferredDeviceName))
        {
            if (devices.Any(d => string.Equals(d, preferredDeviceName, StringComparison.OrdinalIgnoreCase)))
            {
                effective = devices.First(d => string.Equals(d, preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Stale NAME (persisted device removed) → typed fallback to default; never
                // a stored Id (F1: unvalidated Id = process-fatal native crash).
                _log($"sound: preferred device '{preferredDeviceName}' not in fresh enumeration — falling back to default (AudioService.cs:292-293 parity)");
            }
        }

        if (!_backend.TryInit(effective, out var error))
        {
            lock (_gate) { _audioDisabledForSession = true; }
            _log($"sound: device init failed ({error}) — audio disabled for the session");
            return new SoundOutcome.Failed(error ?? "device init failed");
        }

        lock (_gate)
        {
            _initialized = true;
            _audioDisabledForSession = false;
            _preferredDeviceName = preferredDeviceName;
            _activeDeviceName = effective ?? "(default)";
        }
        _log($"sound: playback device up (requested {preferredDeviceName ?? "default"})");
        return new SoundOutcome.Ready(_activeDeviceName!);
    }

    /// <summary>
    /// Change the preferred device NAME and re-probe. In-flight channels are stopped first
    /// (typed, logged) — mid-playback device switch is a named limit (spike limit 10:
    /// SoundFlow SwitchDevice exists, untested; hot-plug UX = q2).
    /// </summary>
    public SoundOutcome SetPreferredDevice(string? deviceName)
    {
        StopAllChannels("device change");
        return Initialize(deviceName);
    }

    // ---------- voice channel ----------

    /// <summary>Immediate voice stop-replace (newest-wins): stops the current line, plays now. Queue is preserved.</summary>
    public SoundOutcome PlayVoice(string path, float gain)
    {
        var player = CreatePlayer(SoundChannel.Voice, path, gain, out var unavailable);
        if (player is null)
        {
            return unavailable!;
        }

        IAudioPlayer? old;
        long gen;
        lock (_gate)
        {
            gen = ++_voiceGeneration;
            old = _voice;
            _voice = player;
            CancelPacingTimerLocked();
            WireVoiceEnded(player, gen);
        }
        StopDispose(old);
        if (!TryStart(player, SoundChannel.Voice, out var startFailure))
        {
            return startFailure!;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_voice, player))
            {
                // Panic/stop-replace landed during start — never leave a player arbitration no longer owns.
                StopDispose(player);
            }
        }
        return new SoundOutcome.Started(SoundChannel.Voice, gen);
    }

    /// <summary>
    /// Queue an ordinary voice line (FIFO). Starts immediately when the channel is idle and
    /// the pacing debt (WPF 2.0 s floor, AvatarTubeWindow.Speech.cs:112-119) has elapsed.
    /// <paramref name="freshness"/> is the caller-supplied max queue age — WPF has NO ms-age
    /// expiry (its freshness is gate-level anti-stale, BarkService.cs:1359-1363); the core
    /// provides the mechanism, q2 owns policy values. Expired at start time → typed
    /// Dropped(Stale), logged, next item tried.
    /// </summary>
    public SoundOutcome QueueVoice(string path, float gain, TimeSpan? freshness = null, TimeSpan? pacing = null)
    {
        lock (_gate)
        {
            if (!ReadyLocked(out var reason))
            {
                return new SoundOutcome.Unavailable(reason);
            }

            _voiceQueue.Enqueue(new VoiceRequest(path, gain, freshness, pacing ?? _options.VoicePacingDelay, _clock.UtcNow));
            var depth = _voiceQueue.Count;
            ScheduleNextVoiceLocked();
            return new SoundOutcome.Queued(SoundChannel.Voice, depth);
        }
    }

    /// <summary>Priority voice: clear ALL queued ordinary lines (typed Preempted, logged) + stop-replace + play now (WPF GigglePriority, AvatarTubeWindow.Speech.cs:319-360).</summary>
    public SoundOutcome PlayVoicePriority(string path, float gain)
    {
        lock (_gate)
        {
            var cleared = _voiceQueue.Count;
            _voiceQueue.Clear();
            if (cleared > 0)
            {
                _log($"sound: voice queue cleared ({cleared}) by priority preempt — dropped (preempted) (:319-360 parity)");
            }
        }
        return PlayVoice(path, gain);
    }

    // ---------- whisper channel ----------

    /// <summary>Whisper stop-replace: one active player; busy set at play, cleared ONLY by the real completion event (or stop/failure/panic).</summary>
    public SoundOutcome PlayWhisper(string path, float gain)
    {
        var player = CreatePlayer(SoundChannel.Whisper, path, gain, out var unavailable);
        if (player is null)
        {
            return unavailable!;
        }

        IAudioPlayer? old;
        long gen;
        bool raiseBusy = false;
        bool raiseBusyFalse = false;
        lock (_gate)
        {
            gen = ++_whisperGeneration;
            old = _whisper;
            _whisper = player;
            WireWhisperEnded(player, gen);
            if (!_whisperBusy)
            {
                _whisperBusy = true;
                raiseBusy = true;
            }
        }
        StopDispose(old);
        if (!TryStart(player, SoundChannel.Whisper, out var startFailure))
        {
            lock (_gate)
            {
                if (ReferenceEquals(_whisper, player))
                {
                    _whisper = null;
                    if (_whisperBusy)
                    {
                        _whisperBusy = false;
                        raiseBusyFalse = true;
                    }
                }
            }
            if (raiseBusyFalse)
            {
                WhisperBusyChanged?.Invoke(false);
            }
            return startFailure!;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_whisper, player))
            {
                // Panic/stop-replace landed during start — never leave a player arbitration no longer owns.
                StopDispose(player);
            }
        }
        if (raiseBusy)
        {
            WhisperBusyChanged?.Invoke(true);
        }
        return new SoundOutcome.Started(SoundChannel.Whisper, gen);
    }

    // ---------- SFX pool ----------

    /// <summary>
    /// Fire-and-forget one-shot. Bounded pool (<see cref="SoundArbitrationOptions.MaxSfxVoices"/>);
    /// at capacity the cue is DROPPED, never queued (typed + logged — ChaosSfx.cs:91-107
    /// parity). Slots reclaim on the real backend completion event.
    /// </summary>
    public SoundOutcome PlaySfx(string path, float gain)
    {
        lock (_gate)
        {
            if (!ReadyLocked(out var reason))
            {
                return new SoundOutcome.Unavailable(reason);
            }

            if (_sfxPool.Count >= _options.MaxSfxVoices)
            {
                _log($"sound: sfx pool full ({_options.MaxSfxVoices}) — dropping cue (ChaosSfx.cs:91-107 parity)");
                return new SoundOutcome.Dropped(SoundChannel.Sfx, SoundDropReason.PoolOverflow);
            }
        }

        IAudioPlayer player;
        try
        {
            player = _backend.CreatePlayer(path, gain);
        }
        catch (Exception ex)
        {
            _log($"sound: sfx player construction failed ({ex.GetType().Name}: {ex.Message})");
            return new SoundOutcome.Failed($"{ex.GetType().Name}: {ex.Message}");
        }

        lock (_gate)
        {
            if (_tornDown || _sfxPool.Count >= _options.MaxSfxVoices)
            {
                // Lost the race (teardown or overflow between check and create) — typed, never silent.
                _log(_tornDown
                    ? "sound: sfx cue dropped (disposed) — arbitration torn down during construction"
                    : $"sound: sfx pool full ({_options.MaxSfxVoices}) — dropping cue (race)");
                StopDispose(player);
                return new SoundOutcome.Dropped(SoundChannel.Sfx, _tornDown ? SoundDropReason.Disposed : SoundDropReason.PoolOverflow);
            }

            _sfxPool.Add(player);
            player.PlaybackEnded += OnSfxEnded;
        }
        if (!TryStart(player, SoundChannel.Sfx, out var startFailure))
        {
            lock (_gate)
            {
                _sfxPool.Remove(player);
            }
            StopDispose(player);
            return startFailure!;
        }

        lock (_gate)
        {
            if (!_sfxPool.Contains(player))
            {
                // Panic landed during start — never leave a player arbitration no longer owns.
                StopDispose(player);
            }
        }
        return new SoundOutcome.Started(SoundChannel.Sfx, 0);
    }

    // ---------- ducking (reference-counted machinery; mechanism via IAudioDuckSink) ----------

    /// <summary>
    /// Acquire one duck hold (fraction REMOVED 0..1). First holder applies the sink (first
    /// strength wins — WPF AudioService.cs:778); overlapping holders just bump the count
    /// (WPF :774-776); a 5-min watchdog force-releases (WPF DuckWatchdogMs, :39); sink
    /// failure = not-held, symmetric (WPF :869-873).
    /// </summary>
    public DuckAttempt AcquireDuck(float strength)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        lock (_gate)
        {
            if (_tornDown)
            {
                return new DuckAttempt(false, null, "arbitration torn down");
            }

            if (_duckCount == 0 && !_duckApplied)
            {
                if (!_duckSink.TryApply(strength, out var error))
                {
                    _log($"sound: duck not applied ({error}) — hold not counted (AudioService.cs:869-873 parity)");
                    return new DuckAttempt(false, null, error);
                }

                _duckApplied = true;
                ArmDuckWatchdogLocked();
            }

            _duckCount++;
            return new DuckAttempt(true, new DuckHandle(this, _duckGeneration), null);
        }
    }

    /// <summary>
    /// Panic release-all: invalidate every outstanding handle (generation bump — stale
    /// releases become no-ops, WPF :892-898) and restore once (WPF ForceUnduck, :1024-1033;
    /// "panic key / app exit"). Idempotent.
    /// </summary>
    public void ForceUnduck()
    {
        lock (_gate)
        {
            _duckGeneration++;
            if (_duckCount == 0 && !_duckApplied)
            {
                return;
            }

            _log($"sound: force-unduck ({_duckCount} hold(s) released)");
            _duckCount = 0;
            _duckWatchdog?.Dispose();
            _duckWatchdog = null;
            RestoreDuckLocked();
        }
    }

    // ---------- panic ----------

    /// <summary>
    /// Panic cleanup: stop+dispose EVERY channel player, clear the voice queue, clear whisper
    /// busy, force-release all ducks, bump all generations so in-flight backend callbacks
    /// become stale no-ops. Idempotent and callback-race safe (pre-approach consult binding).
    /// WPF had no single StopAll entry (only ForceUnduck + per-channel stops separately) —
    /// this composes those WPF outcomes into one path (outcome parity, not divergence).
    /// </summary>
    public void PanicReset()
    {
        List<IAudioPlayer> toDispose;
        int cleared;
        bool wasWhisperBusy;
        lock (_gate)
        {
            _voiceGeneration++;
            _whisperGeneration++;
            toDispose = [];
            if (_voice is not null)
            {
                toDispose.Add(_voice);
                _voice = null;
            }

            if (_whisper is not null)
            {
                toDispose.Add(_whisper);
                _whisper = null;
            }

            toDispose.AddRange(_sfxPool);
            _sfxPool.Clear();
            cleared = _voiceQueue.Count;
            _voiceQueue.Clear();
            CancelPacingTimerLocked();
            wasWhisperBusy = _whisperBusy;
            _whisperBusy = false;
        }

        ForceUnduck();
        foreach (var player in toDispose)
        {
            StopDispose(player);
        }

        _log($"sound: panic-reset — stopped {toDispose.Count} player(s), cleared {cleared} queued line(s), ducks force-released");
        if (wasWhisperBusy)
        {
            WhisperBusyChanged?.Invoke(false);
        }
    }

    // ---------- internals ----------

    private bool ReadyLocked(out string reason)
    {
        if (_tornDown)
        {
            reason = "arbitration torn down";
            return false;
        }

        if (!_initialized)
        {
            reason = "not initialised";
            return false;
        }

        if (_audioDisabledForSession)
        {
            reason = "audio disabled for the session";
            return false;
        }

        reason = "";
        return true;
    }

    private IAudioPlayer? CreatePlayer(SoundChannel channel, string path, float gain, out SoundOutcome? unavailable)
    {
        lock (_gate)
        {
            if (!ReadyLocked(out var reason))
            {
                unavailable = new SoundOutcome.Unavailable(reason);
                return null;
            }
        }

        try
        {
            unavailable = null;
            return _backend.CreatePlayer(path, gain);
        }
        catch (Exception ex)
        {
            _log($"sound: {channel} player construction failed ({ex.GetType().Name}: {ex.Message})");
            unavailable = new SoundOutcome.Failed($"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void WireVoiceEnded(IAudioPlayer player, long gen)
    {
        player.PlaybackEnded += (_, _) =>
        {
            List<Action>? after = null;
            lock (_gate)
            {
                // Generation filter (F2 class): a replaced/stale player's events can never
                // clear or complete the newer line's state (WPF ReferenceEquals discipline,
                // AvatarTubeWindow.Speech.cs:1623-1632).
                if (gen != _voiceGeneration || _tornDown)
                {
                    return;
                }

                _voice = null;
                _lastVoiceEndUtc = _clock.UtcNow;
                after = [() => VoiceCompleted?.Invoke(gen)];
                ScheduleNextVoiceLocked();
            }

            StopDispose(player);
            foreach (var action in after)
            {
                action();
            }
        };
    }

    private void WireWhisperEnded(IAudioPlayer player, long gen)
    {
        player.PlaybackEnded += (_, _) =>
        {
            var raise = false;
            lock (_gate)
            {
                if (gen != _whisperGeneration || _tornDown)
                {
                    return;
                }

                _whisper = null;
                if (_whisperBusy)
                {
                    _whisperBusy = false;
                    raise = true;
                }
            }

            StopDispose(player);
            if (raise)
            {
                WhisperBusyChanged?.Invoke(false);
            }
        };
    }

    private void OnSfxEnded(object? sender, EventArgs e)
    {
        var player = (IAudioPlayer)sender!;
        lock (_gate)
        {
            _sfxPool.Remove(player);
        }

        StopDispose(player);
    }

    private void ScheduleNextVoiceLocked()
    {
        if (_voiceStartScheduled || _voice is not null || _voiceQueue.Count == 0 || _tornDown)
        {
            return;
        }

        var head = _voiceQueue.Peek();
        var due = (_lastVoiceEndUtc + head.Pacing) - _clock.UtcNow;
        _voiceStartScheduled = true;
        _pacingTimer = _clock.Schedule(due, OnPacingFire);
    }

    private void OnPacingFire()
    {
        IAudioPlayer? player = null;
        lock (_gate)
        {
            _voiceStartScheduled = false;
            _pacingTimer = null;
            if (_tornDown || _voice is not null)
            {
                return;
            }

            while (_voiceQueue.Count > 0)
            {
                var next = _voiceQueue.Dequeue();
                if (next.Freshness is { } freshness && _clock.UtcNow - next.EnqueuedUtc > freshness)
                {
                    // Mechanism-only freshness (WPF has no ms-age expiry — gate-level
                    // anti-stale, BarkService.cs:1359-1363); policy values are q2's.
                    _log("sound: queued voice line dropped (stale) — exceeded caller freshness window");
                    continue;
                }

                try
                {
                    player = _backend.CreatePlayer(next.Path, next.Gain);
                }
                catch (Exception ex)
                {
                    _log($"sound: queued voice construction failed ({ex.GetType().Name}: {ex.Message})");
                    continue;
                }

                var gen = ++_voiceGeneration;
                _voice = player;
                WireVoiceEnded(player, gen);
                break;
            }
        }

        if (player is null)
        {
            return;
        }

        // VoiceCompleted(gen) fires at natural end via WireVoiceEnded.
        if (!TryStart(player, SoundChannel.Voice, out _))
        {
            lock (_gate)
            {
                if (ReferenceEquals(_voice, player))
                {
                    _voice = null;
                }
                ScheduleNextVoiceLocked();
            }
            StopDispose(player);
            return;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_voice, player))
            {
                // Panic landed during the queued start — never leave an unowned player.
                StopDispose(player);
            }
        }
    }

    private void CancelPacingTimerLocked()
    {
        _pacingTimer?.Dispose();
        _pacingTimer = null;
        _voiceStartScheduled = false;
    }

    private void ArmDuckWatchdogLocked()
    {
        _duckWatchdog?.Dispose();
        _duckWatchdog = _clock.Schedule(_options.DuckWatchdog, () =>
        {
            lock (_gate)
            {
                if (_duckCount == 0 && !_duckApplied)
                {
                    return;
                }

                _log("sound: duck watchdog fired — force-unduck (AudioService.cs:845-853 parity)");
                _duckGeneration++;
                _duckCount = 0;
                _duckWatchdog = null;
                RestoreDuckLocked();
            }
        });
    }

    private void ReleaseDuck(long gen)
    {
        lock (_gate)
        {
            if (gen != _duckGeneration)
            {
                // Stale-generation release (ForceUnduck/watchdog already invalidated) — ignored (WPF :892-898).
                return;
            }

            if (_duckCount == 0)
            {
                return;
            }

            _duckCount--;
            if (_duckCount > 0)
            {
                return; // overlapping holders still hold (WPF :900-906)
            }

            _duckWatchdog?.Dispose();
            _duckWatchdog = null;
            RestoreDuckLocked();
        }
    }

    private void RestoreDuckLocked()
    {
        if (!_duckApplied)
        {
            return;
        }

        try
        {
            _duckSink.Restore();
            _duckApplied = false;
        }
        catch (Exception ex)
        {
            // WPF :1003-1016: unduck failure preserves state and stays recoverable — never
            // a volume ratchet. Keep the duck applied with one synthetic hold and re-arm the
            // watchdog so ForceUnduck/a later release retries the restore.
            _duckCount = 1;
            _log($"sound: duck restore failed ({ex.GetType().Name}: {ex.Message}) — state preserved, recoverable");
            ArmDuckWatchdogLocked();
        }
    }

    /// <summary>
    /// Play() with the panic-race guard (pre-completion consult finding 2026-07-22): a
    /// PanicReset/Dispose landing between channel install and Play() leaves Play() running
    /// on a disposed player — on a timer thread that is an unhandled threadpool exception.
    /// Typed + logged, never wedged.
    /// </summary>
    private bool TryStart(IAudioPlayer player, SoundChannel channel, out SoundOutcome? failure)
    {
        try
        {
            player.Play();
            failure = null;
            return true;
        }
        catch (Exception ex)
        {
            _log($"sound: {channel} start failed ({ex.GetType().Name}: {ex.Message}) — raced teardown/panic or backend refusal");
            failure = new SoundOutcome.Failed($"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private void StopAllChannels(string why)
    {
        List<IAudioPlayer> toDispose;
        bool wasWhisperBusy;
        lock (_gate)
        {
            _voiceGeneration++;
            _whisperGeneration++;
            toDispose = [];
            if (_voice is not null)
            {
                toDispose.Add(_voice);
                _voice = null;
            }

            if (_whisper is not null)
            {
                toDispose.Add(_whisper);
                _whisper = null;
            }

            toDispose.AddRange(_sfxPool);
            _sfxPool.Clear();
            _voiceQueue.Clear();
            CancelPacingTimerLocked();
            wasWhisperBusy = _whisperBusy;
            _whisperBusy = false;
        }

        foreach (var player in toDispose)
        {
            StopDispose(player);
        }

        if (toDispose.Count > 0)
        {
            _log($"sound: stopped {toDispose.Count} channel player(s) ({why})");
        }

        if (wasWhisperBusy)
        {
            WhisperBusyChanged?.Invoke(false);
        }
    }

    private void StopDispose(IAudioPlayer? player)
    {
        if (player is null)
        {
            return;
        }

        try { player.Stop(); } catch { /* best-effort teardown */ }
        try { player.Dispose(); } catch { /* best-effort teardown */ }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_tornDown)
            {
                return;
            }

            _tornDown = true;
        }

        PanicReset();
        _backend.Dispose();
    }

    private sealed class DuckHandle(SoundArbitration owner, long generation) : IDuckHandle
    {
        private int _disposed;

        /// <inheritdoc/>
        public long Generation { get; } = generation;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.ReleaseDuck(Generation);
            }
        }
    }
}
