// PORTED from ConditioningControlPanel/MainWindow/MainWindow.WhatMoved.cs (WPF 7.1.5): the one-time
// "What moved" card after the 2026-10-06 navigation rework.
//
// Reaches the screen through the startup ladder's passive path (PresentOrInbox): it opens at once on a
// calm launch and becomes an Inbox row while a session, the launcher or a first-launch window owns the
// screen. The flag is spent when the card OPENS, not when the row is posted, so a row waved away unread
// does not burn the only offer. A fresh install never sees it. Replay lives in Help and counts nothing.

using System;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Test seam: stands in for opening the card (readMode).</summary>
        internal Action<bool>? WhatMovedPresenter;

        /// <summary>The card on screen, if any (one at a time; a second ask brings it forward).</summary>
        internal WhatMovedCard? WhatMovedOpen { get; private set; }

        /// <summary>Called once per launch from the first-run fork. A fresh install spends the flag
        /// silently; an upgrade queues the card.</summary>
        internal void OfferWhatMovedIfNeeded(bool freshInstall)
        {
            try
            {
                var s = CoreSettings.Current;

                if (freshInstall)
                {
                    if (s.WhatMovedCardShown <= 0)
                    {
                        s.WhatMovedCardShown = 1;
                        CoreSettings.Save();
                    }
                    return;
                }

                if (!WhatMovedPlan.ShouldOffer(s.WhatMovedCardShown, freshInstall: false)) return;

                Dispatcher.UIThread.Post(() =>
                    Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                    {
                        Key = "intro:whatmoved",
                        Glyph = "🧭",
                        Title = Loc.Get("whatmoved_title"),
                        Summary = Loc.Get("whatmoved_intro"),
                        Open = () =>
                        {
                            var live = CoreSettings.Current;
                            if (live.WhatMovedCardShown <= 0)
                            {
                                live.WhatMovedCardShown = 1;
                                CoreSettings.Save();
                            }
                            ShowWhatMovedCard(readMode: false);
                        },
                    }), DispatcherPriority.Normal);
            }
            catch (Exception ex) { Log.Debug("What moved offer failed: {E}", ex.Message); }
        }

        // The upgrade half of the fork, once the shell is on screen. An unseeded version ("0.0.0") is a
        // render or a test run and never offers, the same rule the release notes dialog follows.
        private void OnWhatMovedShellOpened(object? sender, EventArgs e)
        {
            Opened -= OnWhatMovedShellOpened;
            var version = CoreReleaseContent.AppVersion;
            if (string.IsNullOrEmpty(version) || version == "0.0.0") return;
            OfferWhatMovedIfNeeded(freshInstall: false);
        }

        /// <summary>Opens the card (owned, non-modal). Read mode is the Help replay.</summary>
        internal void ShowWhatMovedCard(bool readMode)
        {
            try
            {
                if (WhatMovedPresenter is { } fake) { fake(readMode); return; }
                if (WhatMovedOpen is { } up) { up.Activate(); return; }
                var card = new WhatMovedCard(WhatMovedShowMe, readMode);
                card.Closed += (_, _) => { if (ReferenceEquals(WhatMovedOpen, card)) WhatMovedOpen = null; };
                WhatMovedOpen = card;
                if (IsVisible) card.Show(this); else card.Show();
            }
            catch (Exception ex) { Log.Warning(ex, "What moved card failed to open"); }
        }

        /// <summary>Show me: navigate to the row's new home, then glow it once.</summary>
        internal void WhatMovedShowMe(WhatMovedRow row)
        {
            if (!string.IsNullOrEmpty(row.SettingsSection)) OpenAppSettingsSection(row.SettingsSection!);
            else ShowTab(row.Tab);

            // After the page is up, so the glow lands on the pill that is now on screen.
            Dispatcher.UIThread.Post(() => GlowNavTarget(row.GlowKey), DispatcherPriority.Normal);
        }

        /// <summary>Help overlay: "What moved in this version".</summary>
        private void BtnWhatMovedReplay_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            CloseTutorialOverlay();
            ShowWhatMovedCard(readMode: true);
        }
    }
}
