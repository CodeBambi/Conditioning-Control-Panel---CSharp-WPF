// PORTED from ConditioningControlPanel/MainWindow/MainWindow.WhatMoved.cs and the palette's
// shell hooks in MainWindow.SectionChrome.cs (OpenLibraryLauncher :275, GlowNavKey :313) - main
// sync #6 lane sync6-nav-search (WPF c2f9dec36, 2847168cc, c7873ed42, 2eeb3da32, 7fbdbe019).

using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Glow the strip pill / rail row / Settings pill for <paramref name="key"/> once,
        /// after a Show me or a palette landing. WPF's own seam (MainWindow.WhatMoved.cs:23):
        /// the section strip implements it; unimplemented, the call compiles away and navigation
        /// still lands. ponytail: body arrives with the section strip + NavGlow (lane sync6-nav-rail).</summary>
        partial void GlowNavTarget(string key);

        /// <summary>WPF GlowNavKey (SectionChrome.cs:313): the palette's door to the private partial.</summary>
        internal void GlowNavKey(string key) => GlowNavTarget(key);

        /// <summary>WPF OpenLibraryLauncher (SectionChrome.cs:275): the Library's four launchers
        /// through the handler their rail row called, so a palette row or a pinned favourite does
        /// not depend on a button being drawn. False for an unknown key.</summary>
        internal bool OpenLibraryLauncher(string key)
        {
            var e = new global::Avalonia.Interactivity.RoutedEventArgs();
            switch (key)
            {
                case "mods": BtnManageMods_Click(this, e); return true;
                case "catalogue": BtnCatalogue_Click(this, e); return true;
                case "phrases": BtnManagePhrases_Click(this, e); return true;
                case "medialog": BtnNavMediaLog_Click(this, e); return true;
                default: return false;
            }
        }

        /// <summary>WPF OfferWhatMovedIfNeeded (WhatMoved.cs:27), called once from the first-run
        /// fork. A fresh install spends the flag silently; an upgrade queues the card through the
        /// passive ladder. The flag is spent when the card OPENS, not when the row is posted.</summary>
        internal void OfferWhatMovedIfNeeded(bool freshInstall)
        {
            try
            {
                if (CoreSettings.Service == null) return;   // render / nav-check: no profile
                var s = CoreSettings.Current;
                if (freshInstall)
                {
                    if (s.WhatMovedCardShown <= 0) { s.WhatMovedCardShown = 1; CoreSettings.Save(); }
                    return;
                }
                if (!WhatMovedPlan.ShouldOffer(s.WhatMovedCardShown, freshInstall: false)) return;

                // One hop, as WPF: the Inbox badge subscribes later in startup.
                Dispatcher.UIThread.Post(() => Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "intro:whatmoved",
                    Glyph = "🧭",
                    Title = Loc.Get("whatmoved_title"),
                    Summary = Loc.Get("whatmoved_intro"),
                    Open = () =>
                    {
                        var live = CoreSettings.Current;
                        if (live.WhatMovedCardShown <= 0) { live.WhatMovedCardShown = 1; CoreSettings.Save(); }
                        ShowWhatMovedCard(readMode: false);
                    },
                }));
            }
            catch (Exception ex) { Log.Debug("What moved offer failed: {E}", ex.Message); }
        }

        /// <summary>Opens the card (owned, non-modal). Read mode is the Help replay.</summary>
        internal WhatMovedCard? ShowWhatMovedCard(bool readMode)
        {
            try
            {
                var card = new WhatMovedCard(WhatMovedShowMe, readMode);
                // Avalonia refuses a hidden owner (the panel sits behind the launcher at boot); WPF
                // shows owned windows anyway, so fall back to the launcher, then to no owner.
                Window? owner = IsVisible ? this : LauncherWindow.Instance is { IsVisible: true } l ? l : null;
                if (owner != null) card.Show(owner); else card.Show();
                return card;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "What moved card failed to open");
                return null;
            }
        }

        /// <summary>Show me: navigate to the row's new home, then glow it once.</summary>
        private void WhatMovedShowMe(WhatMovedRow row)
        {
            // Opened over the launcher: Show me brings the panel up first, as its Panel tile does.
            if (!IsVisible) LauncherWindow.OpenPanel(this);
            if (!string.IsNullOrEmpty(row.SettingsSection))
            {
                ShowTab("appsettings");
                AppSettingsPage?.FocusSection(row.SettingsSection!);
            }
            else ShowTab(row.Tab);

            // After the page is up, so the glow lands on the pill that is now on screen.
            Dispatcher.UIThread.Post(() => GlowNavTarget(row.GlowKey));
        }

        /// <summary>Help panel: "What moved in this version" (header button and the first row).</summary>
        private void BtnWhatMovedReplay_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            CloseTutorialOverlay();
            ShowWhatMovedCard(readMode: true);
        }
    }
}
