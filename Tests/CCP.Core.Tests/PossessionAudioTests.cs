using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>PORTED with WPF PossessionAudio (7.1.5): two synthesised tones (never speech), quiet, behind
/// the Lockdown audio-tics switch and the master volume, and stopped at once on panic.</summary>
[Collection("PossessionAudio")]
public sealed class PossessionAudioTests : IDisposable
{
    private readonly (Func<DateTime>, Func<AppSettings?>, Func<string, float, string, Action>, Func<string>) _was =
        (PossessionAudio.UtcNow, PossessionAudio.Settings, PossessionAudio.Play, PossessionAudio.ClipDir);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-poss-audio-" + Guid.NewGuid().ToString("N"));
    private readonly List<(string Path, float Volume, string Tag)> _played = new();
    private readonly AppSettings _settings = new() { LockdownAudioTics = true, MasterVolume = 50 };
    private DateTime _now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private int _stopped;

    public PossessionAudioTests()
    {
        PossessionAudio.Disarm();
        PossessionAudio.ForgetClips();
        PossessionAudio.UtcNow = () => _now;
        PossessionAudio.Settings = () => _settings;
        PossessionAudio.ClipDir = () => _dir;
        PossessionAudio.Play = (path, volume, tag) => { _played.Add((path, volume, tag)); return () => _stopped++; };
    }

    public void Dispose()
    {
        PossessionAudio.Disarm();
        PossessionAudio.ForgetClips();
        (PossessionAudio.UtcNow, PossessionAudio.Settings, PossessionAudio.Play, PossessionAudio.ClipDir) = _was;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static double PeakDbfs(float[] buf) => 20 * Math.Log10(buf.Max(v => Math.Abs(v)));

    [Fact]
    public void TheTwoCuesAreShortQuietTones_AndRenderTheSameEveryTime()
    {
        var tick = PossessionAudio.SynthTick();
        var stinger = PossessionAudio.SynthStinger();
        Assert.Equal(2205, tick.Length);          // 50 ms
        Assert.Equal(13230, stinger.Length);      // 300 ms
        Assert.Equal(-18.0, PeakDbfs(tick), 2);
        Assert.Equal(-12.0, PeakDbfs(stinger), 2);
        Assert.Equal(0f, tick[0]);                // faded in and out: no click
        Assert.Equal(0f, stinger[^1]);
        Assert.Equal(tick, PossessionAudio.SynthTick());

        var wav = PossessionAudio.WriteWav(tick);
        Assert.Equal(44 + tick.Length * 2, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(44100, BitConverter.ToInt32(wav, 24));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));   // mono
    }

    [Fact]
    public void OnlyABigEffectTicks_AtMostEvery1500ms_AtTheMasterVolume()
    {
        PossessionAudio.OnEffectStarted("nudge", "TxtTitle", isBig: false);
        Assert.Empty(_played);

        PossessionAudio.OnEffectStarted("melt", "CardPips", isBig: true);
        var played = Assert.Single(_played);
        Assert.Equal(PossessionAudio.TickTag, played.Tag);
        Assert.Equal(0.5f, played.Volume);
        Assert.True(File.Exists(played.Path) && new FileInfo(played.Path).Length > 128);

        _now = _now.AddMilliseconds(900);
        PossessionAudio.OnEffectStarted("retitle", "TxtTitle", isBig: true);
        Assert.Single(_played);
        _now = _now.AddMilliseconds(700);
        PossessionAudio.OnEffectStarted("retitle", "TxtTitle", isBig: true);
        Assert.Equal(2, _played.Count);
    }

    [Fact]
    public void TheStingerAnswersARungAboveSettle_AndTheThirdPullAtATripwire()
    {
        PossessionAudio.OnRungChanged(PossessionRung.Settle);
        Assert.Empty(_played);
        PossessionAudio.OnRungChanged(PossessionRung.Drift);
        Assert.Equal(PossessionAudio.DipTag, Assert.Single(_played).Tag);

        _now = _now.AddSeconds(2);
        PossessionAudio.OnRungChanged(PossessionRung.Melt);            // inside the 5 s throttle
        Assert.Single(_played);

        _now = _now.AddSeconds(6);
        PossessionAudio.OnTripwireReacted(new EscapeAttempt(EscapeKinds.Stop, 2, 2, _now));
        Assert.Single(_played);
        PossessionAudio.OnTripwireReacted(new EscapeAttempt(EscapeKinds.Stop, 3, 3, _now));
        Assert.Equal(2, _played.Count);
    }

    [Fact]
    public void TheSwitchOffOrTheVolumeAtZeroIsSilence()
    {
        _settings.LockdownAudioTics = false;
        PossessionAudio.OnEffectStarted("melt", null, true);
        PossessionAudio.OnRungChanged(PossessionRung.Collapse);
        Assert.Empty(_played);

        _settings.LockdownAudioTics = true;
        _settings.MasterVolume = 0;
        _now = _now.AddSeconds(10);
        PossessionAudio.OnEffectStarted("melt", null, true);
        PossessionAudio.OnRungChanged(PossessionRung.Collapse);
        Assert.Empty(_played);
    }

    [Fact]
    public void PanicStopsWhatIsSounding_InTheCall()
    {
        PossessionAudio.OnEffectStarted("melt", null, true);
        PossessionAudio.OnRungChanged(PossessionRung.Drift);
        Assert.Equal(2, _played.Count);
        Assert.Equal(0, _stopped);
        PossessionAudio.StopForPanic();
        Assert.Equal(2, _stopped);
        PossessionAudio.StopForPanic();   // safe twice
        Assert.Equal(2, _stopped);
    }
}

[CollectionDefinition("PossessionAudio", DisableParallelization = true)]
public sealed class PossessionAudioCollection { }
