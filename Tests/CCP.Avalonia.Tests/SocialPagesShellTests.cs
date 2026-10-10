using System;
using System.Collections.Generic;
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
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Lane E1, Social: the Lobby page on the Core LobbyService (paint, narrow switcher, the
/// lease that follows the page), Friends and Leash through the tab registry, and the Leaderboard's
/// 7.1.5 header (All-Time first, no season chrome).</summary>
public sealed class SocialPagesShellTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Pump()
    {
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static LobbyService FakeLobby(LobbySnapshot? _ = null)
    {
        var lobby = new LobbyService(() => null) { SignedIn = () => true, ReadFriends = () => Array.Empty<Friend>() };
        lobby.FetchChess = _ => Task.FromResult(new PbpLobbyReply(
            new[] { new PbpOpenSeat("p_1", "ann", 600000, 0, 0) },
            new[] { new PbpPlayingGame("w", "b", 300000, 3000, 0, 4) }, true));
        lobby.FetchGoon = () => Task.FromResult(OpenTablesReply.Empty);
        lobby.FetchRemote = () => Task.FromResult<(IReadOnlyList<RemoteSeat>, bool)>((Array.Empty<RemoteSeat>(), true));
        return lobby;
    }

    private static LobbySnapshot Snapshot() => LobbyMerge.Build(
        new PbpLobbyReply(new[] { new PbpOpenSeat("p_1", "ann", 600000, 0, 0), new PbpOpenSeat("p_2", "bo", 300000, 3000, 0) },
            new[] { new PbpPlayingGame("w", "b", 300000, 3000, 0, 4) }, true),
        new OpenTablesReply(true, true, new[] { new OpenTable("ABCD", "cy", 3, null, true, "f1", true, 45, false, 10) }, null),
        new[] { new RemoteSeat("u9", "dee", 7, "full", new[] { "voice" }, false) },
        new[] { new Friend("f1", "cy", null, 0, true, new FriendPresence(PresenceActivity.GoonHosting, null, DateTimeOffset.UtcNow), false) },
        signedIn: true, nowMs: 1_000_000);

    [Fact]
    public async Task LobbyPagePaintsThreeColumnsWithGatesAndCounts()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var tab = new AvailableSubjectsTabView();
            var snap = Snapshot();
            // Signed in, no premium: chess joins, Goon hosting locked, Remote hosting locked.
            AvailableSubjectsTabView.PaintInto(tab, snap, LobbyGates.From(true, false, false), error: false);
            var open = (List<LobbyRowView>)tab.Part<ItemsControl>("AvailableSubjectsList").ItemsSource!;
            var playing = (List<LobbyRowView>)tab.Part<ItemsControl>("LobbyPlayingList").ItemsSource!;
            var friends = (List<LobbyRowView>)tab.Part<ItemsControl>("LobbyFriendsList").ItemsSource!;
            Assert.Equal(snap.Open.Count, open.Count);
            Assert.Equal(snap.Playing.Count, playing.Count);
            Assert.Single(friends);                                   // the Goon friend table, list 3 only
            Assert.Equal(snap.Open.Count.ToString(), tab.Part<TextBlock>("LobbyOpenCount").Text);
            Assert.False(tab.Part<Border>("AvailableSubjectsEmptyPanel").IsVisible);
            Assert.False(tab.Part<TextBlock>("LobbyPlayingEmpty").IsVisible);
            Assert.Equal(Loc.GetF("lobby_open_count", snap.OpenCount), tab.Part<TextBlock>("TxtLobbyCount").Text);
            Assert.All(open, r => Assert.False(r.Locked));            // joining needs only an account
            Assert.Contains("🔒", ((TextBlock)tab.Part<Button>("BtnHostGoon").Content!).Text);
            Assert.DoesNotContain("🔒", ((TextBlock)tab.Part<Button>("BtnHostChess").Content!).Text);
            Assert.True(tab.Part<TextBlock>("TxtBecomeASubjectSubtitle").IsVisible);
            Assert.All(playing, r => Assert.False(r.HasButton));      // no game can be watched yet

            // Signed out: empty, every host locked, the sign-in line instead of "nobody's hosting".
            AvailableSubjectsTabView.PaintInto(tab, LobbySnapshot.Empty, LobbyGates.From(false, false, false), error: false);
            Assert.True(tab.Part<Border>("AvailableSubjectsEmptyPanel").IsVisible);
            Assert.Equal(Loc.Get("lobby_signed_out"), tab.Part<TextBlock>("TxtLobbyEmpty").Text);
            Assert.Contains("🔒", ((TextBlock)tab.Part<Button>("BtnHostChess").Content!).Text);
            Assert.False(tab.Part<TextBlock>("TxtBecomeASubjectSubtitle").IsVisible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LobbyGoesNarrowAsATabSwitcherUnder900()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            Assert.True(AvailableSubjectsTabView.IsNarrowWidth(700));
            Assert.False(AvailableSubjectsTabView.IsNarrowWidth(1200));
            var tab = new AvailableSubjectsTabView();
            tab.SetNarrow(true);
            Assert.True(tab.Part<StackPanel>("LobbyTabStrip").IsVisible);
            Assert.True(tab.Part<Border>("LobbyOpenColumn").IsVisible);
            Assert.False(tab.Part<Border>("LobbyPlayingSection").IsVisible);
            tab.SelectColumn(2);
            Assert.False(tab.Part<Border>("LobbyOpenColumn").IsVisible);
            Assert.True(tab.Part<Border>("LobbyFriendsSection").IsVisible);
            Assert.Equal(3, Grid.GetColumnSpan(tab.Part<Border>("LobbyFriendsSection")));
            tab.SetNarrow(false);
            Assert.True(tab.Part<Border>("LobbyOpenColumn").IsVisible && tab.Part<Border>("LobbyPlayingSection").IsVisible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LobbyLeaseFollowsThePageAndRowsArrive()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var lobby = FakeLobby();
            MainShellWindow.UseLobbyForTests(lobby);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                Pump();
                shell.ShowTab("availablesubjects");
                Pump();
                Assert.True(shell.HoldsLobbyPageLease, "the Lobby page takes the poll lease while on screen");
                await lobby.RefreshAsync();
                Pump();
                var page = shell.Named<AvailableSubjectsTabView>("AvailableSubjectsTab")!;
                Assert.Single((List<LobbyRowView>)page.Part<ItemsControl>("AvailableSubjectsList").ItemsSource!);
                Assert.Equal(1, ConditioningControlPanel.Nav.NavBadges.Get(ConditioningControlPanel.Nav.NavSections.Social));

                shell.ShowTab("studio");
                Pump();
                Assert.False(shell.HoldsLobbyPageLease, "leaving the page drops its lease");
                Assert.True(lobby.IsWatched, "the rail badge keeps its own lease while the panel is up");
            }
            finally
            {
                shell.Close();
                MainShellWindow.UseLobbyForTests(null);
                ConditioningControlPanel.Nav.NavBadges.Set(ConditioningControlPanel.Nav.NavSections.Social, 0);
            }
        });
    }

    [Fact]
    public async Task FriendsAndLeashArePagesInTheRegistry()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            MainShellWindow.UseLobbyForTests(FakeLobby());
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                Pump();
                Assert.True(shell.IsRegisteredNavTab("friends"));
                Assert.True(shell.IsRegisteredNavTab("leash"));
                shell.ShowTab("friends");
                Pump();
                var host = shell.Named<Grid>("LaneTabHost")!;
                var friends = host.Children.OfType<FriendsTabView>().Single();
                Assert.True(friends.IsVisible);
                Assert.Equal("friends", shell.CurrentTab);
                Assert.True(double.IsNaN(friends.Drawer.Width), "the page drawer fills the column, not the popup's 300 px");

                shell.ShowTab("leash");
                Pump();
                var leash = host.Children.OfType<LeashTabView>().Single();
                Assert.True(leash.IsVisible && !friends.IsVisible);
                Assert.True(leash.ShowingEmpty, "no leash service built in the shell test: the 7.1.5 empty state");
                leash.EmptyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                Assert.Equal("friends", shell.CurrentTab);
            }
            finally
            {
                shell.Close();
                MainShellWindow.UseLobbyForTests(null);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LeaderboardOpensOnAllTimeWithNoSeasonChrome()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var tab = new LeaderboardTabView();
            Assert.False(tab.IsAllTimeMode);
            tab.IsVisible = false;
            tab.IsVisible = true;                                     // the first show settles the default
            Assert.True(tab.IsAllTimeMode);
            Assert.Equal(Loc.Get("lb_all_time_title"), tab.FindControl<TextBlock>("TxtLeaderboardSeason")!.Text);
            Assert.False(tab.FindControl<Border>("SeasonRecapHost")!.IsVisible, "the recap button's host never shows");

            tab.SetLeaderboardMode(false);
            Assert.Equal(Loc.Get("social_lb_month_title"), tab.FindControl<TextBlock>("TxtLeaderboardSeason")!.Text);
            Assert.Equal(Loc.Get("social_lb_month_sub"), tab.FindControl<TextBlock>("TxtLeaderboardSubtitle")!.Text);
            tab.IsVisible = false;
            tab.IsVisible = true;                                     // a player's own pick is never overridden
            Assert.False(tab.IsAllTimeMode);
            return Task.CompletedTask;
        });
    }
}
