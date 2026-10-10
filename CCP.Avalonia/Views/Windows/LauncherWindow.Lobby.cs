// PORTED from WPF 7.1.5 Windows/Launcher/LauncherWindow.Lobby.cs.
//
// THE LOBBY CHIP: "Lobby · N open" in the bottom row beside the friends chip. A click drops the open
// tables (friends first) with Join, and "Open lobby" at the foot. A Join launches that game straight
// onto the table. The count rides the launcher's open tables tick (60 s while visible, never hidden);
// the dropdown takes a poll lease while it is open. Click-opened, light-dismissed, no text box.
//
// Deviations:
//   - The drop sits ABOVE the chip (Placement Top, as WPF), so it never covers its own trigger.
//   - WPF PopupAnimation.Fade is not carried (the drop just shows); card hover is the WPF 1 px lift,
//     in and out, off when the motion level forbids transitions.
//   - Under Lockdown the chip does nothing, like every other launcher control on this head.
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services.Lobby;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        internal const int LobbyDropMax = 6;

        private static readonly FontFamily LobbyFredoka = new("Fredoka, Segoe UI");
        private Button? _lobbyChip;
        private TextBlock? _lobbyChipText;
        private Popup? _lobbyDrop;
        private StackPanel? _lobbyDropRows;
        private IDisposable? _lobbyDropLease;
        private bool _lobbyChipHooked;

        internal Button? LobbyChip => _lobbyChip;
        internal Popup? LobbyDrop => _lobbyDrop;
        internal StackPanel? LobbyDropRows => _lobbyDropRows;

        private static Color Hue(uint argb) => Color.FromUInt32(argb);

        /// <summary>Built once, beside the friends chip.</summary>
        private void EnsureLobbyChip()
        {
            if (_lobbyChip != null || FriendsChip?.Parent is not Panel row) return;
            _lobbyChipText = new TextBlock { FontFamily = LobbyFredoka, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
            _lobbyChip = new Button
            {
                Content = new Border
                {
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(12, 4, 12, 5),
                    // 7.1.5: the Social hue, never the pink-red that read as unread messages.
                    Background = new SolidColorBrush(Hue(NavStripRules.WithAlpha(NavStripRules.Sky, 0.20))),
                    BorderBrush = new SolidColorBrush(Hue(NavStripRules.WithAlpha(NavStripRules.Sky, 0.40))),
                    BorderThickness = new Thickness(1),
                    Child = _lobbyChipText,
                },
                Template = BareTemplate(),
                Cursor = new Cursor(StandardCursorType.Hand),
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
                IsLightDismissEnabled = true,
                Child = LobbyDropShell(_lobbyDropRows),
            };
            _lobbyDrop.Opened += (_, _) =>
            {
                try { _lobbyDropLease ??= MainShellWindow.Lobby.Watch(); } catch (Exception ex) { Log.Debug("[Launcher] lobby lease: {E}", ex.Message); }
                // Opening the drop is seeing the tables: the chip dims until more open.
                try { NavBadges.MarkSeen(NavSections.Social); } catch { }
                if (_lobbyChip != null) _lobbyChip.Opacity = LobbyChipOpacity(NavBadges.Get(NavSections.Social), NavBadges.Seen(NavSections.Social));
            };
            _lobbyDrop.Closed += (_, _) => { _lobbyDropLease?.Dispose(); _lobbyDropLease = null; };
            row.Children.Add(_lobbyDrop);   // a Popup lives in the tree; it takes no room in the row
            PaintLobbyChip(MainShellWindow.Lobby.Snapshot);
        }

        /// <summary>WPF StartLobbyChip: from the open tables start (launcher shown).</summary>
        internal void StartLobbyChip()
        {
            try
            {
                EnsureLobbyChip();
                var lobby = MainShellWindow.Lobby;
                if (!_lobbyChipHooked) { lobby.Changed += OnLobbyChipChanged; _lobbyChipHooked = true; }
                _ = lobby.RefreshAsync();
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] lobby chip start failed"); }
        }

        /// <summary>WPF StopLobbyChip: launcher hidden or closed.</summary>
        private void StopLobbyChip()
        {
            try
            {
                if (_lobbyDrop != null) _lobbyDrop.IsOpen = false;
                _lobbyDropLease?.Dispose();
                _lobbyDropLease = null;
                if (_lobbyChipHooked) { MainShellWindow.Lobby.Changed -= OnLobbyChipChanged; _lobbyChipHooked = false; }
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] lobby chip stop failed"); }
        }

        private void OnLobbyChipChanged(LobbySnapshot snap)
        {
            try { Dispatcher.UIThread.Post(() => PaintLobbyChip(snap), DispatcherPriority.Normal); }
            catch { }
        }

        internal void ToggleLobbyDrop()
        {
            if (_lobbyDrop == null || MainShellWindow.LockdownActive) return;
            PaintLobbyChip(MainShellWindow.Lobby.Snapshot);
            _lobbyDrop.IsOpen = !_lobbyDrop.IsOpen;
        }

        internal void PaintLobbyChip(LobbySnapshot snap)
        {
            if (_lobbyChipText == null || _lobbyDropRows == null) return;
            _lobbyChipText.Text = ChipText(snap);
            // Same seen rule as the rail badge (NavBadges): bright only when the count rose past what
            // the player last saw, dimmed after.
            try { NavBadges.Set(NavSections.Social, snap.OpenCount); } catch { }
            if (_lobbyChip != null) _lobbyChip.Opacity = LobbyChipOpacity(snap.OpenCount, NavBadges.Seen(NavSections.Social));
            FillLobbyDrop(_lobbyDropRows, snap, MainShellWindow.CurrentLobbyGates(), JoinFromDrop, () =>
            {
                if (_lobbyDrop != null) _lobbyDrop.IsOpen = false;
                OpenPanel(p => p.ShowTab("availablesubjects"));
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

        /// <summary>The chip's opacity: full when there is nothing to count or the count is fresh,
        /// dimmed once the player has seen it (7.1.5). Pure.</summary>
        internal static double LobbyChipOpacity(int openCount, int seen) =>
            openCount <= 0 || NavBadges.IsFresh(openCount, seen) ? 1.0 : NavRailRules.BadgeSeenOpacity + 0.15;

        internal static string ChipText(LobbySnapshot snap) =>
            snap.OpenCount > 0 ? Loc.GetF("launcher_lobby_open", snap.OpenCount) : Loc.Get("launcher_lobby");

        /// <summary>The dropdown's rows: at most <see cref="LobbyDropMax"/> joinable tables, friends
        /// first, then "Open lobby".</summary>
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
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                Tag = "launcher-lobby-open-page",
            };
            open.Click += (_, _) => openPage();
            panel.Children.Add(open);
        }

        private static Control DropRow(LobbyRowView v, Action<LobbyRow> joinRow)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            // The same card as the Lobby page: feature art, rim-lit glass, a Join key that travels.
            var icon = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                Background = v.ArtBrush,
            };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Line(v.Title, 14, 1, new Thickness(0));
            title.FontFamily = LobbyFredoka;
            title.FontWeight = FontWeight.SemiBold;
            text.Children.Add(title);
            text.Children.Add(Line(v.Line, 11.5, 0.72, new Thickness(0, 1, 0, 0)));
            Grid.SetColumn(text, 1);

            var travel = new TranslateTransform();
            var face = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 4, 14, 5),
                Margin = new Thickness(0, 0, 0, 3),
                RenderTransform = travel,
                Background = Vertical(Color.FromRgb(0xFF, 0x8C, 0xC0), Color.FromRgb(0xFF, 0x5F, 0xA2)),
                Child = new TextBlock { Text = v.ButtonText, FontFamily = LobbyFredoka, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x06, 0x18)) },
            };
            var key = new Grid();
            key.Children.Add(new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 3, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0xB2, 0x3A, 0x6B)) });
            key.Children.Add(face);
            var join = new Button
            {
                Content = key,
                Template = BareTemplate(),
                Cursor = new Cursor(StandardCursorType.Hand),
                Opacity = v.ButtonOpacity,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "launcher-lobby-join",
            };
            // Press sinks the face onto its base; release and leave bring it back.
            join.AddHandler(PointerPressedEvent, (_, _) => travel.Y = 3, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            join.AddHandler(PointerReleasedEvent, (_, _) => travel.Y = 0, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            join.PointerExited += (_, _) => travel.Y = 0;
            join.Click += (_, _) => joinRow(v.Row);
            Grid.SetColumn(join, 2);

            grid.Children.Add(icon);
            grid.Children.Add(text);
            grid.Children.Add(join);
            var lift = new TranslateTransform();
            var card = new Border
            {
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(8, 7, 8, 7),
                Background = Vertical(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Vertical(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = grid,
                Margin = new Thickness(0, 0, 0, 6),
                RenderTransform = lift,
            };
            card.PointerEntered += (_, _) => { if (AmbientFxCanvas.Env.AllowTransitions) lift.Y = -1; };
            card.PointerExited += (_, _) => lift.Y = 0;
            return card;
        }

        /// <summary>Join straight from the dropdown: the game opens on the table, and the launcher hides
        /// behind it and comes back when the game closes (LaunchGame). A pending leash punishment sends the
        /// press to the gate on the panel first, like a game tile.</summary>
        internal void JoinFromDrop(LobbyRow row)
        {
            if (_lobbyDrop != null) _lobbyDrop.IsOpen = false;
            if (!row.CanJoin || string.IsNullOrEmpty(row.Key)) return;
            var gates = MainShellWindow.CurrentLobbyGates();
            if (!gates.SignedIn || !gates.CanJoin(row.Game)) { OpenSignIn(); return; }
            try
            {
                switch (row.Game)
                {
                    case LobbyGame.Chess:
                        if (MainShellWindow.LeashBlocksGames) { OpenPanel(p => p.PresentLeashGateFromLauncher()); break; }
                        MainShellWindow.LobbyJoinChess(row.Key);
                        LaunchGame(Games.GameWindow.PbpId);
                        break;
                    case LobbyGame.Goon:
                        if (MainShellWindow.LeashBlocksGames) { OpenPanel(p => p.PresentLeashGateFromLauncher()); break; }
                        MainShellWindow.LobbyJoinGoon(row.Key);
                        LaunchGame("goon");
                        break;
                    case LobbyGame.Remote:
                        _ = Panel?.ClaimRemoteSubjectAsync(row.Key);
                        break;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] lobby join {Game} failed", row.Game); }
        }

        private static LinearGradientBrush Vertical(Color top, Color bottom) => new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
        };

        private static TextBlock Line(string text, double size, double opacity, Thickness margin) => new()
        {
            Text = text,
            FontSize = size,
            Foreground = Brushes.White,
            Opacity = opacity,
            Margin = margin,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        private static IControlTemplate BareTemplate() => new FuncControlTemplate<Button>((b, _) => new ContentPresenter
        {
            [!ContentPresenter.ContentProperty] = b[!ContentProperty],
            Background = Brushes.Transparent,
        });
    }
}
