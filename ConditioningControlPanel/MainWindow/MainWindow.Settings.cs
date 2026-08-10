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
    // Settings load/save and feature-tutorial button handlers (nested).
    public partial class MainWindow
    {
        #region Settings Load/Save

        /// <summary>
        /// Opens the Settings door and scrolls to one of its sections. The generic form of
        /// <see cref="OpenDeviceSettings"/>, added in Phase 2 for the read-only mirrors that stayed
        /// behind in the feature popups ("Configure in Settings" on the System popup's startup and
        /// offline rows). Goes through <c>ShowTab</c> so the nav bark, the door expansion and the
        /// per-tab FX teardown all behave exactly as a rail click would.
        /// Valid keys: <see cref="Views.Tabs.AppSettingsTabView.SectionKeys"/>; an unknown key still
        /// opens the door and simply does not scroll.
        /// </summary>
        internal void OpenAppSettingsSection(string sectionKey)
        {
            try
            {
                ShowTab("appsettings");
                AppSettingsTab?.FocusSection(sectionKey);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "OpenAppSettingsSection({Key}) failed", sectionKey);
            }
        }

        private void LoadSettings()
        {
            var s = App.Settings.Current;

            // PHASE 8: Flash, Visuals, Video/attention and Subliminal are NOT seeded here any more.
            // Their editors are the Studio rack's FeatureControls (Features/*FeatureControl.xaml.cs),
            // each of which calls its own LoadFromSettings() on Loaded and re-reads on
            // AppSettings.PropertyChanged. The dead LegacyDashboardHost twins these lines filled were
            // deleted with the host (Views/Tabs/SettingsTabView.xaml).
            //
            // DualMonitorEnabled went the same way: ChkDualMon was the Collapsed twin;
            // Features/SystemFeatureControl.ChkMultiMon is the live editor, reachable from the
            // "System" quick-toggle pill on Home.

            // Startup group: Phase 2 moved these four out of the Collapsed LegacyDashboardHost into
            // Settings · General. Same x:Names, same round-trip, reachable at last.
            AppSettingsTab.ChkWinStart.IsChecked = s.RunOnStartup;
            AppSettingsTab.ChkVidLaunch.IsChecked = s.ForceVideoOnLaunch;
            AppSettingsTab.ChkAutoRun.IsChecked = s.AutoStartEngine;
            AppSettingsTab.ChkStartHidden.IsChecked = s.StartMinimized;
            AppSettingsTab.ChkNoPanic.IsChecked = !s.PanicKeyEnabled;
            // Offline mode lives on Settings · Data now (its System-popup twin is read-only).
            AppSettingsTab.ChkOfflineMode.IsChecked = s.OfflineMode;
            // PHASE 8: the five LegacyDashboardHost performance twins are gone. Settings · Performance
            // seeds its own controls (PerformanceSettingsSection.LoadFromSettings) and SaveSettings
            // already reads only those, so there is nothing left to mirror here.
            // Phase 2: Settings · Notifications owns this toggle now (it was unreachable in the
            // collapsed LegacyDashboardHost). Same property, same round-trip, new home.
            if (AppSettingsTab?.ChkIntakeNudge != null) AppSettingsTab.ChkIntakeNudge.IsChecked = s.IntakeNudgeEnabled;
            RemoteControlTab.ChkStopEffectsOnRemoteDisconnect.IsChecked = s.StopEffectsOnRemoteDisconnect;
            if (RemoteControlTab.ChkRemoteShareAvatar != null) RemoteControlTab.ChkRemoteShareAvatar.IsChecked = s.RemoteShareAvatar;

            // Emote picker preset list (bound here so OnDeserialized normalization
            // has already run and the ItemsControl always sees exactly 5 entries).
            if (RemoteControlTab.LstEmotePresets != null) RemoteControlTab.LstEmotePresets.ItemsSource = s.RemoteEmotePresets;

            // Splash-overlay (big) picker — same source list, split into two rows
            // around the End Session button via index-keyed ListCollectionView filters.
            // Items are the SAME EmotePreset references as the small picker, so edits
            // in the small picker propagate via INotifyPropertyChanged.
            if (LstEmotePresetsBigTop != null)
            {
                var topView = new System.Windows.Data.ListCollectionView(s.RemoteEmotePresets)
                {
                    Filter = item => s.RemoteEmotePresets.IndexOf((Models.EmotePreset)item) < 3
                };
                LstEmotePresetsBigTop.ItemsSource = topView;
            }
            if (LstEmotePresetsBigBottom != null)
            {
                var bottomView = new System.Windows.Data.ListCollectionView(s.RemoteEmotePresets)
                {
                    Filter = item => s.RemoteEmotePresets.IndexOf((Models.EmotePreset)item) >= 3
                };
                LstEmotePresetsBigBottom.ItemsSource = bottomView;
            }

            // Deeper
            if (AppSettingsTab?.ChkEnableDeeper != null) AppSettingsTab.ChkEnableDeeper.IsChecked = s.EnableDeeper;
            if (BtnDeeper != null) BtnDeeper.Visibility = s.EnableDeeper ? Visibility.Visible : Visibility.Collapsed;

            // Update UI for offline mode state (disable login buttons, browser, etc.)
            if (s.OfflineMode)
            {
                UpdateOfflineModeUI(true);
            }

            // Startup video display
            if (!string.IsNullOrEmpty(s.StartupVideoPath) && System.IO.File.Exists(s.StartupVideoPath))
            {
                AppSettingsTab.TxtStartupVideo.Text = System.IO.Path.GetFileName(s.StartupVideoPath);
            }
            else
            {
                AppSettingsTab.TxtStartupVideo.Text = Loc.Get("label_random");
            }

            // Audio (Phase 2: owned by the Settings door — AppSettingsTab/AudioSettingsSection)
            AppSettingsTab.SliderMaster.Value = s.MasterVolume;
            AppSettingsTab.SliderVideoVolume.Value = s.VideoVolume;
            AppSettingsTab.ChkAudioDuck.IsChecked = s.AudioDuckingEnabled;
            AppSettingsTab.SliderDuck.Value = s.DuckingLevel;
            AppSettingsTab.ChkExcludeBambiCloudDucking.IsChecked = s.ExcludeBambiCloudFromDucking;
            PopulateAudioOutputDevices();

            // PHASE 8: Spiral, Pink Filter, Bubble Pop, Lock Card, Bubble Count, Bouncing Text,
            // Mind Wipe and Brain Drain are NOT seeded here any more. Their editors are the Studio
            // rack's FeatureControls (Features/*FeatureControl.xaml.cs +
            // Views/Controls/Studio/BrainDrainFeatureControl.xaml.cs), each of which calls its own
            // LoadFromSettings() on Loaded and re-reads on AppSettings.PropertyChanged. The dead
            // ProgressionTab twins these lines filled were deleted with the view.

            // Autonomy Mode
            BambiTakeoverTab.ChkAutonomyEnabled.IsChecked = s.AutonomyModeEnabled;
            UpdateAutonomyButtonState(s.AutonomyModeEnabled);
            // Clamp persisted values into the sliders' ranges BEFORE assigning: an
            // out-of-range stored value (old-version scale, cloud-restored settings)
            // silently clamps the slider to full while the label below keeps its XAML
            // default — "intensity = 5 shows a full bar" (#485). Write the clamped
            // value back so the setting and the UI agree.
            s.AutonomyIntensity = Math.Clamp(s.AutonomyIntensity,
                (int)BambiTakeoverTab.SliderAutonomyIntensity.Minimum, (int)BambiTakeoverTab.SliderAutonomyIntensity.Maximum);
            s.AutonomyCooldownSeconds = Math.Clamp(s.AutonomyCooldownSeconds,
                (int)BambiTakeoverTab.SliderAutonomyCooldown.Minimum, (int)BambiTakeoverTab.SliderAutonomyCooldown.Maximum);
            s.AutonomyRandomIntervalSeconds = Math.Clamp(s.AutonomyRandomIntervalSeconds,
                (int)BambiTakeoverTab.SliderAutonomyInterval.Minimum, (int)BambiTakeoverTab.SliderAutonomyInterval.Maximum);
            s.AutonomyAnnouncementChance = Math.Clamp(s.AutonomyAnnouncementChance, 0, 100);
            BambiTakeoverTab.SliderAutonomyIntensity.Value = s.AutonomyIntensity;
            BambiTakeoverTab.SliderAutonomyCooldown.Value = s.AutonomyCooldownSeconds;
            BambiTakeoverTab.SliderAutonomyInterval.Value = s.AutonomyRandomIntervalSeconds;
            BambiTakeoverTab.ChkAutonomyIdle.IsChecked = s.AutonomyIdleTriggerEnabled;
            BambiTakeoverTab.ChkAutonomyRandom.IsChecked = s.AutonomyRandomTriggerEnabled;
            BambiTakeoverTab.ChkAutonomyTimeAware.IsChecked = s.AutonomyTimeAwareEnabled;
            BambiTakeoverTab.ChkAutonomyFlash.IsChecked = s.AutonomyCanTriggerFlash;
            BambiTakeoverTab.ChkAutonomyVideo.IsChecked = s.AutonomyCanTriggerVideo;
            BambiTakeoverTab.ChkAutonomyWebVideo.IsChecked = s.AutonomyCanTriggerWebVideo;
            BambiTakeoverTab.ChkProtectBrowserVideo.IsChecked = s.ProtectBrowserVideoPlayback;
            BambiTakeoverTab.ChkAutonomySubliminal.IsChecked = s.AutonomyCanTriggerSubliminal;
            BambiTakeoverTab.ChkAutonomyBubbles.IsChecked = s.AutonomyCanTriggerBubbles;
            BambiTakeoverTab.ChkAutonomyComment.IsChecked = s.AutonomyCanComment;
            BambiTakeoverTab.ChkAutonomyMindWipe.IsChecked = s.AutonomyCanTriggerMindWipe;
            BambiTakeoverTab.ChkAutonomyLockCard.IsChecked = s.AutonomyCanTriggerLockCard;
            BambiTakeoverTab.ChkAutonomySpiral.IsChecked = s.AutonomyCanTriggerSpiral;
            BambiTakeoverTab.ChkAutonomyPinkFilter.IsChecked = s.AutonomyCanTriggerPinkFilter;
            BambiTakeoverTab.ChkAutonomyBouncingText.IsChecked = s.AutonomyCanTriggerBouncingText;
            BambiTakeoverTab.ChkAutonomyBubbleCount.IsChecked = s.AutonomyCanTriggerBubbleCount;
            BambiTakeoverTab.ChkAutonomyWallpaper.IsChecked = s.AutonomyCanTriggerWallpaper;
            RefreshWallpaperFolderLabel();
            BambiTakeoverTab.ChkAutonomyVoice.IsChecked = s.AutonomyCanTriggerVoiceCommand && s.MicConsentGiven;
            BambiTakeoverTab.ChkAutonomyResumeOnStartup.IsChecked = s.AutonomyResumeOnStartup;
            BambiTakeoverTab.ChkShowTakeoverCountdown.IsChecked = s.ShowTakeoverCountdownBar;
            AppSettingsTab.ChkSpeechWakeWord.IsChecked = s.SpeechWakeWordEnabled && s.MicConsentGiven;
            AppSettingsTab.TxtSpeechWakeWords.Text = string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords;
            AppSettingsTab.ChkSpeechPushToTalk.IsChecked = s.SpeechPushToTalkEnabled && s.MicConsentGiven;
            AppSettingsTab.TxtPttKey.Text = string.IsNullOrWhiteSpace(s.SpeechPushToTalkKey) ? "F8" : s.SpeechPushToTalkKey;
            BambiTakeoverTab.SliderAutonomyAnnounce.Value = s.AutonomyAnnouncementChance;
            // The Slider_Changed handlers bail out while _isLoading, so the value labels
            // next to these four bars kept their XAML defaults ("5", "60s", "30s", "50%")
            // regardless of the loaded values — bars and numbers disagreed on startup
            // (#485). Sync the labels explicitly, matching each handler's format.
            BambiTakeoverTab.TxtAutonomyIntensity.Text = $"{s.AutonomyIntensity}";
            BambiTakeoverTab.TxtAutonomyCooldown.Text = $"{s.AutonomyCooldownSeconds}s";
            BambiTakeoverTab.TxtAutonomyInterval.Text = $"{s.AutonomyRandomIntervalSeconds}s";
            BambiTakeoverTab.TxtAutonomyAnnounce.Text = $"{s.AutonomyAnnouncementChance}%";
            RefreshAutonomyVoiceHint(); // reflect any wake/PTT suppression in the surprise-mantras hint

            // PHASE 8: Scheduler and Intensity Ramp seed themselves too. SchedulerFeatureControl
            // and IntensityRampFeatureControl (hosted by the Studio rack's SchedulerRackPanel /
            // RampRackPanel) load on Loaded and re-read on PropertyChanged, and the rack copy is
            // strictly richer than the deleted twin - it owns CmbRampCurve, which the ghost never
            // had, so seeding the ghost here could only ever have clamped values.

            // Haptics — the whole tab loads itself now (MainWindow/MainWindow.Haptics.cs).
            // The 30-odd per-control assignments that used to live here died with the Phase E
            // rebuild: the routing matrix is data-bound to HapticSettingsV2, so there is nothing
            // left to copy into it by hand. The provider combo (and its Mock-vs-blank fallback
            // dance) is gone too — providers are per-provider checkboxes now.
            LoadHapticsSettingsToUi();

            // Keyword Triggers
            // PHASE 5 (G3): the editors moved off the dead PatreonTab onto the Awareness tab
            // (Views/Controls/Companion/KeywordTriggersPanel.xaml). SyncKeywordRescuePanelUi()
            // seeds every one of them - including the OCR/highlight detail visibilities - and
            // refreshes the trigger list, so nothing is copied into the corpse's twins here any
            // more. Seeding a Collapsed twin would raise its ValueChanged, and those handlers now
            // read the live panel.
            // PHASE 8: the corpse's own lock label and start/stop button went with PatreonTabView,
            // and UpdateKeywordTriggersButtonState with them. The user-facing master is
            // AwarenessTab.ChkAwarenessMaster, and SyncKeywordRescuePanelUi seeds every rescued
            // editor - including the access-gated visibilities - straight from settings.
            SyncKeywordRescuePanelUi();

            // Discord Sharing Settings
            if (DiscordTab.ChkDiscordTabShowOnline != null) DiscordTab.ChkDiscordTabShowOnline.IsChecked = s.ShowOnlineStatus;

            // Update Discord UI (both main tab and Patreon tab)
            UpdateQuickDiscordUI();

            // Update level display
            UpdateLevelDisplay();

            // Update all slider text displays
            UpdateSliderTexts();

            // Start autonomy service if it was enabled (works independently of engine)
            var hasPatreonAccess = App.Patreon?.HasPremiumAccess == true;
            if (hasPatreonAccess && s.AutonomyModeEnabled && s.AutonomyConsentGiven)
            {
                App.Autonomy?.Start();
                App.Logger?.Debug("MainWindow: Started autonomy service on settings load");
            }
        }

        /// <summary>
        /// Updates all slider text displays to match current slider values
        /// Called after loading settings since the value changed events are suppressed during load
        /// </summary>
        private void UpdateSliderTexts()
        {
            // PHASE 8: the 14 Flash / Video / Subliminal label mirrors are gone with
            // LegacyDashboardHost. Every Studio rack panel writes its own value label inside
            // LoadFromSettings(), before its _isLoading guard, so nothing is left to mirror.

            // Audio sliders (Settings door)
            if (AppSettingsTab.TxtMaster != null) AppSettingsTab.TxtMaster.Text = $"{(int)AppSettingsTab.SliderMaster.Value}%";
            if (AppSettingsTab.TxtVideoVolume != null) AppSettingsTab.TxtVideoVolume.Text = $"{(int)AppSettingsTab.SliderVideoVolume.Value}%";
            if (AppSettingsTab.TxtDuck != null) AppSettingsTab.TxtDuck.Text = $"{(int)AppSettingsTab.SliderDuck.Value}%";
            
            // PHASE 8: the Progression/Scheduler label mirrors are gone with ProgressionTabView.
            // Every rack panel writes its own value label inside LoadFromSettings(), before its
            // _isLoading guard, so there is nothing left to mirror for those either.

            // Haptic slider labels are written by LoadHapticsSettingsToUi() and by the sliders'
            // own ValueChanged handlers (which update their label BEFORE the _isLoading guard),
            // so there is nothing left to mirror here.
        }

        private void SaveSettings()
        {
            // PHASE 8: the `LoadSettings()` re-sync preamble that used to open this method is gone.
            // Its ONLY purpose was to refresh the stale ghost twins (LegacyDashboardHost /
            // ProgressionTabView) before the read-backs below consumed them - which is also what
            // made those read-backs provable identity ops. Both halves died together; keeping the
            // preamble would now cost a full LoadSettings (and every side effect it carries:
            // UpdateSliderTexts, UpdateLevelDisplay, UpdateQuickDiscordUI, the autonomy auto-start)
            // on every save, for nothing.
            //
            // Everything read below is a LIVE control on the Settings door, edited by the user and
            // never stale. Nothing else may be added here that reads a control the user cannot see.
            var s = App.Settings.Current;

            // PHASE 8: the 29 LegacyDashboardHost read-backs (Flash x8, Visuals x3, Video +
            // attention x8, Subliminal x6, DualMonitorEnabled) are gone with the host. They were
            // provably identity operations - SaveSettings used to open by calling LoadSettings(),
            // so each `s.X = <ghost>.Y` was preceded in the same call by `<ghost>.Y = s.X` - except
            // where a ghost slider's Min/Max clamped the value, which is exactly the hazard the
            // range-matching comments in the deleted XAML were warding off. Every one of those
            // properties now has a single live writer: the Studio rack's *FeatureControl panels,
            // and Features/SystemFeatureControl.ChkMultiMon for DualMonitorEnabled.

            // System
            s.RunOnStartup = AppSettingsTab.ChkWinStart.IsChecked ?? false;
            s.ForceVideoOnLaunch = AppSettingsTab.ChkVidLaunch.IsChecked ?? false;
            s.AutoStartEngine = AppSettingsTab.ChkAutoRun.IsChecked ?? false;
            s.StartMinimized = AppSettingsTab.ChkStartHidden.IsChecked ?? false;
            s.PanicKeyEnabled = !(AppSettingsTab.ChkNoPanic.IsChecked ?? false);
            s.OfflineMode = AppSettingsTab.ChkOfflineMode.IsChecked ?? false;
            // Performance: the LegacyDashboardHost twins were deleted in Phase 8, so these are the
            // only editors left. One control, one writer.
            if (AppSettingsTab?.ChkPerformanceMode != null) s.PerformanceMode = AppSettingsTab.ChkPerformanceMode.IsChecked ?? false;
            if (AppSettingsTab?.ChkAutoPerformance != null) s.AutoPerformanceMode = AppSettingsTab.ChkAutoPerformance.IsChecked ?? true;
            if (AppSettingsTab?.CmbMotionLevel != null && AppSettingsTab.CmbMotionLevel.SelectedIndex >= 0)
                s.MotionLevel = (Models.MotionLevel)AppSettingsTab.CmbMotionLevel.SelectedIndex;
            if (AppSettingsTab?.ChkVideoHwDecode != null) s.VideoForceHardwareDecoding = AppSettingsTab.ChkVideoHwDecode.IsChecked ?? false;
            if (AppSettingsTab?.ChkUnifiedOverlay != null) s.UnifiedOverlayHost = AppSettingsTab.ChkUnifiedOverlay.IsChecked ?? true;
            // Weekly intake pass nudge. Defaults ON (it is the feature's re-engagement hook), but a
            // recurring popup with no off switch is a bug report waiting to happen.
            if (AppSettingsTab?.ChkIntakeNudge != null) s.IntakeNudgeEnabled = AppSettingsTab.ChkIntakeNudge.IsChecked ?? true;

            // Deeper
            if (AppSettingsTab?.ChkEnableDeeper != null) s.EnableDeeper = AppSettingsTab.ChkEnableDeeper.IsChecked ?? true;

            // Audio (Settings door)
            s.MasterVolume = (int)AppSettingsTab.SliderMaster.Value;
            s.AudioDuckingEnabled = AppSettingsTab.ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)AppSettingsTab.SliderDuck.Value;
            s.ExcludeBambiCloudFromDucking = AppSettingsTab.ChkExcludeBambiCloudDucking.IsChecked ?? true;

            // PHASE 8: the 34 Progression/Brain-Drain/Scheduler/Ramp read-backs are gone with
            // ProgressionTabView. They were provably identity operations - SaveSettings opens by
            // calling LoadSettings(), so each `s.X = <ghost>.Y` was preceded in the same call by
            // `<ghost>.Y = s.X` - except where a ghost slider's Min/Max clamped the value, which is
            // exactly the hazard the range-matching comments in the deleted XAML were warding off.
            // Every one of those 34 properties now has a single live writer in the Studio rack.

            // Scheduler - track if settings changed.
            // KEPT DELIBERATELY, THOUGH CURRENTLY UNREACHABLE (Phase 8 audit §4.2 / concern 7):
            // s.SchedulerEnabled is written by SchedulerFeatureControl.ChkEnabled_Changed BEFORE
            // this method runs, so the rising edge is already consumed and this branch no longer
            // fires. The consequence is documented and accepted in
            // Views/Controls/Studio/SchedulerRackPanel.xaml.cs: enabling the scheduler now arms
            // within one 30s SchedulerTimer_Tick instead of instantly. The real fix is to move
            // this kick into SchedulerFeatureControl, which is a Studio-owned change, not a
            // demolition one - so the guard stays here rather than being silently dropped.
            var schedulerWasEnabled = s.SchedulerEnabled;
            if (s.SchedulerEnabled && !schedulerWasEnabled)
            {
                _schedulerAutoStarted = false;
                _manuallyStoppedDuringSchedule = false;
                // Check scheduler immediately after save completes
                Dispatcher.BeginInvoke(new Action(() => CheckSchedulerAfterSettingsChange()), System.Windows.Threading.DispatcherPriority.Background);
            }

            App.Settings.Save();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // First, apply current settings to the settings object
            SaveSettings();

            // Find current preset
            var currentPresetName = App.Settings.Current.CurrentPresetName;
            var currentPreset = _allPresets.FirstOrDefault(p => p.Name == currentPresetName);

            // Determine if we should create new or overwrite
            if (currentPreset == null || currentPreset.IsDefault || string.IsNullOrEmpty(currentPresetName))
            {
                // #738: SaveSettings() above already wrote the settings, so this dialog is only
                // offering an extra preset. It used to ask "would you like to save...?", which
                // reads as the save itself - answering No and then being told "Settings saved!"
                // looked like the app had ignored the answer. Say what already happened, and drop
                // the second confirmation box: the title now carries it.
                var result = MessageBox.Show(
                    Loc.Get("msg_settings_saved_offer_preset"),
                    Loc.Get("title_settings_saved"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    PromptSaveNewPreset();
                }
            }
            else
            {
                // #738: same framing fix - the settings are already saved, so Cancel is "leave my
                // presets alone", not "don't save". Spelling the three buttons out keeps that clear.
                var result = MessageBox.Show(
                    Loc.GetF("msg_settings_saved_offer_overwrite_format", currentPreset.Name),
                    Loc.Get("title_settings_saved"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    // Overwrite existing preset
                    var updated = Models.Preset.FromSettings(App.Settings.Current, currentPreset.Name, currentPreset.Description);
                    updated.Id = currentPreset.Id;
                    updated.CreatedAt = currentPreset.CreatedAt;

                    var index = App.Settings.Current.UserPresets.FindIndex(p => p.Id == currentPreset.Id);
                    if (index >= 0)
                    {
                        App.Settings.Current.UserPresets[index] = updated;
                        App.Settings.Save();
                        RefreshPresetsList();

                        App.Logger?.Information("Overwritten preset: {Name}", updated.Name);
                        MessageBox.Show(Loc.GetF("msg_preset_0_updated", updated.Name), Loc.Get("title_preset_saved"),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (result == MessageBoxResult.No)
                {
                    // Save as new preset
                    PromptSaveNewPreset();
                }
                // Cancel: nothing more to do. The settings were saved before the dialog and its
                // title said so, so a second "Settings saved!" box only muddied the answer (#738).
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            if (App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (_isRunning)
            {
                var result = MessageBox.Show(Loc.Get("msg_engine_is_running_stop_and_exit"), Loc.Get("title_confirm_exit"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
                StopEngine();
            }
            _exitRequested = true;
            EnsureSessionRestoredForExit();
            SaveSettings();
            // Under ShutdownMode=OnLastWindowClose, Close()ing only the main window leaves the
            // avatar tube and pooled keep-alive overlay windows (Flash/Subliminal/Chaos) alive —
            // especially right after a Chaos run — so the app lingered headless and never reached
            // App.OnExit/Environment.Exit. Shutdown() closes ALL windows (this window still runs
            // its _exitRequested cleanup via OnClosing) and fires OnExit. Matches the tray Exit path.
            Application.Current.Shutdown();
        }

        private void BtnMainHelp_Click(object sender, RoutedEventArgs e)
        {
            // Hide browser (WebView2 doesn't respect WPF z-order)
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Hidden;
            MainTutorialOverlay.Visibility = Visibility.Visible;
        }

        internal void BtnReportBug_Click(object sender, RoutedEventArgs e)
        {
            OpenBugReportWindow();
        }

        private void BtnTutorialReportBug_Click(object sender, RoutedEventArgs e)
        {
            // Close the tutorial overlay first, then open the bug report dialog
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            OpenBugReportWindow();
        }

        private void OpenBugReportWindow()
        {
            try
            {
                var dialog = new BugReportWindow { Owner = this };
                dialog.ShowDialog();
            }
            catch (System.Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open BugReportWindow");
                MessageBox.Show(this, Loc.Get("bug_report_error_toast") + "\n\n" + ex.Message,
                    Loc.Get("bug_report_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainTutorial_Close(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
        }

        private void MainTutorial_Close(object sender, MouseButtonEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
        }

        private void MainTutorial_ContentClick(object sender, MouseButtonEventArgs e)
        {
            // Prevent closing when clicking on the content
            e.Handled = true;
        }

        private TutorialOverlay? _tutorialOverlay;

        private void BtnStartTutorial_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial();
        }

        public void StartTutorial(TutorialType type = TutorialType.FullTour)
        {
            if (_tutorialOverlay != null) return;

            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Hidden;

            // Configure tutorial callbacks for tab switching
            App.Tutorial.ConfigureCallbacks(
                showSettings: () => ShowTab("settings"),
                showPresets: () => { ShowTab("presets"); RefreshPresetsList(); },
                showProgression: () => ShowTab("progression"),
                showAchievements: () => ShowTab("achievements"),
                showCompanion: () => ShowTab("companion"),
                // Exclusives tab eliminated — route tutorial's "patreon" step to the
                // App Info & Data popup which hosts the login/data sections.
                showPatreon: () => ShowAppInfoPopup(),
                showAwareness: () => ShowTab("awareness"),
                showDeeper: () => ShowTab("deeper"),
                // Generic router for every other RequiresTab key (Phase 1's door rail put all
                // 22 of them on screen). Bound to THIS window rather than left to the service's
                // Application.Current.MainWindow fallback, so the tutorial always drives the
                // instance its overlay is attached to.
                showTab: key => ShowTab(key)
            );

            App.Tutorial.Start(type);
            _tutorialOverlay = new TutorialOverlay(this, App.Tutorial);
            _tutorialOverlay.Closed += (s, e) =>
            {
                _tutorialOverlay = null;
                if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            };
            _tutorialOverlay.Show();
        }

        #region Feature Tutorial Button Handlers

        private void BtnTutorialGettingStarted_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.GettingStarted);
        }

        private void BtnTutorialSettings_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Settings);
        }

        private void BtnTutorialPresets_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Presets);
        }

        private void BtnTutorialProgression_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Progression);
        }

        private void BtnTutorialAchievements_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Achievements);
        }

        private void BtnTutorialCompanion_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Companion);
        }

        private void BtnTutorialPatreon_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Patreon);
        }

        private void BtnTutorialAvatar_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartTutorial(TutorialType.Avatar);
        }

        private void BtnTutorialAwareness_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            StartAwarenessTutorial();
        }

        // Same tour, but launched directly from the in-tab "Tutorial" button rather
        // than via the help-menu overlay (so we don't toggle MainTutorialOverlay).
        internal void BtnAwarenessTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartAwarenessTutorial();
        }

        internal void BtnCompanionTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartTutorial(TutorialType.Companion);
        }

        private void StartAwarenessTutorial()
        {
            // One-shot: when the Awareness tour finishes naturally (user reached the
            // last step), pop the Puppy preset editor so they have something concrete
            // to play with while the walkthrough is fresh. Skipping mid-tour does not
            // open the editor — skip means "I'm done with this".
            EventHandler? onCompleted = null;
            onCompleted = (s, args) =>
            {
                App.Tutorial.TutorialCompleted -= onCompleted;
                if (App.Tutorial.CurrentTutorialType != TutorialType.Awareness) return;
                if (App.Tutorial.CurrentStepIndex != App.Tutorial.TotalSteps - 1) return;

                try
                {
                    var puppy = App.KeywordPresets?.GetPreset("builtin.puppy");
                    if (puppy == null) return;
                    var dlg = new AwarenessPresetDetailDialog(puppy) { Owner = this };
                    dlg.Show();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Awareness tutorial editor-open failed: {Error}", ex.Message);
                }
            };
            App.Tutorial.TutorialCompleted += onCompleted;

            StartTutorial(TutorialType.Awareness);
        }

        private void BtnTutorialModding_Click(object sender, RoutedEventArgs e)
        {
            MainTutorialOverlay.Visibility = Visibility.Collapsed;
            if (SettingsTab.BrowserContainer != null) SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
            var modCreator = new ModCreatorWindow(startWithTutorial: true) { Owner = this };
            modCreator.Show();
        }

        #endregion

        private void OpenLinktree()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://linktr.ee/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        #endregion
    }
}
