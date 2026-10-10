// PORTED from ConditioningControlPanel/Services/Possession/PossessionAudio.cs (7.1.5): the two audio
// tics of the Possession layer. Both are TONES synthesised at run time (a 50 ms ember tick, a 300 ms
// 80 Hz stinger): pure maths, no words, so the "no synthetic speech" rule does not reach them.
//
// A big effect starting plays the tick (at most one every 1.5 s; micro-tics stay silent). A rung
// change above Settle, and the third pull at the same tripwire, play the stinger under a 300 ms duck
// (at most one every 5 s). Quiet by construction: the tick peaks at -18 dBFS and the stinger at
// -12 dBFS, scaled by the app's master volume, through the app's ordinary one-shot path (CoreAudio).
// Gates: AppSettings.LockdownAudioTics, MasterVolume > 0. Panic stops whatever is sounding and lifts
// the duck at once. Nothing here throws at a caller.
//
// Head-neutral twin: WPF reads App.* statics; this takes the lockdown and the director at Install and
// plays through CoreAudio.PlayStoppable so a clip can be stopped.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Possession;

public static class PossessionAudio
{
    private const double TickPeakDbfs = -18.0;
    private const double StingerPeakDbfs = -12.0;
    private const int SampleRate = 44100;
    private const double TickSeconds = 0.05;
    private const double StingerSeconds = 0.30;
    internal static readonly TimeSpan TickThrottle = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan DipThrottle = TimeSpan.FromSeconds(5);
    private const int DipMs = 300;
    private const int DipDuckStrength = 60;
    internal const string TickTag = "possession-tick", DipTag = "possession-dip";

    private static readonly object Sync = new();
    private static LockdownService? _lockdown;
    private static Func<PossessionDirector?>? _director;
    private static PossessionDirector? _armedOn;
    private static DateTime _lastTick = DateTime.MinValue, _lastDip = DateTime.MinValue;
    private static string? _tickPath, _stingerPath;
    private static readonly List<Action> _stops = new();
    private static long? _duck;

    // Seams (tests): the clock, the settings, the player (returns the clip's stop) and the clip folder.
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal static Func<Models.AppSettings?> Settings = () => CoreSettings.Current;
    internal static Func<string, float, string, Action> Play = (path, volume, tag) => CoreAudio.PlayStoppable(path, volume, tag);
    internal static Func<string> ClipDir = () => Path.Combine(CorePaths.UserData, "possession");

    public static void Install(LockdownService lockdown, Func<PossessionDirector?> director)
    {
        try
        {
            Uninstall();
            _lockdown = lockdown;
            _director = director;
            lockdown.LockdownActivated += Arm;
            lockdown.LockdownDeactivated += Disarm;
        }
        catch (Exception ex) { Serilog.Log.Warning("PossessionAudio install failed: {Error}", ex.Message); }
    }

    internal static void Uninstall()
    {
        Disarm();
        if (_lockdown is { } l)
        {
            l.LockdownActivated -= Arm;
            l.LockdownDeactivated -= Disarm;
        }
        _lockdown = null;
        _director = null;
    }

    internal static void Arm()
    {
        try
        {
            var director = _director?.Invoke();
            if (director == null) return;
            lock (Sync)
            {
                if (_armedOn != null) return;
                _armedOn = director;
            }
            director.EffectStarted += OnEffectStarted;
            director.RungChanged += OnRungChanged;
            director.TripwireReacted += OnTripwireReacted;
            // Render now, off the UI thread, so the FIRST tick is not the one that pays for the file.
            _ = Task.Run(() => { try { EnsureClips(); } catch { } });
        }
        catch (Exception ex) { Serilog.Log.Warning("PossessionAudio arm failed: {Error}", ex.Message); }
    }

    internal static void Disarm()
    {
        try
        {
            PossessionDirector? director;
            lock (Sync)
            {
                director = _armedOn;
                _armedOn = null;
                _lastTick = DateTime.MinValue;
                _lastDip = DateTime.MinValue;
            }
            if (director != null)
            {
                director.EffectStarted -= OnEffectStarted;
                director.RungChanged -= OnRungChanged;
                director.TripwireReacted -= OnTripwireReacted;
            }
            StopNow();
        }
        catch (Exception ex) { Serilog.Log.Warning("PossessionAudio disarm failed: {Error}", ex.Message); }
    }

    /// <summary>Panic: whatever is sounding stops and the duck lifts, in this call. The tics stay
    /// armed, so they come back with the haunt when it resumes.</summary>
    public static void StopForPanic() => StopNow();

    private static void StopNow()
    {
        Action[] stops;
        long? duck;
        lock (Sync)
        {
            stops = _stops.ToArray();
            _stops.Clear();
            duck = _duck;
            _duck = null;
        }
        foreach (var stop in stops) { try { stop(); } catch { } }
        if (duck is { } generation) { try { CoreAudio.UnduckProvider?.Invoke(generation); } catch { } }
    }

    internal static void OnEffectStarted(string effectId, string? targetKey, bool isBig)
    {
        // Micro-tics stay silent, exactly as they stay unnamed.
        if (!isBig) return;
        if (!Throttle(ref _lastTick, TickThrottle)) return;
        PlayClip(_tickPath ?? EnsureClips().Tick, TickTag);
    }

    internal static void OnRungChanged(PossessionRung rung)
    {
        if (rung == PossessionRung.Settle) return;   // where every lockdown starts: not a change
        Dip();
    }

    internal static void OnTripwireReacted(EscapeAttempt attempt)
    {
        if (attempt.Repeat < 3) return;              // the threshold the warden's stare uses
        Dip();
    }

    /// <summary>The 300 ms sag: everything else down, an 80 Hz stinger under it, then back.</summary>
    private static void Dip()
    {
        if (!CanPlay()) return;
        if (!Throttle(ref _lastDip, DipThrottle)) return;
        PlayClip(_stingerPath ?? EnsureClips().Stinger, DipTag);
        try
        {
            if (CoreAudio.DuckProvider is not { } duck) return;
            duck(DipDuckStrength);
            long generation = CoreAudio.DuckGenerationProvider?.Invoke() ?? 0;
            lock (Sync) _duck = generation;
            // The generation stops this stale callback from cutting a LATER duck short.
            _ = Task.Delay(DipMs).ContinueWith(_ =>
            {
                try
                {
                    lock (Sync) { if (_duck == generation) _duck = null; }
                    CoreAudio.UnduckProvider?.Invoke(generation);
                }
                catch (Exception ex) { Serilog.Log.Debug("PossessionAudio unduck failed: {Error}", ex.Message); }
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { Serilog.Log.Warning("PossessionAudio dip failed: {Error}", ex.Message); }
    }

    internal static bool CanPlay()
    {
        try
        {
            var s = Settings();
            return s != null && s.LockdownAudioTics && s.MasterVolume > 0;
        }
        catch { return false; }
    }

    private static void PlayClip(string? path, string tag)
    {
        if (string.IsNullOrEmpty(path) || !CanPlay()) return;
        try
        {
            // The clip is already mixed at its intended (quiet) level: master volume is the only multiplier.
            float volume = Math.Clamp((Settings()?.MasterVolume ?? 0) / 100f, 0f, 1f);
            if (volume <= 0f) return;
            var stop = Play(path!, volume, tag);
            lock (Sync)
            {
                if (_stops.Count >= 4) _stops.RemoveAt(0);   // a clip is 300 ms at most: old stops are no-ops
                _stops.Add(stop);
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("PossessionAudio {Tag} failed: {Error}", tag, ex.Message); }
    }

    private static bool Throttle(ref DateTime last, TimeSpan gap)
    {
        lock (Sync)
        {
            var now = UtcNow();
            if (now - last < gap) return false;
            last = now;
            return true;
        }
    }

    // The file names carry the synth version: change the maths, change the name.
    internal static (string? Tick, string? Stinger) EnsureClips()
    {
        try
        {
            var dir = ClipDir();
            Directory.CreateDirectory(dir);
            var tick = Path.Combine(dir, "ember_tick_v1.wav");
            if (!IsUsable(tick)) File.WriteAllBytes(tick, WriteWav(SynthTick()));
            _tickPath = tick;
            var stinger = Path.Combine(dir, "ember_stinger_v1.wav");
            if (!IsUsable(stinger)) File.WriteAllBytes(stinger, WriteWav(SynthStinger()));
            _stingerPath = stinger;
        }
        catch (Exception ex) { Serilog.Log.Warning("PossessionAudio could not render its cues: {Error}", ex.Message); }
        return (_tickPath, _stingerPath);
    }

    /// <summary>Tests: forget the rendered paths.</summary>
    internal static void ForgetClips() { _tickPath = null; _stingerPath = null; }

    private static bool IsUsable(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 128; }
        catch { return false; }
    }

    // ---- synth (pure maths, the WPF numbers) ----------------------------------------------------

    internal static float[] SynthTick()
    {
        int n = (int)(SampleRate * TickSeconds);
        var buf = new float[n];
        uint rng = 0x51ED270B;   // fixed seed: byte-identical file on every machine, every render
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double strike = Math.Exp(-t / 0.012);
            double tail = Math.Exp(-t / 0.045);
            double v = 0.65 * Math.Sin(2 * Math.PI * 1420.0 * t) * strike
                     + 0.35 * Math.Sin(2 * Math.PI * 710.0 * t) * tail;
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            double noise = (rng / (double)uint.MaxValue) * 2.0 - 1.0;
            v += 0.25 * noise * Math.Exp(-t / 0.002);
            buf[i] = (float)v;
        }
        LowPass(buf, 4000.0);
        Fade(buf, inMs: 1.5, outMs: 6.0);
        Normalize(buf, TickPeakDbfs);
        return buf;
    }

    internal static float[] SynthStinger()
    {
        int n = (int)(SampleRate * StingerSeconds);
        var buf = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t / 0.07);
            buf[i] = (float)(env * (Math.Sin(2 * Math.PI * 80.0 * t) + 0.25 * Math.Sin(2 * Math.PI * 160.0 * t)));
        }
        Fade(buf, inMs: 3.0, outMs: 25.0);
        Normalize(buf, StingerPeakDbfs);
        return buf;
    }

    internal static void LowPass(float[] buf, double cutoffHz)
    {
        if (buf == null || buf.Length == 0) return;
        double a = 1.0 - Math.Exp(-2.0 * Math.PI * cutoffHz / SampleRate);
        double y = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            y += a * (buf[i] - y);
            buf[i] = (float)y;
        }
    }

    internal static void Fade(float[] buf, double inMs, double outMs)
    {
        if (buf == null || buf.Length == 0) return;
        int fin = Math.Min(buf.Length, (int)(SampleRate * inMs / 1000.0));
        int fout = Math.Min(buf.Length, (int)(SampleRate * outMs / 1000.0));
        for (int i = 0; i < fin; i++) buf[i] *= (float)(i / (double)fin);
        for (int i = 0; i < fout; i++) buf[buf.Length - 1 - i] *= (float)(i / (double)fout);
    }

    internal static void Normalize(float[] buf, double peakDbfs)
    {
        if (buf == null || buf.Length == 0) return;
        double peak = 0;
        for (int i = 0; i < buf.Length; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
        if (peak <= 1e-9) return;
        double gain = Math.Pow(10.0, peakDbfs / 20.0) / peak;
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(buf[i] * gain);
    }

    internal static byte[] WriteWav(float[] samples)
    {
        samples ??= Array.Empty<float>();
        int dataBytes = samples.Length * 2;
        var bytes = new byte[44 + dataBytes];

        void Ascii(int at, string s) { for (int i = 0; i < s.Length; i++) bytes[at + i] = (byte)s[i]; }
        void U32(int at, uint v) { bytes[at] = (byte)v; bytes[at + 1] = (byte)(v >> 8); bytes[at + 2] = (byte)(v >> 16); bytes[at + 3] = (byte)(v >> 24); }
        void U16(int at, ushort v) { bytes[at] = (byte)v; bytes[at + 1] = (byte)(v >> 8); }

        Ascii(0, "RIFF");
        U32(4, (uint)(36 + dataBytes));
        Ascii(8, "WAVE");
        Ascii(12, "fmt ");
        U32(16, 16);                       // PCM chunk size
        U16(20, 1);                        // format = PCM
        U16(22, 1);                        // channels = mono
        U32(24, SampleRate);
        U32(28, SampleRate * 2);           // byte rate (1 channel x 2 bytes)
        U16(32, 2);                        // block align
        U16(34, 16);                       // bits per sample
        Ascii(36, "data");
        U32(40, (uint)dataBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            double v = Math.Clamp(samples[i], -1.0, 1.0);
            short s = (short)Math.Round(v * short.MaxValue);
            bytes[44 + i * 2] = (byte)s;
            bytes[44 + i * 2 + 1] = (byte)((ushort)s >> 8);
        }
        return bytes;
    }
}
