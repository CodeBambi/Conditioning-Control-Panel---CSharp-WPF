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

/// <summary>THE SPIRAL ROOM on this head, through the fuse arming, the fuse rail chip (the way in
/// since the Spiral row left the rail, WPF MainWindow.SpiralRoom.cs:76) and the fuse's kill switch -
/// SpiralTabView.Refresh reading Core SpiralRoom.StateFor.</summary>
public sealed class SpiralRoomShellTests
{
    [Fact]
    public Task FuseArmsTheRailChip_ChipOpensTheFog_KillSwitchEndsBoth() => AvaloniaTestDispatcher.RunAsync(() =>
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
            var chip = shell.Named<global::ConditioningControlPanel.Avalonia.Controls.DescentFuseRailChip>("FuseRailChip")!;
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            Assert.False(chip.IsVisible);   // fuse dark, no block: every install today

            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DescentFusePhase.Vigil, fuse.LastAnnouncedPhase);
            Assert.True(chip.IsVisible);

            chip.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
                { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsVisible);
            Assert.True(tab.FindControl<Grid>("FogHost")!.IsVisible);
            var digits = tab.FindControl<TextBlock>("FogDigits")!;
            Assert.Equal(56, digits.FontSize);   // the hero T-minus, not "any moment now"
            Assert.NotEqual(DescentFuseCopy.FogImminent, digits.Text);
            Assert.False(tab.FindControl<Button>("BtnSpiralHelp")!.IsVisible);   // no "?" over the fog

            fuse.ApplyCeremonyAt(null);   // the kill switch, live
            Dispatcher.UIThread.RunJobs();
            Assert.False(chip.IsVisible);
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
