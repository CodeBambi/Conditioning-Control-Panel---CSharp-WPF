using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Tab navigation: tab-switching logic and content-control visibility management.
    public partial class MainWindow
    {
        #region Tab Navigation

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("settings");
        }

        private void BtnPresets_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("presets");
            RefreshPresetsList();
        }

        // No BtnProgression handler: the button went in the velvet-mosaic rework and the VIEW went
        // in Phase 8. The "progression" tab key still resolves (see the case in ShowTab) and
        // ChromeFx maps it onto BtnSettings for the nav indicator and tutorial spotlights.

        private void BtnQuests_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("quests");
        }

        private void BtnPrograms_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("programs");

            // The pulse is spent the moment the tab is found, whether or not the explainer shows.
            if (App.Settings?.Current is { } s && !s.HasSeenProgramsTab)
            {
                s.HasSeenProgramsTab = true;
                StopProgramsTabPulse();
                App.Settings?.Save();
            }

            // Last, and deliberately after ShowTab: the explainer opens on top of the tab the user
            // just landed on, so dismissing it leaves them looking at the thing it described.
            ProgramsIntroPopup.ShowIfFirstTime(this);
        }

        private void BtnEnhancements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("enhancements");
        }

        // AnimateTabIn now lives in MainWindow.ChromeFx.cs: the bare 200ms fade was replaced by
        // the PR-1 choreography (outgoing fade -> directional slide + fade -> entrance stagger).

        /// <summary>
        /// Live ShowTab key -> the key the companion's bark rules are still written against.
        /// Every built-in mod's bark_rules.json matches navigation eggs with `tab_eq` on the exact
        /// ShowTab strings (54 voiced rules per mod, including the first-run tutorial ladder), and
        /// third-party .ccpmod files on disk carry their own copies we can never edit. So a tab key
        /// that gets renamed or folded into another door MUST land here, mapping the new key back to
        /// the old one - otherwise that tab's barks simply stop firing, silently and untestably.
        /// </summary>
        /// <remarks>
        /// Direction matters and is easy to get backwards: the KEY is the live ShowTab key, the
        /// VALUE is the old key the rules on disk are written against. Never the other way round.
        /// </remarks>
        private static readonly Dictionary<string, string> BarkTabAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            // Phase 6 retired the Lab page into the Play door's card wall. Every built-in mod has
            // a `nav_lab` rule keyed `tab_eq: "lab"` (bark_rules.json, 3 variants each) and every
            // third-party .ccpmod on disk carries its own copy we can never edit, so navigating to
            // the new key still announces itself with the old one. ShowTab("lab") keeps working as
            // a permanent alias and fires "lab" directly - it never round-trips through here.
            ["play"] = "lab",
        };

        /// <summary>
        /// A retired tab key mapped onto the live key of the view that swallowed it. This is the
        /// INVERSE of <see cref="BarkTabAliases"/> and exists for a different consumer: barks must
        /// keep hearing the OLD key, while anything keyed to a VIEW (the ambient-FX registry, the
        /// nav indicator, the door accordion) must be given the NEW one. Getting these two the
        /// same way round is how a canvas ends up running forever behind a hidden tab.
        /// <para>Deliberately NOT applied to <c>tab</c> itself inside <see cref="ShowTab"/>: the
        /// switch below carries the aliases as real <c>case</c> labels so the alias is visible at
        /// the destination rather than laundered at the door.</para>
        /// </summary>
        private static string CanonicalTabKey(string tab) =>
            string.Equals(tab, "lab", StringComparison.OrdinalIgnoreCase) ? "play" : tab;

        internal void ShowTab(string tab)
        {
            // Case is NOT significant here, and every key resolver downstream already agrees:
            // BarkTabAliases, NavDoorForTab and CanonicalTabKey all compare OrdinalIgnoreCase.
            // The dispatch did not - the two `==` redirects below and the `switch` on this
            // string are both ordinal - so a "Settings" from a deep link, a tutorial step or a
            // third-party .ccpmod collapsed every tab, matched no case, and left the window on a
            // blank page with the nav indicator pointing at Home. Every case label and every map
            // key in this file is lower-case, so normalising once at the door unifies all of
            // them without touching a single comparison.
            tab = (tab ?? string.Empty).ToLowerInvariant();

            // Legacy redirect: the "patreon" tab was eliminated; its account/data
            // content lives in the Settings door's Account section now, so this IS
            // a tab switch (ShowAppInfoPopup -> ShowAccountSettings -> appsettings).
            if (tab == "patreon")
            {
                ShowAppInfoPopup();
                return;
            }

            // "fyp" is a window, not a tab: the Exclusives spotlight routes through
            // ShowTab like every other card, so the launch is intercepted here and the
            // active tab is left alone. The card never blocks - OpenFypFeed gates.
            if (tab == "fyp")
            {
                OpenFypFeed();
                return;
            }

            // Bark hook: announce navigation (gated/chanced in the rules so it isn't spammy).
            // Routed through BarkTabAliases so renamed tabs keep answering to their old bark key.
            try
            {
                App.Bark?.NotifyTabNavigated(BarkTabAliases.TryGetValue(tab, out var barkTab) ? barkTab : tab);
            }
            catch { }

            // Park the incoming key for the transition choreography. AnimateTabIn reads it, so the
            // ~25 call sites below stay a single argument and still get a slide direction.
            _pendingTabKey = tab;

            // Stop animations on tabs we're leaving to reduce idle CPU
            StopSeasonTitleShimmer();
            StopLockdownPulse();
            StopSkillTreeAnimations();
            StopExclusivesMotion();
            // Every registered AmbientFxCanvas parks with its tab (see MainWindow.AmbientFx.cs) —
            // new per-tab canvases get the stop hook without touching this method again.
            // CanonicalTabKey, not the raw key: the registry is keyed by the view, and an alias
            // that lands on a view has to RESUME that view's canvas, not park it.
            SwitchTabFx(CanonicalTabKey(tab));
            // A tooltip opened by a stationary cursor outlives the tab it belongs to, because
            // nothing ever moved the mouse off its owner. See MainWindow.ChromeFx.cs.
            CloseStaleToolTip();

            // Hide all tabs
            SettingsTab.Visibility = Visibility.Collapsed;
            PresetsTab.Visibility = Visibility.Collapsed;
            QuestsTab.Visibility = Visibility.Collapsed;
            AchievementsTab.Visibility = Visibility.Collapsed;
            CompanionTab.Visibility = Visibility.Collapsed;
            // PatreonTab is gone (Phase 8). The "patreon" key still works - it early-returns into
            // ShowAppInfoPopup() at the top of this method, which lands on Settings · Account.
            LeaderboardTab.Visibility = Visibility.Collapsed;
            AssetsTab.Visibility = Visibility.Collapsed;
            DiscordTab.Visibility = Visibility.Collapsed;
            EnhancementsTab.Visibility = Visibility.Collapsed;
            if (DeeperTab != null) DeeperTab.Visibility = Visibility.Collapsed;
            // LabTab is gone (Phase 6). PlayTab is the surface both "play" and the permanent
            // "lab" alias land on.
            if (PlayTab != null) PlayTab.Visibility = Visibility.Collapsed;
            AwarenessTab.Visibility = Visibility.Collapsed;
            if (RemoteControlTab != null) RemoteControlTab.Visibility = Visibility.Collapsed;
            if (AvailableSubjectsTab != null) AvailableSubjectsTab.Visibility = Visibility.Collapsed;
            if (BambiTakeoverTab != null) BambiTakeoverTab.Visibility = Visibility.Collapsed;
            // SP5L3: stop polling whenever we leave the Available Subjects
            // tab. Idempotent — safe to call even if not currently polling.
            App.AvailableSubjects?.StopPolling();
            if (StudioTab != null) StudioTab.Visibility = Visibility.Collapsed;
            // Phase 4: HapticsTab is a module INSIDE StudioTab now (see the passthrough below),
            // so collapsing StudioTab already hides it. Kept because it is also the rack's
            // "haptics" panel and both the "studio" and "haptics" cases re-assert the rack's
            // current selection on the way in - the two can never disagree.
            if (HapticsTab != null) HapticsTab.Visibility = Visibility.Collapsed;
            if (LockdownTab != null) LockdownTab.Visibility = Visibility.Collapsed;
            if (BlinkTrainerTab != null)
            {
                // Stop the demo timer AND drop the live-mode OnBlink subscription
                // when leaving the tab so neither runs while the user is
                // elsewhere. Both are idempotent.
                if (BlinkTrainerTab.Visibility == Visibility.Visible)
                {
                    StopBlinkTrainerDemoLoop();
                    UnsubscribeBlinkTrainerLiveBlink();
                    // Reset cached mode so the next entry re-runs the resolver
                    // and starts whatever's appropriate from scratch.
                    _currentBlinkTrainerStageMode = BlinkTrainerStageMode.Demo;
                }
                BlinkTrainerTab.Visibility = Visibility.Collapsed;
            }
            if (SheListeningTab != null) SheListeningTab.Visibility = Visibility.Collapsed;
            if (GradedIntakeTab != null) GradedIntakeTab.Visibility = Visibility.Collapsed;
            if (ProgramsTab != null) ProgramsTab.Visibility = Visibility.Collapsed;
            if (ExclusivesTab != null) ExclusivesTab.Visibility = Visibility.Collapsed;
            if (AppSettingsTab != null) AppSettingsTab.Visibility = Visibility.Collapsed;

            // Phase 1: no more per-tab style swapping. The rail's active state is a real
            // indicator (3px accent bar + tinted row) driven by ApplyNavActiveGlow at the
            // bottom of this method, so every entry keeps the one Style it was authored with
            // and the brand accents (Deeper violet, Subjects neon, Profile blue, Premium red)
            // survive a tab switch instead of being reset and re-applied.
            // "TabButton"/"TabButtonActive" stay untouched in the theme: quest sub-tabs and
            // the roadmap track buttons still use them.

            switch (tab)
            {
                case "settings":
                    SettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SettingsTab);
                    RefreshPremiumRail(); // recompute chip dots (incl. Voice) from live state on every show
                    // Training Programs own the day's feature mix. Re-derived (never latched) on
                    // every show of the Dashboard, so arriving here can never find a stale lock -
                    // not after a crash, an abort, or a session event that fired out of order.
                    RefreshSessionFeatureLock();
                    // Weekly intake pass: paint the centre tile, and play the once-a-week flip
                    // ceremony if this week's reveal hasn't run yet. Must be AFTER the tab is made
                    // visible - the spin is skipped for an off-screen tile so a background login
                    // callback can't burn the reveal on a control nobody is looking at.
                    RefreshIntakePassTile();
                    break;

                case "presets":
                    PresetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(PresetsTab);
                    // Refresh catalogue share statuses on tab open (throttled) so an
                    // approval/rejection reflects on preset + session cards.
                    _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindPresets);
                    _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindSessions);
                    break;

                // PERMANENT ALIAS — do not retire. The "progression" VIEW is gone (Phase 8 deleted
                // ProgressionTabView; the velvet-mosaic rework had already stopped revealing it),
                // but the KEY is API: 54 bark rules per built-in mod carry tab_eq:"progression",
                // and four TutorialService steps declare RequiresTab="progression". Home is the
                // right destination — XP, level and the feature mosaic all live there. Fires its
                // own bark key directly, so it must NOT be added to BarkTabAliases.
                // See also ChromeFx.cs ("progression" => BtnSettings), the door map below, and
                // Services/ChromeFxNav.cs, which are part of the same contract.
                case "progression":
                    SettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SettingsTab);
                    RefreshPremiumRail();
                    break;

                case "quests":
                    QuestsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(QuestsTab);
                    StartSeasonTitleShimmer();
                    RefreshQuestUI();
                    break;

                case "programs":
                    ProgramsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(ProgramsTab);
                    RefreshProgramsUI();
                    break;

                case "enhancements":
                    EnhancementsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(EnhancementsTab);
                    RefreshEnhancementsUI();
                    break;

                case "deeper":
                    if (DeeperTab != null)
                    {
                        DeeperTab.Visibility = Visibility.Visible;
                        AnimateTabIn(DeeperTab);
                        RefreshDeeperLibraryUI();
                        // Phase 2: the Deeper hub's device/monitor pickers moved to
                        // Settings → Devices, so there is nothing to populate here. The refresh
                        // below still fills the consent + calibration status cells, which are
                        // actions this card legitimately keeps.
                        RefreshDeeperWebcamColumn();
                        UpdateWebcamStatusChips(App.Webcam?.IsRunning == true);
                        RefreshBlinkTrainerTrackerButton();
                        // Refresh submission statuses on tab open (throttled) so
                        // an acceptance reflects without restarting the app.
                        _ = CheckDeeperSubmissionStatusesAsync();
                    }
                    break;

                case "achievements":
                    AchievementsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AchievementsTab);
                    RefreshAllAchievementTiles();
                    UpdateAchievementCount();
                    break;

                case "companion":
                    CompanionTab.Visibility = Visibility.Visible;
                    AnimateTabIn(CompanionTab);
                    SyncCompanionTabUI();
                    InitializePhrasePresets();
                    break;

                // Phase 6: the Play door's card wall. "lab" is a PERMANENT alias onto it, not a
                // redirect that skips work - the two labels share one body, so an old caller
                // (tutorial step, notification, Ctrl+K palette, third-party deep link) lands on
                // exactly the same surface with exactly the same refreshes.
                //
                // The bark keys stay honest without any extra work: NotifyTabNavigated fires at
                // the TOP of ShowTab with the incoming key, so arriving here as "lab" announces
                // "lab" directly, and arriving as "play" announces "lab" through
                // BarkTabAliases["play"]. One announcement either way; never two.
                case "lab":
                case "play":
                    PlayTab.Visibility = Visibility.Visible;
                    AnimateTabIn(PlayTab);
                    // Phase 5: SyncLabEffectPermsUI() used to be called here because the AI
                    // effect-permission grid was on this tab while its only sync ran on the
                    // Companion tab (#512). The grid is Z7b of the Companion room now, and
                    // case "companion" -> SyncCompanionTabUI -> SyncAiBrainUI already calls it,
                    // so the sync and the surface finally share a page. Do not re-add it here.
                    // Phase 2: the webcam engine bar (and the seeding it needed) moved to
                    // Settings → Devices, which re-enumerates on its own show via
                    // RefreshDeviceSettingsLists. This wall keeps a read-only status chip, painted
                    // by UpdateWebcamStatusChips off the tracker-state event - and that call is
                    // also what reaches EnsurePr4aFx on this door, so it is not optional.
                    UpdateWebcamStatusChips(App.Webcam?.IsRunning == true);
                    // Everything live on the wall: tier lockbands, the Graded Intake's four pass
                    // states, the Goon perk line, the Deeper master switch, the Bureau account chip
                    // and the once-per-session folder stamp (MainWindow.PlayTab.cs). Also called
                    // from UpdatePatreonUI and from the intake-pass change hook, so the wall is
                    // right whether the user arrived or the entitlement did.
                    RefreshPlayCards();
                    break;

                // Note: "patreon" case is handled at the top of ShowTab as a
                // legacy redirect to the App Info & Data popup (Exclusives tab
                // was eliminated; account/data UI now lives in the dashboard).

                case "leaderboard":
                    LeaderboardTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LeaderboardTab);
                    _ = RefreshLeaderboardAsync(); // Load on first view
                    break;

                case "assets":
                    AssetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AssetsTab);
                    RefreshAssetTree();
                    InitializeAssetPresets();
                    if (PacksSectionEnabled) _ = RefreshPacksAsync();
                    break;

                case "discord":
                    DiscordTab.Visibility = Visibility.Visible;
                    AnimateTabIn(DiscordTab);
                    UpdateDiscordTabUI();
                    break;

                case "awareness":
                    AwarenessTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AwarenessTab);
                    SyncAwarenessTabUI();
                    MaybeShowFeatureIntro("awareness");
                    break;

                case "remotecontrol":
                    RemoteControlTab.Visibility = Visibility.Visible;
                    AnimateTabIn(RemoteControlTab);
                    UpdateRemoteControlUI();
                    break;

                case "availablesubjects":
                    if (AvailableSubjectsTab != null)
                    {
                        AvailableSubjectsTab.Visibility = Visibility.Visible;
                        AnimateTabIn(AvailableSubjectsTab);
                    }
                    EnsureAvailableSubjectsBound();
                    App.AvailableSubjects?.StartPolling();
                    break;

                case "bambitakeover":
                    BambiTakeoverTab.Visibility = Visibility.Visible;
                    AnimateTabIn(BambiTakeoverTab);
                    UpdatePatreonUI();
                    break;

                // Phase 4: the Studio door's effects rack. Every module panel is already
                // instantiated inside StudioTabView; OnTabShown only repaints the mod-aware row
                // captions + state dots and re-asserts the last selection. No ambient canvas is
                // registered for this key and none may be (PLAN §2.7) - SwitchTabFx("studio")
                // above therefore parks all five existing ones for free.
                case "studio":
                    StudioTab.Visibility = Visibility.Visible;
                    AnimateTabIn(StudioTab);
                    StudioTab.OnTabShown();
                    // The rack hosts the real dose dials now, so the session feature lock has to
                    // be re-derived on the way in exactly like the Dashboard does.
                    RefreshSessionFeatureLock();
                    // Haptics is a MODULE of this rack, and its premium gate treatment
                    // (HapticsGate + the content-grid dimming, MainWindow.Patreon.cs:141-152) is
                    // painted only by UpdatePatreonUI. The old top-level "haptics" case called it
                    // on every entry; the rack can now restore a haptics selection through THIS
                    // case, so it has to call it too or the door could open on an unpainted gate.
                    UpdatePatreonUI();
                    // NO MaybeShowFeatureIntro here, deliberately. The FeatureIntros roster
                    // (Windows/FeatureIntroPopup.xaml.cs) has five cards - awareness,
                    // shelistening, blinktrainer, lockdown, haptics - and none of them describes
                    // an effects rack. Borrowing one would explain the wrong thing, and writing a
                    // sixth is the Phase-8 door tour's job, not Phase 4's. The "haptics" card
                    // still fires from its own case below, unchanged.
                    break;

                // Phase 4: haptics is a MODULE of the Studio rack, so this shows the Studio tab
                // and focuses that module. Everything else the old case did is preserved, and
                // the bark key stays "haptics" - NotifyTabNavigated fires at the top of ShowTab
                // with the incoming key, which is still "haptics" on this path, so all three of
                // the mod's haptics rules (and any third-party .ccpmod's) keep matching.
                case "haptics":
                    StudioTab.Visibility = Visibility.Visible;
                    AnimateTabIn(StudioTab);
                    StudioTab.FocusRackEntry("haptics");
                    RefreshSessionFeatureLock();
                    UpdatePatreonUI();
                    MaybeShowFeatureIntro("haptics");
                    break;

                case "lockdown":
                    LockdownTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LockdownTab);
                    StartLockdownPulse();
                    RefreshPremiumGate(LockdownTab.LockdownGate);
                    MaybeShowFeatureIntro("lockdown");
                    break;

                case "blinktrainer":
                    BlinkTrainerTab.Visibility = Visibility.Visible;
                    AnimateTabIn(BlinkTrainerTab);
                    RefreshBlinkTrainerTab();
                    MaybeShowFeatureIntro("blinktrainer");
                    break;

                case "shelistening":
                    SheListeningTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SheListeningTab);
                    RefreshSheListeningTab();
                    MaybeShowFeatureIntro("shelistening");
                    break;

                case "gradedintake":
                    GradedIntakeTab.Visibility = Visibility.Visible;
                    AnimateTabIn(GradedIntakeTab);
                    RefreshGradedIntakeGate();
                    RefreshPastQuizzes();
                    break;

                case "appsettings":
                    AppSettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AppSettingsTab);
                    // Sections that have to re-read live state (device lists, login cards,
                    // update status) get their seam here. Sections that only bind settings
                    // implement nothing and are skipped - see IAppSettingsSection.
                    AppSettingsTab.RefreshSections();
                    break;

                case "exclusives":
                    ExclusivesTab.Visibility = Visibility.Visible;
                    AnimateTabIn(ExclusivesTab);
                    EnsureExclusivesBuilt();     // lazy: first visit builds the shelf
                    RefreshExclusivesTab();      // chips/veils/tier plates from live state
                    StartExclusivesMotion();     // fog canvas + Ken Burns + card sheens
                    break;

            }

            // Reveal the entry we just navigated to. Code-driven navigation (tutorial steps,
            // Exclusives cards, notifications) has to open the owning door too, or the active
            // indicator lands inside a collapsed panel where nobody can see it.
            ExpandDoorForTab(tab);

            // Chrome FX: move the active indicator onto whichever rail entry owns this tab,
            // and light its door header. Last, so it runs whatever the switch above did - and
            // it never throws.
            ApplyNavActiveGlow(NavButtonForTab(tab));
        }

        // ============================== nav rail: doors ==============================

        /// <summary>
        /// The Phase 1 information architecture: six doors over the existing tab keys, plus the
        /// pinned Settings door. Order matches the rail top to bottom, and each door's FIRST tab
        /// is the one its header navigates to.
        /// Every reachable ShowTab key lives in exactly one door; the two ghosts are excluded
        /// ("patreon" redirects to the Settings door's Account section, "fyp" opens a window -
        /// both return before the switch). "progression" rides with Home (Dashboard redirect).
        ///
        /// The pinned Settings door is keyed "appsettings", NOT "settings": the tab key of that
        /// name is the dashboard (Home), and DoorSettings has carried Tag="appsettings" since
        /// Phase 1 - NavDoor_Click matches this door name against that Tag, so the two must stay
        /// identical. The door has a header and no entry list, so NavDoorParts hands back a null
        /// panel and the accordion simply has nothing to open for it.
        /// </summary>
        private static readonly (string Door, string DefaultTab, string[] Tabs)[] NavDoorMap =
        {
            ("home",      "settings",  new[] { "settings", "progression" }),
            ("studio",    "studio",    new[] { "studio", "presets", "haptics" }),
            ("companion", "companion", new[] { "companion", "bambitakeover", "shelistening", "awareness" }),
            // Phase 6: "play" replaced "lab" in place as this door's first entry and default
            // destination. "lab" is deliberately NOT listed - it is a legacy alias, resolved by
            // NavDoorForTab below so an old ShowTab("lab") still opens this door, and listing it
            // as a real entry would claim the rail has a row for it, which it does not.
            ("play",      "play",      new[] { "play", "deeper", "exclusives", "gradedintake", "lockdown",
                                               "blinktrainer", "remotecontrol", "availablesubjects" }),
            ("you",       "discord",   new[] { "discord", "quests", "achievements", "enhancements",
                                               "programs", "leaderboard" }),
            ("library",   "assets",    new[] { "assets" }),
            ("appsettings", "appsettings", new[] { "appsettings" }),
        };

        /// <summary>Row pitch of a rail entry: Height 30 + Margin 0,1 in the NavRailButton style.
        /// The accordion computes its open height from this instead of forcing a measure pass,
        /// so the two MUST stay in step.</summary>
        private const double NavEntryRowHeight = 32;

        private const int NavDoorExpandMs = 160;

        /// <summary>Which door is open. Home ships open, which is why DoorPanelHome is the one
        /// panel authored without an explicit Height.</summary>
        private string _expandedDoor = "home";

        private (Button? Header, Border? Panel, StackPanel? Entries) NavDoorParts(string door) => door switch
        {
            "home" => (DoorHome, DoorPanelHome, DoorEntriesHome),
            "studio" => (DoorStudio, DoorPanelStudio, DoorEntriesStudio),
            "companion" => (DoorCompanion, DoorPanelCompanion, DoorEntriesCompanion),
            "play" => (DoorPlay, DoorPanelPlay, DoorEntriesPlay),
            "you" => (DoorYou, DoorPanelYou, DoorEntriesYou),
            "library" => (DoorLibrary, DoorPanelLibrary, DoorEntriesLibrary),
            // Pinned, entry-less: a header to light, nothing to expand.
            "appsettings" => (DoorSettings, null, null),
            _ => (null, null, null),
        };

        private static string? NavDoorForTab(string? tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return null;
            // Legacy aliases, same idiom as ChromeFxNav.IndexOf: a key that no longer has a rail
            // row of its own still has to resolve to the door that swallowed it, or code-driven
            // navigation (tutorial spotlights, notifications, the Ctrl+K palette) lands with the
            // active indicator inside a door nobody opened.
            tabKey = CanonicalTabKey(tabKey!);
            foreach (var door in NavDoorMap)
                foreach (var t in door.Tabs)
                    if (string.Equals(t, tabKey, StringComparison.OrdinalIgnoreCase))
                        return door.Door;
            return null;
        }

        /// <summary>The door header that owns a tab key, for the active-door indicator.</summary>
        private Button? NavDoorHeaderForTab(string? tabKey)
        {
            var door = NavDoorForTab(tabKey);
            return door == null ? null : NavDoorParts(door).Header;
        }

        /// <summary>True when the door owning <paramref name="tabKey"/> is the open one, i.e. when
        /// that tab's entry row is actually painted rather than clipped to Height 0.</summary>
        private bool IsDoorExpandedForTab(string? tabKey)
        {
            var door = NavDoorForTab(tabKey);
            return door != null && string.Equals(door, _expandedDoor, StringComparison.Ordinal);
        }

        /// <summary>
        /// The rail element that visibly STANDS FOR <paramref name="tabKey"/> right now: its entry
        /// row when the owning door is open, the door header when the door is closed.
        ///
        /// A closed door keeps Visibility=Visible at Height 0 (see SetDoorPanelExpanded), so its
        /// entries still pass FireBurstAt's IsVisible/ActualSize guard and still map through
        /// TransformToVisual - but they all map onto the same zero-height strip at the top of the
        /// clipped panel, which paints as some unrelated rail row. Anything that draws AT a rail row
        /// (celebration bursts, first-launch pulses) must ask for this instead of naming an entry
        /// button directly, or it lands on the wrong row whenever that door happens to be shut.
        /// </summary>
        internal Button? NavAnchorForTab(string? tabKey)
        {
            try
            {
                var entry = NavButtonForTab(tabKey);
                var header = NavDoorHeaderForTab(tabKey);
                if (header == null) return entry;
                return IsDoorExpandedForTab(tabKey) ? (entry ?? header) : header;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("NavAnchorForTab({Tab}): {E}", tabKey, ex.Message);
                return null;
            }
        }

        /// <summary>Door headers currently carrying a first-visit attention pulse, keyed by the tab
        /// key that asked for it - so the matching Stop can release the animation's hold on Opacity
        /// and two announcements (Programs in "you", Deeper in "play") can run side by side.</summary>
        private readonly Dictionary<string, Button> _navHeaderPulses = new(StringComparer.Ordinal);

        /// <summary>
        /// First-visit attention pulse for a rail entry whose door is SHUT. The entry is clipped to
        /// Height 0 there, so the scale pulse it carries plays inside a zero-height ClipToBounds
        /// panel and nobody ever sees it - and the rail opens on Home, so that is every launch. The
        /// door header is always painted and is the row the user has to click first, so the
        /// announcement escalates one level instead of being lost.
        ///
        /// Opacity rather than scale: a door header stretches the full rail width, so growing it
        /// 1.12x would spill over the sidebar's edge onto the page.
        ///
        /// Returns false when the door is already open or has no header - the caller then runs its
        /// own entry-level pulse, which is visible in that case.
        /// </summary>
        private bool StartNavDoorHeaderPulse(string tabKey)
        {
            try
            {
                if (IsDoorExpandedForTab(tabKey)) return false;
                var header = NavDoorHeaderForTab(tabKey);
                if (header == null) return false;
                if (_navHeaderPulses.ContainsKey(tabKey)) return true;   // already announcing

                _navHeaderPulses[tabKey] = header;
                var anim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.35,
                    Duration = TimeSpan.FromMilliseconds(700),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(4),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                anim.Completed += (_, __) => StopNavDoorHeaderPulse(tabKey);
                header.BeginAnimation(UIElement.OpacityProperty, anim);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("StartNavDoorHeaderPulse({Tab}): {E}", tabKey, ex.Message);
                return false;
            }
        }

        /// <summary>Releases a door-header pulse. Passing null to BeginAnimation is what drops the
        /// animation's hold on Opacity - without it the last animated value sticks and the header
        /// stays half-faded for the rest of the session.</summary>
        private void StopNavDoorHeaderPulse(string tabKey)
        {
            try
            {
                if (!_navHeaderPulses.TryGetValue(tabKey, out var header)) return;
                _navHeaderPulses.Remove(tabKey);
                header.BeginAnimation(UIElement.OpacityProperty, null);
                header.Opacity = 1.0;
            }
            catch (Exception ex) { App.Logger?.Debug("StopNavDoorHeaderPulse({Tab}): {E}", tabKey, ex.Message); }
        }

        /// <summary>
        /// Opens the door that contains <paramref name="tabKey"/>'s entry, closing whichever
        /// door was open. Public surface for TutorialOverlay (a spotlight can only measure an
        /// entry once its door is open) and for the future Ctrl+K palette; ShowTab calls it on
        /// every navigation.
        /// </summary>
        internal void ExpandDoorForTab(string tabKey)
        {
            try
            {
                var door = NavDoorForTab(tabKey);
                if (door != null) SetExpandedDoor(door);
            }
            catch (Exception ex) { App.Logger?.Debug("ExpandDoorForTab({Tab}): {E}", tabKey, ex.Message); }
        }

        private void SetExpandedDoor(string door)
        {
            if (string.Equals(_expandedDoor, door, StringComparison.Ordinal)) return;
            var previous = _expandedDoor;
            _expandedDoor = door;

            bool animate = MotionFx.AllowTransitions;
            foreach (var d in NavDoorMap)
            {
                // Only the two doors that actually change state get touched; the rest are
                // already parked at Height 0 and re-animating them would be four idle clocks.
                if (!string.Equals(d.Door, door, StringComparison.Ordinal) &&
                    !string.Equals(d.Door, previous, StringComparison.Ordinal)) continue;

                var parts = NavDoorParts(d.Door);
                if (parts.Panel == null) continue;
                SetDoorPanelExpanded(d.Door, parts.Panel, parts.Entries,
                                     string.Equals(d.Door, door, StringComparison.Ordinal), animate);
            }
        }

        /// <summary>
        /// The accordion itself: a 160ms Height tween on the door's panel, nothing else. No
        /// loop, so there is nothing for the motion kill-switch to stop - at MotionLevel Off
        /// (AllowTransitions false) the panel simply snaps.
        ///
        /// A closed door keeps Visibility=Visible at Height 0 rather than collapsing, so an entry
        /// in a shut door still measures and still maps through TransformToVisual (a Collapsed
        /// element maps nowhere). It does NOT map anywhere USEFUL, though - every entry of a shut
        /// door lands on the same zero-height strip - so anything that draws at a rail row asks
        /// NavAnchorForTab for the row to use and gets the door header while the door is shut.
        /// </summary>
        private void SetDoorPanelExpanded(string door, Border panel, StackPanel? entries, bool expand, bool animate)
        {
            panel.IsHitTestVisible = expand;

            if (!animate)
            {
                panel.BeginAnimation(FrameworkElement.HeightProperty, null);
                panel.Height = expand ? double.NaN : 0;
                return;
            }

            double from = panel.ActualHeight;
            double to = expand ? MeasureDoorPanel(entries) : 0;
            if (Math.Abs(from - to) < 0.5)
            {
                panel.BeginAnimation(FrameworkElement.HeightProperty, null);
                panel.Height = expand ? double.NaN : 0;
                return;
            }

            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(NavDoorExpandMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            anim.Completed += (_, __) =>
            {
                try
                {
                    // A faster click already moved on: whoever owns the panel now finishes it.
                    bool stillOpen = string.Equals(_expandedDoor, door, StringComparison.Ordinal);
                    if (stillOpen != expand) return;
                    panel.BeginAnimation(FrameworkElement.HeightProperty, null);
                    // Hand an open panel back to layout so a later Visibility change on one of
                    // its entries (BtnDeeper follows EnableDeeper) still resizes the door.
                    panel.Height = expand ? double.NaN : 0;
                }
                catch (Exception ex) { App.Logger?.Debug("Door tween completion: {E}", ex.Message); }
            };
            panel.BeginAnimation(FrameworkElement.HeightProperty, anim);
        }

        private static double MeasureDoorPanel(StackPanel? entries)
        {
            if (entries == null) return 0;
            double h = 0;
            foreach (var child in entries.Children.OfType<FrameworkElement>())
                if (child.Visibility == Visibility.Visible) h += NavEntryRowHeight;
            return h;
        }

        /// <summary>A door header navigates to its default tab; ShowTab then expands it.</summary>
        private void NavDoor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string door) return;
            foreach (var d in NavDoorMap)
            {
                if (!string.Equals(d.Door, door, StringComparison.Ordinal)) continue;
                ShowTab(d.DefaultTab);
                return;
            }
            // Every door in the rail is in NavDoorMap, Settings included since Phase 2. A Tag
            // that matches nothing is an authoring mistake, not a navigation - say so and stay
            // put rather than teleporting the user to the Dashboard.
            App.Logger?.Warning("NavDoor_Click: no NavDoorMap entry for door {Door}", door);
        }

        /// <summary>
        /// Phase 4: the Haptics page is a module of the Studio rack rather than a top-level tab,
        /// so the x:Name MainWindow.xaml used to declare is a passthrough now.
        ///
        /// This one property is why the move cost nothing: all ~71 <c>HapticsTab.&lt;x:Name&gt;</c>
        /// dereferences across MainWindow.Haptics.cs, .Patreon.cs, .PremiumRail.cs, .Presets.cs,
        /// .Remember.cs, .SessionFeatureLock.cs, .TabFxTakeoverLabStatus.cs and .xaml.cs (incl.
        /// both <c>features/vibe.png</c> repaint rows and the IsVisibleChanged live-status hook)
        /// resolve through it unchanged. Never rename it.
        /// </summary>
        /// <remarks>Null-conditional on StudioTab so the several <c>if (HapticsTab != null)</c>
        /// guards that already exist keep meaning something if this is ever read before
        /// InitializeComponent has connected the rack.</remarks>
        internal Views.Tabs.HapticsTabView HapticsTab => StudioTab?.HapticsPanel!;

        // Direct entries for the tabs that used to be reachable only through the Exclusives
        // shelf. Awareness is deliberately absent: BtnNavAwareness binds the existing
        // BtnAwareness_Click (MainWindow.AccountShell.cs), which was an orphan until now.
        private void BtnNavStudio_Click(object sender, RoutedEventArgs e) => ShowTab("studio");

        private void BtnNavHaptics_Click(object sender, RoutedEventArgs e) => ShowTab("haptics");

        /// <summary>
        /// The Play door's first rail entry (the button still x:Named BtnLab — that name is API for
        /// NavButtonForTab, the NavButtons list and TutorialService.NavEntryDoorKeys, all of which
        /// are keyed by the x:Names the nav has always used). Phase 6: it navigates to the card
        /// wall's own key. The old <c>BtnLab_Click</c> — a bare <c>ShowTab("lab")</c> — was deleted
        /// with the Lab view; the alias it relied on lives on in the <c>case "lab"</c> label.
        /// </summary>
        private void BtnNavPlay_Click(object sender, RoutedEventArgs e) => ShowTab("play");

        private void BtnNavBambiTakeover_Click(object sender, RoutedEventArgs e) => ShowTab("bambitakeover");

        private void BtnNavSheListening_Click(object sender, RoutedEventArgs e) => ShowTab("shelistening");

        private void BtnNavGradedIntake_Click(object sender, RoutedEventArgs e) => ShowTab("gradedintake");

        private void BtnNavLockdown_Click(object sender, RoutedEventArgs e) => ShowTab("lockdown");

        private void BtnNavBlinkTrainer_Click(object sender, RoutedEventArgs e) => ShowTab("blinktrainer");

        private void BtnNavRemoteControl_Click(object sender, RoutedEventArgs e) => ShowTab("remotecontrol");

        /// <summary>
        /// Phase 7 · the Library door's Media Log row. The only one of that door's four new rows
        /// that needed a handler at all: Mods, Catalogue and Phrase Manager each bind the exact
        /// existing launcher (<c>BtnManageMods_Click</c>, <c>BtnCatalogue_Click</c>,
        /// <c>BtnManagePhrases_Click</c>) straight from XAML.
        ///
        /// <para>This one re-fires the Assets tab's own <c>BtnMediaLog</c> instead of newing a
        /// second <see cref="MediaHistoryWindow"/>, because that button's Click has a SECOND
        /// subscriber: <c>MediaLogButton_Clicked</c> in MainWindow.AssetsFx.cs, which banks
        /// <c>_mediaLogSeenCount</c> so the three-beat "new media since you last looked" pulse goes
        /// quiet once the log has been read. A parallel launcher would open the same window and
        /// leave that badge armed - the failure being a nag nobody can dismiss, from the one entry
        /// point that never touches the Assets tab.</para>
        ///
        /// <para><see cref="InitializeAssetsFx"/> first because that subscription is wired lazily,
        /// on the first show of the Assets tab, and this row is reachable by someone who has never
        /// opened it. The call is idempotent (<c>_assetsFxInitialized</c>).</para>
        ///
        /// <para>Deliberately no <c>ShowTab("assets")</c>: the Media Log is a window, and it is
        /// worth having from wherever you are. Navigating first would also fire
        /// <c>PulseMediaLogIfUnseen</c> one beat before the click that spends it.</para>
        /// </summary>
        private void BtnNavMediaLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                InitializeAssetsFx();
                AssetsTab?.BtnMediaLog?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BtnNavMediaLog_Click failed"); }
        }

        /// <summary>
        /// One-shot explainer cards for tabs whose purpose isn't obvious from their controls
        /// (see FeatureIntros for the roster). Suppressed while a session is running - a modal
        /// must never land on top of live conditioning. FeatureIntroPopup itself guards the
        /// guided tour (which navigates tabs through ShowTab) and paces cards so a user
        /// clicking through every tab doesn't eat a modal per click.
        /// <para>Phase 8: the owning door is handed over so a door can produce at most one card
        /// per launch. Two doors own two cards each (Companion: awareness + she's listening; Play:
        /// lockdown + blink trainer), and walking into a door should never mean two modals - the
        /// sibling is left unspent and introduces itself on a later visit.</para>
        /// </summary>
        private void MaybeShowFeatureIntro(string key)
        {
            try
            {
                if (_sessionEngine?.IsRunning == true) return;
                FeatureIntroPopup.ShowIfFirstTime(key, this, NavDoorForTab(key));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Feature intro hook failed for {Key}", key);
            }
        }

        /// <summary>
        /// Per-tab refresh hook for the Blink Trainer page. Called on every
        /// transition into the tab. Phase C: syncs all control state from
        /// settings + webcam status. Phase D will add live-mode detection
        /// (consent + folders + active session) and skip the demo when live
        /// mode takes over.
        /// </summary>
        private void RefreshBlinkTrainerTab()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s != null)
                {
                    // IncludeVideos toggle — set before rebuilding cards so count
                    // summaries use the current mode.
                    if (BlinkTrainerTab.ToggleBlinkTrainerIncludeVideos != null)
                        BlinkTrainerTab.ToggleBlinkTrainerIncludeVideos.IsChecked = s.BlinkTrainerIncludeVideos;

                    // Duration
                    if (BlinkTrainerTab.SliderBlinkTrainerDurationNew != null)
                        BlinkTrainerTab.SliderBlinkTrainerDurationNew.Value = s.BlinkTrainerDurationMinutes;
                    if (BlinkTrainerTab.TxtBlinkTrainerDurationValue != null)
                        BlinkTrainerTab.TxtBlinkTrainerDurationValue.Text = $"{s.BlinkTrainerDurationMinutes} min";

                    // Opacity
                    if (BlinkTrainerTab.SliderBlinkTrainerOpacityNew != null)
                        BlinkTrainerTab.SliderBlinkTrainerOpacityNew.Value = s.BlinkTrainerOpacity;
                    if (BlinkTrainerTab.TxtBlinkTrainerOpacityValue != null)
                        BlinkTrainerTab.TxtBlinkTrainerOpacityValue.Text = $"{s.BlinkTrainerOpacity}%";

                    // Mix-mode selection visual
                    SetMixModeSelection(s.BlinkTrainerMixImages);
                }

                RebuildBlinkTrainerFolderCards();
                RefreshBlinkTrainerWebcamColumn();
                // Phase 2: this tab no longer carries a camera picker, a monitor picker or a
                // restrict-gaze checkbox (Settings → Devices owns all three), so there is nothing
                // to seed here. The read-only webcam chip is painted by UpdateWebcamStatusChips
                // off the tracker-state event, exactly like the title-bar privacy pill.
                RefreshBlinkTrainerGate();
                RefreshBlinkTrainerTrackerButton();

                // Phase D: status row + stage mode are now state-machine driven.
                // RefreshBlinkTrainerStatusRow paints the dot/text/action button;
                // ApplyBlinkTrainerStageMode handles demo-vs-live transitions.
                // ApplyBlinkTrainerStageMode also calls StartBlinkTrainerDemoLoop
                // when it decides demo mode is appropriate.
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());

                // ApplyBlinkTrainerStageMode is a no-op when the mode hasn't
                // changed (e.g. second tab visit while already in Demo). Cover
                // the initial-show case where there's nothing to transition
                // FROM by ensuring the demo loop is running if we're in Demo.
                if (_currentBlinkTrainerStageMode == BlinkTrainerStageMode.Demo)
                    StartBlinkTrainerDemoLoop();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshBlinkTrainerTab failed");
            }
        }

        #endregion
    }
}
