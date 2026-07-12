using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the whisper VOICE play site (<see cref="WhisperVoicePlayer"/>): it must (a) play +
/// <c>MarkWhisperAudio</c> only when whispers are enabled AND master is not muted AND a real
/// clip exists (spec guardrail; WPF instead plays at vol 0 / 0.05-floor and still marks —
/// SubliminalService.cs:515-517/534, FlashService.cs:2146), (b) feed the clip duration to
/// <c>MarkWhisperAudio</c> so the BARK-3 overtalk gate activates
/// (SubliminalService.cs:534 / FlashService.cs:903), and (c) duck other audio on play and
/// unduck immediately on failure (SubliminalService.cs:206-207/541).
/// </summary>
public class WhisperVoicePlayerTests
{
    /// <summary>
    /// Records PlayOneShot/MarkWhisperAudio calls. PlayOneShot/MarkWhisperAudio are default
    /// interface members on <see cref="IAudioPlayer"/>; implicit public overrides dispatch
    /// correctly through the interface reference (verified separately), matching how
    /// <c>AvaloniaAudioPlayer</c> overrides them in production.
    /// </summary>
    private sealed class RecordingAudioPlayer : IAudioPlayer
    {
        public double DurationToReport = 1.5;
        public int PlayOneShotCalls;
        public double LastVolume = -1;
        public string? LastPath;
        public int MarkCalls;
        public double LastMarkedDuration = -1;
        public bool ThrowOnPlayOneShot;

        public double PlayOneShot(string filePath, double volume01)
        {
            PlayOneShotCalls++;
            LastPath = filePath;
            LastVolume = volume01;
            if (ThrowOnPlayOneShot) throw new InvalidOperationException("play boom");
            return DurationToReport;
        }
        public void MarkWhisperAudio(double durationSeconds)
        {
            MarkCalls++;
            LastMarkedDuration = durationSeconds;
        }
        public bool IsWhisperAudioPlaying => false;
        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop() { }
        public void SetVolume(double volume) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingDucker : ISystemAudioDucker
    {
        public int DuckCalls;
        public int LastDuckLevel = -1;
        public int UnduckCalls;
        public void Duck() { DuckCalls++; }
        public void Duck(int strengthPercent) { DuckCalls++; LastDuckLevel = strengthPercent; }
        public void Unduck() { UnduckCalls++; }
    }

    // WhisperVoicePlayer gates on File.Exists, so hand it a real (zero-byte) temp clip file.
    private static string MakeClip()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-whisper-clip-" + Guid.NewGuid() + ".mp3");
        File.WriteAllBytes(path, new byte[] { 0x49, 0x44, 0x33 });
        return path;
    }

    [Fact]
    public void WhispersDisabled_NoAudioAndNoMark()
    {
        var audio = new RecordingAudioPlayer();
        var sut = new WhisperVoicePlayer(audio);

        var dur = sut.Play(MakeClip(), whispersEnabled: false, masterMuted: false, volume01: 0.5,
            duckEnabled: false, duckLevel: 80);

        Assert.Equal(0, dur);
        Assert.Equal(0, audio.PlayOneShotCalls);
        Assert.Equal(0, audio.MarkCalls);
    }

    [Fact]
    public void MasterMuted_NoAudioAndNoMark()
    {
        // Spec guardrail (masterMuteRespected): master volume 0 ⇒ NO audio AND NO mark, even
        // though WPF plays at vol 0 / 0.05 floor and still marks (SubliminalService.cs:515-517/534).
        var audio = new RecordingAudioPlayer();
        var sut = new WhisperVoicePlayer(audio);

        var dur = sut.Play(MakeClip(), whispersEnabled: true, masterMuted: true, volume01: 0.5,
            duckEnabled: false, duckLevel: 80);

        Assert.Equal(0, dur);
        Assert.Equal(0, audio.PlayOneShotCalls);
        Assert.Equal(0, audio.MarkCalls);
    }

    [Fact]
    public void MissingFile_NoAudioAndNoMark()
    {
        var audio = new RecordingAudioPlayer();
        var sut = new WhisperVoicePlayer(audio);
        var ghost = Path.Combine(Path.GetTempPath(), "ccp-no-such-clip-" + Guid.NewGuid() + ".mp3");

        var dur = sut.Play(ghost, whispersEnabled: true, masterMuted: false, volume01: 0.5,
            duckEnabled: false, duckLevel: 80);

        Assert.Equal(0, dur);
        Assert.Equal(0, audio.PlayOneShotCalls);
        Assert.Equal(0, audio.MarkCalls);
    }

    [Fact]
    public void NullPath_NoAudioAndNoMark()
    {
        var audio = new RecordingAudioPlayer();
        var sut = new WhisperVoicePlayer(audio);

        var dur = sut.Play(null, whispersEnabled: true, masterMuted: false, volume01: 0.5,
            duckEnabled: false, duckLevel: 80);

        Assert.Equal(0, dur);
        Assert.Equal(0, audio.PlayOneShotCalls);
        Assert.Equal(0, audio.MarkCalls);
    }

    [Fact]
    public void Enabled_PlaysAndMarksWithDuration()
    {
        var audio = new RecordingAudioPlayer { DurationToReport = 2.25 };
        var sut = new WhisperVoicePlayer(audio);
        var clip = MakeClip();

        var dur = sut.Play(clip, whispersEnabled: true, masterMuted: false, volume01: 0.35,
            duckEnabled: false, duckLevel: 80);

        Assert.Equal(2.25, dur);
        Assert.Equal(1, audio.PlayOneShotCalls);
        Assert.Equal(clip, audio.LastPath);
        Assert.Equal(0.35, audio.LastVolume);
        // The bark-gate mark must carry the clip duration (WPF SubliminalService.cs:534 / FlashService.cs:903).
        Assert.Equal(1, audio.MarkCalls);
        Assert.Equal(2.25, audio.LastMarkedDuration);
    }

    [Fact]
    public void DuckEnabled_DucksOtherAudioBeforePlay()
    {
        var audio = new RecordingAudioPlayer();
        var ducker = new RecordingDucker();
        var sut = new WhisperVoicePlayer(audio, ducker);

        sut.Play(MakeClip(), whispersEnabled: true, masterMuted: false, volume01: 0.5,
            duckEnabled: true, duckLevel: 70);

        Assert.Equal(1, audio.PlayOneShotCalls);
        Assert.Equal(1, ducker.DuckCalls);              // WPF SubliminalService.cs:206-207 / FlashService.cs:909-911
        Assert.Equal(70, ducker.LastDuckLevel);
    }

    [Fact]
    public void PlayFailure_UnducksImmediatelyAndNoMark()
    {
        // WPF parity: unduck on failure (SubliminalService.cs:541). Gates passed + duck happened,
        // then PlayOneShot threw ⇒ Unduck immediately, no mark, returns 0.
        var audio = new RecordingAudioPlayer { ThrowOnPlayOneShot = true };
        var ducker = new RecordingDucker();
        var sut = new WhisperVoicePlayer(audio, ducker);

        var dur = sut.Play(MakeClip(), whispersEnabled: true, masterMuted: false, volume01: 0.5,
            duckEnabled: true, duckLevel: 80);

        Assert.Equal(0, dur);
        Assert.Equal(1, ducker.DuckCalls);
        Assert.Equal(1, ducker.UnduckCalls);
        Assert.Equal(0, audio.MarkCalls);
    }
}
