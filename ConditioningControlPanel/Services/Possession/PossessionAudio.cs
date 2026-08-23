using System;
using System.IO;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  POSSESSION AUDIO - the haunt's two sounds. Read Services/Possession/POSSESSION.md first.
//
//  Wave 2, item B13. The room already moves and the warden already talks; what it never did was make a
//  NOISE of its own, so a big effect that landed just off the user's gaze went unnoticed. Two cues,
//  both deliberately small:
//
//    the tick  - a ~50 ms ember click on every BIG effect (App.Possession.EffectStarted, isBig), at
//                -18 dBFS and throttled to one per 1.5 s. It is the audible half of "the warden names
//                the big ones": you hear that something was done, then you find what.
//    the dip   - a 300 ms sag at a rung change and at a third repeated escape attempt. The room gets
//                heavier when you climb a rung, or when you keep pulling at the door.
//
//  WHY the dip is a duck plus a stinger rather than a pitch wobble: there is no shared master graph to
//  wobble. AudioService.PlayOneShot opens ONE WaveOutEvent per clip (that is the whole #778/#779 fix)
//  and LayeredAudioService's MixingSampleProvider has no pitch or varispeed stage, so a -2 semitone
//  dip would mean re-plumbing every audio path in the app for a 300 ms effect. The documented fallback
//  is what ships: AudioService.Duck(60) (other sessions to 40 %, our own layered mixer with them, both
//  ref-counted and watchdogged) for 300 ms, under a synthesized 80 Hz stinger.
//
//  WHY the cues are synthesized instead of shipped as assets: they are two files nobody has to author,
//  translate, license or install, and the installer is already on a size diet. They are rendered once
//  into %LOCALAPPDATA%/ConditioningControlPanel/possession/ and then played through the app's ordinary
//  one-shot path, so output-device selection, the concurrency cap, the endpoint circuit breaker and
//  disposal are all AudioService's, exactly as they are for a bubble pop.
//
//  GATES: AppSettings.LockdownAudioTics (default true; NOT LockdownPhotosafe - photosafe is a VISUAL
//  accommodation, and a user who needs the room to stop flashing may still want to hear it move),
//  MasterVolume > 0, and AudioService's own suppression. Nothing here ever throws at a caller.
// =====================================================================================================

/// <summary>The Possession layer's ember tick and its dip. See the file header.</summary>
public static class PossessionAudio
{
    /// <summary>Peak level of the tick. Quiet on purpose: it rides UNDER whatever is already playing.</summary>
    private const double TickPeakDbfs = -18.0;

    /// <summary>The stinger sits a little louder because 80 Hz is perceived far quieter than a click.</summary>
    private const double StingerPeakDbfs = -12.0;

    private const int SampleRate = 44100;
    private const double TickSeconds = 0.05;
    private const double StingerSeconds = 0.30;

    /// <summary>One tick per 1.5 s. An R4 burst of three big effects must not become a woodpecker.</summary>
    private static readonly TimeSpan TickThrottle = TimeSpan.FromSeconds(1.5);

    /// <summary>Dips are rarer than ticks and far more noticeable, so they get their own longer floor.</summary>
    private static readonly TimeSpan DipThrottle = TimeSpan.FromSeconds(5);

    /// <summary>How long the room stays down. Matches the POSSESSION.md wave-2 spec.</summary>
    private const int DipMs = 300;

    /// <summary>Other sessions drop to 40 %, i.e. a duck STRENGTH of 60.</summary>
    private const int DipDuckStrength = 60;

    private static readonly object Sync = new();

    private static bool _installed;
    private static bool _armed;
    private static DateTime _lastTick = DateTime.MinValue;
    private static DateTime _lastDip = DateTime.MinValue;
    private static string? _tickPath;
    private static string? _stingerPath;

    /// <summary>
    /// Hook the lockdown lifecycle. Cheap: two event subscriptions and nothing else until a lockdown
    /// actually starts, at which point <see cref="Arm"/> subscribes to the director and the two WAV
    /// files are rendered (once, ever). Safe to call twice.
    /// </summary>
    public static void Install()
    {
        lock (Sync)
        {
            if (_installed) return;
            _installed = true;
        }
        try
        {
            var lockdown = App.Lockdown;
            if (lockdown == null) return;
            lockdown.LockdownActivated += OnLockdownActivated;
            lockdown.LockdownDeactivated += OnLockdownDeactivated;
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionAudio install failed: {Error}", ex.Message); }
    }

    // -------------------------------------------------------------------------------------------
    //  Arm / disarm around a lockdown
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Armed even when <c>LockdownAudioTics</c> is off: the toggle lives on a card the user can reach
    /// mid-lockdown, and re-reading it at PLAY time is what makes flipping it take effect immediately
    /// instead of at the next lockdown.
    /// </summary>
    private static void OnLockdownActivated() => Arm();

    private static void OnLockdownDeactivated() => Disarm();

    private static void Arm()
    {
        try
        {
            var director = App.Possession;
            if (director == null) return;
            lock (Sync)
            {
                if (_armed) return;
                _armed = true;
            }

            director.EffectStarted += OnEffectStarted;
            director.RungChanged += OnRungChanged;
            director.TripwireReacted += OnTripwireReacted;

            // Render now, off the UI thread, so the FIRST tick is not the one that pays for the file.
            _ = Task.Run(() => { try { EnsureClips(); } catch { } });
            App.Logger?.Debug("PossessionAudio armed");
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionAudio arm failed: {Error}", ex.Message); }
    }

    private static void Disarm()
    {
        try
        {
            lock (Sync)
            {
                if (!_armed) return;
                _armed = false;
                _lastTick = DateTime.MinValue;
                _lastDip = DateTime.MinValue;
            }
            var director = App.Possession;
            if (director == null) return;
            director.EffectStarted -= OnEffectStarted;
            director.RungChanged -= OnRungChanged;
            director.TripwireReacted -= OnTripwireReacted;
            App.Logger?.Debug("PossessionAudio disarmed");
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionAudio disarm failed: {Error}", ex.Message); }
    }

    // -------------------------------------------------------------------------------------------
    //  The cues
    // -------------------------------------------------------------------------------------------

    private static void OnEffectStarted(string effectId, string? targetKey, bool isBig)
    {
        // Micro-tics stay silent, exactly as they stay unnamed (POSSESSION.md, "the warden names the
        // big ones"). A tick on every R0 nudge would turn the tell into wallpaper.
        if (!isBig) return;
        if (!Throttle(ref _lastTick, TickThrottle)) return;
        PlayClip(EnsureTick(), 1.0f, "possession-tick");
    }

    private static void OnRungChanged(PossessionRung rung)
    {
        // Settle is where every lockdown starts, so it is not a CHANGE anyone can hear as one.
        if (rung == PossessionRung.Settle) return;
        Dip("rung " + rung);
    }

    private static void OnTripwireReacted(EscapeAttempt attempt)
    {
        // Third pull at the same door and up: the same threshold the visual reaction uses for the
        // warden's stare, so the sound and the look land together instead of drifting apart.
        if (attempt.Repeat < 3) return;
        Dip("tripwire " + attempt.Kind + " x" + attempt.Repeat);
    }

    /// <summary>The 300 ms sag: everything else down to 40 %, an 80 Hz stinger under it, then back.</summary>
    private static void Dip(string why)
    {
        if (!Throttle(ref _lastDip, DipThrottle)) return;
        if (!CanPlay()) return;

        App.Logger?.Debug("PossessionAudio dip ({Why})", why);
        PlayClip(EnsureStinger(), 1.0f, "possession-dip");

        var audio = App.Audio;
        if (audio == null) return;
        try
        {
            audio.Duck(DipDuckStrength);
            var generation = audio.DuckGeneration;

            // Fire-and-forget with its own guard: the un-duck must run even if the UI thread is busy
            // or the app is tearing down, and it must never surface as an unobserved task exception.
            // The generation is what stops this stale callback from cutting a LATER duck short.
            _ = Task.Delay(DipMs).ContinueWith(_ =>
            {
                try { App.Audio?.Unduck(generation); }
                catch (Exception ex) { App.Logger?.Debug("PossessionAudio unduck failed: {Error}", ex.Message); }
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionAudio dip failed: {Error}", ex.Message); }
    }

    // -------------------------------------------------------------------------------------------
    //  Playback
    // -------------------------------------------------------------------------------------------

    /// <summary>Every gate that can silence a cue, in one place.</summary>
    private static bool CanPlay()
    {
        try
        {
            if (App.Settings?.Current?.LockdownAudioTics != true) return false;
            if ((App.Settings?.Current?.MasterVolume ?? 0) <= 0) return false;
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return false;
            return true;
        }
        catch { return false; }
    }

    private static void PlayClip(string? path, float scale, string tag)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!CanPlay()) return;
        try
        {
            // Same shape as every other one-shot in the app (ChaosSfx.Volume, BubbleService): the clip
            // is already mixed at its intended level, so master volume is the only multiplier.
            float master = Math.Clamp((App.Settings?.Current?.MasterVolume ?? 0) / 100f, 0f, 1f);
            float volume = Math.Clamp(master * scale, 0f, 1f);
            if (volume <= 0f) return;
            App.Audio?.PlayOneShot(path!, volume, tag);
        }
        catch (Exception ex) { App.Logger?.Debug("PossessionAudio {Tag} failed: {Error}", tag, ex.Message); }
    }

    /// <summary>Rate limiter. Returns true and stamps the clock when the caller may fire.</summary>
    private static bool Throttle(ref DateTime last, TimeSpan gap)
    {
        lock (Sync)
        {
            var now = DateTime.UtcNow;
            if (now - last < gap) return false;
            last = now;
            return true;
        }
    }

    // -------------------------------------------------------------------------------------------
    //  Clip files
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Where the rendered cues live. Under UserDataPath rather than %TEMP%: v6.8.3 taught us that this
    /// app deletes things out of its own temp tree (the .NET extraction-cache incident), and a cue that
    /// vanished mid-lockdown would be re-rendered on the audio path.
    /// </summary>
    private static string ClipDir => Path.Combine(App.UserDataPath, "possession");

    // The file names carry the synth version. Change the maths, change the name - never leave a user
    // listening to a stale render of a cue we have since retuned.
    private static string TickFile => Path.Combine(ClipDir, "ember_tick_v1.wav");
    private static string StingerFile => Path.Combine(ClipDir, "ember_stinger_v1.wav");

    private static string? EnsureTick() => _tickPath ?? EnsureClips().Tick;
    private static string? EnsureStinger() => _stingerPath ?? EnsureClips().Stinger;

    /// <summary>Render both cues if they are not already on disk. Idempotent, never throws.</summary>
    private static (string? Tick, string? Stinger) EnsureClips()
    {
        try
        {
            Directory.CreateDirectory(ClipDir);

            var tick = TickFile;
            if (!IsUsable(tick)) File.WriteAllBytes(tick, WriteWav(SynthTick()));
            _tickPath = tick;

            var stinger = StingerFile;
            if (!IsUsable(stinger)) File.WriteAllBytes(stinger, WriteWav(SynthStinger()));
            _stingerPath = stinger;

            return (_tickPath, _stingerPath);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("PossessionAudio could not render its cues: {Error}", ex.Message);
            return (_tickPath, _stingerPath);
        }
    }

    /// <summary>A header-only or truncated file (killed mid-write) counts as absent.</summary>
    private static bool IsUsable(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 128; }
        catch { return false; }
    }

    // ---8<--- SYNTH BEGIN (pure maths: no App, no NAudio, no WPF - verified standalone, see POSSESSION.md)

    /// <summary>
    /// The tick: a filtered click with a pluck body and a soft tail. Two decaying sines (1420 Hz for
    /// the strike, 710 Hz an octave down for the body), a ~3 ms noise transient so it reads as a CLICK
    /// rather than a beep, a one-pole lowpass at 4 kHz to take the fizz off, and short fades at both
    /// ends so the buffer can never start or end on a step (which is itself an audible click).
    /// </summary>
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

            // xorshift noise, audible only in the first few milliseconds
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

    /// <summary>
    /// The stinger: 80 Hz with a fast decay, plus a quarter of its octave so it still exists on a
    /// laptop speaker that cannot reproduce 80 Hz at all.
    /// </summary>
    internal static float[] SynthStinger()
    {
        int n = (int)(SampleRate * StingerSeconds);
        var buf = new float[n];

        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t / 0.07);
            buf[i] = (float)(env * (Math.Sin(2 * Math.PI * 80.0 * t)
                                    + 0.25 * Math.Sin(2 * Math.PI * 160.0 * t)));
        }

        Fade(buf, inMs: 3.0, outMs: 25.0);
        Normalize(buf, StingerPeakDbfs);
        return buf;
    }

    /// <summary>One-pole lowpass, in place.</summary>
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

    /// <summary>Linear fades at both ends so no buffer starts or ends on a discontinuity.</summary>
    internal static void Fade(float[] buf, double inMs, double outMs)
    {
        if (buf == null || buf.Length == 0) return;
        int fin = Math.Min(buf.Length, (int)(SampleRate * inMs / 1000.0));
        int fout = Math.Min(buf.Length, (int)(SampleRate * outMs / 1000.0));
        for (int i = 0; i < fin; i++) buf[i] *= (float)(i / (double)fin);
        for (int i = 0; i < fout; i++) buf[buf.Length - 1 - i] *= (float)(i / (double)fout);
    }

    /// <summary>Scale the buffer so its loudest sample sits exactly at <paramref name="peakDbfs"/>.</summary>
    internal static void Normalize(float[] buf, double peakDbfs)
    {
        if (buf == null || buf.Length == 0) return;
        double peak = 0;
        for (int i = 0; i < buf.Length; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
        if (peak <= 1e-9) return;
        double target = Math.Pow(10.0, peakDbfs / 20.0);
        double gain = target / peak;
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(buf[i] * gain);
    }

    /// <summary>Mono 16-bit PCM RIFF/WAVE. NAudio's AudioFileReader reads this with no codec at all.</summary>
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

    // ---8<--- SYNTH END
}
