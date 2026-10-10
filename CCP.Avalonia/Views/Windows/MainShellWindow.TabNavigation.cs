// PORTED from ConditioningControlPanel/MainWindow/MainWindow.TabNavigation.cs (1,174 lines),
// the part of it that is navigation.
//
// What is REAL here: ShowTab (hide every tab panel, show one, light the rail row that owns it -
// MainShellWindow.NavRail.cs is the section rail). SwitchTabFx
// is real too, and runs inside ShowTab (MainShellWindow.AmbientFx.cs).
//
// What is NOT, on purpose, each named so it is not lost silently:
//   - The transition choreography (AnimateTabIn, the Stop*Shimmer/Pulse/Motion calls).
//   - Per-tab side effects on the way in (RefreshPresetsList, StopPolling on leaving Available Subjects,
//     UpdatePatreonUI, RefreshIntakePassTile, RefreshDashboardRail - its favorites half is restored). Those reach App.* or a service.
//     The FIVE that do not are restored in OnTabShown below:
//       * StudioTab.OnTabShown() for "studio" and StudioTab.FocusRackEntry("haptics") for the
//         haptics alias - ported view state on StudioTabView. Without the second, ShowTab("haptics")
//         landed on the rack's last selection instead of the Haptics module, and OpenStudioModule
//         routes haptics through it.
//       * RefreshSessionFeatureLock() on "settings", "studio" and "haptics" - the same three cases
//         WPF calls it from (MainWindow.TabNavigation.cs:260/476/501). It is real and idempotent on
//         this head (MainShellWindow.SessionFeatureLock.cs) and is re-derived, never latched, so
//         arriving at a tab cannot find a stale lock.
//       * UpdateProfileSharingSummary() on "discord" - see the case itself for why it lands here
//         and not in the FX partial WPF reaches it through.
//       * HasSeenProgramsTab and ProgramsIntroPopup.ShowIfFirstTime on "programs".
//   - EmiDesk (EmiTargets.NoteTabOpened). The Bark hook is REAL now, through CoreBark, and fires
//     in the same place WPF fires it - see ShowTab.
//   - The keys that are WINDOWS, not tabs, and the one launcher door: "patreon" (Settings · Account,
//     as WPF ShowAppInfoPopup - wired), "fyp" (OpenFypFeed), "justdrop" (the shop host).
//     fyp and justdrop are documented no-ops below until their service exists here. The "webapp" door is
//     wired (DoorWebApp_Click opens it through the Launcher).
//
// Every panel and door is resolved by x:Name through FindControl, never by generated field, so
// a panel this head does not carry is skipped rather than a compile error.
//
// THAT IS NOT OPTIONAL ON THIS WINDOW. MainShellWindow.axaml.cs:87 loads with
// AvaloniaXamlLoader.Load(this), which - unlike the generated InitializeComponent - never assigns
// the x:Name fields. So `AppSettingsTab`, `StudioTab`, `SettingsTab` and `MainTutorialOverlay`
// COMPILE and are always null at runtime: a `?.` on one is a silent no-op, not a safe guard.
// Named<T>() below is the only way to reach a control of this window from any of its partials.
//
// AND THE HAZARD IS NOT CONFINED TO THIS WINDOW. An earlier revision of this header claimed "the
// tab views themselves do call InitializeComponent, so THEIR fields are real once found". That was
// never true of all of them: DiscordTabView, PlayTabView, QuestsTabView and PresetsTabView loaded
// the same way, so their named fields were null too and anything reaching in through a field of
// theirs was the same silent no-op one level down. DiscordTabView is fixed at the source (its ctor
// now calls InitializeComponent). The other three are NOT this layer's to touch and are still on
// AvaloniaXamlLoader.Load - reach into them with FindControl, never with a field, until they are.
// The general rule for any file: grep the view's ctor before you trust one of its x:Name fields.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

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

        /// <summary>The tab key currently shown, lower-case. "settings" until the first switch,
        /// which is the panel the XAML leaves visible.</summary>
        internal string CurrentTab { get; private set; } = "settings";

        /// <summary>The tab shown before the current one (the Play zone scroll reads it).</summary>
        private string _tabBefore = "settings";

        /// <summary>Tab key -> the x:Name of the panel it shows. Aliases point at one panel:
        /// "lab" is the old name for the Play card wall, "haptics" is a module inside Studio,
        /// "progression" is a section of Settings. Same table as the WPF switch.</summary>
        private static readonly Dictionary<string, string> TabPanels = new(StringComparer.Ordinal)
        {
            ["settings"] = "SettingsTab",        ["progression"] = "SettingsTab",
            ["presets"] = "PresetsTab",          ["quests"] = "QuestsTab",
            ["programs"] = "ProgramsTab",        ["enhancements"] = "EnhancementsTab",
            ["deeper"] = "DeeperTab",            ["achievements"] = "AchievementsTab",
            ["companion"] = "CompanionTab",      ["play"] = "PlayTab",  ["lab"] = "PlayTab",
            ["leaderboard"] = "LeaderboardTab",  ["assets"] = "AssetsTab",
            ["discord"] = "DiscordTab",          ["awareness"] = "AwarenessTab",
            ["remotecontrol"] = "RemoteControlTab", ["availablesubjects"] = "AvailableSubjectsTab",
            ["bambitakeover"] = "BambiTakeoverTab", ["studio"] = "StudioTab", ["haptics"] = "StudioTab",
            ["lockdown"] = "LockdownTab",        ["blinktrainer"] = "BlinkTrainerTab",
            ["shelistening"] = "SheListeningTab", ["gradedintake"] = "GradedIntakeTab",
            ["appsettings"] = "AppSettingsTab",  ["spiral"] = "SpiralTab",
            ["exclusives"] = "ExclusivesTab", ["chaster"] = "ChasterTab",
            ["friends"] = "FriendsTab",          ["leash"] = "LeashTab",
            // Nav rework zone pills (WPF MainWindow.TabNavigation.cs:521/572/666): places inside a page.
            ["playeyes"] = "PlayTab", ["playsessions"] = "PlayTab", ["folders"] = "AssetsTab", ["ramp"] = "StudioTab",
        };

        /// <summary>Keys that open a window or a service rather than a tab. ShowTab leaves the
        /// current tab alone for them, as WPF does; the launch itself is not on this head yet.</summary>
        private static readonly HashSet<string> WindowKeys = new(StringComparer.Ordinal)
            { "patreon", "fyp", "justdrop", "webapp" };

        /// <summary>
        /// A live tab key mapped onto the BARK key it must keep announcing itself with. Copied from
        /// WPF's <c>BarkTabAliases</c> (MainWindow.TabNavigation.cs:83) and it is head-side by
        /// nature: Phase 6 retired the Lab page into the Play door, every built-in mod has a
        /// `nav_lab` rule keyed `tab_eq: "lab"`, and every third-party .ccpmod on disk carries its
        /// own copy we can never edit. Deriving the bark key from the live key would fire nothing.
        /// <para>ShowTab("lab") still works as a permanent alias and fires "lab" directly, so it is
        /// deliberately NOT an entry here.</para>
        /// </summary>
        private static readonly Dictionary<string, string> BarkTabAliases =
            new(StringComparer.OrdinalIgnoreCase) { ["play"] = "lab" };

        /// <summary>
        /// Shows one tab and hides the rest, and lights the rail row that owns it. Case-insensitive
        /// at the door for the same reason WPF is: a deep link or a mod that says "Settings" must
        /// not land on a blank page. An unknown key is a no-op that keeps the current tab, never a
        /// page with nothing on it.
        /// </summary>
        internal void ShowTab(string? tab)
        {
            tab = (tab ?? string.Empty).ToLowerInvariant();
            // The dashboard's RECENT rail, at the door before the intercepts (WPF :121), so a
            // window key counts as an open like any tab (MainShellWindow.FavoritesRail.cs).
            NoteDestinationOpened(tab);
            // WPF ShowTab("patreon") -> ShowAppInfoPopup -> ShowAccountSettings (MainWindow.TabNavigation.cs:126,
            // MainWindow.AccountShell.cs:73): Settings, scrolled to Account. Before the bark, as there.
            if (tab == "patreon") { OpenAppSettingsSection("account"); return; }
            if (WindowKeys.Contains(tab)) return;                 // a window, not a tab - see header

            // Bark hook: announce navigation (gated/chanced in the rules so it isn't spammy).
            // Routed through BarkTabAliases so renamed tabs keep answering to their old bark key.
            // Placed exactly where WPF places it (MainWindow.TabNavigation.cs:158) - AFTER the
            // window-key intercepts and BEFORE the panel lookup, so an unknown key announces
            // itself here too, as it does there.
            CoreBark.NotifyTabNavigated(BarkTabAliases.TryGetValue(tab, out var barkTab) ? barkTab : tab);

            if (!TabPanels.TryGetValue(tab, out var target)) return;

            foreach (var name in new HashSet<string>(TabPanels.Values))
            {
                var panel = this.FindControl<Control>(name);
                if (panel is not null) panel.IsVisible = name == target;
            }
            NoteTabHistory(tab); // back/forward (MainShellWindow.TabHistory.cs); unknown keys never reach it
            _tabBefore = CurrentTab;
            CurrentTab = tab;
            RefreshSectionRail(tab); // the lit rail row (MainShellWindow.NavRail.cs)
            SwitchTabFx(tab);
            SyncSectionChrome(tab); // the pill strip + breadcrumb (MainShellWindow.SectionChrome.cs)
            // A tooltip opened by a stationary pointer outlives the tab it belongs to, because
            // nothing ever moved the pointer off its owner. Same call, same place, as WPF's
            // MainWindow.TabNavigation.cs:186 (MainShellWindow.ToolTipHygiene.cs).
            CloseStaleToolTip();
            OnTabShown(tab);
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
                    // WPF re-derives the session feature lock on the way into each of these
                    // three (MainWindow.TabNavigation.cs:260/476/501): the Dashboard and the rack
                    // both host real dose dials, so a lock that was latched rather than re-derived
                    // could survive a crash, an abort or an out-of-order session event.
                    case "settings": RefreshFavoritesRail(); RefreshSessionFeatureLock(); MaybeShowFeatureIntro("daily-free", "settings"); break;
                    case "progression": RefreshFavoritesRail(); break; // WPF TabNavigation.cs:308 (RefreshDashboardRail)
                    case "studio": StudioRack?.OnTabShown(); RefreshSessionFeatureLock(); MaybeShowFeatureIntro("studio-rack", "studio"); break;
                    case "haptics": StudioRack?.FocusRackEntry("haptics"); RefreshSessionFeatureLock(); MaybeShowFeatureIntro("haptics"); break;
                    // WPF :666, the "Scheduler & Ramp" zone pill: the rack's scheduler module.
                    case "ramp": StudioRack?.FocusRackEntry("scheduler"); RefreshSessionFeatureLock(); break;

                    // WPF MainWindow.TabNavigation.cs:420/457/527/534 - the door tour cards (studio-rack: :505, case "studio").
                    case "play": case "lab": case "playeyes": case "playsessions":
                        MaybeShowFeatureIntro("play-wall", "play");
                        // WPF :550: Sessions and Eyes are places on the wall; Games scrolls back up
                        // only when the previous pill was another zone (a plain return keeps its scroll).
                        var zone = tab switch
                        {
                            "playsessions" => "sessions",
                            "playeyes" => "eyes",
                            _ => _tabBefore is "playsessions" or "playeyes" ? "games" : null,
                        };
                        if (zone != null) Named<Tabs.PlayTabView>("PlayTab")?.ScrollToZone(zone);
                        break;
                    case "awareness": MaybeShowFeatureIntro("awareness"); break;
                    case "lockdown": MaybeShowFeatureIntro("lockdown"); break;
                    case "blinktrainer": MaybeShowFeatureIntro("blinktrainer"); break;
                    // WPF :581 - only once the room shows the map (its IsVisible hook re-read the gates).
                    case "spiral":
                        if (Named<Tabs.SpiralTabView>("SpiralTab")?.IsShowingSpiral == true)
                            MaybeShowFeatureIntro("descent-spiral", "spiral");
                        break;

                    // WPF DiscordTabView IsVisibleChanged -> MainWindow.ProfileFx.cs:OnProfileTabVisibilityChanged
                    // (incoming tab): sharing footer, share-button gate, me-first, OG loop + card stagger.
                    // ponytail: its vat poll half needs MainShellWindow.ProfileVat.cs (DescentService).
                    case "discord": UpdateProfileSharingSummary(); RefreshProfileShareButton(); ProfilePage?.EnsureProfileMeFirst(); OnProfileTabShownFx(); MaybeShowFeatureIntro("profile-hub", "discord"); break;

                    // WPF MainWindow.TabNavigation.cs:292/367: throttled share-status polls on tab open.
                    case "presets":
                        _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindPresets);
                        _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindSessions);
                        break;
                    // WPF TabNavigation.cs:358: every door into the tab rescans the library.
                    case "deeper": InitializeDeeperHub(); _ = CheckDeeperSubmissionStatusesAsync(); break;

                    // WPF MainWindow.TabNavigation.cs:429: every show re-fetches the board (read-only).
                    case "leaderboard": _ = Named<Tabs.LeaderboardTabView>("LeaderboardTab")?.RefreshLeaderboardAsync(); RefreshSeasonRecapReview(); break;
                    // WPF MainWindow.SocialTabs.cs: subscribe and ask for a fresh list (the page folds itself on hide).
                    case "friends": Named<Tabs.FriendsTabView>("FriendsTab")?.OnShown(); break;

                    // WPF MainWindow.Exclusives.cs RefreshExclusivesTab "on tab show": gates can move between visits.
                    case "exclusives": RefreshExclusivesTab(); break;
                    // WPF MainWindow.TabNavigation.cs:588-593 (AnimateTabIn is the header's ponytail).
                    case "chaster": Named<Tabs.ChasterTabView>("ChasterTab")?.OnTabShown(); break;
                    // WPF MainWindow.TabNavigation.cs:433-438: rescan the library, re-sync presets, AssetsFx entrance.
                    case "assets": Named<Tabs.AssetsTabView>("AssetsTab")?.OnTabShown(); break;
                    // WPF AccountSettingsSection IsVisibleChanged: a login behind another door is never shown stale.
                    case "appsettings": AppSettingsPage?.RefreshSections(); break;
                    // WPF MainWindow.SheListening.cs RefreshSheListeningTab "called on tab show".
                    case "shelistening": RefreshSheListeningTab(); MaybeShowFeatureIntro("shelistening"); break;

                    // WPF MainWindow.TabNavigation.cs:324-337: spend the tab's seen-flag on any
                    // route in, then the one-time explainer on top of the tab just shown.
                    // ponytail: no rail pulse to stop on this head (StopProgramsTabPulse).
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

        /// <summary>WPF MaybeShowFeatureIntro (MainWindow.TabNavigation.cs:1094): through the
        /// startup ladder, so a quiet window turns the card into an Inbox row. ponytail: no
        /// FirstShowService on this head, so WPF's first-show early return has nothing to read.</summary>
        private void MaybeShowFeatureIntro(string key, string? doorTab = null)
        {
            try { FeatureIntroPopup.ShowWhenStartupSettles(key, this, NavDoorForTab(doorTab ?? key)); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Feature intro hook failed for {Key}", key); }
        }

        /// <summary>Latched once the Dashboard's cards have been queued (one settle per launch).</summary>
        private bool _dashboardIntroQueued;

        /// <summary>WPF OnDashboardTabVisibilityChanged (MainWindow.TabNavigation.cs:1131): the app
        /// LANDS on the Dashboard with no ShowTab behind it, so its two cards are queued from the
        /// view's visibility instead. Called by SettingsTabView.</summary>
        internal void OnDashboardTabVisibilityChanged(bool visible)
        {
            if (visible) ResettleBrowserFold();   // WPF :1135, the fold's backstop
            if (!visible || _dashboardIntroQueued) return;
            if (CoreSession.IsSessionRunning) return; // re-shown mid-session, not a launch
            _dashboardIntroQueued = true;
            FeatureIntroPopup.ShowWhenStartupSettles("daily-free", this, NavDoorForTab("settings"));
            FeatureIntroPopup.ShowWhenStartupSettles("one-account", this, NavDoorForTab("settings"));
        }

        /// <summary>The rail section that owns a tab (WPF NavDoorForTab, now the NavSections table):
        /// the per-door budget key of the feature-intro cards.</summary>
        private static string? NavDoorForTab(string tab) =>
            NavSections.SectionForTab(tab == "lab" ? "play" : tab) is { } s ? (s == NavSections.Settings ? "appsettings" : s) : null;

        // WPF MainWindow.TabNavigation.cs:986-994: open the web app, retire the banner beat.
        // No browser -> show the URL, as WPF's BrowserLauncher.OpenUrlOrPrompt does.
        private async void DoorWebApp_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
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
            catch (Exception ex) { Serilog.Log.Warning(ex, "DoorWebApp_Click failed"); }
        }

        // WPF MainWindow.TabNavigation.cs:1066: the Media Log is a window, deliberately no ShowTab.
        private void BtnNavMediaLog_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try { new MediaHistoryWindow().Show(this); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "BtnNavMediaLog_Click failed"); }
        }
    }
}
