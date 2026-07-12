using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Bark;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the BARK-3 whisper-overtalk gate wiring: <see cref="BarkGateSignals.IsWhisperAudioPlaying"/>
/// must delegate to the registered <see cref="IAudioPlayer"/> (WPF parity: App.Audio.IsWhisperAudioPlaying,
/// BarkService.cs:1342), and the narrator signal must stay its non-blocking default (DTRH/Chaos is
/// web-only in the port = no native narrator).
/// </summary>
public class BarkGateSignalsTests
{
    /// <summary>Fake IAudioPlayer whose IsWhisperAudioPlaying the test toggles directly.</summary>
    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public bool Whisper;
        public bool IsWhisperAudioPlaying => Whisper;
        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop() { }
        public void SetVolume(double volume) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Whisper_DelegatesToRegisteredAudioPlayer()
    {
        var audio = new FakeAudioPlayer { Whisper = true };
        var signals = new BarkGateSignals(avatar: null, audio: audio);

        Assert.True(signals.IsWhisperAudioPlaying);
    }

    [Fact]
    public void Whisper_FalseWhenAudioReportsFalse()
    {
        var audio = new FakeAudioPlayer { Whisper = false };
        var signals = new BarkGateSignals(avatar: null, audio: audio);

        Assert.False(signals.IsWhisperAudioPlaying);
    }

    [Fact]
    public void Whisper_FalseWhenNoAudioPlayerRegistered()
    {
        // No audio player (a head without one) must degrade to non-blocking, not throw.
        var signals = new BarkGateSignals(avatar: null, audio: null);

        Assert.False(signals.IsWhisperAudioPlaying);
    }

    [Fact]
    public void Narrator_StaysDefaultFalse_WebOnlyDtrh()
    {
        // The port has no native narrator (DTRH/Chaos went web-only); the signal keeps its
        // interface default false so the bark gate never blocks on it. IsNarratorPlaying is a
        // default-interface member, so read it through the interface (BarkGateSignals does not
        // override it).
        IBarkGateSignals signals = new BarkGateSignals(avatar: null, audio: new FakeAudioPlayer());

        Assert.False(signals.IsNarratorPlaying);
    }
}
