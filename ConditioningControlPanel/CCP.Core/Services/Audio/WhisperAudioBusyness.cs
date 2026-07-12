using System;
using System.Threading;

namespace ConditioningControlPanel.Core.Services.Audio;

/// <summary>
/// Tracks the "whisper audio is still audible" busy window consulted by the bark gate so the
/// companion won't talk over a subliminal/flash whisper. Byte-identical algorithm to the WPF
/// head's <c>AudioService</c> (Services/AudioService.cs:737-761): a duration-based busy window
/// that only ever EXTENDS, never shortens. Portable so both heads and the unit tests share one
/// source of truth; <see cref="IAudioPlayer.IsWhisperAudioPlaying"/> /
/// <see cref="IAudioPlayer.MarkWhisperAudio"/> delegate here.
/// </summary>
/// <remarks>
/// <para>WPF <c>BarkService</c> gates on <c>App.Audio.IsWhisperAudioPlaying</c>
/// (BarkService.cs:1342); WPF marks it at the whisper playback paths
/// (FlashService.cs:903, SubliminalService.cs:534).</para>
/// <para>The clock source is the one deliberate deviation from the WPF text: it is injectable
/// (<c>DateTime.UtcNow</c> by default) so the unit tests can advance time deterministically
/// without <c>Thread.Sleep</c>. The Interlocked busy-window logic itself is unchanged.</para>
/// </remarks>
public sealed class WhisperAudioBusyness
{
    // Approx end time (ticks; 0 = idle) of the subliminal/flash "whisper" clip that is currently
    // audible. Written from off-UI playback threads, read from the bark gate — accessed via
    // Interlocked so the two stay consistent. Mirrors AudioService.cs:37 _whisperBusyUntilTicks.
    private long _whisperBusyUntilTicks;
    private readonly Func<DateTime> _utcNow;

    /// <summary>
    /// <param name="utcNow">UTC-now clock. Defaults to real <see cref="DateTime.UtcNow"/> so
    /// production is byte-identical to WPF; a custom clock lets tests advance time.</param>
    /// </summary>
    public WhisperAudioBusyness(Func<DateTime>? utcNow = null)
        => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>
    /// True while a subliminal/flash "whisper" is (approximately) still playing (WPF parity:
    /// <c>AudioService.IsWhisperAudioPlaying</c>, Services/AudioService.cs:737-744). Approximate:
    /// based on the clip duration captured at play time, not a real playback-stopped callback.
    /// </summary>
    public bool IsBusy
    {
        get
        {
            var until = Interlocked.Read(ref _whisperBusyUntilTicks);
            return until > 0 && _utcNow().Ticks < until;
        }
    }

    /// <summary>
    /// Record that a whisper clip of <paramref name="durationSeconds"/> just started so
    /// <see cref="IsBusy"/> reports busy until it finishes (+ a 0.25s tail) (WPF parity:
    /// <c>AudioService.MarkWhisperAudio</c>, Services/AudioService.cs:752-761). Called by the
    /// subliminal/flash whisper playback paths. Only ever EXTENDS the busy window — a shorter
    /// concurrent clip can't cut a longer one short. No-op for non-positive/NaN durations.
    /// </summary>
    public void Mark(double durationSeconds)
    {
        if (double.IsNaN(durationSeconds) || durationSeconds <= 0) return;
        var until = _utcNow().AddSeconds(durationSeconds + 0.25).Ticks;
        long prev;
        do { prev = Interlocked.Read(ref _whisperBusyUntilTicks); }
        while (until > prev &&
               Interlocked.CompareExchange(ref _whisperBusyUntilTicks, until, prev) != prev);
    }
}
