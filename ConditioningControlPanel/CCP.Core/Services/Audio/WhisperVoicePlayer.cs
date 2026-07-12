using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.Audio;

/// <summary>
/// Plays a one-shot subliminal/flash/session whisper VOICE clip through the
/// <see cref="IAudioPlayer"/> seam and marks the bark-gate whisper window for the clip's
/// duration, so the companion will not talk over it (BARK-3). Portable so both heads and the
/// unit tests share one source of truth for the play/mark/duck decision.
/// </summary>
/// <remarks>
/// <para>WPF parity: <c>SubliminalService.PlayWhisperAudio</c> (Services/Subliminal/
/// SubliminalService.cs:504-543) and <c>FlashService.PlaySound</c> (Services/Flash/
/// FlashService.cs:2132-2166) play via a dedicated NAudio reader, apply a curved volume, and
/// call <c>App.Audio.MarkWhisperAudio(audioFile.TotalTime.TotalSeconds)</c>
/// (SubliminalService.cs:534 / FlashService.cs:903). Duck/Unduck of OTHER audio is done by
/// the WPF caller via <c>App.Audio.Duck(DuckingLevel)</c> (SubliminalService.cs:206-207,
/// FlashService.cs:909-911) with an unduck scheduled on playback stop
/// (SubliminalService.cs:523-530).</para>
/// <para><b>Deliberate divergence from WPF (spec guardrail):</b> when whispers are disabled
/// OR master volume is muted, this helper plays NO audio and issues NO mark. WPF instead
/// plays at a 0 / 0.05-floor volume and still marks (SubliminalService.cs:515-517 computes
/// <c>pow(sub*master, 1.5)</c> → 0 at master 0; FlashService.cs:2146 floors at 0.05). The
/// spec's <c>masterMuteRespected</c> contract requires the skip; the discrepancy is recorded
/// in the task report.</para>
/// </remarks>
public sealed class WhisperVoicePlayer
{
    private readonly IAudioPlayer _audio;
    private readonly ISystemAudioDucker? _ducker;
    private readonly ILogger<WhisperVoicePlayer>? _logger;

    public WhisperVoicePlayer(IAudioPlayer audio, ISystemAudioDucker? ducker = null, ILogger<WhisperVoicePlayer>? logger = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _ducker = ducker;
        _logger = logger;
    }

    /// <summary>
    /// Play <paramref name="audioPath"/> at <paramref name="volume01"/> (0..1) and mark the
    /// bark-gate whisper window for the returned duration. Returns the clip duration in
    /// seconds (0 if not played or duration unknown).
    /// </summary>
    /// <param name="audioPath">Resolved clip path. Null/missing file ⇒ no audio, no mark.</param>
    /// <param name="whispersEnabled">Subliminal gate is <c>SubAudioEnabled</c>
    /// (SubliminalService.cs:203); flash gate is <c>FlashAudioEnabled</c> (FlashService.cs:896).</param>
    /// <param name="masterMuted">True when master volume is 0 (spec: no audio + no mark).</param>
    /// <param name="volume01">Pre-computed curved volume (subliminal: <c>pow(sub*master, 1.5)</c>
    /// no floor; flash: <c>Max(0.05, pow(master, 1.5))</c>).</param>
    /// <param name="duckEnabled">Mirror of <c>AudioDuckingEnabled</c>; ducks other audio right
    /// before play when a ducker is present (SubliminalService.cs:206-207 / FlashService.cs:909-911).</param>
    /// <param name="duckLevel"><c>DuckingLevel</c> percent passed to the ducker.</param>
    public double Play(string? audioPath, bool whispersEnabled, bool masterMuted, double volume01, bool duckEnabled, int duckLevel)
    {
        // Spec guardrail: disabled or master-muted ⇒ no audio and NO mark.
        if (!whispersEnabled || masterMuted) return 0;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath)) return 0;

        // Duck ONLY after all skip-gates pass, immediately before play (WPF caller duck at
        // SubliminalService.cs:206-207 / FlashService.cs:909-911).
        var ducker = duckEnabled ? _ducker : null;
        ducker?.Duck(duckLevel);

        double duration;
        try
        {
            duration = _audio.PlayOneShot(audioPath, Math.Clamp(volume01, 0.0, 1.0));
            _audio.MarkWhisperAudio(duration);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "WhisperVoicePlayer: one-shot play failed for {Path}", audioPath);
            // WPF parity: unduck on failure (SubliminalService.cs:541 calls App.Audio.Unduck()).
            try { ducker?.Unduck(); } catch { /* best-effort */ }
            return 0;
        }

        if (ducker != null)
        {
            // Schedule unduck at duration + 0.5s tail (keyword-trigger precedent
            // AvaloniaKeywordTriggerService.cs:998-1002; WPF SubliminalService.cs:526-530).
            var delaySeconds = duration > 0 ? duration + 0.5 : 0.5;
            _ = Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ContinueWith(
                _ => { try { ducker.Unduck(); } catch { /* best-effort */ } },
                TaskContinuationOptions.None);
        }

        return duration;
    }
}
