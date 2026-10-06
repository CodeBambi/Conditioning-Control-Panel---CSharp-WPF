using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
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

                    // The one-time "moved here" shimmer rides the same three hits as the note.
                    var glowKey = to.Tab;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                        new Action(() => GlowNavTarget(glowKey)));
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
            // Right-click on a pill pins or unpins it to the Home Favourites column, as the rail
            // rows always did. Only pills with a Ctrl+K row are pinnable (a chip opens a row).
            SectionStrip.PillCreated += (tab, pill) =>
            {
                var id = PinIdForPill(tab);
                if (id != null) AttachPinMenu(pill, id, holdRail: false);
            };
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
                    // The Library's dialogs and sites: the same handlers the old rail rows called.
                    if (!OpenLibraryLauncher(tab.Key))
                        App.Logger?.Debug("Launcher pill {Key} has no handler", tab.Key);
                    break;
                default:
                    // Pages, zones and the Just Drop window all go through ShowTab.
                    var before = _navCurrentTab;
                    ShowTab(tab.Key);
                    // Games is Play's first zone: ShowTab only scrolls it when coming from another
                    // zone, so a click on Games from anywhere else (the active Games pill while
                    // scrolled down included) scrolls and glows here.
                    if (tab.Key == "play" && before is not ("playsessions" or "playeyes"))
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                            new Action(() => ScrollPlayZone("games")));
                    break;
            }
        }

        /// <summary>
        /// Opens one of the Library's launchers (mods, catalogue, phrases, media log) through the
        /// handler its rail row used to call. The strip pills and the Ctrl+K rows share this, so
        /// neither depends on a button that is no longer drawn. False for an unknown key.
        /// </summary>
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

        /// <summary>The Favourites destination a strip pill pins as, or null when it has no
        /// Ctrl+K row (zone pills without a row, hidden pills).</summary>
        internal static string? PinIdForPill(NavTab tab)
        {
            var id = tab.Kind == NavTabKind.Launcher ? "launch." + tab.Key
                   : tab.Key == "justdrop" ? "door.justdrop"
                   : "tab." + tab.Key;
            return FavoritesRailRule.IsDestination(id) && SettingsPaletteIndex.ById(id) != null ? id : null;
        }

        /// <summary>Sheen length of the "moved here" glow (Full motion only).</summary>
        internal const int NavGlowSheenMs = NavGlow.SheenMs;

        /// <summary>How long the glow holds before it fades.</summary>
        internal const int NavGlowHoldMs = NavGlow.HoldMs;

        /// <summary>
        /// Glow the place something moved to: a Settings section key ("account", "monitors")
        /// lights that section's pill; a tab key lights its strip pill, its rail row and, for a
        /// zone, the zone header. Full = 600 ms sheen + 2 s glow, Reduced = the glow alone,
        /// Off = nothing (NavGlow). Called by What moved's Show me and by the redirect note.
        /// Every silent return writes a Debug line.
        /// </summary>
        /// <summary>Palette door: a Ctrl+K row that lands inside a page rings its target the
        /// way Show me does (the partial itself is private to the window).</summary>
        internal void GlowNavKey(string key) => GlowNavTarget(key);

        partial void GlowNavTarget(string tabKey)
        {
            try
            {
                var level = MotionFx.Level;
                if (string.IsNullOrEmpty(tabKey)) { App.Logger?.Debug("GlowNavTarget: empty key"); return; }
                if (level == MotionLevel.Off) { App.Logger?.Debug("GlowNavTarget({Key}): motion off", tabKey); return; }

                if (SettingsSectionLabelKey(tabKey) != null && tabKey != "appsettings")
                {
                    var pillName = "SectionPill" + char.ToUpperInvariant(tabKey[0]) + tabKey.Substring(1);
                    if (AppSettingsTab?.FindName(pillName) is FrameworkElement settingsPill)
                        NavGlow.Once(settingsPill, NavStripRules.Accent(NavSections.Settings), level, "settings." + tabKey);
                    else App.Logger?.Debug("GlowNavTarget({Key}): no Settings pill {Name}", tabKey, pillName);
                    return;
                }

                var section = NavSections.SectionForTab(tabKey);
                var accent = NavStripRules.Accent(section);
                bool any = false;
                if (SectionStrip?.PillFor(NavStripRules.ActivePill(tabKey) ?? tabKey) is { } pill)
                    any |= NavGlow.Once(pill, accent, level, "pill." + tabKey);
                if (NavAnchorForTab(tabKey) is { } row) any |= NavGlow.Once(row, accent, level, "rail." + tabKey);
                if (!any) App.Logger?.Debug("GlowNavTarget({Key}): nothing to glow (no pill, no rail row)", tabKey);
            }
            catch (Exception ex) { App.Logger?.Debug("GlowNavTarget({Key}) failed: {E}", tabKey, ex.Message); }
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
