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

        // BtnProgression handler removed in velvet-mosaic phase 6 — the Progression
        // tab no longer has a header button; its features live on the Dashboard now.

        private void BtnQuests_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("quests");
        }

        private void BtnPrograms_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("programs");
        }

        private void BtnEnhancements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("enhancements");
        }

        private void AnimateTabIn(UIElement tab)
        {
            try
            {
                tab.Opacity = 0;
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                tab.BeginAnimation(OpacityProperty, anim);
            }
            catch
            {
                tab.Opacity = 1;
            }
        }

        internal void ShowTab(string tab)
        {
            // Legacy redirect: the "patreon" tab was eliminated and its
            // account/data content lives in the dashboard's App Info popup now.
            // Route any legacy callers there WITHOUT disturbing the currently
            // active tab (opening a popup is overlay-style, not a tab switch).
            if (tab == "patreon")
            {
                ShowAppInfoPopup();
                return;
            }

            // Bark hook: announce navigation (gated/chanced in the rules so it isn't spammy).
            try { App.Bark?.NotifyTabNavigated(tab); } catch { }

            // Stop animations on tabs we're leaving to reduce idle CPU
            StopSeasonTitleShimmer();
            StopLockdownPulse();
            StopSkillTreeAnimations();

            // Hide all tabs
            SettingsTab.Visibility = Visibility.Collapsed;
            PresetsTab.Visibility = Visibility.Collapsed;
            ProgressionTab.Visibility = Visibility.Collapsed;
            QuestsTab.Visibility = Visibility.Collapsed;
            AchievementsTab.Visibility = Visibility.Collapsed;
            CompanionTab.Visibility = Visibility.Collapsed;
            PatreonTab.Visibility = Visibility.Collapsed;
            LeaderboardTab.Visibility = Visibility.Collapsed;
            AssetsTab.Visibility = Visibility.Collapsed;
            DiscordTab.Visibility = Visibility.Collapsed;
            EnhancementsTab.Visibility = Visibility.Collapsed;
            if (DeeperTab != null) DeeperTab.Visibility = Visibility.Collapsed;
            LabTab.Visibility = Visibility.Collapsed;
            AwarenessTab.Visibility = Visibility.Collapsed;
            if (RemoteControlTab != null) RemoteControlTab.Visibility = Visibility.Collapsed;
            if (AvailableSubjectsTab != null) AvailableSubjectsTab.Visibility = Visibility.Collapsed;
            if (BambiTakeoverTab != null) BambiTakeoverTab.Visibility = Visibility.Collapsed;
            // SP5L3: stop polling whenever we leave the Available Subjects
            // tab. Idempotent — safe to call even if not currently polling.
            App.AvailableSubjects?.StopPolling();
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

            // Reset all button styles to inactive. activeStyle is the primary-nav-only v6 variant —
            // quest sub-tabs and roadmap tracks use TabButtonActive directly (see lines further down).
            var inactiveStyle = FindResource("TabButton") as Style;
            var activeStyle = FindResource("TabButtonActivePrimary") as Style;
            BtnSettings.Style = inactiveStyle;
            BtnPresets.Style = inactiveStyle;
            BtnQuests.Style = inactiveStyle;
            if (BtnPrograms != null) BtnPrograms.Style = inactiveStyle;
            BtnEnhancements.Style = inactiveStyle;
            if (BtnDeeper != null) BtnDeeper.Style = FindResource("TabButtonDeeper") as Style;
            if (BtnAvailableSubjects != null) BtnAvailableSubjects.Style = FindResource("TabButtonNeon") as Style;
            BtnAchievements.Style = inactiveStyle;
            BtnCompanion.Style = inactiveStyle;
            BtnLeaderboard.Style = inactiveStyle;
            BtnLab.Style = inactiveStyle;
            BtnOpenAssetsTop.Style = inactiveStyle;
            // BtnAwareness was removed from the primary tab bar — its only entry point
            // is now the Exclusives popup submenu
            // BtnPatreonExclusives keeps its inline Patreon red style defined in XAML

            switch (tab)
            {
                case "settings":
                    SettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SettingsTab);
                    BtnSettings.Style = activeStyle;
                    RefreshPremiumRail(); // recompute chip dots (incl. Voice) from live state on every show
                    // Weekly intake pass: paint the centre tile, and play the once-a-week flip
                    // ceremony if this week's reveal hasn't run yet. Must be AFTER the tab is made
                    // visible - the spin is skipped for an off-screen tile so a background login
                    // callback can't burn the reveal on a control nobody is looking at.
                    RefreshIntakePassTile();
                    break;

                case "presets":
                    PresetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(PresetsTab);
                    BtnPresets.Style = activeStyle;
                    // Refresh catalogue share statuses on tab open (throttled) so an
                    // approval/rejection reflects on preset + session cards.
                    _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindPresets);
                    _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindSessions);
                    break;

                // "progression" tab removed in velvet-mosaic phase 6 — its content
                // is now on the Dashboard. Legacy callers (e.g. older tutorial steps)
                // that request ShowTab("progression") fall through to the Dashboard.
                case "progression":
                    SettingsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SettingsTab);
                    BtnSettings.Style = activeStyle;
                    RefreshPremiumRail();
                    break;

                case "quests":
                    QuestsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(QuestsTab);
                    BtnQuests.Style = activeStyle;
                    StartSeasonTitleShimmer();
                    RefreshQuestUI();
                    break;

                case "programs":
                    ProgramsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(ProgramsTab);
                    BtnPrograms.Style = activeStyle;
                    RefreshProgramsUI();
                    break;

                case "enhancements":
                    EnhancementsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(EnhancementsTab);
                    BtnEnhancements.Style = activeStyle;
                    RefreshEnhancementsUI();
                    break;

                case "deeper":
                    if (DeeperTab != null)
                    {
                        DeeperTab.Visibility = Visibility.Visible;
                        AnimateTabIn(DeeperTab);
                        RefreshDeeperLibraryUI();
                        // Populate the Deeper-hub webcam card (device + monitor
                        // combos populate empty until something asks). Refresh
                        // also fills the consent + calibration status cells.
                        try { PopulateWebcamDeviceCombos(); } catch { }
                        try { RefreshWebcamMonitorList(); } catch { }
                        RefreshDeeperWebcamColumn();
                        RefreshBlinkTrainerTrackerButton();
                        // Refresh submission statuses on tab open (throttled) so
                        // an acceptance reflects without restarting the app.
                        _ = CheckDeeperSubmissionStatusesAsync();
                    }
                    if (BtnDeeper != null) BtnDeeper.Style = FindResource("TabButtonDeeperActive") as Style;
                    break;

                case "achievements":
                    AchievementsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AchievementsTab);
                    BtnAchievements.Style = activeStyle;
                    RefreshAllAchievementTiles();
                    UpdateAchievementCount();
                    break;

                case "companion":
                    CompanionTab.Visibility = Visibility.Visible;
                    AnimateTabIn(CompanionTab);
                    BtnCompanion.Style = activeStyle;
                    SyncCompanionTabUI();
                    InitializePhrasePresets();
                    break;

                case "lab":
                    LabTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LabTab);
                    BtnLab.Style = activeStyle;
                    SyncLabEffectPermsUI();
                    RefreshWebcamDeviceList();
                    RefreshWebcamMonitorList();
                    if (LabTab.ChkRestrictGazeToCalScreen != null && App.Settings?.Current != null)
                        LabTab.ChkRestrictGazeToCalScreen.IsChecked = App.Settings.Current.RestrictGazeContentToCalibratedScreen;
                    if (LabTab.ChkWebcamDriftCorrection != null && App.Settings?.Current != null)
                        LabTab.ChkWebcamDriftCorrection.IsChecked = App.Settings.Current.WebcamAutoDriftCorrection;
                    break;

                // Note: "patreon" case is handled at the top of ShowTab as a
                // legacy redirect to the App Info & Data popup (Exclusives tab
                // was eliminated; account/data UI now lives in the dashboard).

                case "leaderboard":
                    LeaderboardTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LeaderboardTab);
                    BtnLeaderboard.Style = activeStyle;
                    _ = RefreshLeaderboardAsync(); // Load on first view
                    break;

                case "assets":
                    AssetsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AssetsTab);
                    BtnOpenAssetsTop.Style = activeStyle;
                    RefreshAssetTree();
                    InitializeAssetPresets();
                    if (PacksSectionEnabled) _ = RefreshPacksAsync();
                    break;

                case "discord":
                    DiscordTab.Visibility = Visibility.Visible;
                    AnimateTabIn(DiscordTab);
                    // BtnDiscordTab keeps its inline Discord blue style defined in XAML
                    UpdateDiscordTabUI();
                    break;

                case "awareness":
                    AwarenessTab.Visibility = Visibility.Visible;
                    AnimateTabIn(AwarenessTab);
                    SyncAwarenessTabUI();
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
                    if (BtnAvailableSubjects != null)
                        BtnAvailableSubjects.Style = FindResource("TabButtonNeonActive") as Style;
                    EnsureAvailableSubjectsBound();
                    App.AvailableSubjects?.StartPolling();
                    break;

                case "bambitakeover":
                    BambiTakeoverTab.Visibility = Visibility.Visible;
                    AnimateTabIn(BambiTakeoverTab);
                    UpdatePatreonUI();
                    break;

                case "haptics":
                    HapticsTab.Visibility = Visibility.Visible;
                    AnimateTabIn(HapticsTab);
                    UpdatePatreonUI();
                    break;

                case "lockdown":
                    LockdownTab.Visibility = Visibility.Visible;
                    AnimateTabIn(LockdownTab);
                    StartLockdownPulse();
                    RefreshPremiumGate(LockdownTab.LockdownGate);
                    break;

                case "blinktrainer":
                    BlinkTrainerTab.Visibility = Visibility.Visible;
                    AnimateTabIn(BlinkTrainerTab);
                    RefreshBlinkTrainerTab();
                    break;

                case "shelistening":
                    SheListeningTab.Visibility = Visibility.Visible;
                    AnimateTabIn(SheListeningTab);
                    RefreshSheListeningTab();
                    break;

                case "gradedintake":
                    GradedIntakeTab.Visibility = Visibility.Visible;
                    AnimateTabIn(GradedIntakeTab);
                    RefreshGradedIntakeGate();
                    RefreshPastQuizzes();
                    break;

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
            // First-visit flag flip (Phase G) — suppresses the v5.9.8 flagship
            // sticky toast on next launch. Also dismisses the toast in this
            // session if it's currently showing (H.3): once the user finds
            // the feature, the announcement has done its job.
            // Isolated try/catch so a settings failure here can't keep the
            // rest of the refresh from running.
            try
            {
                if (App.Settings?.Current is { HasSeenBlinkTrainerFlagship: false } first)
                {
                    first.HasSeenBlinkTrainerFlagship = true;
                    App.Settings?.Save();

                    // Fade out the toast if it's still on screen, and persist
                    // the dismissal so it can't refire even if HasSeen somehow
                    // doesn't stick.
                    const string flagshipKey = "blink-trainer-flagship-v5.9.8";
                    App.Notifications?.Dismiss(flagshipKey);
                    if (!first.DismissedNotificationKeys.Contains(flagshipKey))
                    {
                        first.DismissedNotificationKeys.Add(flagshipKey);
                        App.Settings?.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "HasSeenBlinkTrainerFlagship flag: failed to set");
            }

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
                // Monitor picker + Restrict-gaze checkbox mirror the Lab card.
                // RefreshWebcamMonitorList now populates both combos; the checkbox
                // gets its initial state here so the BT tab matches without
                // requiring a Lab visit first.
                RefreshWebcamMonitorList();
                if (BlinkTrainerTab.ChkBlinkTrainerRestrictGazeToCalScreen != null && s != null)
                {
                    _restrictGazeCheckboxSyncing = true;
                    try { BlinkTrainerTab.ChkBlinkTrainerRestrictGazeToCalScreen.IsChecked = s.RestrictGazeContentToCalibratedScreen; }
                    finally { _restrictGazeCheckboxSyncing = false; }
                }
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
