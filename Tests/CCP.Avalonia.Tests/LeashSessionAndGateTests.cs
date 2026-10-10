using System;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HC3: a pink / detention punishment starts its session through the shell (WPF
/// StartSessionFromRemote). HC5: the leash gate waits for games, the bubble-count card, a tour and the
/// launcher, as WPF MainWindow.Leash.cs:173-179.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LeashSessionAndGateTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void ALeashSessionIsThePlayersOwnEffects_PinkAllTheWay_NeverStrict()
    {
        var pink = LeashTaskHost.BuildSession(PunishKind.Pink, 10);
        Assert.Equal("leash_pink", pink.Id);
        Assert.Equal(10, pink.DurationMinutes);
        Assert.Equal(0, pink.BonusXP);
        Assert.True(pink.Settings.PinkFilterEnabled);
        Assert.Equal(0, pink.Settings.PinkFilterStartMinute);
        Assert.Equal(-1, pink.Settings.PinkFilterEndMinute);
        Assert.False(pink.Settings.MandatoryVideosEnabled);

        var detention = LeashTaskHost.BuildSession(PunishKind.Detention, 500);
        Assert.Equal("leash_detention", detention.Id);
        Assert.Equal(60, detention.DurationMinutes);   // clamped 1..60
        Assert.False(detention.Settings.PinkFilterEnabled);
        Assert.Equal(1, LeashTaskHost.BuildSession(PunishKind.Detention, 0).DurationMinutes);
    }

    [Fact]
    public void StartSession_GoesThroughTheShellStart_AndStopLeavesAForeignSessionAlone() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var (oldSessions, oldStart, oldProvider) = (LeashTaskHost.Sessions, LeashTaskHost.StartSessionOnShell, CoreSession.IsSessionRunningProvider);
        var log = new SessionLogService();
        var runner = new SessionRunner(log);
        var host = new LeashTaskHost();
        try
        {
            Session? started = null;
            LeashTaskHost.StartSessionOnShell = s => { started = s; return true; };

            LeashTaskHost.Sessions = () => null;
            Assert.False(host.StartSession(PunishKind.Pink, 10));   // nothing to start it on
            Assert.Null(started);

            LeashTaskHost.Sessions = () => runner;
            Assert.False(host.SessionRunning);
            Assert.True(host.StartSession(PunishKind.Pink, 10));
            Assert.Equal("leash_pink", started!.Id);

            LeashTaskHost.StartSessionOnShell = _ => throw new InvalidOperationException("boom");
            Assert.False(host.StartSession(PunishKind.Detention, 5));   // a failed start is reported, never thrown

            host.StopSession();   // nothing running: a no-op
            Assert.False(runner.IsRunning);
        }
        finally
        {
            host.Dispose();
            (LeashTaskHost.Sessions, LeashTaskHost.StartSessionOnShell, CoreSession.IsSessionRunningProvider) = (oldSessions, oldStart, oldProvider);
            log.Dispose();
        }
    });

    [Fact]
    public void TheGateWaitsForAGame_ATour_AndTheBubbleCountCard() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var (oldAll, oldTour) = (PanicSurfaces.All, CoreTutorial.IsActiveProvider);
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            bool game = false, tour = false;
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => { }, () => game) };
            CoreTutorial.IsActiveProvider = () => tour;

            var quiet = shell.ReadLeashWorld(true);
            Assert.False(quiet.GameUp);
            Assert.False(quiet.ModalUp);
            Assert.Equal(BubbleCountWindow.IsAnyOpen() || LockCardWindow.IsAnyOpen(), quiet.LockCardOpen);

            game = true;
            var w = shell.ReadLeashWorld(true);
            Assert.True(w.GameUp);
            Assert.False(LeashGateRule.ShouldShow(w));

            game = false; tour = true;
            w = shell.ReadLeashWorld(true);
            Assert.True(w.ModalUp);
            Assert.False(LeashGateRule.ShouldShow(w));
        }
        finally
        {
            (PanicSurfaces.All, CoreTutorial.IsActiveProvider) = (oldAll, oldTour);
            shell.Close();
        }
    });
}
