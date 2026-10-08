using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>THE SPIRAL ROOM on this head, driven from the shell ctor (InitializeSpiralRoom) through
/// the fuse arming, the rail row's click and the fuse's kill switch - WPF MainWindow.SpiralRoom.cs
/// and SpiralTabView.Refresh, both reading Core SpiralRoom.StateFor.</summary>
public sealed class SpiralRoomShellTests
{
    [Fact]
    public Task FuseArmsTheRailEllipsis_ClickOpensTheFog_KillSwitchEndsBoth() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var (oldAt, oldDone, oldChoice) = (s.DescentCeremonyAtUtc, s.DescentMigrationCompleted, s.PendingDescentMigrationChoice);
        s.DescentCeremonyAtUtc = null;
        s.DescentMigrationCompleted = false;
        s.PendingDescentMigrationChoice = null;
        var oldFuse = global::ConditioningControlPanel.Avalonia.App.DescentCountdown;
        var fuse = new DescentCountdownService();
        global::ConditioningControlPanel.Avalonia.App.DescentCountdown = fuse;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var row = shell.Named<Button>("BtnNavSpiral")!;
            var label = shell.Named<TextBlock>("TxtNavSpiral")!;
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            Assert.False(row.IsVisible);   // fuse dark, no block: every install today

            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DescentFusePhase.Vigil, fuse.LastAnnouncedPhase);
            Assert.True(row.IsVisible);
            Assert.Equal("…", label.Text);   // the fog era does not name the room

            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsVisible);
            Assert.True(tab.FindControl<Grid>("FogHost")!.IsVisible);
            var digits = tab.FindControl<TextBlock>("FogDigits")!;
            Assert.Equal(56, digits.FontSize);   // the hero T-minus, not "any moment now"
            Assert.NotEqual(DescentFuseCopy.FogImminent, digits.Text);
            Assert.False(tab.FindControl<Button>("BtnSpiralHelp")!.IsVisible);   // no "?" over the fog

            fuse.ApplyCeremonyAt(null);   // the kill switch, live
            Dispatcher.UIThread.RunJobs();
            Assert.False(row.IsVisible);
            Assert.False(tab.FindControl<Grid>("FogHost")!.IsVisible);
            Assert.True(tab.FindControl<Border>("WaitingPanel")!.IsVisible);
        }
        finally
        {
            shell.Close();
            global::ConditioningControlPanel.Avalonia.App.DescentCountdown = oldFuse;
            fuse.Dispose();
            (s.DescentCeremonyAtUtc, s.DescentMigrationCompleted, s.PendingDescentMigrationChoice) = (oldAt, oldDone, oldChoice);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
