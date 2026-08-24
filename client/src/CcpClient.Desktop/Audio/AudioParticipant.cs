using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Audio;

/// <summary>
/// The APP-LIFETIME owner of this application's sound: the one
/// <see cref="SoundArbitration"/> and the one <see cref="AudioSettingsDocument"/>.
///
/// <para><b>Why this exists, stated as the defect it removes.</b> <see cref="SoundArbitration"/>
/// has called itself "the APP-WIDE sound arbitration core" since it landed
/// (<c>Audio/SoundArbitration.cs:88-90</c>), but its only construction site was inside the DTRH
/// host window (<c>Features/Dtrh/DtrhHostWindow.axaml.cs</c>, whose own comment said the app-wide
/// lift was a future row). So every seam on it — device enumeration,
/// <see cref="SoundArbitration.SetPreferredDevice"/>, the overlapping SFX pool — existed and was
/// unreachable from anywhere but that one window, and a second window opening meant a SECOND
/// native engine and a second device on the same endpoint. An app-wide seam that one window owns
/// is not a seam the app has.</para>
///
/// <para><b>Why a participant.</b> Upstream's audio service is a field on the application,
/// constructed once during startup (<c>ConditioningControlPanel/App.xaml.cs:1798</c>) and outliving
/// every window and every session; nothing in <c>MainWindow.StartStop.cs</c> creates or destroys
/// it. This port already has one lifetime model for exactly that
/// (<see cref="IBackgroundParticipant"/>, the shape <c>Haptics/HapticParticipant.cs</c> set), and
/// it buys the two properties this needs from machinery that exists: phase 3 loads the settings
/// document before any consumer reads it (persistence contract §4 rule 1), and the composition root
/// flushes it in the reserved pre-drain slot, so a volume moved on the way out reaches disk.</para>
///
/// <para><b>PHASE 3 OPENS NO DEVICE, and that is the product behaviour rather than an
/// optimisation.</b> Bringing the device up is what <see cref="EnsureDevice"/> does, on the first
/// consumer that actually needs sound. Two reasons, both concrete. A launch that grabbed a render
/// endpoint for a user who plays nothing is an unrequested claim on a shared resource — the same
/// class as the haptic participant's ungated connect, which upstream also refuses
/// (<c>App.xaml.cs:2173-2176</c>, "would silently bring up three virtual toys ... at each launch").
/// And device init is where this port's F1 process-fatal crash class lives
/// (<c>Audio/SoundFlowAudioBackend.cs</c> header), so it belongs on a path a user's action reaches
/// rather than on every start of every process that builds a host.
/// <see cref="DeviceInitAttempts"/> exists so that is a FACT rather than a claim.</para>
///
/// <para><b>What it deliberately does NOT do.</b> It applies no gain of its own and mixes nothing:
/// <see cref="MasterVolume"/> is a READING, and each play path applies it with its own law, which
/// is upstream's arrangement (<c>Services/AudioService.cs:643</c> composes master with the module's
/// own volume at the play site; <c>Companion/BarkPipeline.cs:613</c> is this port's first such
/// site). It also owns no UI: the picker, the sliders and the Test-audio button are a separate
/// board row, and this is the seam they attach to.</para>
/// </summary>
public sealed class AudioParticipant : IBackgroundParticipant
{
    private readonly ILogSink _log;
    private readonly PersistenceStore<AudioSettingsDocument> _store;
    private readonly object _deviceGate = new();
    private bool _running;

    /// <param name="infra">The participant infrastructure: the operation owner and the log.</param>
    /// <param name="dataDirectory">Where <see cref="AudioSettingsDocument.FileName"/> lives, beside
    /// every other document.</param>
    /// <param name="backend">
    /// The audio backend. Product default is the real SoundFlow/miniaudio one; a fact injects a
    /// recording fake, which is the only way the wiring above a real device can be driven at all.
    /// <b>Construction opens nothing</b> — <see cref="SoundFlowAudioBackend"/> creates its engine
    /// lazily on the first enumerate/init — so building this participant is as cheap as the
    /// lifecycle contract §4.4 requires.
    /// </param>
    /// <param name="clock">The arbitration's scheduling seam (voice pacing, the device re-probe
    /// cooldown, the duck watchdog). Null takes the real one.</param>
    /// <param name="options">The arbitration's bounds. Null takes the stock ones, whose SFX pool of
    /// eight is the owner-ratified value (<c>Audio/SoundArbitration.cs:90-93</c>).</param>
    public AudioParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        IAudioBackend? backend = null,
        ISoundClock? clock = null,
        SoundArbitrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);

        _log = infra.Log;
        _store = new PersistenceStore<AudioSettingsDocument>(
            infra.OwnerFor("AudioSettings"), infra.Log,
            Path.Combine(dataDirectory, AudioSettingsDocument.FileName),
            AudioSettingsDocument.CurrentSchemaVersion);

        // The arbitration graph, moved here verbatim from the DTRH window's own composition: the
        // named-limit duck sink (cross-app session ducking is a pending-owner platform decision, so
        // a duck is refused TYPED rather than pretended), the real clock, stock options. The clock's
        // fault callback is the log, so a faulting pacing/recovery/duck-watchdog callback is
        // contained and named instead of ending the process from a pool thread.
        Arbitration = new SoundArbitration(
            backend ?? new SoundFlowAudioBackend(infra.Log.Log),
            new UnavailableDuckSink(),
            clock ?? new SystemSoundClock(ex => infra.Log.Log(
                "audio: a scheduled arbitration callback faulted and was contained "
                + $"({ex.GetType().Name}: {ex.Message})")),
            options ?? new SoundArbitrationOptions(),
            infra.Log.Log);
    }

    /// <inheritdoc/>
    public string Name => "Audio";

    /// <inheritdoc/>
    public bool Running => _running;

    /// <summary>The ONE arbitration. Everything that plays a sound in this application goes through
    /// this object, so there is one device, one SFX pool and one device choice.</summary>
    public SoundArbitration Arbitration { get; }

    /// <summary>The persisted settings (public so a panel can save the dial it moved — the shape
    /// every other settings owner in this port has).</summary>
    public PersistenceStore<AudioSettingsDocument> Settings => _store;

    /// <summary>How loud this app is, as it stands on disk (0..100). A READING: applying it is the
    /// play site's job, with the play site's own law.</summary>
    public int MasterVolume => _store.Current.MasterVolume;

    /// <summary>How loud video is, as it stands on disk (0..100), under
    /// <see cref="MasterVolume"/>.</summary>
    public int VideoVolume => _store.Current.VideoVolume;

    /// <summary>The user's endpoint choice by NAME, or null when they have not made one (empty on
    /// disk = the system default, upstream's meaning at <c>Models/AppSettings.cs:1238-1240</c>).</summary>
    public string? OutputDeviceName =>
        _store.Current.OutputDeviceName is { Length: > 0 } name ? name : null;

    /// <summary>
    /// The typed outcome of the last device init, or null when NOTHING HAS EVER ASKED FOR A DEVICE
    /// — which is the state a whole launch stays in if the user plays no sound. Null is not
    /// "unavailable": it is "not asked", and the two must not be collapsed.
    /// </summary>
    public SoundOutcome? DeviceOutcome { get; private set; }

    /// <summary>
    /// How many times this participant has brought a device up. <b>Zero across a launch that
    /// played nothing</b>, and it is evidence rather than a counter: it is what a fact reads to show
    /// that starting the app does not seize a render endpoint.
    /// </summary>
    public int DeviceInitAttempts { get; private set; }

    /// <summary>The render endpoints this build can see, re-enumerated per call (names, never ids —
    /// see <see cref="AudioSettingsDocument.OutputDeviceName"/>). Asking does NOT bring a device up,
    /// so a picker can list endpoints without starting playback.</summary>
    public IReadOnlyList<string> Devices() => Arbitration.EnumerateDevices();

    /// <summary>
    /// Bring the output device up ONCE, on the user's persisted choice — the call every consumer
    /// makes before it expects sound. Idempotent: a second call returns the first call's typed
    /// outcome rather than re-initialising the device underneath whatever is already playing.
    ///
    /// <para>A choice that is no longer connected falls back to the default with a typed log line
    /// rather than failing (<c>Audio/SoundArbitration.cs:325-328</c>, WPF
    /// <c>Services/AudioService.cs:292-293</c>), and a machine with no endpoints at all answers
    /// <see cref="SoundOutcome.Unavailable"/> with the re-probe discipline behind it (WPF #779,
    /// <c>AudioService.cs:163-166</c>) — never an exception into the caller.</para>
    ///
    /// <para><b>A FAILED init is remembered too, and that is deliberate.</b> Recovery from a dead
    /// endpoint belongs to the arbitration's own cooldown re-probe, which a PLAY attempt schedules
    /// once per window (<c>SoundArbitration.ReadyLocked</c>, WPF #779 parity) — where
    /// <see cref="SoundArbitration.Initialize"/> is an unconditional native device attempt with no
    /// cooldown in front of it. Retrying here per consumer would put a device attempt on every
    /// window that opens while an endpoint is down, which is the spin that discipline exists to
    /// stop.</para>
    /// </summary>
    public SoundOutcome EnsureDevice()
    {
        lock (_deviceGate)
        {
            if (DeviceOutcome is { } already)
            {
                return already;
            }

            DeviceInitAttempts++;
            var outcome = Arbitration.Initialize(OutputDeviceName);
            DeviceOutcome = outcome;
            _log.Log($"audio: device init on the app-wide arbitration → {outcome.GetType().Name} (typed; "
                + $"requested {OutputDeviceName ?? "default"})");
            return outcome;
        }
    }

    /// <summary>
    /// Route this app's sound to a named endpoint, or to the system default when
    /// <paramref name="deviceName"/> is null/empty, and REMEMBER the choice.
    ///
    /// <para>The order is deliberate: persist first, then re-probe. A device switch stops every
    /// in-flight channel (<c>Audio/SoundArbitration.cs:429-433</c>, a named limit — mid-playback
    /// hot-swap is not admitted), and a crash between the two halves must not leave the user with
    /// the app on one endpoint and the setting naming another. The write is the store's ordinary
    /// chained save, so it is also flushed by the composition root's pre-drain slot.</para>
    /// </summary>
    public SoundOutcome SelectOutputDevice(string? deviceName)
    {
        var chosen = string.IsNullOrWhiteSpace(deviceName) ? "" : deviceName;
        _store.Mutate(d => d.OutputDeviceName = chosen);
        _ = _store.Save();

        lock (_deviceGate)
        {
            DeviceInitAttempts++;
            var outcome = Arbitration.SetPreferredDevice(OutputDeviceName);
            DeviceOutcome = outcome;
            _log.Log($"audio: output device set to {OutputDeviceName ?? "the system default"} "
                + $"→ {outcome.GetType().Name} (typed)");
            return outcome;
        }
    }

    /// <summary>
    /// Phase 3 loads the one document and does nothing else. See the type summary for why no device
    /// comes up here.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_running)
        {
            return;
        }

        await _store.StartAsync(cancellationToken).ConfigureAwait(false);
        if (_store.LastLoadOutcome is { IsDegraded: true } outcome)
        {
            // Typed Degraded (quarantine/newer schema), never silent. The defaults it falls back to
            // are upstream's own (master 32, video 50, system default endpoint), so the audible
            // consequence of a broken file is a volume the user has to set again — never silence
            // they cannot explain.
            _log.Log($"audio-settings: load → {outcome.GetType().Name} (typed Degraded — audio runs on "
                + "the default volumes and the system default endpoint; original bytes preserved if quarantined)");
        }

        _running = true;
    }

    /// <summary>
    /// Teardown: dispose the arbitration (which panic-stops every channel first and then hands the
    /// native backend teardown to a bounded background thread — <c>SoundArbitration.Dispose</c>),
    /// then stop the store. The arbitration goes first for the reason its own Dispose comment
    /// gives: players are stopped on the calling thread BEFORE the backend is torn down.
    /// </summary>
    public async Task StopAsync()
    {
        Arbitration.Dispose();

        if (!_running)
        {
            return;
        }

        _running = false;
        await _store.StopAsync().ConfigureAwait(false);
    }

    /// <summary>The composition root's pre-drain flush for this document (persistence contract §11):
    /// a volume the user moved on the way out is a persisted setting like any other.</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _store.FlushAsync(boundedWait);

    /// <summary>
    /// The app-wide audio owner this host was built with, or null in a host built without one
    /// (owner-less test hosts, custom participant factories).
    ///
    /// <para>This is the port's existing resolution shape for an app-wide document
    /// (<c>Motion/MotionSettings.cs</c>'s <c>HostedMotion.StoreOf</c>), and it is the whole point of
    /// the lift: any surface holding an <see cref="ApplicationHost"/> can reach the device choice,
    /// the volumes and the play seams — a settings page, a session module, a diagnostic — without
    /// going anywhere near the DTRH window that used to own them.</para>
    /// </summary>
    public static AudioParticipant? Of(ApplicationHost? host) =>
        host?.Participants.OfType<AudioParticipant>().FirstOrDefault();
}
