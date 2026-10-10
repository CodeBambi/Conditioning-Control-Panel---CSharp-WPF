using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Wave B5 (play#7, #8, #9, #6/#42): the Play wall as WPF 7.1.5 draws it, its zone scroll,
/// and the Racing Thoughts door.</summary>
public sealed class PlayWallParityTests
{
    private static void Boot()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task GamesZoneComesFirst_WithTheNineCards_AndThe715RemovalsAreGone()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Boot();
            var view = new PlayTabView();
            var w = new Window { Width = 1400, Height = 900, Content = view };
            w.Show();
            Dispatcher.UIThread.RunJobs();

            string[] games = { "SlotBreakoutDemo", "SlotBreakout", "SlotGoon", "SlotPieceByPiece", "SlotBackRoom",
                               "SlotDtrh", "SlotArcademy", "SlotRacingThoughts", "SlotWebApp" };
            var grid = view.FindControl<Control>("SlotBreakoutDemo")!.Parent as Panel;
            Assert.NotNull(grid);
            Assert.Equal(games, grid!.Children.Select(c => c.Name).ToArray());

            foreach (var gone in new[] { "BtnPlayFallIn", "BtnPlayQuickDrop", "ChkPlayChaosAnnouncer", "ChkPlayChaosWebGame",
                                         "SlotJustDrop", "BtnPlayJustDrop", "TxtPlayGoonPerkSend", "TxtPlayGoonPerkHost", "RabbitHoleFx" })
                Assert.Null(view.FindControl<Control>(gone));

            // Zone order: Games at the top, then Eyes, then Sessions (page order).
            double? games0 = view.ZoneOffset("games"), eyes = view.ZoneOffset("eyes"), sessions = view.ZoneOffset("sessions");
            Assert.Equal(0, games0);
            Assert.True(eyes > 0, "Eyes header not below the games");
            Assert.True(sessions > eyes, "Sessions header not below Eyes");
            Assert.Null(view.ZoneHeader("nope"));
            w.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void RacingThoughts_OpensOnAnyOwnedTrack_AndTheLauncherHasADestination()
    {
        Assert.False(RaceWindow.CanLaunch(_ => false));
        Assert.True(RaceWindow.CanLaunch(id => id == "rt.original.07"));
        Assert.Equal(new[] { 0, 10 }, RaceWindow.OwnedTracks(id => id is "rt.original.00" or "rt.original.10"));
        Assert.Equal("rt.original.03", RaceWindow.RacingTrack(3));
        Assert.True(LauncherWindow.Destinations.ContainsKey("race"));
        Assert.Equal("dtrh/race.html", RaceWindow.Spec.Page);
    }

    [Fact]
    public void RacingCard_GoesToTheRaceHost_NeverToTheCounter()
    {
        // Signed out or trackless, the card must not take the launcher's mystery route (Back Room).
        Assert.False(PlayTabView.RaceDoorOpen(signedIn: false, owns: _ => true));
        Assert.False(PlayTabView.RaceDoorOpen(signedIn: true, owns: _ => false));
        Assert.True(PlayTabView.RaceDoorOpen(signedIn: true, owns: id => id == "rt.original.02"));
    }
    // The Focus Gaze switch is live since lane u1: FocusGazeSwitchTests.
}
