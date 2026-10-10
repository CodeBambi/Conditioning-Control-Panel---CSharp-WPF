using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The one-time "What moved" card after the 2026-10-06 navigation rework.
    ///
    /// <para>Reaches the screen through the startup ladder's passive path (PresentOrInbox): it
    /// opens at once on a calm launch and becomes an Inbox row while a session, the launcher or a
    /// first-launch window owns the screen. The flag is spent when the card OPENS, not when the
    /// row is posted, so a row waved away unread does not burn the only offer (the possession
    /// rules card's lesson). A fresh install never sees it: the new layout is the only one it has
    /// known. Replay lives in Help and counts nothing.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Glow the strip pill / rail row / Settings pill for <paramref name="tabKey"/>
        /// once, after a Show me. Implemented by the integrator on the tab strip; unimplemented,
        /// the call compiles away and Show me still navigates.</summary>
        partial void GlowNavTarget(string tabKey);

        /// <summary>Called once per launch from the constructor's first-run fork. A fresh install
        /// spends the flag silently; an upgrade queues the card.</summary>
        internal void OfferWhatMovedIfNeeded(bool freshInstall)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                if (freshInstall)
                {
                    if (s.WhatMovedCardShown <= 0)
                    {
                        s.WhatMovedCardShown = 1;
                        App.Settings?.Save();
                    }
                    return;
                }

                if (!WhatMovedPlan.ShouldOffer(s.WhatMovedCardShown, freshInstall: false)) return;

                // One hop: the Inbox badge subscribes later in the constructor, and a parked row
                // must land after it does.
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    PresentOrInbox(new Services.Startup.InboxItem
                    {
                        Key = "intro:whatmoved",
                        Glyph = "🧭",
                        Title = Loc.Get("whatmoved_title"),
                        Summary = Loc.Get("whatmoved_intro"),
                        Open = () =>
                        {
                            var live = App.Settings?.Current;
                            if (live != null && live.WhatMovedCardShown <= 0)
                            {
                                live.WhatMovedCardShown = 1;
                                App.Settings?.Save();
                            }
                            ShowWhatMovedCard(readMode: false);
                        },
                    })));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("What moved offer failed: {E}", ex.Message);
            }
        }

        /// <summary>Opens the card (owned, non-modal). Read mode is the Help replay.</summary>
        internal void ShowWhatMovedCard(bool readMode)
        {
            try
            {
                var card = new WhatMovedCard(WhatMovedShowMe, readMode) { Owner = this };
                card.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "What moved card failed to open");
            }
        }

        /// <summary>Show me: navigate to the row's new home, then glow it once.</summary>
        private void WhatMovedShowMe(WhatMovedRow row)
        {
            if (!string.IsNullOrEmpty(row.SettingsSection)) OpenAppSettingsSection(row.SettingsSection!);
            else ShowTab(row.Tab);

            // After the page is up, so the glow lands on the pill that is now on screen.
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => GlowNavTarget(row.GlowKey)));
        }

        /// <summary>Help overlay: "What moved in this version".</summary>
        private void BtnWhatMovedReplay_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null)
                SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            ShowWhatMovedCard(readMode: true);
        }
    }
}
