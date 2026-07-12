namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform audio playback abstraction.
/// </summary>
public interface IAudioPlayer : IAsyncDisposable
{
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);
    Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default);
    void Stop();
    void SetVolume(double volume);

    /// <summary>
    /// Play a one-shot whisper/trigger voice clip at <paramref name="volume01"/> (0..1) and
    /// return its duration in seconds (WPF parity: <c>SubliminalService.PlayWhisperAudio</c>
    /// and <c>FlashService.PlaySound</c> play via NAudio and read
    /// <c>AudioFileReader.TotalTime</c> for the bark-gate mark — SubliminalService.cs:510/534,
    /// FlashService.cs:2140/2156). Callers feed the returned duration to
    /// <see cref="MarkWhisperAudio"/>. Default implementation applies the volume and plays
    /// fire-and-forget (duration unknown → returns 0, so the mark becomes a no-op); the
    /// LibVLC-backed player overrides this to probe the clip duration first. Never blocks.
    /// </summary>
    double PlayOneShot(string filePath, double volume01)
    {
        try { SetVolume(volume01); _ = PlayAsync(filePath); }
        catch { /* best-effort one-shot; callers tolerate a 0 duration */ }
        return 0;
    }

    /// <summary>
    /// Position-preserving pause of the current playback (WPF parity:
    /// AvatarTubeWindow.Speech.cs:1655 PauseSpokenAudio). Default no-op so implementations
    /// that cannot (or need not) pause inherit safe behavior; the LibVLC-backed player
    /// overrides this. Idempotent: a no-op when idle or already paused.
    /// </summary>
    void Pause() { }

    /// <summary>
    /// Resume playback from a position-preserving pause (WPF parity:
    /// AvatarTubeWindow.Speech.cs:1663 ResumeSpokenAudio). Default no-op; idempotent: a
    /// no-op when idle or already playing.
    /// </summary>
    void Resume() { }

    /// <summary>
    /// True while a subliminal/flash "whisper" is (approximately) still audible (WPF parity:
    /// App.Audio.IsWhisperAudioPlaying, Services/AudioService.cs:737-744). Consulted by the bark
    /// gate so the companion won't talk over a whisper (BarkService.cs:1342). Default false so
    /// heads/fakes without the busy window inherit safe non-blocking behavior; the LibVLC-backed
    /// player overrides this (delegating to <see cref="Services.Audio.WhisperAudioBusyness"/>).
    /// </summary>
    bool IsWhisperAudioPlaying => false;

    /// <summary>
    /// Record that a whisper clip of <paramref name="durationSeconds"/> just started so
    /// <see cref="IsWhisperAudioPlaying"/> reports busy until it finishes (+ a 0.25s tail)
    /// (WPF parity: App.Audio.MarkWhisperAudio, Services/AudioService.cs:752-761). Default no-op;
    /// the LibVLC-backed player overrides this. Only ever EXTENDS the window — a shorter
    /// concurrent clip can't cut a longer one short; no-op for non-positive/NaN durations.
    /// </summary>
    void MarkWhisperAudio(double durationSeconds) { }
}
