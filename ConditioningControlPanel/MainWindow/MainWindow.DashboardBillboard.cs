using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Billboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Tonight Board on Home (2026-10-07): the deck of cards in the row the folded browser
    /// gives back. This file only hosts it: it makes the deck and the card host once, keeps the
    /// snoozes in AppSettings and runs a card's button. The rules are <see cref="DashboardBillboard"/>,
    /// the drawing and the juice are <see cref="BillboardCardHost"/>, and the provider list is
    /// <see cref="BillboardWiring"/>.
    /// </summary>
    public partial class MainWindow
    {
        private BillboardCardHost? _billboardHost;
        private bool _billboardProvidersHooked;

        /// <summary>The fold's one hook: the board exists only while the browser is shut.</summary>
        partial void OnBrowserFoldChanged(bool collapsed)
        {
            try { ApplyBillboard(BrowserFoldRule.BillboardShown(collapsed)); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard billboard: fold hook failed"); }
        }

        private void ApplyBillboard(bool show)
        {
            var host = SettingsTab?.DashBillboard;
            if (host == null) return;

            host.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) EnsureBillboard();
            // The host follows its own IsVisible for the clock and the art; this covers the case
            // where the visibility did not change but the gates did.
            _billboardHost?.RefreshMotion();
        }

        /// <summary>
        /// One-time wiring: start the providers, make the deck over the saved snoozes, drop the card
        /// host into its slot and show the first card still. The host pauses itself whenever Home
        /// is not on screen, so switching away costs nothing.
        /// </summary>
        private void EnsureBillboard()
        {
            if (_billboardHost != null) return;
            var slot = SettingsTab?.BillboardHostSlot;
            if (slot == null) return;

            BillboardWiring.Start();

            var settings = App.Settings?.Current;
            var snoozes = settings?.BillboardSnoozedUntil ?? new Dictionary<string, DateTime>(StringComparer.Ordinal);
            if (DashboardBillboard.PruneSnoozes(snoozes, DateTime.UtcNow)) SaveBillboardSnoozes();

            var deck = new BillboardDeck(
                () => BillboardWiring.Providers,
                BillboardWiring.Context,
                snoozes,
                SaveBillboardSnoozes);

            var cardHost = new BillboardCardHost(deck);
            cardHost.ActionRequested += RunBillboardAction;
            slot.Children.Clear();
            slot.Children.Add(cardHost);
            _billboardHost = cardHost;

            if (!_billboardProvidersHooked)
            {
                _billboardProvidersHooked = true;
                // Providers may raise from any thread; the deck only listens on the UI thread.
                BillboardWiring.ProvidersChanged += (_, _) =>
                {
                    try { Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => _billboardHost?.MarkDirty())); }
                    catch (Exception ex) { App.Logger?.Debug("Billboard dirty mark failed: {E}", ex.Message); }
                };
            }

            cardHost.Begin();
        }

        /// <summary>The motion level changed (MainWindow.UiUpdates): the board re-reads its gates.</summary>
        internal void RefreshBillboardMotion()
        {
            try { _billboardHost?.RefreshMotion(); }
            catch (Exception ex) { App.Logger?.Debug("Billboard motion refresh failed: {E}", ex.Message); }
        }

        private void SaveBillboardSnoozes()
        {
            try { App.Settings?.Save(); }
            catch (Exception ex) { App.Logger?.Debug("Billboard snooze save failed: {E}", ex.Message); }
        }

        /// <summary>
        /// A card's one button. A Link goes out through BrowserLauncher (the four-strategy opener
        /// with the clipboard fallback); a Tab is plain in-app navigation; Launch starts a launcher
        /// game the way the launcher does; a Callback goes back to the provider that issued it.
        /// The deck already refused anything outside its rules (<see cref="DeckCard.Action"/>).
        /// </summary>
        private void RunBillboardAction(DeckCard card)
        {
            try
            {
                var action = card.Action;
                switch (action.Kind)
                {
                    case BillboardActionKind.Tab:
                        ShowTab(action.Target);
                        break;
                    case BillboardActionKind.Link:
                        Helpers.BrowserLauncher.OpenUrlOrPrompt(action.Target, card.Spec.Title);
                        break;
                    case BillboardActionKind.Launch:
                        if (!LauncherHost.LaunchGame(action.Target))
                            App.Logger?.Information("Billboard: launch of {Id} was refused", action.Target);
                        break;
                    case BillboardActionKind.Callback:
                        card.Provider?.Invoke(action.Target);
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard billboard: card action failed ({Id})", card.Spec.Id); }
        }
    }
}
