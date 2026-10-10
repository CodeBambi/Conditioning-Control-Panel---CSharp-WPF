using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Views.Tabs;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE LOBBY page (2026-10-02): the old Available Subjects tab, same rail entry and tab key.
    /// Reads <see cref="App.Lobby"/> while the tab is visible (a lease, so the poll stops when
    /// the tab goes) and paints three lists. Every Join and Host goes through the game's OWN
    /// door: chess through the board's lobby, Goon through GoonHostService, Remote through the
    /// directory claim. Locked buttons stay visible; a click shows the gate's refusal.
    /// </summary>
    public partial class MainWindow
    {
        private IDisposable? _lobbyLease;
        private bool _lobbyHooked;

        /// <summary>The rail badge's own lease ("3 open" on Social): held while the panel is on
        /// screen, released when it hides to the tray, minimises or closes. LobbyService counts
        /// leases and runs ONE timer, so this lease and the Lobby tab's (or the launcher's) never
        /// poll twice; each new lease only asks for one round at once.</summary>
        private IDisposable? _lobbyBadgeLease;
        private bool _lobbyBadgeHooked;

        /// <summary>True when the panel should hold the badge lease.</summary>
        internal static bool LobbyBadgeWanted(bool visible, WindowState state) =>
            visible && state != WindowState.Minimized;

        /// <summary>Once, at startup (RegisterSocialTabs): follow the panel's visibility.</summary>
        internal void HookLobbyBadge()
        {
            if (_lobbyBadgeHooked) return;
            _lobbyBadgeHooked = true;
            IsVisibleChanged += (_, _) => SyncLobbyBadgeLease();
            StateChanged += (_, _) => SyncLobbyBadgeLease();
            Closed += (_, _) => { _lobbyBadgeLease?.Dispose(); _lobbyBadgeLease = null; };
            SyncLobbyBadgeLease();
        }

        private void SyncLobbyBadgeLease()
        {
            try
            {
                var lobby = App.Lobby;
                if (lobby == null) return;
                if (!_lobbyHooked) { lobby.Changed += OnLobbyChanged; _lobbyHooked = true; }
                if (LobbyBadgeWanted(IsVisible, WindowState))
                {
                    _lobbyBadgeLease ??= lobby.Watch();
                    NavBadges.Set(NavSections.Social, lobby.Snapshot.OpenCount);
                }
                else
                {
                    _lobbyBadgeLease?.Dispose();
                    _lobbyBadgeLease = null;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Lobby badge lease: {E}", ex.Message); }
        }

        /// <summary>The gates as each game reads them today.</summary>
        internal static LobbyGates CurrentLobbyGates()
        {
            bool signedIn = BackRoomApi.AppIdentity() != null;
            bool remote = App.Patreon?.HasPremiumAccess == true || App.DailyFree?.IsFreeToday("remote") == true;
            return LobbyGates.From(signedIn, GoonHostService.HostingAllowed(), remote);
        }

        /// <summary>ShowTab("availablesubjects"): hook once, take a lease, paint what we have.</summary>
        private void EnterLobbyTab()
        {
            if (App.Lobby == null) return;
            if (!_lobbyHooked) { App.Lobby.Changed += OnLobbyChanged; _lobbyHooked = true; }
            PaintLobby(App.Lobby.Snapshot);
            _lobbyLease ??= App.Lobby.Watch();
        }

        /// <summary>Any tab switch away: drop the lease (the last lease stops the poll).</summary>
        private void LeaveLobbyTab()
        {
            _lobbyLease?.Dispose();
            _lobbyLease = null;
        }

        private void OnLobbyChanged(LobbySnapshot snap)
        {
            // The rail badge: NavBadges marshals nothing itself, the rail does (contract 3).
            try { NavBadges.Set(NavSections.Social, snap.OpenCount); } catch { }
            try { Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => PaintLobby(snap))); }
            catch { }
        }

        /// <summary>Tier or account moved (MainWindow.Patreon): repaint the host bar's locks.</summary>
        private void RefreshBecomeASubjectCta()
        {
            try { PaintLobbyHostBar(CurrentLobbyGates()); }
            catch (Exception ex) { App.Logger?.Debug("Lobby host bar: {E}", ex.Message); }
        }

        private void PaintLobby(LobbySnapshot snap)
        {
            var tab = AvailableSubjectsTab;
            if (tab == null) return;
            PaintLobbyInto(tab, snap, CurrentLobbyGates(), error: snap.SignedIn && App.Lobby?.LastRoundOk == false);
        }

        private void PaintLobbyHostBar(LobbyGates gates)
        {
            if (AvailableSubjectsTab != null) PaintLobbyHostBar(AvailableSubjectsTab, gates);
        }

        /// <summary>Pure paint of the page from a snapshot. Static so the render harness draws
        /// exactly what the app draws.</summary>
        internal static void PaintLobbyInto(AvailableSubjectsTabView tab, LobbySnapshot snap, LobbyGates gates, bool error)
        {
            PaintLobbyHostBar(tab, gates);
            var open = LobbyRowView.From(snap.Open, gates);
            var playing = LobbyRowView.From(snap.Playing, gates);
            var friends = LobbyRowView.From(snap.Friends, gates);
            tab.AvailableSubjectsList.ItemsSource = open;
            tab.LobbyPlayingList.ItemsSource = playing;
            tab.LobbyFriendsList.ItemsSource = friends;

            tab.LobbyOpenCount.Text = snap.Open.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tab.LobbyPlayingCount.Text = snap.Playing.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tab.LobbyFriendsCount.Text = snap.Friends.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tab.LobbyPlayingEmpty.Visibility = snap.Playing.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tab.LobbyFriendsEmpty.Visibility = snap.Friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tab.TabOpen.Content = Loc.Get("lobby_list_open") + "  " + snap.Open.Count;
            tab.TabPlaying.Content = Loc.Get("lobby_list_playing") + "  " + snap.Playing.Count;
            tab.TabFriends.Content = Loc.Get("lobby_list_friends") + "  " + snap.Friends.Count;

            // Entrance: the first fill staggers in; after that only a NEW table pops.
            var ids = open.Concat(playing).Concat(friends).Select(v => v.Id).ToList();
            var fresh = ids.Where(id => !tab.SeenRows.Contains(id)).ToHashSet();
            bool firstFill = tab.SeenRows.Count == 0;
            foreach (var id in ids) tab.SeenRows.Add(id);
            if (ids.Count > 0)
                tab.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try { LobbyEntrance(tab, firstFill ? null : fresh); }
                    catch (Exception ex) { App.Logger?.Debug("Lobby entrance: {E}", ex.Message); }
                }));

            tab.TxtLobbyEmpty.Text = Loc.Get(snap.SignedIn ? "lobby_empty_open" : "lobby_signed_out");
            tab.AvailableSubjectsEmptyPanel.Visibility = snap.Open.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tab.AvailableSubjectsErrorPanel.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
            tab.TxtLobbyCount.Text = snap.OpenCount > 0
                ? Loc.GetF("lobby_open_count", snap.OpenCount)
                : Loc.Get("desc_available_subjects");
        }

        /// <summary>First fill (<paramref name="fresh"/> null): every card staggers in. Later: a card
        /// whose table was not there before pops (a small back-eased scale) and sends out one
        /// ring. Off honours MotionFx; Reduced keeps the stagger and drops the ring.</summary>
        internal static void LobbyEntrance(AvailableSubjectsTabView tab, ISet<string>? fresh)
        {
            if (!MotionFx.AllowTransitions) return;
            var lists = new[] { tab.AvailableSubjectsList, tab.LobbyPlayingList, tab.LobbyFriendsList };
            foreach (var list in lists)
            {
                try { list.UpdateLayout(); } catch { }
                var cards = new List<FrameworkElement>();
                foreach (var item in list.Items)
                {
                    if (item is not LobbyRowView v) continue;
                    if (list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement c) continue;
                    if (fresh == null) { cards.Add(c); continue; }
                    if (fresh.Contains(v.Id)) Pop(c);
                }
                if (cards.Count > 0) MotionFx.StaggerIn(cards);
            }
        }

        private static void Pop(FrameworkElement container)
        {
            var card = FindTagged(container, "lobby-card");
            var ring = FindTagged(container, "lobby-ring");
            if (card != null)
            {
                var sc = new ScaleTransform(0.9, 0.9);
                card.RenderTransform = sc;
                var grow = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
            if (ring != null && MotionFx.AllowParticles)
            {
                var rs = new ScaleTransform(1, 1);
                ring.RenderTransform = rs;
                var spread = new DoubleAnimation(1.0, 1.08, TimeSpan.FromMilliseconds(520)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                rs.BeginAnimation(ScaleTransform.ScaleXProperty, spread);
                rs.BeginAnimation(ScaleTransform.ScaleYProperty, spread);
                ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.9, 0.0, TimeSpan.FromMilliseconds(520)));
            }
        }

        private static FrameworkElement? FindTagged(DependencyObject root, string tag)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe && Equals(fe.Tag, tag)) return fe;
                var deeper = FindTagged(child, tag);
                if (deeper != null) return deeper;
            }
            return null;
        }

        internal static void PaintLobbyHostBar(AvailableSubjectsTabView tab, LobbyGates gates)
        {
            Paint(tab.BtnHostChess, "lobby_host_chess", gates.CanHost(LobbyGame.Chess));
            Paint(tab.BtnHostGoon, "lobby_host_goon", gates.CanHost(LobbyGame.Goon));
            Paint(tab.BtnBecomeASubject, "lobby_host_remote", gates.CanHost(LobbyGame.Remote));
            // The Remote pill's one line for free accounts: what unlocks it.
            tab.TxtBecomeASubjectSubtitle.Visibility = gates.SignedIn && !gates.CanHost(LobbyGame.Remote)
                ? Visibility.Visible : Visibility.Collapsed;

            static void Paint(System.Windows.Controls.Button b, string key, bool open)
            {
                b.Content = (open ? "" : "🔒 ") + Loc.Get(key);
                b.Opacity = open ? 1.0 : 0.7;
            }
        }

        /// <summary>A row's button. Signed out asks to sign in; a gate refusal shows the gate.</summary>
        internal async void OnLobbyRowClick(LobbyRowView view, FrameworkElement button)
        {
            var row = view.Row;
            if (!row.CanJoin || string.IsNullOrEmpty(row.Key)) return;
            var gates = CurrentLobbyGates();
            if (!gates.SignedIn) { OpenUnifiedLoginDialog(); return; }
            if (!gates.CanJoin(row.Game)) { OpenUnifiedLoginDialog(); return; }
            try
            {
                switch (row.Game)
                {
                    case LobbyGame.Chess:
                        PieceByPieceHostService.JoinOpenTable(row.Key);
                        break;
                    case LobbyGame.Goon:
                        GoonHostService.Launch(duckMainWindow: true, joinCode: row.Key);
                        break;
                    case LobbyGame.Remote:
                        button.IsEnabled = false;
                        try { await ClaimRemoteSubjectAsync(row.Key); }
                        finally { button.IsEnabled = true; }
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Lobby] join {Game} failed", row.Game); }
        }

        /// <summary>The host bar (and the empty state's buttons): each game's own host door.</summary>
        internal void LobbyHost(LobbyGame game)
        {
            if (BackRoomApi.AppIdentity() == null) { OpenUnifiedLoginDialog(); return; }
            try
            {
                switch (game)
                {
                    case LobbyGame.Chess:
                        PieceByPieceHostService.HostOpenTable();
                        break;
                    case LobbyGame.Goon:
                        // Patrons host (GoonHostService.HostingAllowed); everyone else sees the gate.
                        if (TierGate.DemandPremium("Goon Game")) GoonHostService.LaunchToHost();
                        break;
                    case LobbyGame.Remote:
                        // Hosting a Remote table = opting in to the directory from the Remote tab,
                        // with its consent copy (first to claim wins, no screening). Same gate as before.
                        if (TierGate.DemandPremium("Remote Control", "remote")) ShowTab("remotecontrol");
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Lobby] host {Game} failed", game); }
        }
    }
}
