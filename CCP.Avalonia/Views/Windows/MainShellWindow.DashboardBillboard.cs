using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.Invites;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Tonight Board on Home (WPF 7.1.5 MainWindow.DashboardBillboard.cs). This file only hosts
    /// it: the first time the Home slot joins this window it makes the deck and the card host,
    /// keeps the snoozes, runs a card's button and polls the marquee for the board's version. The
    /// rules are <see cref="DashboardBillboard"/> (Core), the drawing and the juice are
    /// <see cref="BillboardDeckView"/>, the provider list is <see cref="BillboardWiring"/>.
    ///
    /// <para>Differences from 7.1.5, all because the port lacks the thing: snoozes live in board/snoozes.json (no
    /// AppSettings.BillboardSnoozedUntil yet); there is no Live (no Lobby), no Showcase (no clips),
    /// no program day (no ProgramService), and the Back Room house card stays out of the deck (no
    /// Back Room host). The board stays silent, as 7.1.5 shipped it.</para>
    /// </summary>
    public partial class MainShellWindow
    {
        private BillboardDeckView? _billboardHost;
        private DispatcherTimer? _billboardMarquee;
        private bool _billboardMarqueeBusy;

        private static readonly object BillboardWireGate = new();
        private static bool _billboardWired;
        private static WeakReference<MainShellWindow>? _billboardShell;
        private static readonly HttpClient BillboardHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>The card host once the board is up (tests read it).</summary>
        internal BillboardDeckView? DashboardBillboardHost => _billboardHost;

        /// <summary>
        /// The slot's one call (<see cref="BillboardHomeSlot"/>): start the providers, make the deck
        /// over the saved snoozes, drop the card host in, show the board if the browser is folded
        /// (MainShellWindow.DashboardFold.cs owns that from then on) and show the first card. Once per window; the host pauses itself whenever Home is off screen.
        /// </summary>
        internal void AttachDashboardBillboard(BillboardHomeSlot slot)
        {
            if (_billboardHost != null) return;
            var tab = slot.FindAncestorOfType<SettingsTabView>();
            var frame = tab?.FindControl<Border>("DashBillboard");
            if (frame == null) return;

            _billboardShell = new WeakReference<MainShellWindow>(this);
            WireBillboardOnce();
            BillboardWiring.Start();

            var store = BillboardSnoozeStore.ForUserData();
            var snoozes = store.Load();
            if (DashboardBillboard.PruneSnoozes(snoozes, DateTime.UtcNow)) store.Save(snoozes);
            var deck = new BillboardDeck(
                () => BillboardWiring.Providers,
                BillboardWiring.Context,
                snoozes,
                () => store.Save(snoozes));

            var host = new BillboardDeckView(deck);
            host.ActionRequested += RunBillboardAction;
            slot.Children.Clear();
            slot.Children.Add(host);
            _billboardHost = host;

            // The board lives in the row the folded browser gives back (parity lane E3, WPF 7.1.5):
            // shown only while the card is folded, the logo dial keeps the centre cell.
            frame.IsVisible = BrowserFoldRule.BillboardShown(BrowserFolded);

            // Providers may raise from any thread; the deck only listens on the UI thread.
            EventHandler dirty = (_, _) =>
            {
                try { Dispatcher.UIThread.Post(() => _billboardHost?.MarkDirty()); }
                catch (Exception ex) { Log.Debug("Billboard dirty mark failed: {E}", ex.Message); }
            };
            BillboardWiring.ProvidersChanged += dirty;
            Closed += (_, _) =>
            {
                BillboardWiring.ProvidersChanged -= dirty;
                _billboardMarquee?.Stop();
                _billboardMarquee = null;
                _billboardHost?.Shutdown();
                slot.Children.Clear();
            };

            HookBillboardInvites();
            host.Begin();
            StartBillboardMarquee();
        }

        /// <summary>The static half, once per process: tier, head providers, shell hooks.</summary>
        private static void WireBillboardOnce()
        {
            lock (BillboardWireGate)
            {
                if (_billboardWired) return;
                _billboardWired = true;
            }

            BillboardWiring.TierReader = () =>
                CoreAccount.HasLabAccess ? BillboardTier.Prime
                : CoreAccount.HasPremiumAccess ? BillboardTier.Basic
                : BillboardTier.Free;

            // Every hook finds the live shell at click time (a closed window never answers).
            var hooks = new BillboardShellHooks
            {
                ShowTab = key => BillboardShell()?.ShowBillboardTab(key),
                OpenInvites = () => BillboardShell()?.OpenInvitesCard(),
                StartSession = id => BillboardShell()?.StartBillboardSession(id) == true,
            };
            BillboardWiring.HeadProviders = () => new IBillboardProvider[]
            {
                new WaitingProvider(hooks),
                new ResumeProvider(hooks),
                new EventProvider(() => Platform.ChasterHead.Service),
            };
            // No Back Room host on this head (ExclusivesTabView.IsOnThisBuild): HouseProvider.OpenBackRoom
            // stays null, so the Daily Daze card stays out of the deck.
        }

        private static MainShellWindow? BillboardShell() =>
            _billboardShell != null && _billboardShell.TryGetTarget(out var w) ? w : null;

        private void ShowBillboardTab(string key)
        {
            if (string.IsNullOrEmpty(key) || !TabPanels.ContainsKey(key))
            {
                Log.Information("Billboard: no page {Key} on this head", key);
                return;
            }
            ShowTab(key);
        }

        /// <summary>WPF StartSessionFromCompanion: the Sessions page's Start (it asks first).
        /// False = a session is running or the id is gone; the card then opens the Sessions page.</summary>
        private bool StartBillboardSession(string id)
        {
            if (CoreSession.IsSessionRunning || string.IsNullOrEmpty(id)) return false;
            var session = Session.GetAllSessions().FirstOrDefault(s => s != null && s.IsAvailable && s.Id == id);
            if (session == null) return false;
            BtnStartSession_Click(session);
            return true;
        }

        /// <summary>
        /// The invite card hears every invites read the shell makes: the header ticket's server
        /// read (wrapped, so the reader itself is unchanged), the invites card's own reads, and the
        /// ticket hiding (account change, unreachable).
        /// </summary>
        private void HookBillboardInvites()
        {
            try
            {
                var inner = InviteTicketApi;
                InviteTicketApi = () => new NotingInviteApi(inner());
                if (InvitesCard is { } card) card.Read += WaitingSignals.NoteInvites;
                if (Named<Button>("BtnInviteTicket") is { } ticket)
                {
                    ticket.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == IsVisibleProperty && e.NewValue is false)
                            WaitingSignals.NoteInvites(InviteMine.Unreachable);
                    };
                }
            }
            catch (Exception ex) { Log.Debug("Billboard invite hooks failed: {E}", ex.Message); }
        }

        /// <summary>
        /// A card's one button. A Link opens in the browser (no browser: the address goes to the
        /// clipboard and a dialog says so, as WPF BrowserLauncher.OpenUrlOrPrompt did); a Tab is
        /// in-app navigation; Launch goes through the launcher's own LaunchGame; a Callback goes
        /// back to the provider that issued it. The deck already refused anything outside its rules.
        /// </summary>
        private void RunBillboardAction(DeckCard card)
        {
            try
            {
                var action = card.Action;
                switch (action.Kind)
                {
                    case BillboardActionKind.Tab:
                        ShowBillboardTab(action.Target);
                        break;
                    case BillboardActionKind.Link:
                        _ = OpenBillboardLinkAsync(action.Target, card.Spec.Title);
                        break;
                    case BillboardActionKind.Launch:
                        if (!LauncherWindow.LaunchGame(this, action.Target))
                            Log.Information("Billboard: launch of {Id} was refused", action.Target);
                        break;
                    case BillboardActionKind.Callback:
                        card.Provider?.Invoke(action.Target);
                        break;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Dashboard billboard: card action failed ({Id})", card.Spec.Id); }
        }

        private async Task OpenBillboardLinkAsync(string url, string what)
        {
            try
            {
                if (await Platform.ExternalOpener.OpenAsync(this, url)) return;
                try { if (Clipboard is { } cb) await cb.SetTextAsync(url); } catch { /* clipboard may be unavailable */ }
                await Dialogs.MessageDialog.ShowAsync(this, Loc.Get("title_open_link_in_browser"),
                    Loc.GetF("msg_browser_no_default_for", what) + Loc.GetF("msg_browser_link_copied", url));
            }
            catch (Exception ex) { Log.Warning(ex, "Billboard link failed"); }
        }

        /// <summary>
        /// The board's version rides the marquee config (WPF polled /config/marquee every 5 min and
        /// handed the body to BoardService). The port's banner does not fetch it yet, so the board
        /// polls on its own: first read 8 s after Home shows, then every 5 min (never in a headless
        /// test host). Offline is silent:
        /// the deck simply has no Board card.
        /// </summary>
        private void StartBillboardMarquee()
        {
            if (_billboardMarquee != null) return;
            // Only in a running app: a headless test host (no lifetime) never reaches the network.
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not global::Avalonia.Controls.ApplicationLifetimes.IControlledApplicationLifetime) return;
            _billboardMarquee = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _billboardMarquee.Tick += async (_, _) =>
            {
                if (_billboardMarquee != null) _billboardMarquee.Interval = TimeSpan.FromMinutes(5);
                await PollBoardMarqueeAsync();
            };
            _billboardMarquee.Start();
        }

        private async Task PollBoardMarqueeAsync()
        {
            if (_billboardMarqueeBusy) return;
            _billboardMarqueeBusy = true;
            try
            {
                var json = await BillboardHttp.GetStringAsync(BoardService.ServerBase + "/config/marquee").ConfigureAwait(false);
                BoardService.Shared.OnMarquee(json);
            }
            catch (Exception ex) { Log.Debug("Billboard marquee read failed: {E}", ex.GetType().Name); }
            finally { _billboardMarqueeBusy = false; }
        }

        /// <summary>Test seam: forget the once-per-process wiring.</summary>
        internal static void ResetBillboardWiringForTests()
        {
            lock (BillboardWireGate) _billboardWired = false;
            _billboardShell = null;
        }
    }
}
