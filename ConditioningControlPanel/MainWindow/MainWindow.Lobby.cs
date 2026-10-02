using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;
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
            bool firstFill = tab.AvailableSubjectsList.Items.Count == 0 && snap.Open.Count > 0;
            PaintLobbyInto(tab, snap, CurrentLobbyGates(), error: snap.SignedIn && App.Lobby?.LastRoundOk == false);
            if (firstFill && tab.IsVisible) StaggerSubjectCards(allowRetry: true);
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
            tab.AvailableSubjectsList.ItemsSource = LobbyRowView.From(snap.Open, gates);
            tab.LobbyPlayingList.ItemsSource = LobbyRowView.From(snap.Playing, gates);
            tab.LobbyFriendsList.ItemsSource = LobbyRowView.From(snap.Friends, gates);
            tab.LobbyPlayingSection.Visibility = snap.Playing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            tab.LobbyFriendsSection.Visibility = snap.Friends.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            tab.TxtLobbyEmpty.Text = Loc.Get(snap.SignedIn ? "lobby_empty_open" : "lobby_signed_out");
            tab.AvailableSubjectsEmptyPanel.Visibility = snap.Open.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tab.AvailableSubjectsErrorPanel.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
            tab.TxtLobbyCount.Text = snap.OpenCount > 0
                ? Loc.GetF("lobby_open_count", snap.OpenCount)
                : Loc.Get("desc_available_subjects");
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
