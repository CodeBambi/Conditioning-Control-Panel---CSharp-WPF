using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>THE SPIRAL ROOM on this head, driven from the shell ctor (InitializeSpiralRoom) through
/// the fuse arming, the door and the fuse's kill switch - WPF 7.1.5 MainWindow.SpiralRoom.cs and
/// SpiralTabView.Refresh, both reading Core SpiralRoom.StateFor. Since the nav rework the Spiral row
/// is off the rail: "spiral" is a hidden You tab (no pill), reached through ShowTab.</summary>
public sealed class SpiralRoomShellTests
{
    [Fact]
    public Task FuseArmed_ShowTabOpensTheFog_KillSwitchEndsIt() => AvaloniaTestDispatcher.RunAsync(() =>
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
            Assert.Null(shell.Named<Button>("BtnNavSpiral"));   // the rail row is retired
            Assert.Equal("you", NavSections.SectionForTab(SpiralRoom.TabKey));
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;

            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DescentFusePhase.Vigil, fuse.LastAnnouncedPhase);

            shell.ShowTab(SpiralRoom.TabKey);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsVisible);
            // Hidden tab: the You strip draws no pill for it, even while it is on screen.
            Assert.DoesNotContain(shell.GetVisualDescendants().OfType<Button>(), b => b.Name == "NavPill_spiral");
            Assert.True(tab.FindControl<Grid>("FogHost")!.IsVisible);
            var digits = tab.FindControl<TextBlock>("FogDigits")!;
            Assert.Equal(56, digits.FontSize);   // the hero T-minus, not "any moment now"
            Assert.NotEqual(DescentFuseCopy.FogImminent, digits.Text);
            Assert.False(tab.FindControl<Button>("BtnSpiralHelp")!.IsVisible);   // no "?" over the fog

            fuse.ApplyCeremonyAt(null);   // the kill switch, live
            Dispatcher.UIThread.RunJobs();
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
