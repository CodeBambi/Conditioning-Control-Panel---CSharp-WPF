using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

using CcpClient.Desktop.Audio;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The real <see cref="IDtrhAudioBackend"/> on the selected SoundFlow 1.4.1
/// (MiniAudio) — one playback device, per-channel SoundPlayers on its MasterMixer
/// (channel ownership, audio-backend-spike.md §6). API shape mirrors the admitted spike
/// harness (CcpSpike.Audio SoundFlowHarness.cs) — device period 10 ms recorded there.
///
/// F1 discipline (process-fatal crash class, observed 2× 2026-07-21): an unvalidated
/// DeviceInfo.Id reaches ma_device_init as a wild native pointer → uncatchable
/// 0xC0000005. So <see cref="TryInit"/> RE-ENUMERATES immediately before init, matches
/// the requested device by NAME, and passes only the FRESH snapshot's DeviceInfo struct —
/// never a stored one. The device NAME is what callers persist, never the Id.
/// </summary>
public sealed class SoundFlowDtrhAudio : IDtrhAudioBackend
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

    public SoundFlowDtrhAudio(Action<string> log)
    {
        _log = log;
        // Bounded orphan-safe construction (see AudioSeams.OrphanSafePlayerFactory).
        // This ALSO removes the earlier inline Task.Run(...).GetAwaiter().GetResult()
        // duplicate of OffSyncContext — the factory constructs on a pool thread always, so
        // the property now lives in ONE place for both backends. The residual line
        // verified by READING only: attach's `_device!.MasterMixer.AddComponent(p.Player)`.
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
            tag: "dtrh-audio");
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
            // Session facts (RDP Sink class on WSLg — the A6 shape; device names are
            // hardware endpoints, never user data).
            _log($"dtrh-audio: {_engine.PlaybackDevices.Count()} render endpoint(s): {string.Join(" | ", _engine.PlaybackDevices.Select(d => d.IsDefault ? d.Name + " (default)" : d.Name))}");
            DeviceInfo? info = null;
            if (!string.IsNullOrEmpty(deviceName))
            {
                if (_engine.PlaybackDevices.Any(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase)))
                {
                    info = _engine.PlaybackDevices.First(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    // Name absent from the fresh snapshot → refuse to default (never pass
                    // a stale struct). WPF parity: missing device falls back to default.
                    _log($"dtrh-audio: device '{deviceName}' not in fresh enumeration — falling back to default");
                }
            }

            var config = new MiniAudioDeviceConfig { PeriodSizeInMilliseconds = 10 };
            var device = _engine.InitializePlaybackDevice(info, Format, config);
            device.Start();
            _device?.Stop();
            _device?.Dispose();
            _device = device;
            _log($"dtrh-audio: playback device up (requested {(deviceName ?? "default")})");
            return true;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            _log($"dtrh-audio: device init failed ({error})");
            return false;
        }
    }

    /// <inheritdoc/>
    public IDtrhAudioPlayer CreatePlayer(string path, float volume)
    {
        if (_engine is null || _device is null)
        {
            throw new InvalidOperationException("SoundFlowDtrhAudio: TryInit must succeed before players are created.");
        }

        // The off-sync-context and bound/orphan invariants live in the factory —
        // construction always on a pool thread, never a SynchronizationContext.
        return _players.Create(path, volume);
    }

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

    private sealed class SoundFlowPlayer : IDtrhAudioPlayer
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

        public DtrhPlayerState State => _player.State switch
        {
            PlaybackState.Playing => DtrhPlayerState.Playing,
            PlaybackState.Paused => DtrhPlayerState.Paused,
            _ => DtrhPlayerState.Stopped,
        };

        public double PositionSec => _player.Time;

        public void Play() => _player.Play();

        public void Pause() => _player.Pause();

        // SoundFlow backend behavior fact (A2): explicit Stop does NOT fire
        // PlaybackEnded — interruption stays distinguishable from completion.
        public void Stop() => _player.Stop();

        public void Dispose()
        {
            try { _device.MasterMixer.RemoveComponent(_player); } catch { /* best-effort */ }
            try { _player.Dispose(); } catch { /* best-effort */ }
        }
    }
}
