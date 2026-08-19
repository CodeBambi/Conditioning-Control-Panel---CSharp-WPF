using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Audio;

/// <summary>
/// The Windows <see cref="IAudioPresence"/>: an <see cref="IAudioBackend"/> for the sound, and
/// <see cref="WasapiRenderReadback"/> for the PROOF.
///
/// <para><b>The split is the whole design.</b> The backend layer already exists and is not
/// re-implemented here — <c>Audio/AudioSeams.cs</c> owns the device seam, the F1 re-enumerate
/// discipline, the SP-025 off-sync-context rule and the SP-072/SP-083 orphan-safe bounded player
/// construction, and <see cref="SoundFlowAudioBackend"/> implements all of it. What the backend
/// cannot do, and what nothing inside this process can do, is tell you the OPERATING SYSTEM agrees
/// that sound is going out. So this class does exactly one new thing: after every device call it
/// ASKS, and it returns <see cref="CapabilityState.Available"/> only when the answer comes back
/// affirmative.</para>
///
/// <para><b>The rule stated as a rule:</b> no method here may return <c>Available</c> on any path
/// that has not just read <see cref="AudioRenderObservation.Confirmed"/> as true. Not "we called
/// TryInit and it returned true" — that is the exact shape this port has refused four times, and
/// audio is where it is cheapest to get away with because the failure is inaudible from inside.
/// <see cref="Open"/> narrows a successful native init to
/// <see cref="AudioReasonCodes.RenderSessionUnconfirmed"/> when the OS does not back it up, and
/// <see cref="Cue"/> narrows a genuinely started player to <see cref="CapabilityState.Degraded"/>
/// for the same reason.</para>
///
/// <para><b>One device for the whole session, not one per clip — a deliberate divergence.</b> WPF
/// builds a fresh <c>WaveOutEvent</c> per clip (<c>Services/LockCard/MindWipeService.cs:791</c>,
/// <c>Services/LockCard/BrainDrainService.cs:231</c>) and then has to move the whole build
/// off-thread because opening a device is a half-second blocking call that made the app hitch every
/// few seconds (<c>MindWipeService.cs:781-783</c>, issue #890). The port's backend is one playback
/// device with a MasterMixer that players are added to (<c>SoundFlowAudioBackend.cs</c>,
/// audio-backend-spike.md §6 channel ownership), so the device is opened once and the per-clip cost
/// is a player. Same user-visible outcome; the hitch upstream had to engineer around does not
/// arise. Recorded as a divergence.</para>
///
/// <para><b>Slots, and why replacement rather than mixing.</b> Both upstream services keep exactly
/// ONE player field and a generation counter, and a new clip displaces the old one under a lock
/// (<c>MindWipeService.cs:821-853</c>, <c>BrainDrainService.cs:265-292</c>). A slot here is that one
/// field, keyed by the caller's channel id, so two modules cannot displace each other's audio while
/// each still replaces its own — which is what upstream gets from having two separate services.</para>
///
/// <para><b>Threading.</b> <see cref="_gate"/> guards the slot table and the last-open state and
/// nothing slow: it is never held across a device call, a player construction or a read-back. That
/// is upstream's own rule for its audio lock, in upstream's own words — "Held ONLY for cheap work
/// ... Never held across a build, a Stop or a Dispose, so a background build can never block a stop
/// and a stop can never block the UI thread" (<c>MindWipeService.cs:35-41</c>).</para>
/// </summary>
public sealed class WasapiAudioPresence : IAudioPresence
{
    private readonly IAudioBackend _backend;
    private readonly Func<string?, AudioRenderObservation> _readback;
    private readonly Func<int> _endpointCount;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, IAudioPlayer> _slots = new(StringComparer.Ordinal);

    private string? _endpointName;
    private bool _deviceUp;
    private bool _disposed;
    private CapabilityState? _lastOpen;
    private AudioRenderObservation _lastObservation = AudioRenderObservation.NotAsked;

    /// <param name="backend">The device/player seam. Product: <see cref="SoundFlowAudioBackend"/>.</param>
    /// <param name="log">Diagnostic sink.</param>
    /// <param name="readback">How the OS is asked. Injected so a test can drive the UNCONFIRMED
    /// branch — the branch that only exists because a device call returning true is not proof — on a
    /// machine whose OS would confirm. Product passes the real WASAPI read-back.</param>
    /// <param name="endpointCount">How the OS is asked for endpoints, same reason.</param>
    public WasapiAudioPresence(
        IAudioBackend backend,
        Action<string> log,
        Func<string?, AudioRenderObservation>? readback = null,
        Func<int>? endpointCount = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(log);
        _backend = backend;
        _log = log;
        _readback = readback ?? DefaultReadback;
        _endpointCount = endpointCount ?? DefaultEndpointCount;
    }

    /// <inheritdoc/>
    public CapabilityState? LastOpen
    {
        get { lock (_gate) { return _lastOpen; } }
    }

    /// <inheritdoc/>
    public AudioRenderObservation LastObservation
    {
        get { lock (_gate) { return _lastObservation; } }
    }

    /// <inheritdoc/>
    /// <remarks>Derived from the last read-back the OS answered, never from a "we called Open" bool.
    /// Both clauses matter: the device must still be open here, AND the operating system must have
    /// backed it up the last time it was asked.</remarks>
    public bool IsRendering
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _deviceUp && _lastObservation.Confirmed;
            }
        }
    }

    /// <inheritdoc/>
    public CapabilityState Open()
    {
        bool alreadyUp;
        string? name;
        lock (_gate)
        {
            if (_disposed)
            {
                return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                    AudioReasonCodes.RenderPresenceDisposed,
                    "this audio presence was disposed; its device is gone and it will never render again")));
            }

            alreadyUp = _deviceUp;
            name = _endpointName;
        }

        // IDEMPOTENT, and the shape of the idempotence is load-bearing. A second Open must NOT
        // re-init the device: the two audio modules share one presence, so a mid-session quick-toggle
        // of one of them would otherwise stop and re-create the device out from under the other one's
        // playing clip. What a second Open DOES do is ask the OS again — which is the whole point of
        // the call, and the only part worth repeating.
        if (!alreadyUp)
        {
            // Asked BEFORE anything is attempted, and it is a real OS question rather than a
            // platform check: zero endpoints is WPF's own suppression precondition
            // (Services/AudioService.cs:163-166, issue #779 — suppressed with a cooldown, never
            // session-permanent).
            if (_endpointCount() <= 0)
            {
                return Remember(new CapabilityState.DependencyMissing(
                    "an active audio render endpoint",
                    new CapabilityReason(
                        AudioReasonCodes.NoRenderEndpoint,
                        "the OS reports no active render endpoint, so there is nowhere for this process "
                        + "to play. Nothing was attempted. Plugging in or enabling an output device and "
                        + "starting the session again re-asks — this is not latched for the process")));
            }

            // Device call OUTSIDE the gate: TryInit re-enumerates and opens a native device (the F1
            // discipline lives in the backend) and must never be held across this class's lock.
            if (!_backend.TryInit(null, out var error))
            {
                return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                    AudioReasonCodes.RenderDeviceRefused,
                    $"an endpoint exists and the native device open was really attempted and refused: {error}")));
            }

            name = _backend.EnumerateDevices().FirstOrDefault();
            lock (_gate)
            {
                _deviceUp = true;
                _endpointName = name;
            }
        }

        // THE EARNING STEP. Everything above is this process talking to itself; this is the OS
        // answering. A capability that stopped one line earlier would report Available here for a
        // device that opened and rendered nothing.
        var observation = ReadAndRemember(name);
        if (!observation.Confirmed)
        {
            _log("audio: device init returned success and the OS reports no active render session for this process");
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                AudioReasonCodes.RenderSessionUnconfirmed,
                $"the native device open succeeded on '{name ?? "(unnamed endpoint)"}' and the OS does NOT "
                + $"confirm it: asked IAudioSessionControl2::GetProcessId over the default render "
                + $"endpoint's {observation.SessionsOnEndpoint} session(s) and this process "
                + $"({Environment.ProcessId}) "
                + (observation.SessionOwnedByThisProcess
                    ? "owns one whose IAudioSessionControl::GetState is not AudioSessionStateActive"
                    : "owns none of them")
                + ". A success reported here would be the API's word, not the operating system's")));
        }

        return Remember(new CapabilityState.Available(
            $"the OS reports an active render session owned by this process ({Environment.ProcessId}) on "
            + $"'{observation.EndpointName ?? "(unnamed endpoint)"}' — IAudioSessionControl2::GetProcessId "
            + "matched and IAudioSessionControl::GetState is AudioSessionStateActive. It does NOT mean "
            + "the endpoint is unmuted, that a DAC converted anything, or that anybody heard it"));
    }

    /// <inheritdoc/>
    public CapabilityState Cue(AudioCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);

        lock (_gate)
        {
            if (_disposed)
            {
                return new CapabilityState.Unavailable(new CapabilityReason(
                    AudioReasonCodes.RenderPresenceDisposed,
                    $"this audio presence was disposed, so the '{cue.Slot}' cue was not attempted"));
            }

            if (!_deviceUp)
            {
                return new CapabilityState.Unavailable(new CapabilityReason(
                    AudioReasonCodes.RenderDeviceNotOpen,
                    $"no render device is open, so the '{cue.Slot}' cue was not attempted"));
            }
        }

        IAudioPlayer player;
        try
        {
            // Outside the gate: construction is bounded and orphan-safe but it is not instant, and
            // upstream's own rule is that the audio lock is never held across a build
            // (MindWipeService.cs:35-41).
            player = _backend.CreatePlayer(cue.Path, Math.Clamp(cue.Volume, 0f, 1f));
        }
        catch (Exception ex)
        {
            _log($"audio: cue '{cue.Slot}' produced no player ({ex.GetType().Name}: {ex.Message})");
            return new CapabilityState.Unavailable(new CapabilityReason(
                AudioReasonCodes.CuePlaybackFailed,
                $"the '{cue.Slot}' cue could not be turned into a player: {ex.GetType().Name}: {ex.Message}"));
        }

        IAudioPlayer? displaced;
        lock (_gate)
        {
            // Play() only queues the device thread, so it is cheap enough to hold the gate across —
            // and holding it is what stops a Silence from landing between starting the player and
            // publishing it where a Silence can find it. Upstream's own reasoning, verbatim in
            // shape: MindWipeService.cs:399-402.
            player.Play();
            _slots.Remove(cue.Slot, out displaced);
            _slots[cue.Slot] = player;
        }

        SafeStopAndDispose(displaced);

        // Asked again, per cue. This is what keeps IsRendering an ANSWER rather than a memory: a
        // device that the OS dropped mid-session stops reading as rendering at the next cue, and the
        // module's dot goes with it.
        var observation = ReadAndRemember(_endpointName);
        if (!observation.Confirmed)
        {
            // The player really started and the OS no longer backs the session. Degraded rather than
            // Available, and Degraded rather than Unavailable: one half genuinely holds.
            return new CapabilityState.Degraded(
                $"a player for the '{cue.Slot}' cue is attached to the device's mixer and reports "
                + $"{player.State}",
                new CapabilityReason(
                    AudioReasonCodes.RenderSessionUnconfirmed,
                    "the OS does not report an active render session for this process, so nothing can be "
                    + "claimed about the sound reaching an output device"));
        }

        return new CapabilityState.Available(
            $"the '{cue.Slot}' cue is on the device's mixer (player state {player.State}) and the OS "
            + $"reports an active render session owned by this process on "
            + $"'{observation.EndpointName ?? "(unnamed endpoint)"}'. Audibility is not claimed");
    }

    /// <inheritdoc/>
    public CapabilityState Silence(string slot)
    {
        ArgumentException.ThrowIfNullOrEmpty(slot);

        IAudioPlayer? player;
        lock (_gate)
        {
            _slots.Remove(slot, out player);
        }

        if (player is null)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                AudioReasonCodes.RenderDeviceNotOpen,
                $"the '{slot}' slot had nothing playing, so nothing was stopped"));
        }

        // Never under the gate: stop/dispose reach native teardown, and upstream keeps its own lock
        // out of exactly those calls (MindWipeService.cs:568-573 / BrainDrainService.cs:304-308).
        SafeStopAndDispose(player);
        return new CapabilityState.Available($"the '{slot}' slot was stopped and its player disposed");
    }

    /// <inheritdoc/>
    public AudioRenderObservation Observe()
    {
        string? name;
        lock (_gate)
        {
            if (_disposed)
            {
                return AudioRenderObservation.NotAsked;
            }

            name = _endpointName;
        }

        return _readback(name);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        List<IAudioPlayer> players;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _deviceUp = false;
            // The remembered ANSWER is dropped with the device it was about, so nothing downstream
            // can keep reading a torn-down presence as rendering.
            _lastObservation = AudioRenderObservation.NotAsked;
            players = [.. _slots.Values];
            _slots.Clear();
        }

        // Players first, then the device. Upstream's stop order for the same reason: a pair is torn
        // down before the endpoint it is attached to (MindWipeService.cs:243-251 — audio teardown
        // FIRST, inline and unconditional).
        foreach (var player in players)
        {
            SafeStopAndDispose(player);
        }

        try
        {
            _backend.Dispose();
        }
        catch (Exception ex)
        {
            _log($"audio: backend teardown reported {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static AudioRenderObservation DefaultReadback(string? endpointName) =>
        OperatingSystem.IsWindows()
            ? WasapiRenderReadback.Observe(endpointName)
            // Reached only if this class is constructed off Windows, which the factory never does.
            // It answers NotAsked rather than a shaped zero, so it can never be mistaken for a
            // read-back that ran and found nothing.
            : AudioRenderObservation.NotAsked;

    private static int DefaultEndpointCount() =>
        OperatingSystem.IsWindows() ? WasapiRenderReadback.ActiveRenderEndpointCount() : 0;

    /// <summary>One read-back, remembered. The ONLY writer of the remembered observation, so
    /// <see cref="IsRendering"/> can never be true from anything but an answer the OS gave.</summary>
    private AudioRenderObservation ReadAndRemember(string? endpointName)
    {
        var observation = _readback(endpointName);
        lock (_gate)
        {
            _lastObservation = observation;
        }

        return observation;
    }

    private CapabilityState Remember(CapabilityState state)
    {
        lock (_gate)
        {
            _lastOpen = state;
            if (state is not CapabilityState.Available)
            {
                // An open that did not EARN Available leaves nothing to render behind. The remembered
                // answer is cleared rather than left standing, so a second open that fails after a
                // first one succeeded cannot keep IsRendering true off a stale confirmation.
                // _deviceUp is deliberately NOT cleared: the native device may genuinely be up, and a
                // later cue is entitled to report the honest half (Degraded) rather than pretend the
                // device was never opened.
                _lastObservation = AudioRenderObservation.NotAsked;
            }
        }

        return state;
    }

    private void SafeStopAndDispose(IAudioPlayer? player)
    {
        if (player is null)
        {
            return;
        }

        try { player.Stop(); } catch (Exception ex) { _log($"audio: player stop reported {ex.GetType().Name}"); }
        try { player.Dispose(); } catch (Exception ex) { _log($"audio: player dispose reported {ex.GetType().Name}"); }
    }
}
