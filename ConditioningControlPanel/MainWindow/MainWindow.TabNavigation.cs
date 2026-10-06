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

        // The rail button carries nothing of its own any more: spending HasSeenProgramsTab and
        // opening the first-run explainer both moved into ShowTab's "programs" arm, because this was
        // never the only way in. The Dashboard's Today card and the session-end toast both call
        // ShowTab("programs") directly and used to bypass both - so the pulse kept announcing a tab
        // the user had already been using, and the explainer was skipped for exactly the people who
        // arrived without clicking the rail.
        private void BtnPrograms_Click(object sender, RoutedEventArgs e) => ShowTab("programs");

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

        // ============================== lane tab registry ==============================
        // Nav rework 2026-10-06 (BRIEF contract 1). New section pages (Friends, Leash,
        // Personality, Permissions, Links, Folders...) are not XAML children of MainWindow:
        // each lane registers a host here from its own partial file and ShowTab falls through
        // to it when the switch has no case. The view is created on its first show, into the
        // LaneTabHost cell (same cell as every other view, inside the page AdornerDecorator).

        /// <summary>One lane-owned page. <paramref name="Create"/> runs once, on the first ShowTab(key).</summary>
        internal sealed record NavTabHost(string Key, Func<FrameworkElement> Create,
            Action<FrameworkElement>? OnShown = null, Action<FrameworkElement>? OnHidden = null);

        private readonly Dictionary<string, NavTabHost> _navTabHosts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _navTabViews = new(StringComparer.OrdinalIgnoreCase);
        private string? _shownLaneTab;

        /// <summary>Register a lane page. A second registration of the same key replaces the
        /// first only while the view has not been created yet.</summary>
        internal void RegisterNavTab(NavTabHost host)
        {
            if (host == null || string.IsNullOrWhiteSpace(host.Key)) return;
            var key = host.Key.ToLowerInvariant();
            if (_navTabViews.ContainsKey(key))
            {
                App.Logger?.Warning("RegisterNavTab({Key}) ignored: the view already exists", key);
                return;
            }
            _navTabHosts[key] = host with { Key = key };
        }

        /// <summary>True when a lane registered this key.</summary>
        internal bool IsRegisteredNavTab(string key) => _navTabHosts.ContainsKey(key ?? string.Empty);

        // Implemented by the SOCIAL / COMPANION / REHOME lanes in their own partial files.
        partial void RegisterSocialTabs();
        partial void RegisterCompanionTabs();
        partial void RegisterRehomeTabs();

        private bool _laneTabsRegistered;

        /// <summary>Called once from the constructor, after InitializeComponent.</summary>
        private void RegisterLaneNavTabs()
        {
            if (_laneTabsRegistered) return;
            _laneTabsRegistered = true;
            try { RegisterSocialTabs(); } catch (Exception ex) { App.Logger?.Warning(ex, "RegisterSocialTabs failed"); }
            try { RegisterCompanionTabs(); } catch (Exception ex) { App.Logger?.Warning(ex, "RegisterCompanionTabs failed"); }
            try { RegisterRehomeTabs(); } catch (Exception ex) { App.Logger?.Warning(ex, "RegisterRehomeTabs failed"); }
        }

        /// <summary>Collapse the shown lane page (part of ShowTab's collapse-all).</summary>
        private void HideLaneTabs()
        {
            foreach (var view in _navTabViews.Values) view.Visibility = Visibility.Collapsed;
            if (_shownLaneTab != null && _navTabHosts.TryGetValue(_shownLaneTab, out var host)
                && _navTabViews.TryGetValue(_shownLaneTab, out var shown))
            {
                try { host.OnHidden?.Invoke(shown); }
                catch (Exception ex) { App.Logger?.Warning(ex, "NavTab {Key} OnHidden failed", _shownLaneTab); }
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
                    App.Logger?.Error(ex, "NavTab {Key} failed to build", key);
                    return false;
                }
                if (view == null) return false;
                if (view.Margin == default) view.Margin = new Thickness(10, 5, 10, 10);
                _navTabViews[key] = view;
                LaneTabHost.Children.Add(view);
            }
            view.Visibility = Visibility.Visible;
            _shownLaneTab = key;
            AnimateTabIn(view);
            try { host.OnShown?.Invoke(view); }
            catch (Exception ex) { App.Logger?.Warning(ex, "NavTab {Key} OnShown failed", key); }
            return true;
        }

        /// <summary>
        /// Every key the ShowTab switch answers itself (its case labels). ShowTab refuses a key
        /// that is neither here nor registered BEFORE it collapses anything, so an unknown key
        /// logs and leaves the page on screen. Pinned against the switch by SectionTabStripTests.
        /// </summary>
        internal static readonly HashSet<string> BuiltInTabKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "settings", "presets", "progression", "quests", "programs", "enhancements", "deeper",
            "achievements", "companion", "lab", "play", "playsessions", "playeyes", "leaderboard",
            "assets", "discord", "awareness", "remotecontrol", "availablesubjects", "bambitakeover",
            "studio", "ramp", "haptics", "lockdown", "blinktrainer", "shelistening", "gradedintake",
            "appsettings", "spiral", "chaster", "folders",
        };

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

            // Nav rework: old keys with a new home ("exclusives" -> Settings > Account & Plans,
            // "together" -> Social > Lobby) land there and say "Moved" the first three times.
            if (TryRedirectMovedTab(tab)) return;

            // The dashboard's RECENT rail. At the door, before the three intercepts below, so a
            // window key (fyp, justdrop) counts as an open like any tab; the rule itself skips
            // the dashboard, its aliases, "patreon" and any row the server has withheld.
            NoteDestinationOpened(tab);

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

            // "justdrop" is a WINDOW too, exactly like "fyp" above - it stopped being a tab when
            // the shop moved into its own ChaosWebViewHost (Services/JustDrop/JustDropHostService).
            // The key survives as a launcher because half the app already speaks it: the dashboard
            // tease tile, the Ctrl+K palette row and any bark rule all route through ShowTab, and
            // giving them each a different entry point would be four ways to open one shop.
            //
            // The withheld refusal stays HERE, at the one door every caller comes through, and is
            // still a no-op rather than a redirect: the user asked for a page that does not exist
            // for them, and moving them somewhere else would be a teleport they did not ask for.
            // Deliberately before the bark hook, so a door nobody can see never announces itself.
            if (tab == "justdrop")
            {
                if (!Services.JustDrop.JustDropService.DoorAvailable)
                {
                    App.Logger?.Debug("ShowTab(justdrop) ignored - the door is not available on this account");
                    return;
                }
                Services.JustDrop.JustDropHostService.LaunchShop();
                return;
            }

            // Unknown key: log and stay. Checked before anything is collapsed, so a typo from a
            // deep link, a tutorial step or a third-party .ccpmod never leaves a blank page.
            if (!BuiltInTabKeys.Contains(tab) && !_navTabHosts.ContainsKey(tab))
            {
                App.Logger?.Warning("ShowTab({Tab}) ignored: no such tab", tab);
                return;
            }

            // Mouse back / forward (MainWindow.TabHistory.cs). After the three window keys above,
            // which never change the tab on screen.
            NoteTabHistory(tab);

            // Bark hook: announce navigation (gated/chanced in the rules so it isn't spammy).
            // Routed through BarkTabAliases so renamed tabs keep answering to their old bark key.
            try
            {
                App.Bark?.NotifyTabNavigated(BarkTabAliases.TryGetValue(tab, out var barkTab) ? barkTab : tab);
            }
            catch { }

            // EMI Desk: her ring ranks doors by decayed opens, and an open is an open however it
            // was reached. The three keys intercepted above (patreon, fyp, justdrop) are counted
            // at their own launchers instead, so no door is counted twice.
            try { Services.EmiDesk.EmiTargets.NoteTabOpened(tab); } catch { }

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
            LeaveLobbyTab();
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
            // Collapsing the Spiral Room is what tears its WebView2 down: the view watches
            // IsVisibleChanged (Loaded fires once) and disposes the embed on the way out, so
            // leaving the tab leaves no idle Chromium behind it.
            if (SpiralTab != null) SpiralTab.Visibility = Visibility.Collapsed;
            if (ChasterTab != null) ChasterTab.Visibility = Visibility.Collapsed;
            if (AppSettingsTab != null) AppSettingsTab.Visibility = Visibility.Collapsed;
            // Lane-registered pages (the registry above).
            HideLaneTabs();

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
                    RefreshDashboardRail(); // rail + price tags from live state on every show
                    // Training Programs own the day's feature mix. Re-derived (never latched) on
                    // every show of the Dashboard, so arriving here can never find a stale lock -
                    // not after a crash, an abort, or a session event that fired out of order.
                    RefreshSessionFeatureLock();
                    // Weekly intake pass: paint the centre tile, and play the once-a-week flip
                    // ceremony if this week's reveal hasn't run yet. Must be AFTER the tab is made
                    // visible - the spin is skipped for an off-screen tile so a background login
                    // callback can't burn the reveal on a control nobody is looking at.
                    RefreshIntakePassTile();
                    // v6.8.0 door tour: the ? box. NOT the only trigger, and it cannot be - the
                    // Dashboard is the tab the app LANDS on, painted straight from XAML with no
                    // ShowTab behind it, so a first-launch user would never reach this line.
                    // OnDashboardTabVisibilityChanged covers that case (and defers past the
                    // startup dialogs); this call is what makes the card immediate for someone
                    // who walks back to Home later in the launch. Double-firing is free: the
                    // seen-flag and the _opening latch inside FeatureIntroPopup make the second
                    // attempt a no-op.
                    MaybeShowFeatureIntro("daily-free", "settings");
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
                    RefreshDashboardRail();
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

                    // Here rather than in BtnPrograms_Click: the Dashboard's Today card and the
                    // session-end toast both arrive through ShowTab, and both used to skip the
                    // explainer entirely while leaving the rail still pulsing at a tab the user was
                    // already looking at. The pulse is spent the moment the tab is reached by ANY
                    // route, whether or not the explainer itself shows.
                    if (App.Settings?.Current is { } programsSettings && !programsSettings.HasSeenProgramsTab)
                    {
                        programsSettings.HasSeenProgramsTab = true;
                        StopProgramsTabPulse();
                        App.Settings?.Save();
                    }

                    // Last, and deliberately after the tab is up: the explainer opens on top of the
                    // tab the user just landed on, so dismissing it leaves them looking at the thing
                    // it described. Its own seen-flag and _opening latch make repeat calls no-ops.
                    ProgramsIntroPopup.ShowIfFirstTime(this);
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
                        // The hub's lazy init is what unblocks ApplyDeeperFilterAndSort, so it has to
                        // run on EVERY door into this tab, not only the rail entry: the Play card, the
                        // Ctrl+K row and the Settings jumps are all bare ShowTab calls, and before this
                        // they landed on a scanned-but-never-projected list (pills reading 0, no rows,
                        // no empty state). Idempotent, and it does the first scan itself, so only a
                        // later show pays for a second one.
                        if (!InitializeDeeperHub()) RefreshDeeperLibraryUI();
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
                // Nav rework zone pills: Sessions and Eyes are places on the Play wall, not pages
                // of their own. Same body as "play", then a scroll to the zone after layout.
                case "playsessions":
                case "playeyes":
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
                    // v6.8.0 door tour. Fires for the "lab" alias too, on purpose: someone who
                    // deep-links to the old key is precisely the person who needs to be told the
                    // Lab page became this wall. Shares the Play door's one-card-per-launch
                    // budget with the lockdown and blink-trainer cards.
                    MaybeShowFeatureIntro("play-wall", "play");
                    {
                        // The Games pill scrolls back to the top only when the previous pill was
                        // another Play zone; a plain return to Play keeps its scroll.
                        string? zone = tab switch
                        {
                            "playsessions" => "sessions",
                            "playeyes" => "eyes",
                            _ => _navCurrentTab is "playsessions" or "playeyes" ? "games" : null,
                        };
                        if (zone != null)
                            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ScrollPlayZone(zone)));
                    }
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
                case "folders":
                    AssetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AssetsTab);
                    RefreshAssetTree();
                    InitializeAssetPresets();
                    if (PacksSectionEnabled) _ = RefreshPacksAsync();
                    // Library > Folders is a zone of the Assets page (the folder chip in its header
                    // row): same page, then scroll + glow the chip once layout has settled.
                    if (string.Equals(tab, "folders", StringComparison.OrdinalIgnoreCase))
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => AssetsTab.ScrollToZone("folders")));
                    break;

                case "discord":
                    DiscordTab.Visibility = Visibility.Visible;
                    AnimateTabIn(DiscordTab);
                    UpdateDiscordTabUI();
                    // v6.8.0 door tour. The card is about the HEADER bubble, not this tab - but
                    // this tab is where clicking the bubble lands you, so it is the one place the
                    // explainer can arrive without ambushing somebody mid-anything. The vat's
                    // card is the You door's other one (MainWindow.ProfileVat.cs) and the two
                    // share the door's single per-launch slot.
                    MaybeShowFeatureIntro("profile-hub", "discord");
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
                    EnterLobbyTab();
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
                    // v6.8.0 door tour: the sixth card Phase 4 said it was not going to write.
                    // The rack REPLACED the dashboard's per-feature popups, so first-timers meet
                    // a list of rows where they last saw a modal - exactly the case an explainer
                    // exists for. Shares the Studio door's one-card-per-launch budget with the
                    // "haptics" card below; whichever fires first, the other waits for a later
                    // launch.
                    MaybeShowFeatureIntro("studio-rack", "studio");
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

                // Nav rework zone pill "Scheduler & Ramp": the Studio rack's scheduler module.
                case "ramp":
                    StudioTab.Visibility = Visibility.Visible;
                    AnimateTabIn(StudioTab);
                    StudioTab.FocusRackEntry("scheduler");
                    RefreshSessionFeatureLock();
                    UpdatePatreonUI();
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

                // THE SPIRAL ROOM (CONTRACT-FUSE-0816 §2.4). Reachable at any time and from five
                // doors - the rail row, the fuse chip, the Trainer Card plate, the account menu row
                // and the first-light reveal - so the view re-reads every gate on the way in rather
                // than trusting whatever it last painted. The old window's "return without opening"
                // refusal is now "show the appropriate state": withheld or fog era => the fog, no
                // block => the waiting room, otherwise the canvas.
                case "spiral":
                    if (SpiralTab != null)
                    {
                        SpiralTab.Visibility = Visibility.Visible;
                        AnimateTabIn(SpiralTab);
                        SpiralTab.OnTabShown();

                        // THE EXPLAINER, and OnTabShown above is what earns the right to ask.
                        // That call re-reads every gate and paints the room, so IsShowingSpiral
                        // is the room's own answer to "is there a map on screen" rather than a
                        // second, drifting copy of the gate arithmetic here. A user in the fog
                        // era or mid-reveal leaves the card unspent and gets it on the visit
                        // where it would actually be describing something they can see - the
                        // same "explain it where it exists" rule descent-vat follows on the
                        // Trainer Card.
                        if (SpiralTab.IsShowingSpiral) MaybeShowFeatureIntro("descent-spiral", "spiral");
                    }
                    break;

                // CIRCE'S TAB. Its door is the padlock chip in the rail's pinned cluster, which is
                // not a door medallion, so the key has no NavDoorMap row: nothing expands and no
                // "you are here" ring lights, the same as the spiral medallion it replaced.
                case "chaster":
                    if (ChasterTab != null)
                    {
                        ChasterTab.Visibility = Visibility.Visible;
                        AnimateTabIn(ChasterTab);
                        ChasterTab.OnTabShown();
                    }
                    break;

                // "exclusives" has no arm: the Velvet Vault retired into Settings > Account & Plans
                // and TryRedirectMovedTab lands the old key there before this switch.

                // Lane-registered pages (RegisterNavTab). The door check at the top already
                // refused keys nobody registered.
                default:
                    if (!ShowLaneTab(tab))
                        App.Logger?.Warning("ShowTab({Tab}): the registered page could not be shown", tab);
                    break;
            }

            // Nav rework: the strip, the breadcrumb, the window title and last-tab memory.
            _navCurrentTab = tab;
            SyncSectionChrome(tab);

            // Reveal the entry we just navigated to. Code-driven navigation (tutorial steps,
            // Exclusives cards, notifications) has to open the owning door too, or the active
            // indicator lands inside a collapsed panel where nobody can see it.
            ExpandDoorForTab(tab);

            // Chrome FX: the section row that owns this tab stays lit. Last, so it runs whatever
            // the switch above did - and it never throws.
            ApplyNavActiveGlow(tab);
        }

        // ============================== nav rail: sections ==============================
        // Nav rework (2026-10-06): the six doors + accordion are gone; the rail is seven labelled
        // sections + the Settings gear (MainWindow.NavRail.cs), and Services.UI.NavSections is the
        // one table. These helpers keep the names other files call (ChromeFx, EventFx, BankFx,
        // the first-visit pulses, the tutorial overlay, feature intros), answering with the
        // SECTION ROW now. A row's Tag is its section key; the gear's is "appsettings".

        /// <summary>Where the web app (and every other web nudge) points. The dashboard root,
        /// not the link-device page: sign-in and device linking are both discoverable from there.</summary>
        internal const string WebAppUrl = "https://app.cclabs.app";

        /// <summary>Where a public profile is created, edited, rotated and switched off. Web-only
        /// on purpose: no slug ever renders in the desktop app.</summary>
        internal const string ProfileSharingUrl = WebAppUrl + "/dashboard/profile-sharing";

        /// <summary>The rail row Tag that owns a tab key ("appsettings" for Settings), or null.
        /// Legacy aliases resolve through CanonicalTabKey first. Feature intros use this as their
        /// per-door budget key, so it stays a string.</summary>
        private static string? NavDoorForTab(string? tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return null;
            var section = Services.UI.NavSections.SectionForTab(CanonicalTabKey(tabKey!));
            return section == null ? null : Controls.NavRail.NavRailRules.DoorTagForSection(section);
        }

        /// <summary>The rail row that owns a tab key (null for a key no section owns).</summary>
        private Button? NavDoorHeaderForTab(string? tabKey)
        {
            var door = NavDoorForTab(tabKey);
            if (door == null) return null;
            foreach (var btn in NavSectionButtons)
                if (btn.Tag is string tag && string.Equals(tag, door, StringComparison.Ordinal)) return btn;
            return null;
        }

        /// <summary>
        /// Where a burst or a pulse aimed at a tab should land on the rail: its section row (the
        /// rows that tabs used to have are pills on the page now). Never throws.
        /// </summary>
        internal Button? NavAnchorForTab(string? tabKey)
        {
            try { return NavDoorHeaderForTab(tabKey); }
            catch (Exception ex)
            {
                App.Logger?.Debug("NavAnchorForTab({Tab}): {E}", tabKey, ex.Message);
                return null;
            }
        }

        private readonly Dictionary<string, Button> _navHeaderPulses = new(StringComparer.Ordinal);

        /// <summary>
        /// The Deeper / Programs first-visit announcement, on the owning section row. Returns
        /// false when that section is already the lit one (the caller then pulses its own page
        /// control), true when the row took the announcement or the quiet window swallowed it.
        /// Four soft opacity dips, then done; Motion Off shows nothing and still answers true.
        /// </summary>
        private bool StartNavDoorHeaderPulse(string tabKey)
        {
            try
            {
                if (App.StartupLadder?.IsQuiet == true) return true;

                var header = NavDoorHeaderForTab(tabKey);
                if (header == null) return false;
                if (string.Equals(NavDoorForTab(_activeTabKey), NavDoorForTab(tabKey), StringComparison.Ordinal))
                    return false;
                if (_navHeaderPulses.ContainsKey(tabKey)) return true;
                if (!MotionFx.AllowTransitions) return true;

                _navHeaderPulses[tabKey] = header;
                var anim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.35,
                    Duration = TimeSpan.FromMilliseconds(MotionFx.Level == MotionLevel.Reduced ? 350 : 700),
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
        /// Called by ShowTab on every navigation (and by the tutorial overlay): lights the section
        /// row that owns the tab. The name is kept for its callers; nothing expands any more.
        /// </summary>
        internal void ExpandDoorForTab(string tabKey)
        {
            try { RefreshSectionRail(tabKey); }
            catch (Exception ex) { App.Logger?.Debug("ExpandDoorForTab({Tab}): {E}", tabKey, ex.Message); }
        }

        /// <summary>
        /// Opens the CC Labs web app in the default browser through BrowserLauncher (the
        /// 4-strategy opener with the clipboard fallback) and retires the One Account banner beat.
        /// The rail door is gone; Play > Games carries the tile that calls this.
        /// </summary>
        internal void OpenWebAppFromNav()
        {
            try
            {
                Helpers.BrowserLauncher.OpenUrlOrPrompt(WebAppUrl, "open the CC Labs web app");
                RetireWebBannerBeat();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "OpenWebAppFromNav failed"); }
        }

        private void DoorWebApp_Click(object sender, RoutedEventArgs e) => OpenWebAppFromNav();

        /// <summary>
        /// Phase 4: the Haptics page is a module of the Studio rack rather than a top-level tab,
        /// so the x:Name MainWindow.xaml used to declare is a passthrough now. ~71
        /// <c>HapticsTab.&lt;x:Name&gt;</c> dereferences resolve through it. Never rename it.
        /// </summary>
        internal Views.Tabs.HapticsTabView HapticsTab => StudioTab?.HapticsPanel!;

        /// <summary>
        /// Library > Media Log. Re-fires the Assets tab's own <c>BtnMediaLog</c> instead of newing
        /// a second <see cref="MediaHistoryWindow"/>, because that button's Click has a second
        /// subscriber (<c>MediaLogButton_Clicked</c>, MainWindow.AssetsFx.cs) that banks the
        /// "new media since you last looked" count. <see cref="InitializeAssetsFx"/> first: that
        /// subscription is wired lazily. Kept for the Library strip's Media Log launcher pill.
        /// </summary>
        internal void BtnNavMediaLog_Click(object sender, RoutedEventArgs e)
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
        /// <param name="doorTab">
        /// The TAB whose door owns this card, when the card's key is not itself a tab key. The
        /// five v6.8.0 cards are named after surfaces rather than tabs ("studio-rack" is a rack,
        /// "profile-hub" is a header bubble), and NavDoorForTab returns null for a key it cannot
        /// find - which would silently opt those cards out of the per-door budget and let one
        /// door hand out two modals in a launch. Pass the tab; the door is derived from it.
        /// </param>
        private void MaybeShowFeatureIntro(string key, string? doorTab = null)
        {
            if (Services.FirstShow.FirstShowService.IsActive) return;
            try
            {
                // Through the presenter, like the dashboard's own card: inside the quiet window
                // (first ten minutes, a tour, a session, a modal up) the card becomes an Inbox row
                // instead of a modal explainer on every tab a new user clicks through. The
                // session check that used to live here is one of the presenter's quiet inputs.
                FeatureIntroPopup.ShowWhenStartupSettles(key, this, NavDoorForTab(doorTab ?? key));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Feature intro hook failed for {Key}", key);
            }
        }

        /// <summary>Latched once the Dashboard's card has been queued, so walking back to Home
        /// arms one settle clock per launch rather than one per visit.</summary>
        private bool _dashboardIntroQueued;

        /// <summary>
        /// Called from SettingsTabView's IsVisibleChanged - the Dashboard's own file, the same
        /// seam DiscordTabView uses for the Profile tab. It exists because Home is the ONE tab
        /// nothing navigates to: the view ships Visible in MainWindow.xaml and the app lands on
        /// it, so <c>case "settings"</c> in ShowTab never runs on a first launch and the ? box's
        /// explainer would never be seen by the people it was written for.
        ///
        /// <para>The card is QUEUED here, not shown: this fires while the startup ladder is still
        /// running (update dialog, What's New, season recap, first-run wizard, guided tour), and
        /// FeatureIntroPopup.ShowWhenStartupSettles is what waits all of that out before opening
        /// anything. Suppression there is never fatal - the seen-flag stays unspent and the next
        /// launch tries again.</para>
        /// </summary>
        internal void OnDashboardTabVisibilityChanged(bool visible)
        {
            try
            {
                // The browser fold's backstop: re-derive the card, the rows, the chevron and the
                // billboard from the saved bool every time the dashboard comes on screen, so a
                // surface that a killed animation left behind cannot outlive one tab switch.
                if (visible) ResettleBrowserFold();

                if (!visible || _dashboardIntroQueued) return;
                // A session running at this point means the window was re-shown mid-session, not
                // a launch. Leave the queue unarmed so a later, quieter visit gets the card.
                if (_sessionEngine?.IsRunning == true) return;
                _dashboardIntroQueued = true;
                FeatureIntroPopup.ShowWhenStartupSettles("daily-free", this, NavDoorForTab("settings"));
                // v6.8.0 One Account. Same settle path, same owning door, queued second: the
                // Home door's one-card-per-launch budget means daily-free introduces itself on
                // the first quiet launch and this card takes the NEXT one - a deliberate drip,
                // not a pile-up. Fresh installs get it too, which matters: they never see
                // What's New, so this card is their first mention of the web at all.
                FeatureIntroPopup.ShowWhenStartupSettles("one-account", this, NavDoorForTab("settings"));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Dashboard intro hook failed");
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
