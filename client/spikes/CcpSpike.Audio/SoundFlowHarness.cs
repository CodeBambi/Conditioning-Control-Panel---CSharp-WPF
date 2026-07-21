using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace CcpSpike.Audio;

/// <summary>
/// SoundFlow 1.4.1 (MiniAudio backend) harness — admitted PRIMARY candidate.
/// Completion = native <see cref="SoundFlow.Abstracts.SoundPlayerBase.PlaybackEnded"/> event.
/// Device configured at 10 ms period (recorded — latency quantization per consult item 5).
/// </summary>
public sealed class SoundFlowHarness : HarnessBase
{
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;
    private SoundPlayer? _voice;
    private SoundPlayer? _whisper;
    private readonly Dictionary<long, SoundPlayer> _sfx = new();
    private long _nextHandle;
    private long _voiceEndTicks = -1;
    private long _voiceRawEndCount;
    private volatile bool _whisperBusy;
    private long _whisperEndTicks = -1;

    public const int PeriodMs = 10;

    public override string Name => "soundflow";
    public override bool SupportedHere => true;
    public override string CompletionMechanism => "native-event(PlaybackEnded)";

    private static SoundFlow.Structs.AudioFormat Format => new()
    {
        Format = SampleFormat.F32,
        Channels = 1,
        SampleRate = ToneGen.SampleRate
    };

    public override IReadOnlyList<DeviceEntry> EnumerateDevices()
    {
        EnsureEngine();
        _engine!.UpdateAudioDevicesInfo();
        return _engine.PlaybackDevices
            .Select(d => new DeviceEntry(d.Id.ToString() ?? "", d.Name ?? "", d.IsDefault))
            .ToList();
    }

    public override bool TryInit(string? deviceId, out string? error)
    {
        error = null;
        try
        {
            EnsureEngine();
            DeviceInfo? info = null;
            if (deviceId != null)
            {
                // SP-017 FINDING (observed 2x, 2026-07-21): an unvalidated DeviceInfo.Id reaches
                // ma_device_init as a wild native pointer -> uncatchable access violation
                // (0xC0000005, process-fatal). Invalid ids must be refused at the validation
                // layer BEFORE any native init. Ids are process-lifetime POINTERS (they differ
                // across runs) -> product must match devices by NAME (WPF parity: FriendlyName
                // prefix matching, AudioService.cs:219-296), never persist the Id.
                var found = _engine!.PlaybackDevices.Any(d => d.Id.ToString() == deviceId);
                if (!found)
                {
                    error = "device id not in backend enumeration - refused before native init (unvalidated Id => native AV crash, recorded finding)";
                    return false;
                }
                info = _engine.PlaybackDevices.First(d => d.Id.ToString() == deviceId);
            }
            var config = new MiniAudioDeviceConfig { PeriodSizeInMilliseconds = PeriodMs };
            var device = _engine!.InitializePlaybackDevice(info, Format, config);
            device.Start();
            _device?.Stop();
            _device?.Dispose();
            _device = device;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private void EnsureEngine() => _engine ??= new MiniAudioEngine();

    private SoundPlayer NewPlayer(string path, float gain)
    {
        if (_device == null) throw new InvalidOperationException("harness not initialised");
        var provider = new AssetDataProvider(_engine!, path);
        var player = new SoundPlayer(_engine!, Format, provider) { Volume = gain };
        _device.MasterMixer.AddComponent(player);
        return player;
    }

    public override void VoicePlay(string path, float gain)
    {
        VoiceStop();
        Volatile.Write(ref _voiceEndTicks, -1);
        var p = NewPlayer(path, gain);
        p.PlaybackEnded += (_, _) =>
        {
            Interlocked.Increment(ref _voiceRawEndCount);
            if (ReferenceEquals(p, _voice)) Volatile.Write(ref _voiceEndTicks, Stamp());
        };
        _voice = p;
        p.Play();
    }

    public override void VoiceStop()
    {
        var p = _voice;
        _voice = null;
        if (p == null) return;
        try { p.Stop(); } catch { /* best-effort */ }
        try { _device?.MasterMixer.RemoveComponent(p); p.Dispose(); } catch { /* best-effort */ }
    }

    public override void VoicePause() { if (_voice is { State: PlaybackState.Playing } p) p.Pause(); }
    public override void VoiceResume() { if (_voice is { State: PlaybackState.Paused } p) p.Play(); }
    public override double VoicePositionSec => _voice?.Time ?? 0;
    public override bool VoiceActive => _voice is { State: PlaybackState.Playing or PlaybackState.Paused };
    public override double? VoiceEndAtMs => TicksToMs(Volatile.Read(ref _voiceEndTicks));
    public override void ClearVoiceEnd() => Volatile.Write(ref _voiceEndTicks, -1);
    public override long VoiceRawEndCount => Interlocked.Read(ref _voiceRawEndCount);

    public override void WhisperPlay(string path, float gain)
    {
        var old = _whisper;
        if (old != null)
        {
            try { old.Stop(); _device?.MasterMixer.RemoveComponent(old); old.Dispose(); } catch { /* best-effort */ }
        }
        Volatile.Write(ref _whisperEndTicks, -1);
        var p = NewPlayer(path, gain);
        p.PlaybackEnded += (_, _) =>
        {
            if (ReferenceEquals(p, _whisper))
            {
                Volatile.Write(ref _whisperEndTicks, Stamp());
                _whisperBusy = false;
            }
        };
        _whisper = p;
        _whisperBusy = true;
        p.Play();
    }

    public override bool WhisperBusy => _whisperBusy;
    public override double? WhisperEndAtMs => TicksToMs(Volatile.Read(ref _whisperEndTicks));

    public override long SfxPlay(string path, float gain)
    {
        var p = NewPlayer(path, gain);
        long h = ++_nextHandle;
        p.PlaybackEnded += (_, _) =>
        {
            try { _device?.MasterMixer.RemoveComponent(p); p.Dispose(); } catch { /* best-effort */ }
        };
        _sfx[h] = p;
        p.Play();
        return h;
    }

    public override bool SfxActive(long handle) =>
        _sfx.TryGetValue(handle, out var p) && p.State == PlaybackState.Playing;

    public override double SfxPositionSec(long handle) =>
        _sfx.TryGetValue(handle, out var p) ? p.Time : 0;

    public override void SetVoiceGain(float gain) { if (_voice != null) _voice.Volume = gain; }
    public override float GetVoiceGain() => _voice?.Volume ?? -1;

    public override void Dispose()
    {
        VoiceStop();
        foreach (var p in _sfx.Values)
        {
            try { p.Stop(); _device?.MasterMixer.RemoveComponent(p); p.Dispose(); } catch { /* best-effort */ }
        }
        _sfx.Clear();
        if (_whisper != null)
        {
            try { _whisper.Stop(); _device?.MasterMixer.RemoveComponent(_whisper); _whisper.Dispose(); } catch { /* best-effort */ }
            _whisper = null;
        }
        try { _device?.Stop(); _device?.Dispose(); } catch { /* best-effort */ }
        _device = null;
        try { _engine?.Dispose(); } catch { /* best-effort */ }
        _engine = null;
    }
}
