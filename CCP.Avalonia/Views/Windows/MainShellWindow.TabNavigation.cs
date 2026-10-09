// PORTED from WPF 7.1.5 ConditioningControlPanel/MainWindow/MainWindow.TabNavigation.cs (1,133
// lines) + MainWindow.SectionChrome.cs (379 lines): ShowTab, the tab registry (NavTabHost /
// RegisterNavTab), old keys that resolve to their new home (silent and "Moved" redirects), and
// the section page chrome ShowTab syncs on every navigation (strip, breadcrumb, window title,
// last-tab memory, page wash). Core NavSections is the one table.
//
// What is REAL here: ShowTab (hide every tab panel, show one), the per-tab side effects that
// resolve on this head (OnTabShown), the registry, the redirects, the zone keys (Play > Eyes /
// Sessions, Studio > Scheduler & Ramp, Library > Folders), the Library launcher pills, the strip
// wiring and the section wash.
//
// Stand-ins: none left. The four Companion pages, Friends and Leash all register through
// RegisterNavTab (RegisterCompanionTabs / RegisterSocialTabs).
// Zones scroll as in WPF: Play through ScrollPlayZoneFor (MainShellWindow.PlayTab.cs), Assets
// through AssetsTabView.ScrollToZone.
//
// Every panel is resolved by x:Name through FindControl, never by generated field:
// MainShellWindow.axaml.cs loads with AvaloniaXamlLoader.Load(this), which never assigns the
// x:Name fields. Named<T>() below is the only way to reach a control of this window.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The one way to reach a control of this window - see the header. Cheap: a
        /// namescope dictionary lookup, no tree walk.</summary>
        internal T? Named<T>(string name) where T : Control => this.FindControl<T>(name);

        /// <summary>The Settings door, and the effects rack. Resolved on every read for the same
        /// reason: the generated fields of this window are never assigned.</summary>
        internal Tabs.AppSettingsTabView? AppSettingsPage => Named<Tabs.AppSettingsTabView>("AppSettingsTab");
        internal Tabs.StudioTabView? StudioRack => Named<Tabs.StudioTabView>("StudioTab");

        /// <summary>The section page header (breadcrumb + pills).</summary>
        internal SectionTabStrip? NavStrip => Named<SectionTabStrip>("SectionStrip");

        /// <summary>The tab key currently shown, lower-case. "settings" until the first switch,
        /// which is the panel the XAML leaves visible.</summary>
        internal string CurrentTab { get; private set; } = "settings";

        /// <summary>Tab key -> the x:Name of the panel it shows. Aliases and zones point at one
        /// panel: "lab" is the old name for the Play wall, "playsessions"/"playeyes" are places on
        /// it, "haptics"/"ramp" are modules of the Studio rack, "progression" is the dashboard,
        /// "folders" is a zone of the Assets page, "premium" is the full vault (7.1.5 Home page).</summary>
        private static readonly Dictionary<string, string> TabPanels = new(StringComparer.Ordinal)
        {
            ["settings"] = "SettingsTab",        ["progression"] = "SettingsTab",
            ["presets"] = "PresetsTab",          ["quests"] = "QuestsTab",
            ["programs"] = "ProgramsTab",        ["enhancements"] = "EnhancementsTab",
            ["deeper"] = "DeeperTab",            ["achievements"] = "AchievementsTab",
            ["companion"] = "CompanionTab",      ["play"] = "PlayTab",  ["lab"] = "PlayTab",
            ["playsessions"] = "PlayTab",        ["playeyes"] = "PlayTab",
            ["leaderboard"] = "LeaderboardTab",  ["assets"] = "AssetsTab", ["folders"] = "AssetsTab",
            ["discord"] = "DiscordTab",          ["awareness"] = "AwarenessTab",
            ["remotecontrol"] = "RemoteControlTab", ["availablesubjects"] = "AvailableSubjectsTab",
            ["bambitakeover"] = "BambiTakeoverTab", ["studio"] = "StudioTab", ["haptics"] = "StudioTab",
            ["ramp"] = "StudioTab",
            ["lockdown"] = "LockdownTab",        ["blinktrainer"] = "BlinkTrainerTab",
            ["shelistening"] = "SheListeningTab", ["gradedintake"] = "GradedIntakeTab",
            ["appsettings"] = "AppSettingsTab",  ["spiral"] = "SpiralTab",
            ["premium"] = "ExclusivesTab",       ["chaster"] = "ChasterTab",
        };

        /// <summary>Keys that open a window or a service rather than a tab. ShowTab leaves the
        /// current tab alone for them, as WPF does; the launch itself is not on this head yet.</summary>
        private static readonly HashSet<string> WindowKeys = new(StringComparer.Ordinal)
            { "patreon", "fyp", "justdrop", "webapp" };

        /// <summary>
        /// A live tab key mapped onto the BARK key it must keep announcing itself with (WPF
        /// <c>BarkTabAliases</c>): every built-in mod has a `nav_lab` rule keyed `tab_eq: "lab"`,
        /// and a .ccpmod on disk may say `tab_eq: "exclusives"` for the vault. The KEY is the live
        /// key, the VALUE the old one the rules are written against.
        /// </summary>
        private static readonly Dictionary<string, string> BarkTabAliases =
            new(StringComparer.OrdinalIgnoreCase) { ["play"] = "lab", ["premium"] = "exclusives" };

        /// <summary>A retired or zone key mapped onto the live key of the VIEW that shows it, for
        /// anything keyed to a view (the ambient-FX registry, the lit rail row). The inverse
        /// direction of <see cref="BarkTabAliases"/>; never applied to the key ShowTab records.</summary>
        private static string CanonicalTabKey(string tab) =>
            string.Equals(tab, "lab", StringComparison.OrdinalIgnoreCase) ? "play" : tab;

        private static string ViewKeyFor(string tab) => tab switch
        {
            "lab" or "playsessions" or "playeyes" => "play",
            "ramp" => "studio",
            "folders" => "assets",
            "progression" => "settings",
            _ => tab,
        };

        // ============================== the tab registry ==============================
        // Nav rework (WPF 7.1.5 BRIEF contract 1). New section pages (Friends, Leash,
        // Personality, Permissions, Links...) are not XAML children of this window: each lane
        // registers a host from its own partial file and ShowTab falls through to it. The view is
        // created on its first show, into the LaneTabHost cell (same cell as every other view).

        /// <summary>One lane-owned page. <paramref name="Create"/> runs once, on the first ShowTab(key).</summary>
        internal sealed record NavTabHost(string Key, Func<Control> Create,
            Action<Control>? OnShown = null, Action<Control>? OnHidden = null);

        private readonly Dictionary<string, NavTabHost> _navTabHosts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Control> _navTabViews = new(StringComparer.OrdinalIgnoreCase);
        private string? _shownLaneTab;

        /// <summary>Register a lane page. A second registration of the same key replaces the
        /// first only while the view has not been created yet.</summary>
        internal void RegisterNavTab(NavTabHost host)
        {
            if (host == null || string.IsNullOrWhiteSpace(host.Key)) return;
            var key = host.Key.ToLowerInvariant();
            if (_navTabViews.ContainsKey(key))
            {
                Log.Warning("RegisterNavTab({Key}) ignored: the view already exists", key);
                return;
            }
            _navTabHosts[key] = host with { Key = key };
        }

        /// <summary>True when a lane registered this key.</summary>
        internal bool IsRegisteredNavTab(string key) => _navTabHosts.ContainsKey(key ?? string.Empty);

        /// <summary>Collapse the shown lane page (part of ShowTab's collapse-all).</summary>
        private void HideLaneTabs()
        {
            foreach (var view in _navTabViews.Values) view.IsVisible = false;
            if (_shownLaneTab != null && _navTabHosts.TryGetValue(_shownLaneTab, out var host)
                && _navTabViews.TryGetValue(_shownLaneTab, out var shown))
            {
                try { host.OnHidden?.Invoke(shown); }
                catch (Exception ex) { Log.Warning(ex, "NavTab {Key} OnHidden failed", _shownLaneTab); }
            }
            _shownLaneTab = null;
        }

        /// <summary>Show a registered lane page, creating it on first use. False when the key
        /// is not registered or its view could not be built.</summary>
        private bool ShowLaneTab(string key)
        {
            if (!_navTabHosts.TryGetValue(key, out var host)) return false;
            if (!_navTabViews.TryGetValue(key, out var view))
            {
                try { view = host.Create(); }
                catch (Exception ex)
                {
                    Log.Error(ex, "NavTab {Key} failed to build", key);
                    return false;
                }
                if (view == null) return false;
                if (view.Margin == default) view.Margin = new Thickness(10, 5, 10, 10);
                var hostCell = Named<Grid>("LaneTabHost");
                if (hostCell == null) return false;
                _navTabViews[key] = view;
                hostCell.Children.Add(view);
            }
            view.IsVisible = true;
            _shownLaneTab = key;
            try { host.OnShown?.Invoke(view); }
            catch (Exception ex) { Log.Warning(ex, "NavTab {Key} OnShown failed", key); }
            return true;
        }

        /// <summary>
        /// Shows one tab and hides the rest, lights the section row and syncs the page header.
        /// Case-insensitive at the door: a deep link or a mod that says "Settings" must not land
        /// on a blank page. An unknown key logs and keeps the current tab, never a blank page.
        /// </summary>
        internal void ShowTab(string? tab)
        {
            tab = (tab ?? string.Empty).ToLowerInvariant();

            // Nav rework: old keys with a new home ("exclusives" -> Home > Premium, silent;
            // "together" -> Social > Lobby) land there and say "Moved" the first three times.
            if (TryRedirectSilentTab(tab)) return;
            if (TryRedirectMovedTab(tab)) return;

            if (WindowKeys.Contains(tab)) return;                 // a window, not a tab - see header

            // Unknown key: log and stay. Checked before anything is collapsed.
            bool lane = _navTabHosts.ContainsKey(tab);
            string? target = null;
            if (!lane && !TabPanels.TryGetValue(tab, out target))
            {
                Log.Warning("ShowTab({Tab}) ignored: no such tab on this head", tab);
                return;
            }

            // Bark hook: announce navigation (gated/chanced in the rules so it isn't spammy),
            // routed through BarkTabAliases so renamed tabs keep answering to their old bark key.
            CoreBark.NotifyTabNavigated(BarkTabAliases.TryGetValue(tab, out var barkTab) ? barkTab : tab);

            foreach (var name in TabPanels.Values.Distinct())
            {
                var panel = this.FindControl<Control>(name);
                if (panel is not null) panel.IsVisible = name == target;
            }
            HideLaneTabs();
            if (lane && !ShowLaneTab(tab))
                Log.Warning("ShowTab({Tab}): the registered page could not be shown", tab);

            NoteTabHistory(tab); // back/forward (MainShellWindow.TabHistory.cs)
            CurrentTab = tab;
            SwitchTabFx(ViewKeyFor(tab));
            // A tooltip opened by a stationary pointer outlives the tab it belongs to
            // (MainShellWindow.ToolTipHygiene.cs).
            CloseStaleToolTip();
            OnTabShown(tab);
            // Home's RECENT row fills from every destination, not only chip clicks (WPF 7.1.5).
            NoteDestinationOpened(tab);

            // Nav rework: the strip, the breadcrumb, the window title and last-tab memory, then
            // the lit section row. Last, so they run whatever OnTabShown did.
            SyncSectionChrome(tab);
            RefreshSectionRail(tab);
        }

        /// <summary>
        /// The per-tab side effects that resolve on this head. Guarded as a whole: an entry-time
        /// repaint must never cost the navigation that asked for it.
        /// </summary>
        private void OnTabShown(string tab)
        {
            try
            {
                RefreshEntitlementVeils();
                switch (tab)
                {
                    // WPF re-derives the session feature lock on the way into the dashboard and the
                    // rack: both host real dose dials.
                    case "settings": RefreshSessionFeatureLock(); MaybeShowFeatureIntro("daily-free", "settings"); break;
                    case "studio": StudioRack?.OnTabShown(); RefreshSessionFeatureLock(); MaybeShowFeatureIntro("studio-rack", "studio"); break;
                    case "haptics": StudioRack?.FocusRackEntry("haptics"); RefreshSessionFeatureLock(); MaybeShowFeatureIntro("haptics"); break;
                    // Nav rework zone pill "Scheduler & Ramp": the Studio rack's scheduler module.
                    case "ramp": StudioRack?.FocusRackEntry("scheduler"); RefreshSessionFeatureLock(); break;

                    case "play": case "lab": case "playsessions": case "playeyes": MaybeShowFeatureIntro("play-wall", "play"); ScrollPlayZoneFor(tab); break;
                    case "awareness": MaybeShowFeatureIntro("awareness"); break;
                    case "lockdown": MaybeShowFeatureIntro("lockdown"); break;
                    case "blinktrainer": MaybeShowFeatureIntro("blinktrainer"); break;
                    case "spiral":
                        if (Named<Tabs.SpiralTabView>("SpiralTab")?.IsShowingSpiral == true)
                            MaybeShowFeatureIntro("descent-spiral", "spiral");
                        break;

                    // WPF reaches this through DiscordTabView's visibility hook; the one line of
                    // it that resolves on this head lands here (see ProfileFx).
                    case "discord": UpdateProfileSharingSummary(); ProfilePage?.EnsureProfileMeFirst(); MaybeShowFeatureIntro("profile-hub", "discord"); break;

                    case "presets":
                        _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindPresets);
                        _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindSessions);
                        break;
                    case "deeper": _ = CheckDeeperSubmissionStatusesAsync(); break;

                    case "leaderboard": _ = Named<Tabs.LeaderboardTabView>("LeaderboardTab")?.RefreshLeaderboardAsync(); break;

                    // The vault re-reads its gates on every show (they can move between visits).
                    case "premium": RefreshExclusivesTab(); break;
                    case "chaster": Named<Tabs.ChasterTabView>("ChasterTab")?.OnTabShown(); break;
                    case "shelistening": RefreshSheListeningTab(); MaybeShowFeatureIntro("shelistening"); break;

                    case "programs":
                        if (!CoreSettings.Current.HasSeenProgramsTab)
                        {
                            CoreSettings.Current.HasSeenProgramsTab = true;
                            CoreSettings.Save();
                        }
                        ProgramsIntroPopup.ShowIfFirstTime(this);
                        break;
                }
            }
            catch { /* a navigation must never throw */ }
        }

        /// <summary>WPF MaybeShowFeatureIntro: through the startup ladder, so a quiet window turns
        /// the card into an Inbox row. The door is the owning section's rail Tag.</summary>
        private void MaybeShowFeatureIntro(string key, string? doorTab = null)
        {
            try { FeatureIntroPopup.ShowWhenStartupSettles(key, this, NavDoorForTab(doorTab ?? key)); }
            catch (Exception ex) { Log.Warning(ex, "Feature intro hook failed for {Key}", key); }
        }

        /// <summary>Latched once the Dashboard's cards have been queued (one settle per launch).</summary>
        private bool _dashboardIntroQueued;

        /// <summary>WPF OnDashboardTabVisibilityChanged: the app LANDS on the Dashboard with no
        /// ShowTab behind it, so its two cards are queued from the view's visibility instead.
        /// Called by SettingsTabView.</summary>
        internal void OnDashboardTabVisibilityChanged(bool visible)
        {
            if (!visible || _dashboardIntroQueued) return;
            if (CoreSession.IsSessionRunning) return; // re-shown mid-session, not a launch
            _dashboardIntroQueued = true;
            FeatureIntroPopup.ShowWhenStartupSettles("daily-free", this, NavDoorForTab("settings"));
            FeatureIntroPopup.ShowWhenStartupSettles("one-account", this, NavDoorForTab("settings"));
        }

        /// <summary>The rail row Tag that owns a tab key ("appsettings" for Settings), or null.
        /// Feature intros use this as their per-door budget key, so it stays a string.</summary>
        private static string? NavDoorForTab(string? tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return null;
            var section = NavSections.SectionForTab(CanonicalTabKey(tabKey!));
            return section == null ? null : NavRailRules.DoorTagForSection(section);
        }

        // ============================== redirects ==============================

        /// <summary>An old key that lands on its new home without a word
        /// (SectionChromeRules.SilentRedirectKeys). True when handled.</summary>
        private bool TryRedirectSilentTab(string tab)
        {
            if (Array.IndexOf(SectionChromeRules.SilentRedirectKeys, tab) < 0) return false;
            if (!NavSections.Redirects.TryGetValue(tab, out var to)) return false;
            if (to.Section == NavSections.Settings) OpenAppSettingsSection(to.Tab);
            else ShowTab(to.Tab);
            return true;
        }

        /// <summary>An old ShowTab key with a new home: navigate there and, the first few times,
        /// say so. True when the key was a redirect and has been handled.</summary>
        private bool TryRedirectMovedTab(string tab)
        {
            if (Array.IndexOf(SectionChromeRules.MovedRedirectKeys, tab) < 0) return false;
            if (!NavSections.Redirects.TryGetValue(tab, out var to)) return false;

            if (to.Section == NavSections.Settings) OpenAppSettingsSection(to.Tab);
            else ShowTab(to.Tab);

            try
            {
                // AppSettings.NavMovedToastHits counts across the app's life, as WPF 7.1.5 does; like
                // the last-tab memory it rides the next save (navigation writes no settings here).
                var s = CoreSettings.Current;
                if (s.NavMovedToastHits < SectionChromeRules.NavMovedNoteLimit)
                {
                    s.NavMovedToastHits++;
                    var section = SafeNavLoc(NavSections.Find(to.Section)?.LabelKey ?? string.Empty, to.Section);
                    var page = to.Section == NavSections.Settings
                        ? SafeNavLoc(SettingsSectionLabelKey(to.Tab) ?? string.Empty, to.Tab)
                        : SafeNavLoc(NavStripRules.PageLabelKey(to.Tab) ?? string.Empty, to.Tab);
                    var note = SafeNavLoc("nav_moved_toast", string.Empty);
                    if (!string.IsNullOrEmpty(note))
                        NavStrip?.ShowMovedNote(string.Format(note, section, page));
                }
            }
            catch (Exception ex) { Log.Debug("Moved note failed: {E}", ex.Message); }
            // Ring the place it moved to, after the page has laid out (WPF SectionChrome GlowNavTarget).
            Dispatcher.UIThread.Post(() => GlowNavTarget(to.Tab), DispatcherPriority.Normal);
            return true;
        }

        // ============================== section chrome ==============================

        /// <summary>
        /// Last-tab memory, section -> tab as JSON (NavStripRules.WithLastTab), kept in
        /// AppSettings.NavLastTabBySection as WPF 7.1.5 does. WPF also saves on the spot; here it rides
        /// the next save (any settings write, and the exit save in App.SaveSettingsOnExit), because
        /// navigation must never write settings.json on this head (the deferred entitlement-lapse
        /// write, PremiumGatesTests.LapsePass_ClearsInMemory_AndOnlyAnEntitlementEventWrites).
        /// </summary>
        internal string? NavLastTabJson
        {
            get => CoreSettings.Current.NavLastTabBySection;
            set => CoreSettings.Current.NavLastTabBySection = value ?? "";
        }

        /// <summary>The tab a section returns to (its remembered tab, else its default).</summary>
        internal string NavLastTabFor(string section) =>
            NavStripRules.LastTabFor(NavLastTabJson, section) ?? NavSections.DefaultTab(section) ?? "settings";

        private bool _sectionStripWired;

        /// <summary>Sync the strip, breadcrumb, title and last-tab memory to the tab on screen.</summary>
        private void SyncSectionChrome(string tab)
        {
            if (!_navRailReady) return;
            try
            {
                WireSectionStrip();
                var section = NavSections.SectionForTab(tab);

                string? pageLabel = null;
                if (section == NavSections.Settings) pageLabel = CurrentSettingsSectionLabel();
                NavStrip?.Show(section, tab, pageLabel);
                PaintSectionWash(section);
                UpdateNavTitle(section, tab, pageLabel);

                // Last tab per section. Old keys are not remembered (their new home is).
                var memo = tab == "lab" ? "play" : tab;
                if (section != null && !NavSections.Redirects.ContainsKey(memo))
                    NavLastTabJson = NavStripRules.WithLastTab(NavLastTabJson, section, memo);
            }
            catch (Exception ex) { Log.Debug("SyncSectionChrome({Tab}) failed: {E}", tab, ex.Message); }
        }

        private void WireSectionStrip()
        {
            if (_sectionStripWired || NavStrip is not { } strip) return;
            _sectionStripWired = true;

            strip.AccessProvider = NavAccess;
            strip.TabRequested += OnSectionPillChosen;
            strip.PillCreated += OnSectionPillCreated;   // shell#23: every pill gets a pin menu (MainShellWindow.FavoritePins.cs)
            strip.SectionRequested += section =>
            {
                if (section == NavSections.Settings) ShowTab("appsettings");
                else ShowTab(NavLastTabFor(section));
            };

            // Settings keeps its own left pill column; the breadcrumb follows it.
            AppSettingsPage?.AddHandler(ToggleButton.IsCheckedChangedEvent, (_, e) =>
            {
                if (e.Source is RadioButton { IsChecked: true } rb && (rb.Name ?? string.Empty).StartsWith("SectionPill", StringComparison.Ordinal)
                    && AppSettingsPage?.IsVisible == true)
                {
                    var label = CurrentSettingsSectionLabel();
                    strip.Show(NavSections.Settings, "appsettings", label);
                    UpdateNavTitle(NavSections.Settings, "appsettings", label);
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private void OnSectionPillChosen(NavTab tab)
        {
            switch (tab.Kind)
            {
                case NavTabKind.Launcher:
                    // The Library's dialogs and sites: the same handlers the old rail rows called.
                    if (!OpenLibraryLauncher(tab.Key)) Log.Debug("Launcher pill {Key} has no handler", tab.Key);
                    break;
                default:
                    // Pages, zones and the Just Drop window all go through ShowTab.
                    ShowTab(tab.Key);
                    break;
            }
        }

        /// <summary>
        /// Opens one of the Library's launchers (mods, catalogue, phrases, media log) through the
        /// handler its rail row used to call. The strip pills and the Ctrl+K rows share this.
        /// False for an unknown key.
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

        private string? _washSection;

        /// <summary>Tints the page ground in the section hue (NavStripRules.Accent, the one table)
        /// and repaints the rail's shadow to match. A tab no section owns keeps the current wash.
        /// ponytail: WPF eases the colour over SectionChromeRules.SectionWashMs; Avalonia has no
        /// gradient-stop colour transition, so the wash swaps at once (as WPF's Motion Off).</summary>
        private void PaintSectionWash(string? section)
        {
            try
            {
                if (section == null || section == _washSection) return;
                _washSection = section;
                var hue = NavStripRules.Accent(section);
                PaintSectionInk(section);
                if (Named<Border>("SectionPageWash") is { } wash)
                {
                    wash.Background = NavPaint.Diagonal(new[]
                    {
                        (NavRailRules.WithAlpha(hue, SectionChromeRules.SectionWashAlpha), 0.0),
                        (NavRailRules.WithAlpha(hue, 0), 0.66),
                    });
                }
                if (Named<Border>("SectionWashLine") is { } line)
                    line.Background = NavPaint.Solid(NavRailRules.WithAlpha(hue, SectionChromeRules.SectionWashLineAlpha));
                PaintDepthRail(hue);
                PaintDepthHud(hue);
                PaintSectionEdge(hue, NavRailRules.Ms(SectionChromeRules.SectionWashMs, global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level));
            }
            catch (Exception ex) { Log.Debug("PaintSectionWash failed: {E}", ex.Message); }
        }

        /// <summary>The section ink resources (SectionInk / Tint / Rule / Outline, colour and brush)
        /// pages bind with DynamicResource, swapped at once the way mod theming swaps its keys.</summary>
        internal static void PaintSectionInk(string? section)
        {
            var res = Application.Current?.Resources;
            if (res == null) return;
            res["SectionInk"] = NavPaint.C(NavStripRules.Ink(section));
            res["SectionTint"] = NavPaint.C(NavStripRules.Tint(section));
            res["SectionRule"] = NavPaint.C(NavStripRules.Rule(section));
            res["SectionOutline"] = NavPaint.C(NavStripRules.Outline(section));
            res["SectionInkBrush"] = NavPaint.Solid(NavStripRules.Ink(section));
            res["SectionTintBrush"] = NavPaint.Solid(NavStripRules.Tint(section));
            res["SectionRuleBrush"] = NavPaint.Solid(NavStripRules.Rule(section));
            res["SectionOutlineBrush"] = NavPaint.Solid(NavStripRules.Outline(section));
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
            var page = AppSettingsPage;
            if (page == null) return null;
            foreach (var t in NavSections.Find(NavSections.Settings)?.Tabs ?? Array.Empty<NavTab>())
            {
                var pillName = "SectionPill" + char.ToUpperInvariant(t.Key[0]) + t.Key.Substring(1);
                if (page.FindControl<RadioButton>(pillName) is { IsChecked: true })
                    return SafeNavLoc(t.LabelKey, t.Key);
            }
            return null;
        }

        private string? _navBaseTitle;

        /// <summary>"Conditioning Control Panel - Play > Lobby": the fourth "you are here" cue.</summary>
        private void UpdateNavTitle(string? section, string tab, string? pageLabel)
        {
            _navBaseTitle ??= Title;
            var sectionLabel = section == null ? null : SafeNavLoc(NavSections.Find(section)?.LabelKey ?? string.Empty, section);
            // Home's pages: the dashboard names only the section; Premium (a hidden tab) names itself.
            var page = pageLabel ?? (section == NavSections.Home && (string.IsNullOrEmpty(tab) || tab == "settings")
                ? null : SafeNavLoc(NavStripRules.PageLabelKey(tab) ?? string.Empty, string.Empty));
            Title = string.IsNullOrEmpty(sectionLabel) ? _navBaseTitle
                : string.IsNullOrEmpty(page)
                    ? $"{_navBaseTitle} - {sectionLabel}"
                    : $"{_navBaseTitle} - {sectionLabel} {SafeNavLoc("nav_crumb_sep", ">")} {page}";
        }

        // ============================== launchers ==============================

        // WPF MainWindow.TabNavigation.cs: open the web app, retire the banner beat. 7.1.5 moved
        // the door from the rail into Play > Games; the handler stays for that card.
        // No browser -> show the URL, as WPF's BrowserLauncher.OpenUrlOrPrompt does.
        private async void DoorWebApp_Click(object? sender, RoutedEventArgs e)
        {
            const string url = "https://app.cclabs.app";
            try
            {
                if (!await Platform.ExternalOpener.OpenAsync(this, url))
                {
                    try { if (Clipboard is { } cb) await cb.SetTextAsync(url); } catch { /* clipboard may be unavailable */ }
                    await Dialogs.MessageDialog.ShowAsync(this, Loc.Get("title_open_link_in_browser"),
                        Loc.GetF("msg_browser_no_default_for", "open the CC Labs web app") + Loc.GetF("msg_browser_link_copied", url));
                }
                RetireWebBannerBeat();
            }
            catch (Exception ex) { Log.Warning(ex, "DoorWebApp_Click failed"); }
        }

        // WPF: the Media Log is a window, deliberately no ShowTab.
        private void BtnNavMediaLog_Click(object? sender, RoutedEventArgs e)
        {
            try { new MediaHistoryWindow().Show(this); }
            catch (Exception ex) { Log.Warning(ex, "BtnNavMediaLog_Click failed"); }
        }
    }
}
