using System.Diagnostics;

namespace CcpSpike.Audio;

public sealed record DeviceEntry(string Id, string Name, bool IsDefault);

/// <summary>
/// Minimal backend harness surface exercised identically per admitted backend (packet:
/// "Per-backend comparison executed identically"). Timing stamps use
/// <see cref="Stopwatch.GetElapsedTime(long)"/> on <see cref="Stopwatch.GetTimestamp"/> so
/// probe-observed and backend-event-observed times share one monotonic clock.
/// </summary>
public interface IAudioHarness : IDisposable
{
    string Name { get; }

    /// <summary>False when the backend is honestly not supported on this OS (NAudio off Windows).</summary>
    bool SupportedHere { get; }

    /// <summary>"native-event" or "state-poll@Nms" — the completion-claim mechanism (honesty framing b).</summary>
    string CompletionMechanism { get; }

    IReadOnlyList<DeviceEntry> EnumerateDevices();

    /// <summary>Initialise on the given device (null = default). Returns false with error on failure.</summary>
    bool TryInit(string? deviceId, out string? error);

    // Voice channel (exclusive; stop-replace semantics live in the probes, not the harness).
    void VoicePlay(string path, float gain);
    void VoiceStop();
    void VoicePause();
    void VoiceResume();
    double VoicePositionSec { get; }
    bool VoiceActive { get; } // playing or paused
    /// <summary>Monotonic ms when the backend signalled voice end (event or poll per mechanism), null if not yet.</summary>
    double? VoiceEndAtMs { get; }
    void ClearVoiceEnd();

    /// <summary>
    /// Count of ALL voice end signals the backend raised (unfiltered — includes any fired by an
    /// explicit Stop). VoiceEndAtMs is the FILTERED signal (current-player identity check).
    /// The pair is the interrupt≠completion discrimination evidence (consult item 6).
    /// </summary>
    long VoiceRawEndCount { get; }

    // Whisper channel (exclusive) with busy window driven by REAL completion (not a duration estimate —
    // the WPF WhisperAudioBusyness estimate is the thing this spike tests a replacement for).
    void WhisperPlay(string path, float gain);
    bool WhisperBusy { get; }
    double? WhisperEndAtMs { get; }

    // SFX channel (overlapping one-shots on the same device/context).
    long SfxPlay(string path, float gain);
    bool SfxActive(long handle);
    double SfxPositionSec(long handle);

    // Per-channel gain (voice channel carries the volume probe).
    void SetVoiceGain(float gain);
    float GetVoiceGain();
}

public abstract class HarnessBase : IAudioHarness
{
    public abstract string Name { get; }
    public abstract bool SupportedHere { get; }
    public abstract string CompletionMechanism { get; }
    public abstract IReadOnlyList<DeviceEntry> EnumerateDevices();
    public abstract bool TryInit(string? deviceId, out string? error);
    public abstract void VoicePlay(string path, float gain);
    public abstract void VoiceStop();
    public abstract void VoicePause();
    public abstract void VoiceResume();
    public abstract double VoicePositionSec { get; }
    public abstract bool VoiceActive { get; }
    public abstract double? VoiceEndAtMs { get; }
    public abstract void ClearVoiceEnd();
    public abstract long VoiceRawEndCount { get; }
    public abstract void WhisperPlay(string path, float gain);
    public abstract bool WhisperBusy { get; }
    public abstract double? WhisperEndAtMs { get; }
    public abstract long SfxPlay(string path, float gain);
    public abstract bool SfxActive(long handle);
    public abstract double SfxPositionSec(long handle);
    public abstract void SetVoiceGain(float gain);
    public abstract float GetVoiceGain();
    public abstract void Dispose();

    protected static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    // Harnesses store end signals as Stopwatch ticks (-1 = unset) in a volatile long —
    // atomic cross-thread visibility without locks (double? can't be volatile).
    protected static long Stamp() => Stopwatch.GetTimestamp();
    protected static double? TicksToMs(long ticks) =>
        ticks < 0 ? null : ticks * 1000.0 / Stopwatch.Frequency;
}
