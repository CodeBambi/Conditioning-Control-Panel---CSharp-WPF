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
}
