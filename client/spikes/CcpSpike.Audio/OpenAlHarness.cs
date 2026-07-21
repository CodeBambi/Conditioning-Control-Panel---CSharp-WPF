using Silk.NET.OpenAL;

namespace CcpSpike.Audio;

/// <summary>
/// Silk.NET.OpenAL 2.23.0 + OpenAL Soft 1.23.1 (bundled native) harness — admitted SECONDARY
/// candidate. OpenAL has NO completion callback: completion = AL_SOURCE_STATE transition to
/// STOPPED observed by a 5 ms poll thread (mechanism named per pre-approach consult item 7).
/// Position via AL_SAMPLE_OFFSET. Device enumeration via ALC_ENUMERATE_ALL_EXT when present.
/// </summary>
public sealed unsafe class OpenAlHarness : HarnessBase
{
    private ALContext? _alc;
    private AL? _al;
    private Device* _device;
    private Context* _context;
    private uint _voiceBuf, _voiceSrc;
    private uint _whisperBuf, _whisperSrc;
    private readonly Dictionary<long, (uint buf, uint src)> _sfx = new();
    private long _nextHandle;
    private Thread? _poller;
    private volatile bool _pollerRun;
    private long _voiceEndTicks = -1;
    private long _voiceRawEndCount;
    private volatile bool _whisperBusy;
    private long _whisperEndTicks = -1;
    private readonly List<long> _sfxEnded = new();

    public const int PollMs = 5;

    public override string Name => "openal";
    public override bool SupportedHere => true;
    public override string CompletionMechanism => $"state-poll@{PollMs}ms(AL_SOURCE_STATE→STOPPED)";

    public override IReadOnlyList<DeviceEntry> EnumerateDevices()
    {
        EnsureApis();
        var list = new List<DeviceEntry>();
        try
        {
            // Current default device name — real backend fact via alcGetString(DeviceSpecifier).
            var def = _alc!.GetContextProperty(null, GetContextString.DeviceSpecifier);
            if (!string.IsNullOrWhiteSpace(def))
                list.Add(new DeviceEntry(def, def, true));
            // ALC_ENUMERATE_ALL_EXT: the Silk 2.23 binding exposes no AllDevicesSpecifier enum and
            // its string marshaler returns only the FIRST entry of the multi-string list — full
            // enumeration is NOT reachable through this binding (recorded finding, not patched).
            if (_alc.IsExtensionPresent(null, "ALC_ENUMERATE_ALL_EXT"))
            {
                var first = _alc.GetContextProperty(null, (GetContextString)0x1013 /* ALC_ALL_DEVICES_SPECIFIER */);
                if (!string.IsNullOrWhiteSpace(first) && first != def)
                    list.Add(new DeviceEntry(first, first + " (first entry only — multi-string enumeration not marshalable via Silk 2.23 GetContextProperty)", false));
            }
        }
        catch (Exception ex)
        {
            list.Add(new DeviceEntry("", $"default (enumeration faulted: {ex.GetType().Name})", true));
        }
        if (list.Count == 0)
            list.Add(new DeviceEntry("", "default (no devices enumerated)", true));
        return list;
    }

    public override bool TryInit(string? deviceId, out string? error)
    {
        error = null;
        try
        {
            EnsureApis();
            var dev = _alc!.OpenDevice(deviceId); // null = default; bogus name → null pointer
            if (dev == null)
            {
                error = "alcOpenDevice returned null";
                return false;
            }
            var ctx = _alc.CreateContext(dev, null);
            if (ctx == null)
            {
                _alc.CloseDevice(dev);
                error = "alcCreateContext returned null";
                return false;
            }
            TeardownDevice();
            _device = dev;
            _context = ctx;
            _alc.MakeContextCurrent(ctx);
            StartPoller();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private void EnsureApis()
    {
        _alc ??= ALContext.GetApi();
        _al ??= AL.GetApi();
    }

    private void StartPoller()
    {
        if (_poller != null) return;
        _pollerRun = true;
        _poller = new Thread(() =>
        {
            while (_pollerRun)
            {
                try
                {
                    if (_al != null && _context != null)
                    {
                        if (_voiceSrc != 0 && Volatile.Read(ref _voiceEndTicks) < 0 && StateOf(_voiceSrc) == (int)SourceState.Stopped)
                        {
                            Interlocked.Increment(ref _voiceRawEndCount);
                            Volatile.Write(ref _voiceEndTicks, Stamp());
                        }
                        if (_whisperSrc != 0 && _whisperBusy && StateOf(_whisperSrc) == (int)SourceState.Stopped)
                        {
                            Volatile.Write(ref _whisperEndTicks, Stamp());
                            _whisperBusy = false;
                        }
                    }
                }
                catch { /* poll thread best-effort; facts recorded from probe side too */ }
                Thread.Sleep(PollMs);
            }
        }) { IsBackground = true, Name = "openal-completion-poll" };
        _poller.Start();
    }

    private int StateOf(uint src)
    {
        _al!.GetSourceProperty(src, GetSourceInteger.SourceState, out int s);
        return s;
    }

    private (uint buf, uint src) Load(string path, float gain)
    {
        var pcm = ToneGen.ReadWavPcm16(path);
        var buf = _al!.GenBuffer();
        _al.BufferData(buf, BufferFormat.Mono16, pcm, ToneGen.SampleRate);
        var src = _al.GenSource();
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(src, SourceFloat.Gain, gain);
        return (buf, src);
    }

    private void Free(uint buf, uint src)
    {
        if (src != 0) { _al!.SourceStop(src); _al.DeleteSource(src); }
        if (buf != 0) _al!.DeleteBuffer(buf);
    }

    public override void VoicePlay(string path, float gain)
    {
        VoiceStop();
        ClearVoiceEnd();
        (_voiceBuf, _voiceSrc) = Load(path, gain);
        _al!.SourcePlay(_voiceSrc);
    }

    public override void VoiceStop()
    {
        if (_voiceSrc == 0) return;
        Free(_voiceBuf, _voiceSrc);
        _voiceBuf = _voiceSrc = 0;
    }

    public override void VoicePause() { if (_voiceSrc != 0) _al!.SourcePause(_voiceSrc); }
    public override void VoiceResume()
    {
        if (_voiceSrc != 0 && StateOf(_voiceSrc) == (int)SourceState.Paused)
            _al!.SourcePlay(_voiceSrc);
    }

    public override double VoicePositionSec
    {
        get
        {
            if (_voiceSrc == 0) return 0;
            _al!.GetSourceProperty(_voiceSrc, GetSourceInteger.SampleOffset, out int off);
            return off / (double)ToneGen.SampleRate;
        }
    }

    public override bool VoiceActive
    {
        get
        {
            if (_voiceSrc == 0) return false;
            var s = StateOf(_voiceSrc);
            return s == (int)SourceState.Playing || s == (int)SourceState.Paused;
        }
    }

    public override double? VoiceEndAtMs => TicksToMs(Volatile.Read(ref _voiceEndTicks));
    public override void ClearVoiceEnd() => Volatile.Write(ref _voiceEndTicks, -1);
    // Poll-observed stops only: explicit VoiceStop deletes the source, so no late signal exists
    // to count — raw == filtered by construction on this backend (mechanism column names it).
    public override long VoiceRawEndCount => Interlocked.Read(ref _voiceRawEndCount);

    public override void WhisperPlay(string path, float gain)
    {
        if (_whisperSrc != 0) { Free(_whisperBuf, _whisperSrc); _whisperBuf = _whisperSrc = 0; }
        Volatile.Write(ref _whisperEndTicks, -1);
        (_whisperBuf, _whisperSrc) = Load(path, gain);
        _whisperBusy = true;
        _al!.SourcePlay(_whisperSrc);
    }

    public override bool WhisperBusy => _whisperBusy;
    public override double? WhisperEndAtMs => TicksToMs(Volatile.Read(ref _whisperEndTicks));

    public override long SfxPlay(string path, float gain)
    {
        var (buf, src) = Load(path, gain);
        long h = ++_nextHandle;
        _sfx[h] = (buf, src);
        _al!.SourcePlay(src);
        // Error capture: OpenAL signals failures via alGetError, not exceptions — an SFX that
        // never starts is indistinguishable from a fast one without this (observed 4/8 finding).
        LastSfxError = _al.GetError();
        LastSfxState = StateOf(src);
        return h;
    }

    /// ><summary>AL error code captured right after the last SfxPlay (None = 0).</summary>
    public AudioError LastSfxError { get; private set; } = AudioError.NoError;
    /// ><summary>Source state captured right after the last SfxPlay's SourcePlay.</summary>
    public int LastSfxState { get; private set; }

    public override bool SfxActive(long handle) =>
        _sfx.TryGetValue(handle, out var e) && StateOf(e.src) == (int)SourceState.Playing;

    public override double SfxPositionSec(long handle)
    {
        if (!_sfx.TryGetValue(handle, out var e)) return 0;
        _al!.GetSourceProperty(e.src, GetSourceInteger.SampleOffset, out int off);
        return off / (double)ToneGen.SampleRate;
    }

    public override void SetVoiceGain(float gain)
    {
        if (_voiceSrc != 0) _al!.SetSourceProperty(_voiceSrc, SourceFloat.Gain, gain);
    }

    public override float GetVoiceGain()
    {
        if (_voiceSrc == 0) return -1;
        _al!.GetSourceProperty(_voiceSrc, SourceFloat.Gain, out float g);
        return g;
    }

    private void TeardownDevice()
    {
        _pollerRun = false;
        _poller?.Join(200);
        _poller = null;
        VoiceStop();
        if (_whisperSrc != 0) { Free(_whisperBuf, _whisperSrc); _whisperBuf = _whisperSrc = 0; }
        foreach (var e in _sfx.Values) Free(e.buf, e.src);
        _sfx.Clear();
        if (_context != null)
        {
            _alc!.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
            _context = null;
        }
        if (_device != null)
        {
            _alc!.CloseDevice(_device);
            _device = null;
        }
    }

    public override void Dispose()
    {
        TeardownDevice();
        _al?.Dispose();
        _al = null;
        _alc?.Dispose();
        _alc = null;
    }
}
