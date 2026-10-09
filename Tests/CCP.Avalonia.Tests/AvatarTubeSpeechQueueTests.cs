using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Wave B tube: speech queue + pacing (T6), typewriter maths (T17), latch cap (T25),
/// talk planning (T3) and recorded phrase clips (ai#10), against WPF 7.1.5's numbers.</summary>
public sealed class AvatarTubeSpeechQueueTests
{
    [Fact]
    public void PacingAndTypewriterNumbersMatchWpf()
    {
        Assert.Equal(2.0, AvatarTubeWindow.RequiredDelayAfter(AvatarTubeWindow.SpeechSource.Preset, 50));
        Assert.Equal(7.0, AvatarTubeWindow.RequiredDelayAfter(AvatarTubeWindow.SpeechSource.AI, 50));
        Assert.Equal(9.0, AvatarTubeWindow.RequiredDelayAfter(AvatarTubeWindow.SpeechSource.AI, 200), 3);
        Assert.Equal(0, AvatarTubeWindow.EstimateTypewriterDurationMs(0));
        Assert.Equal(30 * 20, AvatarTubeWindow.EstimateTypewriterDurationMs(20));            // AI: capped 30 ms/char
        Assert.Equal(75 * 20, AvatarTubeWindow.EstimateTypewriterDurationMs(20, slow: true)); // preset: 75 ms/char
        // AI reply: typing + max(user, 12 chars/s floor).
        var ai = AvatarTubeWindow.DisplaySeconds(new string('x', 240), AvatarTubeWindow.SpeechSource.AI, 2, typed: true);
        Assert.Equal(AvatarTubeWindow.EstimateTypewriterDurationMs(240) / 1000.0 + 20.0, ai, 3);
        Assert.Equal(2.0 + 1.5, AvatarTubeWindow.DisplaySeconds(new string('x', 20), AvatarTubeWindow.SpeechSource.Preset, 2, true), 3);
        Assert.True(AvatarTubeWindow.IsLatchStale(true, false, false, true, false, false));
        Assert.False(AvatarTubeWindow.IsLatchStale(true, true, false, true, false, false));
        Assert.Equal(TimeSpan.FromMinutes(2), AvatarTubeWindow.MaxHoverHold);
        Assert.Equal("Naughty Bambi here", AvatarTubeWindow.StripLinks("[Naughty Bambi](https://x.test/a) here"));
    }

    [Fact]
    public void TalkRulesMatchWpf()
    {
        Assert.True(AvatarTubeWindow.IsNonverbalLine("*giggles*"));
        Assert.True(AvatarTubeWindow.IsNonverbalLine("mmm~ hehe"[..4]));
        Assert.False(AvatarTubeWindow.IsNonverbalLine("Good girl, keep going"));
        Assert.Equal(2.5, AvatarTubeWindow.EstimateDurationSec(null));
        Assert.Equal(2.0, AvatarTubeWindow.EstimateDurationSec("hi"));
        Assert.Equal(7.0, AvatarTubeWindow.EstimateDurationSec(string.Join(' ', new string[40]).Replace(" ", " w")));
    }

    [Fact]
    public void PhraseClipOverrideWinsAndMissingStaysSilent()
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var dir = Directory.CreateTempSubdirectory("ccp-phrase-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "mine.mp3"), new byte[] { 1 });
            CoreSettings.Current.PhraseAudioOverrides = new Dictionary<string, string> { ["Idle:1"] = "mine.mp3", ["Idle:2"] = "gone.mp3" };
            Assert.Equal("Idle:1", CompanionPhraseAudio.PhraseId("Idle", "b", new[] { "a", "b" }));
            Assert.Null(CompanionPhraseAudio.PhraseId("Idle", "zzz", new[] { "a" }));
            Assert.Equal(Path.Combine(dir, "mine.mp3"), CompanionPhraseAudio.OverrideClip("Idle:1", dir));
            Assert.Null(CompanionPhraseAudio.OverrideClip("Idle:2", dir));   // file missing: text only
            File.WriteAllBytes(Path.Combine(dir, "level up good girl.mp3"), new byte[] { 1 });
            Assert.Equal(Path.Combine(dir, "level up good girl.mp3"), CompanionPhraseAudio.EventClip("LEVEL UP! Good girl!~", dir));
            Assert.Null(CompanionPhraseAudio.EventClip("never recorded", dir));
        }
        finally { Directory.Delete(dir, true); service.SealForReset(); }
    }

    [Fact]
    public Task SecondPresetWaitsItsTurnAndPanicDropsTheQueue() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.Giggle("first line");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("first line", text.Text);
            tube.Giggle("second line");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("first line", text.Text);   // not replaced: queued
            Assert.Equal(1, tube.QueuedSpeechCount);
            tube.PanicSilence();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.False(tube.IsSpeaking);
        }
        finally { tube.Close(); service.SealForReset(); }
        return Task.CompletedTask;
    });
}
