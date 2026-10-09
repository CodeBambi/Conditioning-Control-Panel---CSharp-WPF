using System;
using System.Collections.Generic;
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

/// <summary>tube w2: the self-starting speech loops (WPF AvatarTubeWindow.Speech.cs greeting, idle
/// chatter, Trigger Mode) speak into the bubble, and the WPF numbers/copy hold.</summary>
public sealed class AvatarTubeSpeechLoopTests
{
    [Fact]
    public void GreetingCopyAndTriggerTimingMatchWpf()
    {
        Assert.Equal("Welcome back! 💖", AvatarTubeWindow.FormatGreetingName("Welcome back, {name}! 💖", null));
        Assert.Equal("Welcome back, Sam! 💖", AvatarTubeWindow.FormatGreetingName("Welcome back, {name}! 💖", " Sam "));
        Assert.Null(AvatarTubeWindow.BuildAbsenceGreeting(null));
        var line = AvatarTubeWindow.BuildAbsenceGreeting(DateTime.UtcNow.AddDays(-60), "Sam", new Random(1))!;
        Assert.DoesNotContain("—", line);
        Assert.DoesNotContain("{name}", line);
        Assert.Equal(5.0, AvatarTubeWindow.TriggerDisplaySeconds(0));
        Assert.Equal(6.0, AvatarTubeWindow.TriggerDisplaySeconds(20));
        Assert.Equal(14.0, AvatarTubeWindow.TriggerDisplaySeconds(1000));
    }

    [Fact]
    public Task IdleBeatAndTriggerTickSpeakIntoTheBubble() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarMuted = false;
        CoreSettings.Current.CustomTriggers = new List<string> { "LET GO" };
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var bubble = tube.FindControl<Border>("SpeechBubble")!;
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            bubble.IsVisible = false;
            text.Text = "";

            Assert.True(tube.IsSpeechReady());
            tube.OnTriggerTick(null, EventArgs.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.True(bubble.IsVisible);
            Assert.Equal("LET GO", text.Text);
            Assert.False(tube.IsSpeechReady());          // a bubble is up: the next beat skips

            tube.OnIdleTick(null, EventArgs.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("LET GO", text.Text);           // not talked over
        }
        finally
        {
            tube.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
