// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SectionChrome.cs (nav rework
// 2026-10-06): the section page chrome ShowTab syncs on every navigation - the pill strip, the
// breadcrumb and the last-tab memory - plus the pill routing (OnSectionPillChosen,
// OpenLibraryLauncher).
//
// ponytail: not yet here (lane sync6-nav-rail-b): the section wash and ink (PaintSectionWash,
// PaintSectionInk), the window-title crumb (UpdateNavTitle), the "Moved" redirects and their note,
// GlowNavTarget, the right-click pin (PinIdForPill/AttachPinMenu) and the Play zone scroll.

using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _sectionStripWired;

        internal SectionTabStrip? PageStrip => Named<SectionTabStrip>("SectionStrip");

        /// <summary>The tab a section returns to (its remembered tab, else its default). WPF :97.</summary>
        internal static string NavLastTabFor(string section) =>
            NavStripTable.LastTabFor(CoreSettings.Current.NavLastTabBySection, section)
            ?? NavSections.DefaultTab(section) ?? "settings";

        /// <summary>Sync the strip, breadcrumb and last-tab memory to the tab on screen. WPF :102.</summary>
        private void SyncSectionChrome(string tab)
        {
            try
            {
                WireSectionStrip();
                var section = NavSections.SectionForTab(tab);
                PageStrip?.Show(section, tab, section == NavSections.Settings ? CurrentSettingsSectionLabel() : null);

                // Last tab per section. Old keys are not remembered (their new home is).
                var memo = tab == "lab" ? "play" : tab;
                var s = CoreSettings.Current;
                if (section != null && !NavSections.Redirects.ContainsKey(memo))
                {
                    var json = NavStripTable.WithLastTab(s.NavLastTabBySection, section, memo);
                    if (!string.Equals(json, s.NavLastTabBySection, StringComparison.Ordinal))
                    {
                        s.NavLastTabBySection = json;
                        CoreSettings.Save();
                    }
                }
            }
            catch (Exception ex) { Serilog.Log.Debug("SyncSectionChrome({Tab}) failed: {E}", tab, ex.Message); }
        }

        private void WireSectionStrip()
        {
            if (_sectionStripWired || PageStrip is not { } strip) return;
            _sectionStripWired = true;
            // WPF NavStripRules.PillLocked: a paying account sees no tier sign on pages it owns.
            strip.PillLocked = tier => tier > 0 && (tier == 1 ? !CoreAccount.HasPremiumAccess : !CoreAccount.HasLabAccess);
            strip.CanOpen = CanOpenNavTab;
            strip.TabRequested += OnSectionPillChosen;
            strip.SectionRequested += section =>
                ShowTab(section == NavSections.Settings ? "appsettings" : NavLastTabFor(section));
            // Settings keeps its own left pill column; the breadcrumb follows it (WPF :233).
            AppSettingsPage?.AddHandler(ToggleButton.IsCheckedChangedEvent, (_, e) =>
            {
                if (e.Source is RadioButton { IsChecked: true } rb && rb.Name?.StartsWith("SectionPill", StringComparison.Ordinal) == true
                    && CurrentTab == "appsettings")
                    strip.Show(NavSections.Settings, "appsettings", CurrentSettingsSectionLabel());
            }, RoutingStrategies.Bubble);
        }

        /// <summary>Pills this head can open. A page another lane has not ported yet (Friends,
        /// Leash, the Companion pages, Folders' scroll...) is not drawn rather than drawn dead.</summary>
        private static bool CanOpenNavTab(NavTab tab) => tab.Kind == NavTabKind.Launcher
            ? tab.Key is "mods" or "catalogue" or "phrases" or "medialog"
            : TabPanels.ContainsKey(tab.Key);

        /// <summary>WPF OnSectionPillChosen (:246): launchers through their old handlers, pages and
        /// zones through ShowTab.</summary>
        private void OnSectionPillChosen(NavTab tab)
        {
            if (tab.Kind == NavTabKind.Launcher) OpenLibraryLauncher(tab.Key);
            else ShowTab(tab.Key);
        }

        /// <summary>WPF OpenLibraryLauncher (:274): the handlers the Library's rail rows call.</summary>
        internal bool OpenLibraryLauncher(string key)
        {
            var e = new RoutedEventArgs();
            switch (key)
            {
                case "mods": BtnManageMods_Click(this, e); return true;
                case "catalogue": BtnCatalogue_Click(this, e); return true;
                case "phrases": BtnManagePhrases_Click(this, e); return true;
                case "medialog": BtnNavMediaLog_Click(this, e); return true;
                default: return false;
            }
        }

        /// <summary>
        /// Ctrl+K from every page (WPF EnsurePaletteShortcut, d858d6108). WPF reads the raw
        /// keystroke before routing so no focused control can swallow it; the window's Tunnel pass
        /// is that point here, and handledEventsToo keeps it whatever a child marks. Ctrl alone
        /// (Ctrl+Alt+K is the camera). ponytail: Avalonia reports no key-repeat flag, so a held chord
        /// re-toggles where WPF took the first press only. The palette has its own Ctrl+K toggle.
        /// </summary>
        private void InitializePaletteShortcut() =>
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != global::Avalonia.Input.Key.K || e.KeyModifiers != global::Avalonia.Input.KeyModifiers.Control) return;
                e.Handled = true;
                SettingsPaletteWindow.Toggle(this);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

        /// <summary>The label of the Settings section whose pill is checked (null if none). WPF :340.</summary>
        private string? CurrentSettingsSectionLabel()
        {
            var key = AppSettingsPage?.CheckedSectionKey;
            if (key is null) return null;
            foreach (var t in NavSections.Find(NavSections.Settings)?.Tabs ?? Array.Empty<NavTab>())
                if (t.Key == key) return Loc.Get(t.LabelKey);
            return null;
        }
    }
}
