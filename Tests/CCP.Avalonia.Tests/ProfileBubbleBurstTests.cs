using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane k16: the level-up burst at the profile bubble (WPF MainWindow.ProfileBubble.cs:602,
/// FireBurstAt(BtnProfileBubble, count: 45)), gated by the motion level like WPF EventFxAllowed.
/// It opens a shell and swaps the settings provider, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ProfileBubbleBurstTests
{
    [Fact]
    public Task TheBubbleBurstsOnLevelUpAndMotionOffRefusesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            Assert.Equal(45, MainShellWindow.ProfileBubbleBurstCount);
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            shell = new MainShellWindow();
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();

            // Motion off: no sparks.
            Assert.False(shell.BurstProfileBubble());
            Assert.Equal(0, shell.ProfileBubbleBursts);

            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            Dispatcher.UIThread.RunJobs();
            if (!global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowParticles)
                return Task.CompletedTask;   // a tier with no particle budget on this host: WPF refuses too
            Assert.True(shell.BurstProfileBubble());
            Assert.Equal(1, shell.ProfileBubbleBursts);
        }
        finally
        {
            try { shell?.Close(); } catch { }
            CoreSettings.ServiceProvider = oldSettings;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });
}
