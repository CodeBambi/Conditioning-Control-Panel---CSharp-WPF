using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Launcher;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Lobby page and the launcher dropdown, built offscreen from a sample snapshot through the
/// same static paint the app calls. Set CCP_LOBBY_SHOT_DIR to also write PNGs for a look.
/// </summary>
public class LobbyRenderTests
{
    private const long Now = 2_000_000_000_000L;

    internal static LobbySnapshot Sample()
    {
        var chess = new PbpLobbyReply(
            new[]
            {
                new PbpOpenSeat("p_a1", "velvetpawn", 600000, 0, Now - 95_000),
                new PbpOpenSeat("p_b2", "dizzy_rook", 300000, 3000, Now - 20_000),
            },
            new[] { new PbpPlayingGame("sleepy_kat", "mirrorgirl", 900000, 10000, Now - 300_000, 24) },
            true);
        var goon = new OpenTablesReply(true, true, new[]
        {
            new OpenTable("PINK42", "hazel", 12, null, false, null, true, 45, true, 60),
            new OpenTable("FRND77", "juno", 30, null, true, "u_juno", false, 30, true, 12),
        }, null);
        var remote = new[]
        {
            new RemoteSeat("u_r1", "softdrone", 8, "standard", new[] { "trance", "audio ok" }, false),
            new RemoteSeat("u_r2", "lacey", 21, "full", new[] { "bimbo" }, true),
        };
        var friends = new[]
        {
            new Friend("u_juno", "juno", null, 2, true, new FriendPresence(PresenceActivity.GoonHosting, null, DateTimeOffset.UtcNow), false),
            new Friend("u_milo", "milo", null, 1, true, new FriendPresence(PresenceActivity.Chess, null, DateTimeOffset.UtcNow), false),
        };
        return LobbyMerge.Build(chess, goon, remote, friends, true, Now);
    }

    private static AvailableSubjectsTabView Page(LobbySnapshot snap, LobbyGates gates, bool error = false)
    {
        var tab = new AvailableSubjectsTabView();
        MainWindow.PaintLobbyInto(tab, snap, gates, error);
        return tab;
    }

    [Fact]
    public void Page_draws_three_lists_and_a_locked_host_pill_for_a_free_account()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var snap = Sample();
            var tab = Page(snap, LobbyGates.From(true, goonHosting: false, remoteHosting: false));
            Layout(tab, 900, 760);
            Assert.Equal(snap.Open.Count, tab.AvailableSubjectsList.Items.Count);
            Assert.Equal(Visibility.Visible, tab.LobbyPlayingSection.Visibility);
            Assert.Equal(Visibility.Visible, tab.LobbyFriendsSection.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.AvailableSubjectsEmptyPanel.Visibility);
            // locked never means hidden
            Assert.Equal(Visibility.Visible, tab.BtnHostGoon.Visibility);
            Assert.StartsWith("\U0001F512", (string)tab.BtnHostGoon.Content);
            Assert.DoesNotContain("\U0001F512", (string)tab.BtnHostChess.Content);
            Assert.True(tab.ActualHeight > 0);
        });

    [Fact]
    public void Signed_out_page_shows_the_one_line_and_no_rows()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var tab = Page(LobbySnapshot.Empty, LobbyGates.From(false, false, false));
            Layout(tab, 900, 500);
            Assert.Equal(Visibility.Visible, tab.AvailableSubjectsEmptyPanel.Visibility);
            Assert.Empty(tab.AvailableSubjectsList.Items);
            Assert.Equal(Visibility.Collapsed, tab.LobbyPlayingSection.Visibility);
        });

    [Fact]
    public void Dropdown_lists_joinable_tables_friends_first_capped()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var snap = Sample();
            var rows = new StackPanel();
            LauncherWindow.FillLobbyDrop(rows, snap, LobbyGates.From(true, true, true), _ => { }, () => { });
            // rows + the "Open lobby" foot
            Assert.Equal(Math.Min(snap.OpenCount, LauncherWindow.LobbyDropMax) + 1, rows.Children.Count);
            Assert.True(snap.Joinable[0].Friend);
        });

    [Fact]
    public void Shot_WhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_LOBBY_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        WpfRenderHarness.OnStaThread(() =>
        {
            Directory.CreateDirectory(dir);
            var bg = new SolidColorBrush(Color.FromRgb(0x1A, 0x10, 0x24));
            var snap = Sample();
            Save(new Border { Background = bg, Child = Page(snap, LobbyGates.From(true, false, false)) }, 900, 820, Path.Combine(dir, "lobby-page-free.png"));
            Save(new Border { Background = bg, Child = Page(snap, LobbyGates.From(true, true, true)) }, 900, 820, Path.Combine(dir, "lobby-page-patron.png"));
            Save(new Border { Background = bg, Child = Page(LobbySnapshot.Empty with { SignedIn = true }, LobbyGates.From(true, true, true)) }, 900, 420, Path.Combine(dir, "lobby-page-empty.png"));

            var rows = new StackPanel();
            LauncherWindow.FillLobbyDrop(rows, snap, LobbyGates.From(true, true, true), _ => { }, () => { });
            var chip = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x5F, 0xA2)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x5F, 0xA2)), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = LauncherWindow.ChipText(snap), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI") },
            };
            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(LauncherWindow.LobbyDropShell(rows));
            stack.Children.Add(chip);
            Save(new Border { Background = new SolidColorBrush(Color.FromRgb(0x24, 0x14, 0x30)), Child = stack }, 440, 0, Path.Combine(dir, "launcher-lobby-dropdown.png"));
        });
    }

    private static void Layout(FrameworkElement e, double w, double h)
    {
        e.Measure(new Size(w, h > 0 ? h : double.PositiveInfinity));
        var desired = e.DesiredSize;
        e.Arrange(new Rect(0, 0, w, h > 0 ? h : desired.Height));
        e.UpdateLayout();
    }

    private static void Save(FrameworkElement e, double w, double h, string path)
    {
        Layout(e, w, h);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(e.ActualWidth), (int)Math.Ceiling(e.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
