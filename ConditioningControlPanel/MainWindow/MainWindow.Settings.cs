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

        private void LoadSettings()
        {
            var s = App.Settings.Current;

            // Flash
            SettingsTab.ChkFlashEnabled.IsChecked = s.FlashEnabled;
            SettingsTab.ChkClickable.IsChecked = s.FlashClickable;
            SettingsTab.ChkCorruption.IsChecked = s.CorruptionMode;
            SettingsTab.ChkHydraLinked.IsChecked = s.HydraLinkedTiming;
            SettingsTab.ChkFlashGlow.IsChecked = s.FlashGlowEnabled;
            SettingsTab.SliderPerMin.Value = s.FlashFrequency;
            SettingsTab.SliderImages.Value = s.SimultaneousImages;
            SettingsTab.SliderMaxOnScreen.Value = s.HydraLimit;

            // Visuals
            SettingsTab.SliderSize.Value = s.ImageScale;
            SettingsTab.SliderOpacity.Value = s.FlashOpacity;
            SettingsTab.SliderFade.Value = s.FadeDuration;
            SettingsTab.SliderFlashDuration.Value = s.FlashDuration;
            SettingsTab.ChkFlashAudio.IsChecked = s.FlashAudioEnabled;
            SettingsTab.SliderFlashDuration.IsEnabled = !s.FlashAudioEnabled;
            SettingsTab.SliderFlashDuration.Opacity = s.FlashAudioEnabled ? 0.5 : 1.0;
            
            // Set audio link state based on frequency
            _isLoading = false;
            UpdateAudioLinkState();
            _isLoading = true;

            // Video
            SettingsTab.ChkVideoEnabled.IsChecked = s.MandatoryVideosEnabled;
            SettingsTab.SliderPerHour.Value = s.VideosPerHour;
            SettingsTab.ChkStrictLock.IsChecked = s.StrictLockEnabled;
            SettingsTab.ChkMiniGameEnabled.IsChecked = s.AttentionChecksEnabled;
            SettingsTab.SliderTargets.Value = s.AttentionDensity;
            SettingsTab.ChkRandomizeTargets.IsChecked = s.RandomizeAttentionTargets;
            SettingsTab.SliderDuration.Value = s.AttentionLifespan;
            SettingsTab.SliderTargetSize.Value = s.AttentionSize;

            // Subliminals
            SettingsTab.ChkSubliminalEnabled.IsChecked = s.SubliminalEnabled;
            SettingsTab.SliderSubPerMin.Value = s.SubliminalFrequency;
            SettingsTab.SliderFrames.Value = s.SubliminalDuration;
            SettingsTab.SliderSubOpacity.Value = s.SubliminalOpacity;
            SettingsTab.ChkAudioWhispers.IsChecked = s.SubAudioEnabled;
            SettingsTab.SliderWhisperVol.Value = s.SubAudioVolume;

            // System
            SettingsTab.ChkDualMon.IsChecked = s.DualMonitorEnabled;
            SettingsTab.ChkWinStart.IsChecked = s.RunOnStartup;
            SettingsTab.ChkVidLaunch.IsChecked = s.ForceVideoOnLaunch;
            SettingsTab.ChkAutoRun.IsChecked = s.AutoStartEngine;
            SettingsTab.ChkStartHidden.IsChecked = s.StartMinimized;
            SettingsTab.ChkNoPanic.IsChecked = !s.PanicKeyEnabled;
            SettingsTab.ChkOfflineMode.IsChecked = s.OfflineMode;
            if (SettingsTab.ChkPerformanceMode != null) SettingsTab.ChkPerformanceMode.IsChecked = s.PerformanceMode;
            if (SettingsTab.ChkAutoPerformance != null) SettingsTab.ChkAutoPerformance.IsChecked = s.AutoPerformanceMode;
            if (SettingsTab.CmbMotionLevel != null) SettingsTab.CmbMotionLevel.SelectedIndex = (int)s.MotionLevel;
            if (SettingsTab.ChkVideoHwDecode != null) SettingsTab.ChkVideoHwDecode.IsChecked = s.VideoForceHardwareDecoding;
            if (SettingsTab.ChkUnifiedOverlay != null) SettingsTab.ChkUnifiedOverlay.IsChecked = s.UnifiedOverlayHost;
            if (SettingsTab.ChkIntakeNudge != null) SettingsTab.ChkIntakeNudge.IsChecked = s.IntakeNudgeEnabled;
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
            if (SettingsTab.ChkEnableDeeper != null) SettingsTab.ChkEnableDeeper.IsChecked = s.EnableDeeper;
            if (BtnDeeper != null) BtnDeeper.Visibility = s.EnableDeeper ? Visibility.Visible : Visibility.Collapsed;

            // Update UI for offline mode state (disable login buttons, browser, etc.)
            if (s.OfflineMode)
            {
                UpdateOfflineModeUI(true);
            }

            // Startup video display
            if (!string.IsNullOrEmpty(s.StartupVideoPath) && System.IO.File.Exists(s.StartupVideoPath))
            {
                SettingsTab.TxtStartupVideo.Text = System.IO.Path.GetFileName(s.StartupVideoPath);
            }
            else
            {
                SettingsTab.TxtStartupVideo.Text = Loc.Get("label_random");
            }

            // Audio
            SettingsTab.SliderMaster.Value = s.MasterVolume;
            SettingsTab.SliderVideoVolume.Value = s.VideoVolume;
            SettingsTab.ChkAudioDuck.IsChecked = s.AudioDuckingEnabled;
            SettingsTab.SliderDuck.Value = s.DuckingLevel;
            SettingsTab.ChkExcludeBambiCloudDucking.IsChecked = s.ExcludeBambiCloudFromDucking;
            PopulateAudioOutputDevices();

            // Progression
            ProgressionTab.ChkSpiralEnabled.IsChecked = s.SpiralEnabled;
            ProgressionTab.SliderSpiralOpacity.Value = s.SpiralOpacity;
            ProgressionTab.ChkPinkFilterEnabled.IsChecked = s.PinkFilterEnabled;
            ProgressionTab.SliderPinkOpacity.Value = s.PinkFilterOpacity;
            ProgressionTab.ChkBubblesEnabled.IsChecked = s.BubblesEnabled;
            ProgressionTab.SliderBubbleFreq.Value = s.BubblesFrequency;
            ProgressionTab.SliderBubbleVolume.Value = s.BubblesVolume;
            ProgressionTab.ChkLockCardEnabled.IsChecked = s.LockCardEnabled;
            ProgressionTab.SliderLockCardFreq.Value = s.LockCardFrequency;
            ProgressionTab.SliderLockCardRepeats.Value = s.LockCardRepeats;
            ProgressionTab.ChkLockCardStrict.IsChecked = s.LockCardStrict;
            ProgressionTab.ChkBubbleCountEnabled.IsChecked = s.BubbleCountEnabled;
            ProgressionTab.ChkBubbleCountStrict.IsChecked = s.BubbleCountStrictLock;
            ProgressionTab.SliderBubbleCountFreq.Value = s.BubbleCountFrequency;
            ProgressionTab.TxtBubbleCountFreq.Text = s.BubbleCountFrequency.ToString();
            ProgressionTab.CmbBubbleCountDifficulty.SelectedIndex = s.BubbleCountDifficulty;
            ProgressionTab.ChkBouncingTextEnabled.IsChecked = s.BouncingTextEnabled;
            ProgressionTab.ChkBouncingTextAlwaysOnTop.IsChecked = s.BouncingTextAlwaysOnTop;

            // Mind Wipe
            ProgressionTab.ChkMindWipeEnabled.IsChecked = s.MindWipeEnabled;
            ProgressionTab.SliderMindWipeFreq.Value = s.MindWipeFrequency;
            ProgressionTab.SliderMindWipeVolume.Value = s.MindWipeVolume;
            ProgressionTab.ChkMindWipeLoop.IsChecked = s.MindWipeLoop;

            // Brain Drain
            ProgressionTab.ChkBrainDrainEnabled.IsChecked = s.BrainDrainEnabled;
            ProgressionTab.SliderBrainDrainIntensity.Value = s.BrainDrainIntensity;
            ProgressionTab.ChkBrainDrainHighRefresh.IsChecked = s.BrainDrainHighRefresh;

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
            BambiTakeoverTab.ChkSpeechWakeWord.IsChecked = s.SpeechWakeWordEnabled && s.MicConsentGiven;
            BambiTakeoverTab.TxtSpeechWakeWords.Text = string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords;
            BambiTakeoverTab.ChkSpeechPushToTalk.IsChecked = s.SpeechPushToTalkEnabled && s.MicConsentGiven;
            BambiTakeoverTab.TxtPttKey.Text = string.IsNullOrWhiteSpace(s.SpeechPushToTalkKey) ? "F8" : s.SpeechPushToTalkKey;
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

            // Bouncing Text Size (add if not already loaded above)
            ProgressionTab.SliderBouncingTextSize.Value = s.BouncingTextSize;

            // Scheduler
            ProgressionTab.ChkSchedulerEnabled.IsChecked = s.SchedulerEnabled;
            ProgressionTab.TxtStartTime.Text = s.SchedulerStartTime;
            ProgressionTab.TxtEndTime.Text = s.SchedulerEndTime;
            ProgressionTab.ChkMon.IsChecked = s.SchedulerMonday;
            ProgressionTab.ChkTue.IsChecked = s.SchedulerTuesday;
            ProgressionTab.ChkWed.IsChecked = s.SchedulerWednesday;
            ProgressionTab.ChkThu.IsChecked = s.SchedulerThursday;
            ProgressionTab.ChkFri.IsChecked = s.SchedulerFriday;
            ProgressionTab.ChkSat.IsChecked = s.SchedulerSaturday;
            ProgressionTab.ChkSun.IsChecked = s.SchedulerSunday;
            ProgressionTab.ChkRampEnabled.IsChecked = s.IntensityRampEnabled;
            ProgressionTab.SliderRampDuration.Value = s.RampDurationMinutes;
            ProgressionTab.SliderMultiplier.Value = s.SchedulerMultiplier;
            
            // Ramp Links
            ProgressionTab.ChkRampLinkFlash.IsChecked = s.RampLinkFlashOpacity;
            ProgressionTab.ChkRampLinkSpiral.IsChecked = s.RampLinkSpiralOpacity;
            ProgressionTab.ChkRampLinkPink.IsChecked = s.RampLinkPinkFilterOpacity;
            ProgressionTab.ChkRampLinkMaster.IsChecked = s.RampLinkMasterAudio;
            ProgressionTab.ChkRampLinkSubAudio.IsChecked = s.RampLinkSubliminalAudio;
            ProgressionTab.ChkEndAtRamp.IsChecked = s.EndSessionOnRampComplete;

            // Haptics — the whole tab loads itself now (MainWindow/MainWindow.Haptics.cs).
            // The 30-odd per-control assignments that used to live here died with the Phase E
            // rebuild: the routing matrix is data-bound to HapticSettingsV2, so there is nothing
            // left to copy into it by hand. The provider combo (and its Mock-vs-blank fallback
            // dance) is gone too — providers are per-provider checkboxes now.
            LoadHapticsSettingsToUi();

            // Keyword Triggers
            {
                PatreonTab.SliderKeywordBufferTimeout.Value = s.KeywordBufferTimeoutMs;
                PatreonTab.SliderKeywordGlobalCooldown.Value = s.KeywordGlobalCooldownSeconds;
                PatreonTab.SliderKeywordSessionMultiplier.Value = s.KeywordSessionMultiplier;

                var hasKeywordAccess = KeywordTriggerService.HasAccess();

                // Show/hide lock indicator
                if (PatreonTab.TxtKeywordTriggersLocked != null)
                    PatreonTab.TxtKeywordTriggersLocked.Visibility = hasKeywordAccess ? Visibility.Collapsed : Visibility.Visible;
                if (PatreonTab.BtnKeywordTriggersStartStop != null)
                    PatreonTab.BtnKeywordTriggersStartStop.IsEnabled = hasKeywordAccess;

                UpdateKeywordTriggersButtonState();
                RefreshKeywordTriggerList();

                // Screen OCR
                if (PatreonTab.ChkScreenOcrEnabled != null)
                {
                    PatreonTab.ChkScreenOcrEnabled.IsChecked = s.ScreenOcrEnabled;
                    PatreonTab.ChkScreenOcrEnabled.IsEnabled = hasKeywordAccess;
                    PatreonTab.SliderScreenOcrInterval.Value = s.ScreenOcrIntervalMs / 1000.0;
                    PatreonTab.ScreenOcrIntervalPanel.Visibility = s.ScreenOcrEnabled && hasKeywordAccess ? Visibility.Visible : Visibility.Collapsed;
                    if (PatreonTab.CmbOcrConfirmation != null)
                        PatreonTab.CmbOcrConfirmation.SelectedIndex = Math.Clamp(s.OcrConfirmationScans - 1, 0, 2);
                }
                if (PatreonTab.ChkKeywordHighlightEnabled != null)
                {
                    PatreonTab.ChkKeywordHighlightEnabled.IsChecked = s.KeywordHighlightEnabled;
                    if (PatreonTab.HighlightDurationPanel != null)
                    {
                        PatreonTab.HighlightDurationPanel.Visibility = s.KeywordHighlightEnabled ? Visibility.Visible : Visibility.Collapsed;
                        PatreonTab.SliderKeywordHighlightDuration.Value = s.KeywordHighlightDurationMs / 1000.0;
                        PatreonTab.TxtKeywordHighlightDuration.Text = $"{s.KeywordHighlightDurationMs / 1000.0:0.0}s";
                        if (PatreonTab.CmbOcrHighlightMode != null)
                            PatreonTab.CmbOcrHighlightMode.SelectedIndex = s.OcrHighlightAll ? 0 : 1;
                        if (PatreonTab.ChkHighlightVisibleInCapture != null)
                            PatreonTab.ChkHighlightVisibleInCapture.IsChecked = s.OcrHighlightVisibleInCapture;
                    }
                }
            }

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
            // Flash sliders
            if (SettingsTab.TxtPerMin != null) SettingsTab.TxtPerMin.Text = ((int)SettingsTab.SliderPerMin.Value).ToString();
            if (SettingsTab.TxtImages != null) SettingsTab.TxtImages.Text = ((int)SettingsTab.SliderImages.Value).ToString();
            if (SettingsTab.TxtMaxOnScreen != null) SettingsTab.TxtMaxOnScreen.Text = ((int)SettingsTab.SliderMaxOnScreen.Value).ToString();
            if (SettingsTab.TxtSize != null) SettingsTab.TxtSize.Text = $"{(int)SettingsTab.SliderSize.Value}%";
            if (SettingsTab.TxtOpacity != null) SettingsTab.TxtOpacity.Text = $"{(int)SettingsTab.SliderOpacity.Value}%";
            if (SettingsTab.TxtFade != null) SettingsTab.TxtFade.Text = $"{(int)SettingsTab.SliderFade.Value}%";
            
            // Video sliders
            if (SettingsTab.TxtPerHour != null) SettingsTab.TxtPerHour.Text = ((int)SettingsTab.SliderPerHour.Value).ToString();
            if (SettingsTab.TxtTargets != null) SettingsTab.TxtTargets.Text = ((int)SettingsTab.SliderTargets.Value).ToString();
            if (SettingsTab.TxtDuration != null) SettingsTab.TxtDuration.Text = $"{(int)SettingsTab.SliderDuration.Value}s";
            if (SettingsTab.TxtTargetSize != null) SettingsTab.TxtTargetSize.Text = $"{(int)SettingsTab.SliderTargetSize.Value}px";
            
            // Subliminal sliders
            if (SettingsTab.TxtSubPerMin != null) SettingsTab.TxtSubPerMin.Text = ((int)SettingsTab.SliderSubPerMin.Value).ToString();
            if (SettingsTab.TxtFrames != null) SettingsTab.TxtFrames.Text = ((int)SettingsTab.SliderFrames.Value).ToString();
            if (SettingsTab.TxtSubOpacity != null) SettingsTab.TxtSubOpacity.Text = $"{(int)SettingsTab.SliderSubOpacity.Value}%";
            if (SettingsTab.TxtWhisperVol != null) SettingsTab.TxtWhisperVol.Text = $"{(int)SettingsTab.SliderWhisperVol.Value}%";
            
            // Audio sliders
            if (SettingsTab.TxtMaster != null) SettingsTab.TxtMaster.Text = $"{(int)SettingsTab.SliderMaster.Value}%";
            if (SettingsTab.TxtVideoVolume != null) SettingsTab.TxtVideoVolume.Text = $"{(int)SettingsTab.SliderVideoVolume.Value}%";
            if (SettingsTab.TxtDuck != null) SettingsTab.TxtDuck.Text = $"{(int)SettingsTab.SliderDuck.Value}%";
            
            // Progression sliders
            if (ProgressionTab.TxtSpiralOpacity != null) ProgressionTab.TxtSpiralOpacity.Text = $"{(int)ProgressionTab.SliderSpiralOpacity.Value}%";
            if (ProgressionTab.TxtPinkOpacity != null) ProgressionTab.TxtPinkOpacity.Text = $"{(int)ProgressionTab.SliderPinkOpacity.Value}%";
            if (ProgressionTab.TxtBubbleFreq != null) ProgressionTab.TxtBubbleFreq.Text = ((int)ProgressionTab.SliderBubbleFreq.Value).ToString();
            if (ProgressionTab.TxtBubbleVolume != null) ProgressionTab.TxtBubbleVolume.Text = $"{(int)ProgressionTab.SliderBubbleVolume.Value}%";
            if (ProgressionTab.TxtLockCardFreq != null) ProgressionTab.TxtLockCardFreq.Text = ((int)ProgressionTab.SliderLockCardFreq.Value).ToString();
            if (ProgressionTab.TxtLockCardRepeats != null) ProgressionTab.TxtLockCardRepeats.Text = $"{(int)ProgressionTab.SliderLockCardRepeats.Value}x";
            if (ProgressionTab.TxtBouncingTextSize != null) ProgressionTab.TxtBouncingTextSize.Text = $"{(int)ProgressionTab.SliderBouncingTextSize.Value}%";
            if (ProgressionTab.TxtMindWipeFreq != null) ProgressionTab.TxtMindWipeFreq.Text = $"{(int)ProgressionTab.SliderMindWipeFreq.Value}/h";
            if (ProgressionTab.TxtMindWipeVolume != null) ProgressionTab.TxtMindWipeVolume.Text = $"{(int)ProgressionTab.SliderMindWipeVolume.Value}%";
            if (ProgressionTab.TxtBrainDrainIntensity != null) ProgressionTab.TxtBrainDrainIntensity.Text = $"{(int)ProgressionTab.SliderBrainDrainIntensity.Value}%";
            
            // Scheduler sliders
            if (ProgressionTab.TxtRampDuration != null) ProgressionTab.TxtRampDuration.Text = $"{(int)ProgressionTab.SliderRampDuration.Value} min";
            if (ProgressionTab.TxtMultiplier != null) ProgressionTab.TxtMultiplier.Text = $"{ProgressionTab.SliderMultiplier.Value:F1}x";

            // Haptic slider labels are written by LoadHapticsSettingsToUi() and by the sliders'
            // own ValueChanged handlers (which update their label BEFORE the _isLoading guard),
            // so there is nothing left to mirror here.
        }

        private void SaveSettings()
        {
            // velvet-mosaic: feature popups write to App.Settings.Current on every edit,
            // so the settings object is already the source of truth. The legacy dashboard
            // controls (now inside SettingsTab.LegacyDashboardHost, Collapsed) can be stale. Re-sync
            // them from settings before this method reads them, otherwise stale control
            // values would clobber the popup changes.
            var wasLoading = _isLoading;
            _isLoading = true;
            try { LoadSettings(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "SaveSettings: legacy control refresh failed"); }
            finally { _isLoading = wasLoading; }

            var s = App.Settings.Current;

            // Flash
            s.FlashEnabled = SettingsTab.ChkFlashEnabled.IsChecked ?? true;
            s.FlashClickable = SettingsTab.ChkClickable.IsChecked ?? true;
            s.CorruptionMode = SettingsTab.ChkCorruption.IsChecked ?? false;
            s.HydraLinkedTiming = SettingsTab.ChkHydraLinked.IsChecked ?? true;
            s.FlashGlowEnabled = SettingsTab.ChkFlashGlow.IsChecked ?? true;
            s.FlashFrequency = (int)SettingsTab.SliderPerMin.Value;
            s.SimultaneousImages = (int)SettingsTab.SliderImages.Value;
            s.HydraLimit = (int)SettingsTab.SliderMaxOnScreen.Value;

            // Visuals
            s.ImageScale = (int)SettingsTab.SliderSize.Value;
            s.FlashOpacity = (int)SettingsTab.SliderOpacity.Value;
            s.FadeDuration = (int)SettingsTab.SliderFade.Value;

            // Video
            s.MandatoryVideosEnabled = SettingsTab.ChkVideoEnabled.IsChecked ?? false;
            s.VideosPerHour = (int)SettingsTab.SliderPerHour.Value;
            s.StrictLockEnabled = SettingsTab.ChkStrictLock.IsChecked ?? false;
            s.AttentionChecksEnabled = SettingsTab.ChkMiniGameEnabled.IsChecked ?? false;
            s.AttentionDensity = (int)SettingsTab.SliderTargets.Value;
            s.RandomizeAttentionTargets = SettingsTab.ChkRandomizeTargets.IsChecked ?? false;
            s.AttentionLifespan = (int)SettingsTab.SliderDuration.Value;
            s.AttentionSize = (int)SettingsTab.SliderTargetSize.Value;

            // Subliminals
            s.SubliminalEnabled = SettingsTab.ChkSubliminalEnabled.IsChecked ?? false;
            s.SubliminalFrequency = (int)SettingsTab.SliderSubPerMin.Value;
            s.SubliminalDuration = (int)SettingsTab.SliderFrames.Value;
            s.SubliminalOpacity = (int)SettingsTab.SliderSubOpacity.Value;
            s.SubAudioEnabled = SettingsTab.ChkAudioWhispers.IsChecked ?? false;
            s.SubAudioVolume = (int)SettingsTab.SliderWhisperVol.Value;

            // System
            s.DualMonitorEnabled = SettingsTab.ChkDualMon.IsChecked ?? true;
            s.RunOnStartup = SettingsTab.ChkWinStart.IsChecked ?? false;
            s.ForceVideoOnLaunch = SettingsTab.ChkVidLaunch.IsChecked ?? false;
            s.AutoStartEngine = SettingsTab.ChkAutoRun.IsChecked ?? false;
            s.StartMinimized = SettingsTab.ChkStartHidden.IsChecked ?? false;
            s.PanicKeyEnabled = !(SettingsTab.ChkNoPanic.IsChecked ?? false);
            s.OfflineMode = SettingsTab.ChkOfflineMode.IsChecked ?? false;
            if (SettingsTab.ChkPerformanceMode != null) s.PerformanceMode = SettingsTab.ChkPerformanceMode.IsChecked ?? false;
            if (SettingsTab.ChkAutoPerformance != null) s.AutoPerformanceMode = SettingsTab.ChkAutoPerformance.IsChecked ?? true;
            if (SettingsTab.CmbMotionLevel != null && SettingsTab.CmbMotionLevel.SelectedIndex >= 0)
                s.MotionLevel = (Models.MotionLevel)SettingsTab.CmbMotionLevel.SelectedIndex;
            if (SettingsTab.ChkVideoHwDecode != null) s.VideoForceHardwareDecoding = SettingsTab.ChkVideoHwDecode.IsChecked ?? false;
            if (SettingsTab.ChkUnifiedOverlay != null) s.UnifiedOverlayHost = SettingsTab.ChkUnifiedOverlay.IsChecked ?? true;
            // Weekly intake pass nudge. Defaults ON (it is the feature's re-engagement hook), but a
            // recurring popup with no off switch is a bug report waiting to happen.
            if (SettingsTab.ChkIntakeNudge != null) s.IntakeNudgeEnabled = SettingsTab.ChkIntakeNudge.IsChecked ?? true;

            // Deeper
            if (SettingsTab.ChkEnableDeeper != null) s.EnableDeeper = SettingsTab.ChkEnableDeeper.IsChecked ?? true;

            // Audio
            s.MasterVolume = (int)SettingsTab.SliderMaster.Value;
            s.AudioDuckingEnabled = SettingsTab.ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)SettingsTab.SliderDuck.Value;
            s.ExcludeBambiCloudFromDucking = SettingsTab.ChkExcludeBambiCloudDucking.IsChecked ?? true;

            // Progression
            s.SpiralEnabled = ProgressionTab.ChkSpiralEnabled.IsChecked ?? false;
            s.SpiralOpacity = (int)ProgressionTab.SliderSpiralOpacity.Value;
            s.PinkFilterEnabled = ProgressionTab.ChkPinkFilterEnabled.IsChecked ?? false;
            s.PinkFilterOpacity = (int)ProgressionTab.SliderPinkOpacity.Value;
            s.BubblesEnabled = ProgressionTab.ChkBubblesEnabled.IsChecked ?? false;
            s.BubblesFrequency = (int)ProgressionTab.SliderBubbleFreq.Value;
            s.LockCardEnabled = ProgressionTab.ChkLockCardEnabled.IsChecked ?? false;
            s.LockCardFrequency = (int)ProgressionTab.SliderLockCardFreq.Value;
            s.LockCardRepeats = (int)ProgressionTab.SliderLockCardRepeats.Value;
            s.LockCardStrict = ProgressionTab.ChkLockCardStrict.IsChecked ?? false;

            // Brain Drain
            s.BrainDrainEnabled = ProgressionTab.ChkBrainDrainEnabled.IsChecked ?? false;
            s.BrainDrainIntensity = (int)ProgressionTab.SliderBrainDrainIntensity.Value;
            s.BrainDrainHighRefresh = ProgressionTab.ChkBrainDrainHighRefresh.IsChecked ?? false;

            // Scheduler - track if settings changed
            var schedulerWasEnabled = s.SchedulerEnabled;
            s.SchedulerEnabled = ProgressionTab.ChkSchedulerEnabled.IsChecked ?? false;
            s.SchedulerStartTime = ProgressionTab.TxtStartTime.Text;
            s.SchedulerEndTime = ProgressionTab.TxtEndTime.Text;
            s.SchedulerMonday = ProgressionTab.ChkMon.IsChecked ?? true;
            s.SchedulerTuesday = ProgressionTab.ChkTue.IsChecked ?? true;
            s.SchedulerWednesday = ProgressionTab.ChkWed.IsChecked ?? true;
            s.SchedulerThursday = ProgressionTab.ChkThu.IsChecked ?? true;
            s.SchedulerFriday = ProgressionTab.ChkFri.IsChecked ?? true;
            s.SchedulerSaturday = ProgressionTab.ChkSat.IsChecked ?? true;
            s.SchedulerSunday = ProgressionTab.ChkSun.IsChecked ?? true;

            // If scheduler was just enabled or settings changed, reset flags and check immediately
            if (s.SchedulerEnabled && !schedulerWasEnabled)
            {
                _schedulerAutoStarted = false;
                _manuallyStoppedDuringSchedule = false;
                // Check scheduler immediately after save completes
                Dispatcher.BeginInvoke(new Action(() => CheckSchedulerAfterSettingsChange()), System.Windows.Threading.DispatcherPriority.Background);
            }
            s.IntensityRampEnabled = ProgressionTab.ChkRampEnabled.IsChecked ?? false;
            s.RampDurationMinutes = (int)ProgressionTab.SliderRampDuration.Value;
            s.SchedulerMultiplier = ProgressionTab.SliderMultiplier.Value;
            
            // Ramp Links
            s.RampLinkFlashOpacity = ProgressionTab.ChkRampLinkFlash.IsChecked ?? false;
            s.RampLinkSpiralOpacity = ProgressionTab.ChkRampLinkSpiral.IsChecked ?? false;
            s.RampLinkPinkFilterOpacity = ProgressionTab.ChkRampLinkPink.IsChecked ?? false;
            s.RampLinkMasterAudio = ProgressionTab.ChkRampLinkMaster.IsChecked ?? false;
            s.RampLinkSubliminalAudio = ProgressionTab.ChkRampLinkSubAudio.IsChecked ?? false;
            s.EndSessionOnRampComplete = ProgressionTab.ChkEndAtRamp.IsChecked ?? false;

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
                showDeeper: () => ShowTab("deeper")
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
