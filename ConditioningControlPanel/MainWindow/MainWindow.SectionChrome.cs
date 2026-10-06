using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel
{
    // Nav rework 2026-10-06, TABSTRIP lane: the section page chrome (pill strip, breadcrumb,
    // window title, last-tab memory, the "Moved" note) that ShowTab syncs on every navigation.
    public partial class MainWindow
    {
        /// <summary>The ShowTab key on screen (after redirects). Null before the first ShowTab.</summary>
        private string? _navCurrentTab;

        private string? _navBaseTitle;
        private bool _sectionStripWired;

        /// <summary>Old keys that land on a new home with a "Moved" note. "lab", "progression" and
        /// "patreon" keep their own ShowTab arms (bark and tutorial API), so they are not here.</summary>
        internal static readonly string[] MovedRedirectKeys = { "exclusives", "together" };

        /// <summary>How many times the "Moved" note shows across the app's life, then never.</summary>
        internal const int NavMovedNoteLimit = 3;

        /// <summary>Scroll the Play wall to a zone: "games" | "sessions" | "eyes". Implemented by
        /// the REHOME lane (PlayTab.ScrollToZone); until then the pill lands on the wall's top.</summary>
        partial void ScrollPlayZone(string zone);

        /// <summary>
        /// An old ShowTab key with a new home: navigate there and, the first three times, say so.
        /// True when the key was a redirect and has been handled.
        /// </summary>
        private bool TryRedirectMovedTab(string tab)
        {
            if (Array.IndexOf(MovedRedirectKeys, tab) < 0) return false;
            if (!NavSections.Redirects.TryGetValue(tab, out var to)) return false;

            if (to.Section == NavSections.Settings) OpenAppSettingsSection(to.Tab);
            else ShowTab(to.Tab);

            try
            {
                var s = App.Settings?.Current;
                if (s != null && s.NavMovedToastHits < NavMovedNoteLimit)
                {
                    s.NavMovedToastHits++;
                    App.Settings?.Save();
                    var section = Loc.Get(NavSections.Find(to.Section)?.LabelKey ?? string.Empty);
                    var page = to.Section == NavSections.Settings
                        ? Loc.Get(SettingsSectionLabelKey(to.Tab) ?? string.Empty)
                        : Loc.Get(NavStripRules.PageLabelKey(to.Tab) ?? string.Empty);
                    SectionStrip?.ShowMovedNote(string.Format(Loc.Get("nav_moved_toast"), section, page));
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Moved note failed: {E}", ex.Message); }
            return true;
        }

        /// <summary>The tab a section returns to (its remembered tab, else its default).</summary>
        internal string NavLastTabFor(string section) =>
            NavStripRules.LastTabFor(App.Settings?.Current?.NavLastTabBySection, section)
            ?? NavSections.DefaultTab(section) ?? "settings";

        /// <summary>Sync the strip, breadcrumb, title and last-tab memory to the tab on screen.</summary>
        private void SyncSectionChrome(string tab)
        {
            try
            {
                WireSectionStrip();
                var section = NavSections.SectionForTab(tab);

                string? pageLabel = null;
                if (section == NavSections.Settings) pageLabel = CurrentSettingsSectionLabel();
                SectionStrip?.Show(section, tab, pageLabel);

                UpdateNavTitle(section, tab, pageLabel);

                // Last tab per section. Old keys are not remembered (their new home is).
                var memo = tab == "lab" ? "play" : tab;
                if (section != null && !NavSections.Redirects.ContainsKey(memo) && App.Settings?.Current is { } s)
                {
                    var json = NavStripRules.WithLastTab(s.NavLastTabBySection, section, memo);
                    if (!string.Equals(json, s.NavLastTabBySection, StringComparison.Ordinal))
                    {
                        s.NavLastTabBySection = json;
                        App.Settings?.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("SyncSectionChrome({Tab}) failed: {E}", tab, ex.Message);
            }
        }

        private void WireSectionStrip()
        {
            if (_sectionStripWired || SectionStrip == null) return;
            _sectionStripWired = true;

            SectionStrip.TabRequested += OnSectionPillChosen;
            SectionStrip.SectionRequested += section =>
            {
                if (section == NavSections.Settings) ShowTab("appsettings");
                else ShowTab(NavLastTabFor(section));
            };

            // Settings keeps its own left pill column; the breadcrumb follows it.
            AppSettingsTab?.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, e) =>
            {
                if (e.OriginalSource is RadioButton rb && rb.Name.StartsWith("SectionPill", StringComparison.Ordinal)
                    && AppSettingsTab.Visibility == Visibility.Visible)
                {
                    var label = CurrentSettingsSectionLabel();
                    SectionStrip.Show(NavSections.Settings, "appsettings", label);
                    UpdateNavTitle(NavSections.Settings, "appsettings", label);
                }
            }));
        }

        private void OnSectionPillChosen(NavTab tab)
        {
            switch (tab.Kind)
            {
                case NavTabKind.Launcher:
                    // The Library's dialogs and sites: same handlers as their rail rows (the
                    // x:Names stay, so this reaches them wherever the rail draws them).
                    var name = tab.Key switch
                    {
                        "mods" => "BtnNavMods",
                        "catalogue" => "BtnNavCatalogue",
                        "phrases" => "BtnNavPhrases",
                        "medialog" => "BtnNavMediaLog",
                        _ => null,
                    };
                    if (name != null && FindName(name) is ButtonBase button)
                        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                    else
                        App.Logger?.Debug("Launcher pill {Key} has no handler", tab.Key);
                    break;
                default:
                    // Pages, zones and the Just Drop window all go through ShowTab.
                    ShowTab(tab.Key);
                    break;
            }
        }

        private static string? SettingsSectionLabelKey(string sectionKey)
        {
            foreach (var t in NavSections.Find(NavSections.Settings)?.Tabs ?? Array.Empty<NavTab>())
                if (string.Equals(t.Key, sectionKey, StringComparison.OrdinalIgnoreCase)) return t.LabelKey;
            return null;
        }

        /// <summary>The label of the Settings section whose pill is checked (null if none).</summary>
        private string? CurrentSettingsSectionLabel()
        {
            if (AppSettingsTab == null) return null;
            foreach (var t in NavSections.Find(NavSections.Settings)?.Tabs ?? Array.Empty<NavTab>())
            {
                var pillName = "SectionPill" + char.ToUpperInvariant(t.Key[0]) + t.Key.Substring(1);
                if (AppSettingsTab.FindName(pillName) is RadioButton { IsChecked: true })
                    return Loc.Get(t.LabelKey);
            }
            return null;
        }

        /// <summary>"Conditioning Control Panel - Play › Lobby": the fourth "you are here" cue.</summary>
        private void UpdateNavTitle(string? section, string tab, string? pageLabel)
        {
            _navBaseTitle ??= Title;
            var sectionLabel = section == null ? null : Loc.Get(NavSections.Find(section)?.LabelKey ?? string.Empty);
            var page = pageLabel ?? (section == NavSections.Home ? null : Loc.Get(NavStripRules.PageLabelKey(tab) ?? string.Empty));
            if (string.IsNullOrEmpty(sectionLabel)) { Title = _navBaseTitle; return; }
            Title = string.IsNullOrEmpty(page)
                ? $"{_navBaseTitle} - {sectionLabel}"
                : $"{_navBaseTitle} - {sectionLabel} {Loc.Get("nav_crumb_sep")} {page}";
        }
    }
}
