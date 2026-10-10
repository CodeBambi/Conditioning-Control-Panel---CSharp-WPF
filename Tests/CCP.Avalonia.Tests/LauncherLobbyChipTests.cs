using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The launcher's Lobby chip and dropdown (WPF Windows/Launcher/LauncherWindow.Lobby.cs) and the
/// Remote table's Join (WPF MainWindow.ClaimRemoteSubjectAsync). Swaps the process-wide Lobby, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LauncherLobbyChipTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static LobbyService FakeLobby()
    {
        var lobby = new LobbyService(() => null) { SignedIn = () => true, ReadFriends = () => Array.Empty<Friend>() };
        lobby.FetchChess = _ => Task.FromResult(new PbpLobbyReply(Array.Empty<PbpOpenSeat>(), Array.Empty<PbpPlayingGame>(), true));
        lobby.FetchGoon = () => Task.FromResult(OpenTablesReply.Empty);
        lobby.FetchRemote = () => Task.FromResult<(IReadOnlyList<RemoteSeat>, bool)>((Array.Empty<RemoteSeat>(), true));
        return lobby;
    }

    private static LobbySnapshot Snapshot(int chessSeats) => LobbyMerge.Build(
        new PbpLobbyReply(Enumerable.Range(1, chessSeats).Select(i => new PbpOpenSeat("p_" + i, "ann" + i, 600000, 0, 0)).ToArray(),
            Array.Empty<PbpPlayingGame>(), true),
        OpenTablesReply.Empty,
        new[] { new RemoteSeat("u9", "dee", 7, "full", new[] { "trance" }, false) },
        Array.Empty<Friend>(), signedIn: true, nowMs: 1_000_000);

    [Fact]
    public void Chip_text_and_dim_rule()
    {
        Assert.Equal(Loc.Get("launcher_lobby"), LauncherWindow.ChipText(LobbySnapshot.Empty));
        var snap = Snapshot(2);
        Assert.Equal(Loc.GetF("launcher_lobby_open", snap.OpenCount), LauncherWindow.ChipText(snap));
        Assert.Equal(1.0, LauncherWindow.LobbyChipOpacity(0, 0));     // nothing to count
        Assert.Equal(1.0, LauncherWindow.LobbyChipOpacity(3, 1));     // fresh
        Assert.Equal(NavRailRules.BadgeSeenOpacity + 0.15, LauncherWindow.LobbyChipOpacity(3, 3));   // seen
    }

    [Fact]
    public Task Chip_sits_beside_the_friends_chip_and_the_drop_lists_six_tables_then_open_lobby() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        CoreSettings.Current.LauncherSoundEnabled = false;
        MainShellWindow.UseLobbyForTests(FakeLobby());
        var w = new LauncherWindow();
        try
        {
            w.StartLobbyChip();
            var chip = w.LobbyChip;
            Assert.NotNull(chip);
            var friends = w.FindControl<FriendsRailChip>("FriendsChip")!;
            var row = (Panel)friends.Parent!;
            Assert.Equal(row.Children.IndexOf(friends) + 1, row.Children.IndexOf(chip!));
            w.StartLobbyChip();   // idempotent: one chip
            Assert.Single(row.Children.OfType<Button>(), b => (b.Tag as string) == "launcher-lobby-chip");

            var snap = Snapshot(8);   // 8 chess + 1 remote joinable
            w.PaintLobbyChip(snap);
            var rows = w.LobbyDropRows!;
            Assert.Equal(LauncherWindow.LobbyDropMax + 1, rows.Children.Count);
            var open = Assert.IsType<Button>(rows.Children[^1]);
            Assert.Equal("launcher-lobby-open-page", open.Tag);

            // Nothing joinable: one line says so, and the door to the page is still there.
            w.PaintLobbyChip(LobbySnapshot.Empty);
            Assert.Equal(2, rows.Children.Count);
            Assert.IsType<TextBlock>(rows.Children[0]);

            // A click opens the drop; a second closes it.
            chip!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(w.LobbyDrop!.IsOpen);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(w.LobbyDrop.IsOpen);
        }
        finally
        {
            w.Close();
            MainShellWindow.UseLobbyForTests(null);
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Signed_out_join_from_the_drop_never_claims() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        CoreSettings.Current.LauncherSoundEnabled = false;
        var (oldClaim, oldIdentity) = (MainShellWindow.LobbyClaimRemote, LobbyWire.DefaultIdentity);
        var claims = 0;
        MainShellWindow.LobbyClaimRemote = _ => { claims++; return Task.FromResult<(string?, bool)>((null, false)); };
        LobbyWire.DefaultIdentity = () => null;
        MainShellWindow.UseLobbyForTests(FakeLobby());
        var w = new LauncherWindow();
        try
        {
            w.StartLobbyChip();
            var remote = Snapshot(0).Joinable.Single(r => r.Game == LobbyGame.Remote);
            if (!MainShellWindow.CurrentLobbyGates().SignedIn)
            {
                w.JoinFromDrop(remote);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, claims);
            }
        }
        finally
        {
            w.Close();
            MainShellWindow.UseLobbyForTests(null);
            (MainShellWindow.LobbyClaimRemote, LobbyWire.DefaultIdentity) = (oldClaim, oldIdentity);
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Remote_join_claims_and_hands_the_session_url_to_the_browser_once() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var (oldClaim, oldOpen) = (MainShellWindow.LobbyClaimRemote, MainShellWindow.LobbyOpenSessionUrl);
        var asked = new List<string>();
        var opened = new List<string>();
        (string?, bool) answer = ("https://cclabs.app/remote/#code=ABC123&pin=0420", false);
        MainShellWindow.LobbyClaimRemote = key => { asked.Add(key); return Task.FromResult(answer); };
        MainShellWindow.LobbyOpenSessionUrl = (_, url) => { opened.Add(url); return Task.FromResult(true); };
        var lobby = FakeLobby();
        MainShellWindow.UseLobbyForTests(lobby);
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await shell.ClaimRemoteSubjectAsync("u9");
            Assert.Equal(new[] { "u9" }, asked);
            Assert.Equal(new[] { "https://cclabs.app/remote/#code=ABC123&pin=0420" }, opened);

            // Someone claimed first: nothing opens (the list is re-read and the row flips).
            answer = (null, true);
            await shell.ClaimRemoteSubjectAsync("u9");
            Assert.Single(opened);

            await shell.ClaimRemoteSubjectAsync("");
            Assert.Equal(2, asked.Count);
        }
        finally
        {
            shell.Close();
            MainShellWindow.UseLobbyForTests(null);
            (MainShellWindow.LobbyClaimRemote, MainShellWindow.LobbyOpenSessionUrl) = (oldClaim, oldOpen);
        }
    });
}
