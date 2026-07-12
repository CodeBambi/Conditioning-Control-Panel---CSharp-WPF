using LibVLCSharp.Shared;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Audio;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform audio player shim using LibVLC. LibVLC is initialized lazily
/// on the first playback so it does not add startup time or memory.
/// </summary>
public sealed class AvaloniaAudioPlayer : IAudioPlayer
{
    private readonly ILibVlcProvider _libVlcProvider;
    private readonly IAudioDeviceService? _audioDeviceService;
    private readonly object _lock = new();
    private MediaPlayer? _player;
    private Media? _currentMedia;
    private double _pendingVolume = 1.0;
    private bool _disposed;

    // Whisper-audio busy window consulted by the bark gate (WPF parity: AudioService.cs:37
    // _whisperBusyUntilTicks + IsWhisperAudioPlaying/MarkWhisperAudio, AudioService.cs:737-761).
    // The port's subliminal/flash whisper playback is not yet wired, so nothing calls
    // MarkWhisperAudio today; the gate is INERT-BUT-READY and lights up automatically when
    // whisper voice audio is ported (mirror WPF FlashService.cs:903 / SubliminalService.cs:534).
    private readonly WhisperAudioBusyness _whisper = new();

    public AvaloniaAudioPlayer(ILibVlcProvider libVlcProvider, IAudioDeviceService? audioDeviceService = null)
    {
        _libVlcProvider = libVlcProvider;
        _audioDeviceService = audioDeviceService;

        if (_audioDeviceService != null)
        {
            _audioDeviceService.PreferredDeviceChanged += OnPreferredDeviceChanged;
        }
    }

    private void OnPreferredDeviceChanged(object? sender, EventArgs e)
    {
        ApplyPreferredOutputDevice();
    }

    public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlayer();
        StopInternal();
        ApplyPreferredOutputDevice();
        var libVlc = _libVlcProvider.Value;
        _currentMedia = new Media(libVlc, filePath);
        _player!.Play(_currentMedia);
        return Task.CompletedTask;
    }

    public Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePlayer();
        StopInternal();
        ApplyPreferredOutputDevice();
        var libVlc = _libVlcProvider.Value;
        _currentMedia = new Media(libVlc, filePath);
        _currentMedia.AddOption(":input-repeat=-1");
        _player!.Play(_currentMedia);
        return Task.CompletedTask;
    }

    // One-shot whisper/trigger voice clip that ALSO returns its duration so callers can mark
    // the bark-gate window (WPF parity: SubliminalService.cs:510/534 + FlashService.cs:2140/2156
    // read AudioFileReader.TotalTime from a dedicated NAudio reader). Mirrors PlayAsync's
    // structure (EnsurePlayer → StopInternal → ApplyPreferredOutputDevice → new Media → Play)
    // and inserts a bounded local-file duration probe before Play (port precedent:
    // BubbleCountWindow.axaml.cs:326 `media.Parse(MediaParseOptions.ParseLocal, 2000)` and
    // AvaloniaKeywordTriggerService.cs:1114-1115). Volume is applied per-clip; the shared
    // LibVLC player keeps the last-set volume, which is fine here because the bark gate keeps
    // the companion silent for the clip's duration and the next consumer resets it.
    public double PlayOneShot(string filePath, double volume01)
    {
        volume01 = Math.Clamp(volume01, 0.0, 1.0);
        try
        {
            EnsurePlayer();
            StopInternal();
            ApplyPreferredOutputDevice();
            var libVlc = _libVlcProvider.Value;
            _currentMedia = new Media(libVlc, filePath);

            double duration = 0;
            try
            {
                _currentMedia.Parse(MediaParseOptions.ParseLocal, 2000);
                if (_currentMedia.Duration > 0)
                    duration = _currentMedia.Duration / 1000.0;
            }
            catch
            {
                // Duration unknown — playback still proceeds; caller's mark becomes a no-op.
            }

            SetVolume(volume01);
            _player!.Play(_currentMedia);
            return duration;
        }
        catch
        {
            return 0;
        }
    }

    public void Stop() => StopInternal();

    // Position-preserving pause/resume of the current voice line for DTRH world-freeze
    // (WPF parity: AvatarTubeWindow.Speech.cs:1655/1663 PauseSpokenAudio/ResumeSpokenAudio,
    // which drive NAudio Pause()/Play()). LibVLC holds the buffer position across
    // SetPause(true); Play() resumes from that position. Guarded by State so both are
    // idempotent no-ops when idle / already in the target state — never toggling.
    public void Pause()
    {
        lock (_lock)
        {
            if (_player is { State: VLCState.Playing })
                _player.SetPause(true);
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_player is { State: VLCState.Paused })
                _player.Play();
        }
    }

    // Whisper-audio busy window exposed on the IAudioPlayer seam (WPF parity:
    // AudioService.cs:737-761 IsWhisperAudioPlaying/MarkWhisperAudio). Delegates to the
    // shared WhisperAudioBusyness so the algorithm stays in one place (see that type).
    public bool IsWhisperAudioPlaying => _whisper.IsBusy;

    public void MarkWhisperAudio(double durationSeconds) => _whisper.Mark(durationSeconds);

    public void SetVolume(double volume)
    {
        volume = Math.Clamp(volume, 0.0, 1.0);
        lock (_lock)
        {
            _pendingVolume = volume;
            if (_player != null)
                _player.Volume = (int)(volume * 100);
        }
    }

    private void EnsurePlayer()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AvaloniaAudioPlayer));
            if (_player != null) return;

            var libVlc = _libVlcProvider.Value;
            _player = new MediaPlayer(libVlc);
            _player.Volume = (int)(_pendingVolume * 100);
            ApplyPreferredOutputDevice();
        }
    }

    private void ApplyPreferredOutputDevice()
    {
        try
        {
            if (_player == null) return;
            var deviceId = _audioDeviceService?.GetDefaultOutputDeviceId();
            if (string.IsNullOrEmpty(deviceId))
                return;

            // Validate the endpoint against LibVLC's own enumeration first: LibVLC
            // silently routes audio to nowhere (effective mute) when handed a stale or
            // foreign device ID (WPF #270/#251/#244 cluster; WS0 lot 4 A1-13). If the
            // ID isn't recognized, keep the default endpoint instead.
            var known = false;
            try
            {
                var enumerated = _player.AudioOutputDeviceEnum;
                if (enumerated != null)
                {
                    foreach (var dev in enumerated)
                    {
                        if (string.Equals(dev.DeviceIdentifier, deviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            known = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Enumeration unsupported on this platform/output module - attempt anyway,
                // matching the video path's fall-through behavior (LibVlcAudioHelper).
                known = true;
            }

            if (!known)
                return;

            // LibVLCSharp provides SetOutputDevice on MediaPlayer for LibVLC 3.x+.
            // Failure is non-fatal: playback falls back to the default endpoint.
            _player.SetOutputDevice(deviceId);
        }
        catch
        {
            // Device enumeration or selection may not be supported on this platform.
        }
    }

    private void StopInternal()
    {
        _player?.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;

            if (_audioDeviceService != null)
            {
                _audioDeviceService.PreferredDeviceChanged -= OnPreferredDeviceChanged;
            }

            StopInternal();
            _player?.Dispose();
            _player = null;
            // Do not dispose LibVLC: it is a shared singleton owned by the provider.
            return ValueTask.CompletedTask;
        }
    }
}
