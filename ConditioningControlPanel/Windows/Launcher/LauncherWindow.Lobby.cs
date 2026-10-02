using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Views.Tabs;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// THE LOBBY CHIP (2026-10-02): "Lobby · N open" in the bottom row beside the friends chip.
/// A click drops the open tables (friends first) with Join, and "Open lobby" at the foot. A Join
/// launches that game straight onto the table. The count rides the launcher's existing open
/// tables tick (60 s while visible, never hidden); the dropdown takes a 10 s lease while open.
/// The popup is click-opened with StaysOpen=false (no hover capture trap) and holds no text box.
/// </summary>
public partial class LauncherWindow
{
    internal const int LobbyDropMax = 6;

    private static readonly FontFamily Fredoka = new("/Fonts/#Fredoka, Segoe UI");
    private Button? _lobbyChip;
    private TextBlock? _lobbyChipText;
    private Popup? _lobbyDrop;
    private StackPanel? _lobbyDropRows;
    private IDisposable? _lobbyDropLease;
    private bool _lobbyHooked;

    /// <summary>Built once, beside the friends chip.</summary>
    private void EnsureLobbyChip()
    {
        if (_lobbyChip != null || FriendsChip?.Parent is not Panel row) return;
        _lobbyChipText = new TextBlock { FontFamily = Fredoka, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        _lobbyChip = new Button
        {
            Content = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 4, 12, 5),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x5F, 0xA2)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x5F, 0xA2)),
                BorderThickness = new Thickness(1),
                Child = _lobbyChipText,
            },
            Template = BareTemplate(),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "launcher-lobby-chip",
        };
        _lobbyChip.Click += (_, _) => ToggleLobbyDrop();
        row.Children.Insert(row.Children.IndexOf(FriendsChip) + 1, _lobbyChip);

        _lobbyDropRows = new StackPanel();
        _lobbyDrop = new Popup
        {
            PlacementTarget = _lobbyChip,
            Placement = PlacementMode.Top,
            VerticalOffset = -8,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = MotionFx.AllowTransitions ? PopupAnimation.Fade : PopupAnimation.None,
            Child = LobbyDropShell(_lobbyDropRows),
        };
        _lobbyDrop.Opened += (_, _) => { _lobbyDropLease ??= App.Lobby?.Watch(); };
        _lobbyDrop.Closed += (_, _) => { _lobbyDropLease?.Dispose(); _lobbyDropLease = null; };
        PaintLobbyChip(App.Lobby?.Snapshot ?? LobbySnapshot.Empty);
    }

    private void StartLobbyChip()
    {
        EnsureLobbyChip();
        if (App.Lobby == null) return;
        if (!_lobbyHooked) { App.Lobby.Changed += OnLobbyChanged; _lobbyHooked = true; }
        _ = App.Lobby.RefreshAsync();
    }

    private void StopLobbyChip()
    {
        if (_lobbyDrop != null) _lobbyDrop.IsOpen = false;
        if (_lobbyHooked && App.Lobby != null) { App.Lobby.Changed -= OnLobbyChanged; _lobbyHooked = false; }
    }

    private void OnLobbyChanged(LobbySnapshot snap)
    {
        try { Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => PaintLobbyChip(snap))); }
        catch { }
    }

    private void ToggleLobbyDrop()
    {
        if (_lobbyDrop == null) return;
        PaintLobbyChip(App.Lobby?.Snapshot ?? LobbySnapshot.Empty);
        _lobbyDrop.IsOpen = !_lobbyDrop.IsOpen;
    }

    internal void PaintLobbyChip(LobbySnapshot snap)
    {
        if (_lobbyChipText == null || _lobbyDropRows == null) return;
        _lobbyChipText.Text = ChipText(snap);
        FillLobbyDrop(_lobbyDropRows, snap, MainWindow.CurrentLobbyGates(), JoinFromDrop, () =>
        {
            if (_lobbyDrop != null) _lobbyDrop.IsOpen = false;
            LauncherHost.OpenPanelTab("availablesubjects");
        });
    }

    /// <summary>The dropdown's glass card: solid enough to read over any tile art.</summary>
    internal static Border LobbyDropShell(StackPanel rows) => new()
    {
        Width = 380,
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(12),
        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x16, 0x0B, 0x22)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        Child = rows,
    };

    internal static string ChipText(LobbySnapshot snap) =>
        snap.OpenCount > 0 ? Loc.GetF("launcher_lobby_open", snap.OpenCount) : Loc.Get("launcher_lobby");

    /// <summary>The dropdown's rows: at most <see cref="LobbyDropMax"/> joinable tables, friends
    /// first, then "Open lobby". Static so the render harness draws the very same panel.</summary>
    internal static void FillLobbyDrop(StackPanel panel, LobbySnapshot snap, LobbyGates gates, Action<LobbyRow> join, Action openPage)
    {
        panel.Children.Clear();
        var rows = snap.Joinable.Take(LobbyDropMax).ToList();
        if (rows.Count == 0)
            panel.Children.Add(Line(Loc.Get(snap.SignedIn ? "lobby_empty_open" : "lobby_signed_out"), 13, 0.75, new Thickness(6, 4, 6, 10)));
        foreach (var r in rows) panel.Children.Add(DropRow(new LobbyRowView(r, gates), join));

        var label = Line(Loc.Get("launcher_lobby_open_page"), 13, 1, new Thickness(0));
        label.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0xC8));
        var open = new Button
        {
            Content = label,
            Template = BareTemplate(),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        open.Click += (_, _) => openPage();
        panel.Children.Add(open);
    }

    private static FrameworkElement DropRow(LobbyRowView v, Action<LobbyRow> joinRow)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Image { Source = ConditioningControlPanel.Helpers.EmojiImage.Get(v.GameIcon), Width = 20, Height = 20, Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Line(v.Title, 14, 1, new Thickness(0));
        title.FontFamily = Fredoka;
        title.FontWeight = FontWeights.SemiBold;
        text.Children.Add(title);
        var sub = string.IsNullOrEmpty(v.Summary) ? v.GameLabel : v.GameLabel + " · " + v.Summary;
        text.Children.Add(Line(sub, 11.5, 0.7, new Thickness(0, 1, 0, 0)));
        Grid.SetColumn(text, 1);

        var join = new Button
        {
            Content = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 4, 14, 5),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0xA2)),
                Child = new TextBlock { Text = v.ButtonText, FontFamily = Fredoka, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x06, 0x18)) },
            },
            Template = BareTemplate(),
            Cursor = Cursors.Hand,
            Opacity = v.ButtonOpacity,
            VerticalAlignment = VerticalAlignment.Center,
        };
        join.Click += (_, _) => joinRow(v.Row);
        Grid.SetColumn(join, 2);

        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(join);
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Child = grid,
            Margin = new Thickness(0, 0, 0, 6),
        };
    }

    /// <summary>Join straight from the dropdown: the game opens on the table, and the launcher
    /// hides behind it and comes back when the game closes (LauncherHost.LaunchGame).</summary>
    private void JoinFromDrop(LobbyRow row)
    {
        if (_lobbyDrop != null) _lobbyDrop.IsOpen = false;
        if (!row.CanJoin || string.IsNullOrEmpty(row.Key)) return;
        var gates = MainWindow.CurrentLobbyGates();
        if (!gates.SignedIn || !gates.CanJoin(row.Game))
        {
            try { LauncherHost.RequestSignIn?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "[Launcher] lobby sign-in failed"); }
            return;
        }
        try
        {
            switch (row.Game)
            {
                case LobbyGame.Chess:
                    if (App.MainWindowRef?.LeashBlocksGames != true) PieceByPieceHostService.JoinOpenTable(row.Key);
                    LauncherHost.LaunchGame("piecebypiece");
                    break;
                case LobbyGame.Goon:
                    if (App.MainWindowRef?.LeashBlocksGames != true) GoonHostService.Launch(duckMainWindow: true, joinCode: row.Key);
                    LauncherHost.LaunchGame("goon");
                    break;
                case LobbyGame.Remote:
                    _ = App.MainWindowRef?.ClaimRemoteSubjectAsync(row.Key);
                    break;
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] lobby join {Game} failed", row.Game); }
    }

    private static TextBlock Line(string text, double size, double opacity, Thickness margin) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brushes.White,
        Opacity = opacity,
        Margin = margin,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static ControlTemplate BareTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }
}
