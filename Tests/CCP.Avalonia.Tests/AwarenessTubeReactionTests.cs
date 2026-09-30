using System;
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

/// <summary>shell-awareness: an observed window change makes the tube say the preset line
/// (WPF AvatarTubeWindow.Reactions.cs OnActivityChanged, AI off), and the cooldown holds the next.</summary>
public sealed class AwarenessTubeReactionTests
{
    [Fact]
    public Task AWindowChangeSpeaksOnceThenTheCooldownHolds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AwarenessReactionCooldownSeconds = 600;
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            tube._startupTime = DateTime.MinValue;   // past WPF's 3 s startup quiet
            var bubble = tube.FindControl<Border>("SpeechBubble")!;
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            bubble.IsVisible = false;
            text.Text = "";

            tube.OnActivityChanged(null, new ActivityChangedEventArgs(
                ActivityCategory.Gaming, ActivityCategory.Unknown, "Steam", "Steam"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(bubble.IsVisible);
            Assert.False(string.IsNullOrWhiteSpace(text.Text));

            bubble.IsVisible = false;
            text.Text = "";
            tube.OnActivityChanged(null, new ActivityChangedEventArgs(
                ActivityCategory.Media, ActivityCategory.Gaming, "YouTube", "YouTube"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(bubble.IsVisible);          // MarkReaction armed the 600 s cooldown
            Assert.Equal("", text.Text);
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
