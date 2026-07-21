#if WINDOWS
using NAudio.Wave;
#endif

namespace CcpSpike.Audio;

/// <summary>
/// NAudio 2.2.1 harness — WINDOWS-REFERENCE BASELINE ONLY (the WPF incumbent; never a
/// cross-platform candidate). Mirrors the WPF shape: one WaveOutEvent + AudioFileReader per
/// channel/cue, OS-mixed polyphony, PlaybackStopped event. NAudio 2.2.1 ships WaveOutEvent
/// only in windows-TFM assemblies (verified by reflection on the restored nupkg), so the
/// net10.0 (Linux) build compiles the honest not-supported stub below.
/// WaveOutEvent defaults preserved (WPF parity: WPF only overrides latency in EnhancementAudioPlayer).
/// </summary>
#if !WINDOWS
public sealed class NAudioHarness : HarnessBase
{
    public override string Name => "naudio";
    public override bool SupportedHere => false;
    public override string CompletionMechanism => "native-event(PlaybackStopped)";
    public override IReadOnlyList<DeviceEntry> EnumerateDevices() => Array.Empty<DeviceEntry>();
    public override bool TryInit(string? deviceId, out string? error)
    { error = "NAudio harness compiled out on non-windows TFM (windows-only incumbent)"; return false; }
    public override void VoicePlay(string path, float gain) { }
    public override void VoiceStop() { }
    public override void VoicePause() { }
    public override void VoiceResume() { }
    public override double VoicePositionSec => 0;
    public override bool VoiceActive => false;
    public override double? VoiceEndAtMs => null;
    public override void ClearVoiceEnd() { }
    public override long VoiceRawEndCount => 0;
    public override void WhisperPlay(string path, float gain) { }
    public override bool WhisperBusy => false;
    public override double? WhisperEndAtMs => null;
    public override long SfxPlay(string path, float gain) => -1;
    public override bool SfxActive(long handle) => false;
    public override double SfxPositionSec(long handle) => 0;
    public override void SetVoiceGain(float gain) { }
    public override float GetVoiceGain() => -1;
    public override void Dispose() { }
}
#else
public sealed class NAudioHarness : HarnessBase
{
    private WaveOutEvent? _voiceOut;
    private AudioFileReader? _voiceReader;
    private WaveOutEvent? _whisperOut;
    private AudioFileReader? _whisperReader;
    private readonly Dictionary<long, (WaveOutEvent output, AudioFileReader reader)> _sfx = new();
    private long _nextHandle;
    private long _voiceEndTicks = -1;
    private long _voiceRawEndCount;
    private volatile bool _whisperBusy;
    private long _whisperEndTicks = -1;
    private int _deviceNumber = -1;

    public override string Name => "naudio";
    public override bool SupportedHere => OperatingSystem.IsWindows();
    public override string CompletionMechanism => "native-event(PlaybackStopped)";

    public override IReadOnlyList<DeviceEntry> EnumerateDevices()
    {
        if (!SupportedHere) return Array.Empty<DeviceEntry>();
        var list = new List<DeviceEntry> { new("-1", "System default (WAVE_MAPPER)", true) };
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            list.Add(new DeviceEntry(i.ToString(), caps.ProductName, false));
        }
        return list;
    }

    public override bool TryInit(string? deviceId, out string? error)
    {
        error = null;
        if (!SupportedHere) { error = "not supported off Windows"; return false; }
        var prev = _deviceNumber;
        // Invalid ids must not throw at the harness boundary: a non-numeric id is NOT in the
        // enumeration -> probe the wave layer with an out-of-range device number, which surfaces
        // the backend's real invalid-device error (NAudio MmException), then report rejection.
        if (!int.TryParse(deviceId, out var num))
            num = int.MaxValue - 1;
        _deviceNumber = deviceId == null ? -1 : num;
        // Device validity only surfaces when a reader is Init'ed (NAudio shape) — probe with silence.
        try
        {
            using var probe = new WaveOutEvent { DeviceNumber = _deviceNumber };
            probe.Init(new SilenceProvider(new WaveFormat(ToneGen.SampleRate, 16, 1)));
            return true;
        }
        catch (Exception ex)
        {
            _deviceNumber = prev;
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    public override void VoicePlay(string path, float gain)
    {
        VoiceStop();
        ClearVoiceEnd();
        var reader = new AudioFileReader(path) { Volume = gain };
        var output = new WaveOutEvent { DeviceNumber = _deviceNumber };
        output.PlaybackStopped += (_, _) =>
        {
            Interlocked.Increment(ref _voiceRawEndCount);
            if (ReferenceEquals(output, _voiceOut)) Volatile.Write(ref _voiceEndTicks, Stamp());
        };
        output.Init(reader);
        _voiceReader = reader;
        _voiceOut = output;
        output.Play();
    }

    public override void VoiceStop()
    {
        var o = _voiceOut;
        _voiceOut = null;
        _voiceReader = null;
        if (o == null) return;
        try { o.Stop(); } catch { /* best-effort */ }
        try { o.Dispose(); } catch { /* best-effort */ }
    }

    public override void VoicePause() { if (_voiceOut is { PlaybackState: PlaybackState.Playing } o) o.Pause(); }
    public override void VoiceResume() { if (_voiceOut is { PlaybackState: PlaybackState.Paused } o) o.Play(); }

    public override double VoicePositionSec =>
        _voiceReader is { } r ? r.Position / (double)r.WaveFormat.AverageBytesPerSecond : 0;

    public override bool VoiceActive =>
        _voiceOut is { PlaybackState: PlaybackState.Playing or PlaybackState.Paused };

    public override double? VoiceEndAtMs => TicksToMs(Volatile.Read(ref _voiceEndTicks));
    public override void ClearVoiceEnd() => Volatile.Write(ref _voiceEndTicks, -1);
    public override long VoiceRawEndCount => Interlocked.Read(ref _voiceRawEndCount);

    public override void WhisperPlay(string path, float gain)
    {
        if (_whisperOut != null)
        {
            try { _whisperOut.Stop(); _whisperOut.Dispose(); } catch { /* best-effort */ }
        }
        Volatile.Write(ref _whisperEndTicks, -1);
        var reader = new AudioFileReader(path) { Volume = gain };
        var output = new WaveOutEvent { DeviceNumber = _deviceNumber };
        output.PlaybackStopped += (_, _) =>
        {
            if (ReferenceEquals(output, _whisperOut))
            {
                Volatile.Write(ref _whisperEndTicks, Stamp());
                _whisperBusy = false;
            }
        };
        output.Init(reader);
        _whisperReader = reader;
        _whisperOut = output;
        _whisperBusy = true;
        output.Play();
    }

    public override bool WhisperBusy => _whisperBusy;
    public override double? WhisperEndAtMs => TicksToMs(Volatile.Read(ref _whisperEndTicks));

    public override long SfxPlay(string path, float gain)
    {
        var reader = new AudioFileReader(path) { Volume = gain };
        var output = new WaveOutEvent { DeviceNumber = _deviceNumber };
        output.Init(reader);
        long h = ++_nextHandle;
        _sfx[h] = (output, reader);
        output.Play();
        return h;
    }

    public override bool SfxActive(long handle) =>
        _sfx.TryGetValue(handle, out var e) && e.output.PlaybackState == PlaybackState.Playing;

    public override double SfxPositionSec(long handle) =>
        _sfx.TryGetValue(handle, out var e)
            ? e.reader.Position / (double)e.reader.WaveFormat.AverageBytesPerSecond
            : 0;

    public override void SetVoiceGain(float gain) { if (_voiceReader != null) _voiceReader.Volume = gain; }
    public override float GetVoiceGain() => _voiceReader?.Volume ?? -1;

    public override void Dispose()
    {
        VoiceStop();
        if (_whisperOut != null)
        {
            try { _whisperOut.Stop(); _whisperOut.Dispose(); } catch { /* best-effort */ }
            _whisperOut = null;
            _whisperReader = null;
        }
        foreach (var e in _sfx.Values)
        {
            try { e.output.Stop(); e.output.Dispose(); e.reader.Dispose(); } catch { /* best-effort */ }
        }
        _sfx.Clear();
    }
}
#endif
