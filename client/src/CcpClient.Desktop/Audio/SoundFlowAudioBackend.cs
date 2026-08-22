using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace CcpClient.Desktop.Audio;

/// <summary>
/// The real <see cref="IAudioBackend"/> on the spike-selected SoundFlow 1.4.1 (MiniAudio) —
/// one playback device, per-channel SoundPlayers on its MasterMixer (channel ownership,
/// audio-backend-spike.md §6). API shape mirrors the admitted spike harness and the
/// DTRH seam (Features/Dtrh/SoundFlowDtrhAudio.cs — which stays the DTRH-LOCAL owner; this
/// is the app-wide arbitration backend; the boundary is recorded in the packet record.md).
///
/// F1 discipline (process-fatal crash class, observed 2× 2026-07-21): an unvalidated
/// DeviceInfo.Id reaches ma_device_init as a wild native pointer → uncatchable 0xC0000005.
/// So <see cref="TryInit"/> RE-ENUMERATES immediately before init, matches the requested
/// device by NAME, and passes only the FRESH snapshot's DeviceInfo struct — never a stored
/// one. The device NAME is what callers persist, never the Id.
/// </summary>
public sealed class SoundFlowAudioBackend : IAudioBackend
{
    private static readonly AudioFormat Format = new()
    {
        Format = SampleFormat.F32,
        Channels = 2,
        SampleRate = 48000,
    };

    private readonly Action<string> _log;
    private readonly OrphanSafePlayerFactory<SoundFlowPlayer> _players;
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;

    public SoundFlowAudioBackend(Action<string> log)
    {
        _log = log;
        // Bounded orphan-safe construction (see AudioSeams.OrphanSafePlayerFactory).
        // The residual line verified by READING only (no headless fact can reach it):
        // attach's `_device!.MasterMixer.AddComponent(p.Player)`.
        _players = new OrphanSafePlayerFactory<SoundFlowPlayer>(
            construct: (path, volume) =>
            {
                var provider = new AssetDataProvider(_engine!, path);
                var player = new SoundPlayer(_engine!, Format, provider) { Volume = volume };
                return new SoundFlowPlayer(player, _device!);
            },
            attach: p => _device!.MasterMixer.AddComponent(p.Player),
            dispose: p => p.Dispose(),
            log: log,
            tag: "sound");
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateDevices()
    {
        try
        {
            _engine ??= new MiniAudioEngine();
            _engine.UpdateAudioDevicesInfo();
            return _engine.PlaybackDevices.Select(d => d.Name).ToArray();
        }
        catch (Exception ex)
        {
            _log($"sound: device enumeration failed ({ex.GetType().Name}: {ex.Message})");
            return [];
        }
    }

    /// <inheritdoc/>
    public bool TryInit(string? deviceName, out string? error)
    {
        error = null;
        try
        {
            _engine ??= new MiniAudioEngine();
            // F1: fresh enumeration immediately before init; Ids are process-lifetime
            // pointers — match by NAME against THIS snapshot (WPF FriendlyName
            // prefix-matching parity, AudioService.cs:219-296).
            _engine.UpdateAudioDevicesInfo();
            DeviceInfo? info = null;
            if (!string.IsNullOrEmpty(deviceName))
            {
                info = _engine.PlaybackDevices.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
                if (info is null)
                {
                    // Name absent from the fresh snapshot → refuse to default (never pass a
                    // stale struct). WPF parity: missing device falls back to default.
                    _log($"sound: device '{deviceName}' not in fresh enumeration — falling back to default");
                }
            }

            var config = new MiniAudioDeviceConfig { PeriodSizeInMilliseconds = 10 };
            var device = _engine.InitializePlaybackDevice(info, Format, config);
            device.Start();
            _device?.Stop();
            _device?.Dispose();
            _device = device;
            return true;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            _log($"sound: device init failed ({error})");
            return false;
        }
    }

    /// <inheritdoc/>
    public IAudioPlayer CreatePlayer(string path, float volume)
    {
        if (_engine is null || _device is null)
        {
            throw new InvalidOperationException("SoundFlowAudioBackend: TryInit must succeed before players are created.");
        }

        // Off-sync-context + bound/orphan invariant live in the factory —
        // construction always on a pool thread, never a SynchronizationContext.
        return _players.Create(path, volume);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Teardown runs under the factory lifecycle lock — serialized against
        // orphan disposal, never concurrent with it.
        _players.Teardown(() =>
        {
            try { _device?.Stop(); } catch { /* best-effort */ }
            try { _device?.Dispose(); } catch { /* best-effort */ }
            _device = null;
            try { _engine?.Dispose(); } catch { /* best-effort */ }
            _engine = null;
        });
    }

    private sealed class SoundFlowPlayer : IAudioPlayer
    {
        private readonly SoundPlayer _player;
        private readonly AudioPlaybackDevice _device;

        public SoundFlowPlayer(SoundPlayer player, AudioPlaybackDevice device)
        {
            _player = player;
            _device = device;
            _player.PlaybackEnded += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The wrapped player — the factory's attach delegate adds THIS to the mixer.</summary>
        internal SoundPlayer Player => _player;

        public event EventHandler? PlaybackEnded;

        public AudioPlayerState State => _player.State switch
        {
            PlaybackState.Playing => AudioPlayerState.Playing,
            PlaybackState.Paused => AudioPlayerState.Paused,
            _ => AudioPlayerState.Stopped,
        };

        public double PositionSec => _player.Time;

        public float Volume { get => _player.Volume; set => _player.Volume = value; }

        public void Play() => _player.Play();

        public void Pause() => _player.Pause();

        // SoundFlow backend behavior fact (spike fact A2): explicit Stop does NOT fire
        // PlaybackEnded — interruption stays distinguishable from completion. The
        // arbitration generation filter covers backends that DO fire (NAudio F2 class).
        public void Stop() => _player.Stop();

        public void Dispose()
        {
            try { _device.MasterMixer.RemoveComponent(_player); } catch { /* best-effort */ }
            try { _player.Dispose(); } catch { /* best-effort */ }
        }
    }
}
