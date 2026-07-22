using System.Threading;

namespace CcpClient.Desktop.Audio;

/// <summary>Playback state of an <see cref="IAudioPlayer"/> (backend-agnostic).</summary>
public enum AudioPlayerState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// One audio player on a channel. Real = SoundFlow <c>SoundPlayer</c> on the playback
/// device's MasterMixer; tests = recording fake. Backend behavior facts (SP-017):
/// SoundFlow fires ZERO end events on explicit Stop (A2) — interruption stays
/// distinguishable from completion — but the generation-token filter in
/// <see cref="SoundArbitration"/> is still REQUIRED because other backends fire on stop
/// (NAudio <c>PlaybackStopped</c>, finding F2). Never assume per-backend.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>Backend-emitted natural-completion event (never fires on explicit Stop on SoundFlow).</summary>
    event EventHandler? PlaybackEnded;

    AudioPlayerState State { get; }

    /// <summary>Decoder-reported position in seconds (evidence: freeze/position checks).</summary>
    double PositionSec { get; }

    /// <summary>Per-player gain 0..1 (volume = mechanism-only, SP-017 A7).</summary>
    float Volume { get; set; }

    void Play();

    void Pause();

    void Stop();
}

/// <summary>
/// The audio backend seam: device init with the F1 discipline + player construction.
/// Real = <see cref="SoundFlowAudioBackend"/> (SoundFlow 1.4.1, SP-017 selection);
/// tests = recording fake.
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>
    /// Fresh render-endpoint NAMEs (session facts — SP-017 A6 shape: "RDP Sink" class on
    /// WSLg; device names are hardware endpoints, never user data).
    /// </summary>
    IReadOnlyList<string> EnumerateDevices();

    /// <summary>
    /// Initialise the playback device. F1 discipline (SP-017, process-fatal crash class,
    /// observed 2× 2026-07-21): RE-ENUMERATE immediately before init, match the requested
    /// device by NAME, pass only a FRESH enumeration snapshot's DeviceInfo — never a stored
    /// one; callers persist the NAME, never the Id. null/unknown name → default device.
    /// </summary>
    bool TryInit(string? deviceName, out string? error);

    /// <summary>
    /// Create (not yet playing) a player for a local audio file at gain 0..1. Implementations
    /// MUST construct off-sync-context (<see cref="OffSyncContext"/>): SoundFlow 1.4.1's
    /// AssetDataProvider ctor is sync-over-async and deadlocks any thread carrying a
    /// SynchronizationContext (SP-025, dump-proven).
    /// </summary>
    IAudioPlayer CreatePlayer(string path, float volume);
}

/// <summary>
/// The platform duck MECHANISM seam (what a duck actually does to audio outside the
/// arbitration core). q1 registers <see cref="UnavailableDuckSink"/> — the cross-app
/// session-volume sink (WASAPI session enumeration on Windows / PipeWire-Pulse policy on
/// Linux) is a separate pending-owner platform decision (audio-backend-spike.md named
/// limit 8; pre-approach consult binding 2026-07-22). The reference-counted machinery in
/// <see cref="SoundArbitration"/> is platform-independent and fully implemented against
/// this seam.
/// </summary>
public interface IAudioDuckSink
{
    /// <summary>Apply a duck of <paramref name="strength"/> (fraction REMOVED, 0..1). False = mechanism unavailable/failed (typed, never silent).</summary>
    bool TryApply(float strength, out string? error);

    /// <summary>Restore pre-duck state. Throwing keeps the duck recoverable (WPF AudioService.cs:1003-1016 — never a volume ratchet).</summary>
    void Restore();
}

/// <summary>
/// The q1 duck sink: no cross-app session-duck mechanism is admitted yet (named limit —
/// WASAPI/PipeWire policy = future row, owner-decided). Reports typed Unavailable so the
/// refcount machinery treats ducks as not-held (WPF symmetric-failure parity,
/// AudioService.cs:869-873) and callers get an honest outcome.
/// </summary>
public sealed class UnavailableDuckSink : IAudioDuckSink
{
    /// <inheritdoc/>
    public bool TryApply(float strength, out string? error)
    {
        error = "cross-app session ducking not admitted (platform policy pending-owner — audio-backend-spike.md named limit 8)";
        return false;
    }

    /// <inheritdoc/>
    public void Restore() { /* never applied */ }
}

/// <summary>
/// Injectable clock/timer seam (pre-approach consult binding 2026-07-22: watchdog +
/// pacing delays must be unit-testable without real waits). Real = <see cref="SystemSoundClock"/>;
/// tests = manual-advance fake.
/// </summary>
public interface ISoundClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>One-shot scheduled callback. due &lt;= 0 fires as soon as possible. Dispose cancels; a disposed timer's callback never runs (best-effort on the real clock — callbacks re-check state under the arbitration gate).</summary>
    IDisposable Schedule(TimeSpan due, Action fire);
}

/// <summary>The real clock on <see cref="System.Threading.Timer"/>.</summary>
public sealed class SystemSoundClock : ISoundClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public IDisposable Schedule(TimeSpan due, Action fire)
    {
        var ms = Math.Max(0, (long)due.TotalMilliseconds);
        return new Timer(_ => fire(), null, ms, Timeout.Infinite);
    }
}

/// <summary>
/// Off-sync-context construction marshal (SP-025, dump-proven 2026-07-22): SoundFlow
/// 1.4.1's AssetDataProvider ctor is SYNC-OVER-ASYNC (GetResult on an async metadata
/// read); on any thread carrying a SynchronizationContext (the Avalonia UI thread) the
/// continuation can never run and the dispatcher wedges silently. The SP-017 console
/// spike never saw it (no sync context). Rule (port-lessons 2026-07-22, binding): any
/// SoundFlow player/provider construction runs through here.
/// </summary>
public static class OffSyncContext
{
    /// <summary>Run <paramref name="work"/> inline when no SynchronizationContext is present, else on a thread-pool thread (no context) and block for the result.</summary>
    public static T Run<T>(Func<T> work)
    {
        return SynchronizationContext.Current is null
            ? work()
            : Task.Run(work).GetAwaiter().GetResult();
    }
}
