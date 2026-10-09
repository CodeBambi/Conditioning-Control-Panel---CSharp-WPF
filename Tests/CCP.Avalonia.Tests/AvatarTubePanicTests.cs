using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>tube#T2: panic silences the tube (WPF AvatarTubeWindow.Speech.cs:1912 PanicSilence):
/// the voiced line is cut, the thinking dots stop and the bubble leaves the screen.</summary>
public sealed class AvatarTubePanicTests
{
    [Fact]
    public Task PanicCutsTheVoiceAndTakesTheBubbleDown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarMuted = false;
        CoreSettings.Current.MasterVolume = 80;
        var clip = Path.Combine(Path.GetTempPath(), $"ccp-tube-panic-{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(clip, new byte[] { 0 });
        var oldProvider = CoreAudio.PlayStoppableProvider;
        int stops = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => stops++;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var bubble = tube.FindControl<Border>("SpeechBubble")!;

            tube.GigglePriority("hello", phraseAudioPath: clip);
            Dispatcher.UIThread.RunJobs();
            Assert.True(bubble.IsVisible);
            Assert.True(tube.IsSpeaking);

            tube.PanicSilence();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, stops);                     // the voice line was cut
            Assert.False(bubble.IsVisible);
            Assert.False(tube.IsSpeaking);

            tube.StartThinkingAnimation();
            Dispatcher.UIThread.RunJobs();
            Assert.True(bubble.IsVisible);
            Assert.True(tube.IsCompanionBusy(0));
            tube.PanicSilence();
            Dispatcher.UIThread.RunJobs();
            Assert.False(bubble.IsVisible);             // thinking dots gone
            Assert.False(tube.IsCompanionBusy(0));

            tube.ShowListeningBubble("listening");
            Dispatcher.UIThread.RunJobs();
            tube.PanicSilence();
            Dispatcher.UIThread.RunJobs();
            Assert.False(bubble.IsVisible);
            tube.PanicSilence();                        // idempotent when idle
            Assert.Equal(1, stops);
        }
        finally
        {
            CoreAudio.PlayStoppableProvider = oldProvider;
            tube.Close();
            Dispatcher.UIThread.RunJobs();
            try { File.Delete(clip); } catch { }
            service.SealForReset(); CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
