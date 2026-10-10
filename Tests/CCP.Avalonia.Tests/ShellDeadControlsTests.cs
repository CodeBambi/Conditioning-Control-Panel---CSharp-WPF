using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// H20 / X9: the controls that sat hidden and dead. The Spiral Room reads the Descent block (WPF
/// SpiralTabView.Refresh: App.Descent?.Current, App.DescentMigration?.SpiralWithheld), and the Home
/// wall's centre tile shows the weekly Intake Pass card (WPF RefreshIntakePassTile).
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ShellDeadControlsTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    [Fact]
    public void TheWithholdIsWpfArithmetic()
    {
        Assert.False(DescentMigration.SpiralWithheldFor(null, true, true));
        var s = new AppSettings();
        Assert.False(DescentMigration.SpiralWithheldFor(s, false, false));
        Assert.True(DescentMigration.SpiralWithheldFor(s, true, false));
        Assert.True(DescentMigration.SpiralWithheldFor(s, false, true));
        s.DescentMigrationOffered = true;
        Assert.True(DescentMigration.SpiralWithheldFor(s, false, false));
        s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;   // answered wins
        Assert.False(DescentMigration.SpiralWithheldFor(s, true, true));
        s.PendingDescentMigrationChoice = null;
        s.DescentMigrationCompleted = true;
        Assert.False(DescentMigration.SpiralWithheldFor(s, true, true));
    }

    [Fact]
    public Task TheSpiralRoomFollowsTheBlockAndTheWithhold() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var saved = (CoreSettings.ServiceProvider, MainShellWindow.ProfileSpiralBlock, MainShellWindow.ProfileSpiralWithheld, AvApp.DescentCountdown);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        AvApp.DescentCountdown = null;
        var block = new DescentBlock { DevotionDays = 12, Stage = new DescentStage { N = 3, NextAt = 30 } };
        var expectSpiral = SpiralRoom.StateFor(service.Current, DescentFusePhase.Dark, false, false, true) == SpiralRoomState.Spiral;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            MainShellWindow.ProfileSpiralBlock = () => null;
            MainShellWindow.ProfileSpiralWithheld = () => false;
            shell.ShowTab(SpiralRoom.TabKey);
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            Assert.False(tab.IsShowingSpiral);                      // no block: the waiting room, as WPF

            MainShellWindow.ProfileSpiralBlock = () => block;
            shell.ShowTab("settings");
            shell.ShowTab(SpiralRoom.TabKey);
            Dispatcher.UIThread.RunJobs();
            Assert.True(expectSpiral, "Core SpiralRoom no longer opens the spiral for a block in hand");
            Assert.True(tab.IsShowingSpiral);                       // the block opens the room (was hardcoded shut)

            MainShellWindow.ProfileSpiralWithheld = () => true;     // the migration withholds it
            shell.ShowTab("settings");
            shell.ShowTab(SpiralRoom.TabKey);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.IsShowingSpiral);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, MainShellWindow.ProfileSpiralBlock, MainShellWindow.ProfileSpiralWithheld, AvApp.DescentCountdown) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheIntakePassCardShowsOnlyWhileAPassIsWaiting() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var saved = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider,
            CoreAccount.HasPremiumAccessProvider, MainShellWindow.IntakePassFaceHoldMs);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.MotionLevel = MotionLevel.Off;
        s.IntakePassSpentWeek = "";
        s.IntakePassSpentUtc = null;
        bool signedIn = true, premium = false;
        CoreAccount.IsLoggedInProvider = () => signedIn;
        CoreAccount.HasLabAccessProvider = () => premium;
        CoreAccount.HasPremiumAccessProvider = () => premium;
        MainShellWindow.IntakePassFaceHoldMs = 20;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            var dash = shell.SettingsPage!;
            var face = dash.FindControl<Border>("IntakePassFace")!;
            var logo = dash.FindControl<Border>("LogoFaceLogo")!;
            Assert.Equal(IntakePassState.Available, AvApp.IntakePass.State);

            // Motion Off: the card, still. Nothing turns.
            shell.RefreshIntakePassTile();
            Assert.True(shell.IntakePassShowingCard);
            Assert.False(shell.IntakePassLoopRunning);
            Assert.True(face.IsVisible);
            Assert.False(logo.IsVisible);
            Assert.Equal(Loc.Get("intake_pass_card_headline"), dash.FindControl<TextBlock>("IntakePassHeadline")!.Text);
            Assert.Equal(Loc.Get("intake_pass_card_cta"), dash.FindControl<TextBlock>("IntakePassCta")!.Text);
            Assert.True(dash.FindControl<Border>("IntakePassArt")!.IsVisible);   // the niche card art ships

            // The run is spent: only the logo, as WPF (the card never shows a spent week).
            s.IntakePassSpentWeek = IntakePassService.CurrentWeekKey();
            AvApp.IntakePass.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.False(face.IsVisible);
            Assert.True(logo.IsVisible);

            // Prime / Basic never see it either.
            s.IntakePassSpentWeek = "";
            premium = true;
            AvApp.IntakePass.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.IntakePassShowingCard);

            // Motion on, a pass waiting: the tile alternates, logo first, and reaches the card.
            premium = false;
            s.MotionLevel = MotionLevel.Full;
            shell.RefreshIntakePassTile();
            Assert.True(shell.IntakePassLoopRunning);
            Assert.False(shell.IntakePassShowingCard);
            var reached = false;
            for (var i = 0; i < 300 && !reached; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
                reached = shell.IntakePassShowingCard;
            }
            Assert.True(reached, "the tile never turned to the pass card");

            // Leaving Home stops the turn and squares the tile.
            shell.ShowTab("presets");
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.IntakePassLoopRunning);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider,
                CoreAccount.HasPremiumAccessProvider, MainShellWindow.IntakePassFaceHoldMs) = saved;
        }
    });
}
