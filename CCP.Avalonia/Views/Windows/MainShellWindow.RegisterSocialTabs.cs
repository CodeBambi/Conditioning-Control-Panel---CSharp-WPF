// PORTED from WPF 7.1.5 MainWindow/MainWindow.SocialTabs.cs (Friends + Leash through the tab
// registry, the Social rail badge) + the shell half of MainWindow/MainWindow.Lobby.cs (the Lobby
// service, its leases, the gates, Join and Host).
//
// Deviations:
//   - WPF App.Lobby is built by App; this head's App belongs to another lane, so the shell owns
//     one LobbyService (static, built on first use) and seeds Core's LobbyWire with the friends
//     door (FriendsHead.Identity / BaseUrl: a sandbox without a loopback url sends nothing).
//   - The Lobby page asks for its lease from its own effective visibility
//     (AvailableSubjectsTabView.EffectiveVisibilityChanged) instead of ShowTab's switch.
//   - Join and Host reach games this head does not host yet: chess (PieceByPieceHostService),
//     Goon (GoonHostService) and the Remote directory claim (AvailableSubjectsService.TryClaimAsync).
//     Each one logs and stays put (ponytail below); the gates, the sign-in door and the Remote host
//     route (TierGate -> the Remote tab) are real.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Lobby;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        partial void RegisterSocialTabs()
        {
            RegisterNavTab(new NavTabHost("friends", () => new FriendsTabView(),
                OnShown: v => (v as FriendsTabView)?.OnShown(),
                OnHidden: v => (v as FriendsTabView)?.OnHidden()));

            RegisterNavTab(new NavTabHost("leash", () => new LeashTabView(),
                OnShown: v => (v as LeashTabView)?.OnShown()));

            // OnInitialized runs before this window's name scope exists: find the page one turn later.
            Dispatcher.UIThread.Post(() =>
            {
                try { HookLobbyPage(); HookLobbyBadge(); }
                catch (Exception ex) { Log.Warning(ex, "Lobby hooks failed"); }
            }, DispatcherPriority.Normal);
        }

        // =====================================================================================
        //  the service (WPF App.Lobby)
        // =====================================================================================

        private static LobbyService? _lobby;
        private static readonly object LobbyGate = new();

        /// <summary>WPF App.Lobby. Built on first use with the friends door and a UI-thread timer.</summary>
        internal static LobbyService Lobby
        {
            get
            {
                lock (LobbyGate)
                {
                    if (_lobby != null) return _lobby;
                    var url = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
                        Environment.GetEnvironmentVariable(FriendsHead.EnvVar));
                    LobbyWire.DefaultIdentity = FriendsHead.Identity;
                    // Same rule as the friends service it rides with: no friends service built (a
                    // sandbox without a loopback url, a test host) means no proxy, nothing sent.
                    LobbyWire.DefaultBaseUrl = () => FriendsHead.Service != null ? url : null;
                    _lobby = new LobbyService(() => new FriendsHead.Timer())
                    {
                        ReadFriends = () => FriendsHead.Service?.Snapshot?.Friends ?? (IReadOnlyList<Friend>)Array.Empty<Friend>(),
                    };
                    return _lobby;
                }
            }
        }

        /// <summary>The suite swaps the service (fake wires) before a window is built.</summary>
        internal static void UseLobbyForTests(LobbyService? lobby) { lock (LobbyGate) _lobby = lobby; }

        private IDisposable? _lobbyLease;
        private IDisposable? _lobbyBadgeLease;
        private bool _lobbyHooked, _lobbyBadgeHooked, _lobbyPageHooked;

        /// <summary>The gates as each game reads them today (WPF CurrentLobbyGates).</summary>
        internal static LobbyGates CurrentLobbyGates()
        {
            bool signedIn = LobbyWire.DefaultIdentity() != null || FriendsHead.Identity() != null;
            bool premium = CoreAccount.HasPremiumAccess;
            bool remote = premium || CoreEntitlement.IsFreeToday("remote");
            // GoonHostService.HostingAllowed is App.Patreon.HasPremiumAccess.
            return LobbyGates.From(signedIn, premium, remote);
        }

        private AvailableSubjectsTabView? LobbyPage => Named<AvailableSubjectsTabView>("AvailableSubjectsTab");

        /// <summary>The page takes the poll lease while it is on screen.</summary>
        private void HookLobbyPage()
        {
            if (_lobbyPageHooked || LobbyPage is not { } page) return;
            _lobbyPageHooked = true;
            page.EffectiveVisibilityChanged += shown => { if (shown) EnterLobbyTab(); else LeaveLobbyTab(); };
            if (page.IsShownOnScreen) EnterLobbyTab();
        }

        private void HookLobbyChanged()
        {
            if (_lobbyHooked) return;
            _lobbyHooked = true;
            Lobby.Changed += OnLobbyChanged;
            Closed += (_, _) => { try { Lobby.Changed -= OnLobbyChanged; } catch { } LeaveLobbyTab(); };
        }

        /// <summary>WPF EnterLobbyTab: hook once, take a lease, paint what we have.</summary>
        internal void EnterLobbyTab()
        {
            HookLobbyChanged();
            PaintLobby(Lobby.Snapshot, animate: false);
            _lobbyLease ??= Lobby.Watch();
        }

        /// <summary>True while the Lobby page holds its poll lease (tests).</summary>
        internal bool HoldsLobbyPageLease => _lobbyLease != null;

        /// <summary>WPF LeaveLobbyTab: drop the lease (the last lease stops the poll).</summary>
        internal void LeaveLobbyTab()
        {
            _lobbyLease?.Dispose();
            _lobbyLease = null;
        }

        /// <summary>True when the panel should hold the badge lease (WPF LobbyBadgeWanted).</summary>
        internal static bool LobbyBadgeWanted(bool visible, WindowState state) =>
            visible && state != WindowState.Minimized;

        /// <summary>The rail badge's own lease ("3 open" on Social): held while the panel is on
        /// screen, released when it hides to the tray, minimises or closes. One timer either way.</summary>
        private void HookLobbyBadge()
        {
            if (_lobbyBadgeHooked) return;
            _lobbyBadgeHooked = true;
            this.GetObservable(IsVisibleProperty).Subscribe(new Observer<bool>(_ => SyncLobbyBadgeLease()));
            this.GetObservable(WindowStateProperty).Subscribe(new Observer<WindowState>(_ => SyncLobbyBadgeLease()));
            Closed += (_, _) => { _lobbyBadgeLease?.Dispose(); _lobbyBadgeLease = null; };
        }

        private void SyncLobbyBadgeLease()
        {
            try
            {
                HookLobbyChanged();
                if (LobbyBadgeWanted(IsVisible, WindowState))
                {
                    _lobbyBadgeLease ??= Lobby.Watch();
                    PublishLobbyBadge(Lobby.Snapshot.OpenCount);
                }
                else
                {
                    _lobbyBadgeLease?.Dispose();
                    _lobbyBadgeLease = null;
                }
            }
            catch (Exception ex) { Log.Debug("Lobby badge lease: {E}", ex.Message); }
        }

        private void OnLobbyChanged(LobbySnapshot snap)
        {
            // The rail badge: NavBadges marshals nothing itself, the rail does.
            PublishLobbyBadge(snap.OpenCount);
            try { Dispatcher.UIThread.Post(() => PaintLobby(snap, animate: true), DispatcherPriority.Normal); }
            catch { }
        }

        private int _lobbyBadgePublished;

        /// <summary>The Social badge follows the Lobby's count, written only when that count moves,
        /// so a round that changes nothing never resets a badge another source set.</summary>
        private void PublishLobbyBadge(int count)
        {
            if (count == _lobbyBadgePublished) return;
            _lobbyBadgePublished = count;
            try { NavBadges.Set(NavSections.Social, count); } catch { }
        }

        /// <summary>Tier or account moved: repaint the host bar's locks.</summary>
        internal void RefreshBecomeASubjectCta()
        {
            try { if (LobbyPage is { } page) AvailableSubjectsTabView.PaintHostBar(page, CurrentLobbyGates()); }
            catch (Exception ex) { Log.Debug("Lobby host bar: {E}", ex.Message); }
        }

        private void PaintLobby(LobbySnapshot snap, bool animate)
        {
            if (LobbyPage is not { } page) return;
            AvailableSubjectsTabView.PaintInto(page, snap, CurrentLobbyGates(),
                error: snap.SignedIn && !Lobby.LastRoundOk, animate: animate);
        }

        /// <summary>A row's button. Signed out asks to sign in; a gate refusal shows the gate.</summary>
        internal async void OnLobbyRowClick(LobbyRowView view, Button button)
        {
            var row = view.Row;
            if (!row.CanJoin || string.IsNullOrEmpty(row.Key)) return;
            var gates = CurrentLobbyGates();
            if (!gates.SignedIn || !gates.CanJoin(row.Game)) { await OpenUnifiedLoginDialog(); return; }
            // ponytail: chess -> PieceByPieceHostService.JoinOpenTable(key), Goon ->
            // GoonHostService.Launch(joinCode: key), Remote -> ClaimRemoteSubjectAsync(key)
            // (AvailableSubjectsService.TryClaimAsync + the session url). None of the three games or
            // the claim is hosted on this head yet; the click is logged and the page stays.
            Log.Information("[Lobby] join {Game} not hosted on this head yet", row.Game);
        }

        /// <summary>The host bar (and the empty state's buttons): each game's own host door.</summary>
        internal void LobbyHost(LobbyGame game)
        {
            if (!CurrentLobbyGates().SignedIn) { _ = OpenUnifiedLoginDialog(); return; }
            try
            {
                switch (game)
                {
                    case LobbyGame.Chess:
                        // ponytail: PieceByPieceHostService.HostOpenTable() (chess is not hosted here).
                        Log.Information("[Lobby] host chess not hosted on this head yet");
                        break;
                    case LobbyGame.Goon:
                        // Patrons host; everyone else sees the gate. ponytail: GoonHostService.LaunchToHost().
                        if (TierGate.DemandPremium("Goon Game")) Log.Information("[Lobby] host Goon not hosted on this head yet");
                        break;
                    case LobbyGame.Remote:
                        // Hosting a Remote table = opting in to the directory from the Remote tab,
                        // with its consent copy. Same gate as before.
                        if (TierGate.DemandPremium("Remote Control", "remote")) ShowTab("remotecontrol");
                        break;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[Lobby] host {Game} failed", game); }
        }

        /// <summary>A tiny IObserver for GetObservable(...).Subscribe without System.Reactive.</summary>
        private sealed class Observer<T> : IObserver<T>
        {
            private readonly Action<T> _next;
            public Observer(Action<T> next) => _next = next;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _next(value);
        }
    }
}
