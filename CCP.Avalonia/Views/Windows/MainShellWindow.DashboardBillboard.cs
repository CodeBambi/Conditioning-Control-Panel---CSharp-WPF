// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DashboardBillboard.cs (the Tonight Board
// on Home). This file only hosts the deck: it makes the deck and the card host once, keeps the snoozes
// in AppSettings and runs a card's button. The rules are Core DashboardBillboard/BillboardDeck, the
// drawing is Controls/Billboard/BillboardCardHost. Providers so far: the house cards and the Prime
// tips, and Core's Waiting/Resume/Event adapters over this head's services.
// ponytail: Live (no Lobby service on this head), Board and Showcase are sync6-tonight-board-c.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BillboardCardHost? _billboardHost;

        /// <summary>The card host once Home has shown the board (test seam).</summary>
        internal BillboardCardHost? BillboardHost => _billboardHost;

        private Tabs.SettingsTabView? Dash => Named<Tabs.SettingsTabView>("SettingsTab");

        /// <summary>WPF ApplyBillboard: the fold's hook - the board exists only while the browser is shut.</summary>
        private void ApplyBillboard(bool show)
        {
            var host = Dash?.FindControl<Border>("DashBillboard");
            if (host == null) return;
            host.IsVisible = show;
            if (show) EnsureBillboard();
            _billboardHost?.RefreshMotion();
        }

        /// <summary>WPF EnsureBillboard: the deck over the saved snoozes, the host in its slot, the
        /// first card still.</summary>
        private void EnsureBillboard()
        {
            if (_billboardHost != null) return;
            var slot = Dash?.FindControl<Grid>("BillboardHostSlot");
            if (slot == null) return;

            // A static seam: hold the shell weakly so a closed shell is never rooted by it.
            var weak = new WeakReference<MainShellWindow>(this);
            HouseProvider.OpenBackRoom = () => { if (weak.TryGetTarget(out var shell)) shell.OpenBillboardBackRoom(); };
            var settings = CoreSettings.Current;
            var snoozes = settings.BillboardSnoozedUntil ??= new Dictionary<string, DateTime>(StringComparer.Ordinal);
            if (DashboardBillboard.PruneSnoozes(snoozes, DateTime.UtcNow)) SaveBillboardSnoozes();

            var providers = BillboardProviders(weak);
            var deck = new BillboardDeck(() => providers, BillboardContextNow, snoozes, SaveBillboardSnoozes);
            var cardHost = new BillboardCardHost(deck);
            cardHost.ActionRequested += RunBillboardAction;
            slot.Children.Clear();
            slot.Children.Add(cardHost);
            _billboardHost = cardHost;
            cardHost.Begin();
        }

        /// <summary>WPF BillboardWiring.Start + BillboardProviders.CreateAll, in deck order, over this
        /// head's services: a service that is not up (null) leaves its provider silent. A provider's
        /// Changed may come from any thread; the deck is marked dirty on the UI thread (WPF
        /// MainWindow.DashboardBillboard ProvidersChanged). Everything holds the shell weakly.</summary>
        private static IBillboardProvider[] BillboardProviders(WeakReference<MainShellWindow> weak)
        {
            MainShellWindow? Shell() => weak.TryGetTarget(out var s) ? s : null;
            var head = new BillboardHead
            {
                Quests = () => App.Quests,
                Programs = () => App.Programs,
                SessionLog = () => App.Sessions?.SessionLog,
                Chaster = () => Platform.ChasterHead.Service,
                SessionRunning = () => App.Sessions?.IsRunning == true,
                StartSession = id => Shell()?.StartSessionFromBillboard(id) == true,
                PlayDeeper = path => Shell()?.PlayDeeperLibraryEntry(path),
                ShowTab = tab => Shell()?.ShowTab(tab),
                OpenInvites = () => Shell()?.OpenInvitesCard(),
            };
            var providers = new IBillboardProvider[]
            {
                new HouseProvider(), new WaitingProvider(head), new ResumeProvider(head), new EventProvider(head), new TipCardsProvider(),
            };
            foreach (var p in providers)
                p.Changed += (_, _) => Dispatcher.UIThread.Post(() => Shell()?._billboardHost?.MarkDirty());
            return providers;
        }

        /// <summary>WPF MainWindow.StartSessionFromCompanion: the Sessions page's own Start path (it
        /// asks first). False while a session runs or when the session is gone.</summary>
        internal bool StartSessionFromBillboard(string sessionId)
        {
            if (App.Sessions?.IsRunning == true) return false;
            var session = Named<Tabs.PresetsTabView>("PresetsTab")?.RevealSession(sessionId);
            if (session is not { IsAvailable: true }) return false;
            BtnStartSession_Click(session);
            return true;
        }

        /// <summary>The motion gate or the tab's visibility changed: the board re-reads its gates.</summary>
        private void RefreshBillboardMotion() => _billboardHost?.RefreshMotion();

        /// <summary>WPF BillboardWiring.Context: Prime = Lab access, Basic = Premium access.</summary>
        private static BillboardContext BillboardContextNow()
        {
            var tier = CoreAccount.HasLabAccess ? BillboardTier.Prime
                : CoreAccount.HasPremiumAccess ? BillboardTier.Basic : BillboardTier.Free;
            return new BillboardContext(tier, DateTime.UtcNow, DateTime.Now);
        }

        private static void SaveBillboardSnoozes()
        {
            try { CoreSettings.Save(); }
            catch (Exception ex) { Log.Debug("Billboard snooze save failed: {E}", ex.Message); }
        }

        /// <summary>WPF RunBillboardAction: Tab navigates, Link leaves through the one opener, Callback
        /// goes back to its provider. Launch cards come from providers not on this head yet.</summary>
        private void RunBillboardAction(DeckCard card)
        {
            try
            {
                var action = card.Action;
                switch (action.Kind)
                {
                    case BillboardActionKind.Tab: ShowTab(action.Target); break;
                    case BillboardActionKind.Link: BillboardOpenUrl(action.Target); break;
                    case BillboardActionKind.Callback: card.Provider?.Invoke(action.Target); break;
                    case BillboardActionKind.Launch: Log.Information("Billboard: launch {Id} not on this head", action.Target); break;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Dashboard billboard: card action failed ({Id})", card.Spec.Id); }
        }

        /// <summary>WPF OpenBackRoomInApp: signed out, sign-in first. The Back Room itself is not on
        /// this head, so a signed-in player lands on Play, where its door will live.</summary>
        private void OpenBillboardBackRoom()
        {
            if (Platform.FriendsHead.Identity() == null) { _ = OpenUnifiedLoginDialog(); return; }
            ShowTab("play");
        }

        /// <summary>The link opener (seam: tests never launch a browser).</summary>
        internal static Action<string> BillboardOpenUrl = url => Platform.ExternalOpener.Open(url);

        /// <summary>WPF TipProvider: the tier is all it reads.</summary>
        private sealed class TipCardsProvider : IBillboardProvider
        {
            public string Id => "tip";
            public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => TipCards.Decide(context.Tier, context.NowUtc, Loc.Get);
            public void Invoke(string actionTarget) { }
            public event EventHandler? Changed { add { } remove { } }
        }
    }
}
