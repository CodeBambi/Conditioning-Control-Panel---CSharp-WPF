using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class SettingsTabView : UserControl
    {
        /// <summary>Width of the marquee's edge fade, in the banner's own units.</summary>
        private const double MarqueeFadePx = 40;

        public SettingsTabView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Rebuilds the marquee's edge-fade mask whenever the banner is resized.
        ///
        /// Self-contained on purpose (no MainWindow hop): it needs nothing but its own width, and
        /// the banner resizes with every window resize. The brush uses
        /// <see cref="BrushMappingMode.Absolute"/> so its start/end points are this element's own
        /// coordinates rather than a bounding box - the shipped relative-mapped version was
        /// stretched across the marquee text's full (thousands of pixels wide) bounds, which put
        /// its 6% fade entirely off-screen and made the effect invisible. Absolute mapping also
        /// means the fade stays a constant ~40px instead of growing with the window.
        /// </summary>
        private void MarqueeFadeHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement host) return;
                double w = e.NewSize.Width;
                if (double.IsNaN(w) || w <= 16) { host.OpacityMask = null; return; }

                // Never eat more than a quarter of the banner from each side on a narrow window.
                double fade = Math.Min(MarqueeFadePx, w / 4.0);
                double stop = fade / w;

                var brush = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(w, 0),
                };
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
                brush.GradientStops.Add(new GradientStop(Colors.Black, stop));
                brush.GradientStops.Add(new GradientStop(Colors.Black, 1.0 - stop));
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                brush.Freeze();
                host.OpacityMask = brush;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MarqueeFadeHost_SizeChanged: {E}", ex.Message);
            }
        }

        private void BrowserLoadingText_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BrowserLoadingText_Click(sender, e);
        }
        private void BrowserSiteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BrowserSiteToggle_Click(sender, e);
        }
        private void BtnAttentionStyle_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnAttentionStyle_Click(sender, e);
        }
        // Phase 2: BtnAudioOutputRefresh_Click / BtnAudioLayers_Click moved with the Audio
        // section to Views/Controls/AppSettings/AudioSettingsSection.xaml.cs.
        // Phase 2: BtnClearStartupVideo_Click / BtnSelectStartupVideo_Click moved with the startup
        // group to Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDiscord_Click(sender, e);
        }
        private void BtnManageAttention_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnManageAttention_Click(sender, e);
        }
        private void BtnManageMessages_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnManageMessages_Click(sender, e);
        }
        private void BtnOpenAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnOpenAssetsFolder_Click(sender, e);
        }
        private void BtnPickAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPickAssetsFolder_Click(sender, e);
        }
        private void BtnPopOutBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPopOutBrowser_Click(sender, e);
        }
        private void BtnMuteBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnMuteBrowser_Click(sender, e);
        }
        private void ChipTakeover_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumChip_Click(PremiumFeature.Takeover);
        }
        private void ChipAwareness_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumChip_Click(PremiumFeature.Awareness);
        }
        private void ChipHaptics_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumChip_Click(PremiumFeature.Haptics);
        }
        private void ChipGradedIntake_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumChip_Click(PremiumFeature.GradedIntake);
        }
        private void ChipVoice_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumChip_Click(PremiumFeature.Voice);
        }
        private void ChipFyp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumChip_Click(PremiumFeature.Fyp);
        }
        private void BtnLockdownMinus_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumLockdownAdjust(-5);
        }
        private void BtnLockdownPlus_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumLockdownAdjust(5);
        }
        private void BtnLockdownGo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumLockdownActivate();
        }
        private void BtnBlinkMinus_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumBlinkAdjust(-5);
        }
        private void BtnBlinkPlus_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumBlinkAdjust(5);
        }
        private void BtnBlinkGo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumBlinkToggle();
        }
        private void ChipRemote_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumRemoteOpenFlyout();
        }
        private void BtnRemoteStart_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumRemoteStart();
        }
        private void BtnQuickLogout_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnQuickLogout_Click(sender, e);
        }
        private void BtnLinkPhone_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LinkPhoneDialog { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        }
        private void BtnReloadBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnReloadBrowser_Click(sender, e);
        }
        private void BtnSubliminalSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSubliminalSettings_Click(sender, e);
        }
        // Phase 2: BtnExportPhrases_Click / BtnImportPhrases_Click moved with the phrase-backup
        // card to Views/Controls/AppSettings/DataSettingsSection.xaml.cs.
        private void BtnTestVideo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTestVideo_Click(sender, e);
        }
        private void BtnUnifiedLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnUnifiedLogin_Click(sender, e);
        }
        private void BtnWebcamTracking_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnWebcamTracking_Click(sender, e);
        }
        private void CardBouncingText_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardBouncingText_Click(sender, e);
        }
        private void CardBubbleCount_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardBubbleCount_Click(sender, e);
        }
        private void CardBubblePop_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardBubblePop_Click(sender, e);
        }
        private void CardFlash_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardFlash_Click(sender, e);
        }
        private void CardLockCard_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardLockCard_Click(sender, e);
        }
        private void CardMindWipe_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardMindWipe_Click(sender, e);
        }
        private void CardPinkFilter_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardPinkFilter_Click(sender, e);
        }
        private void CardSpiral_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardSpiral_Click(sender, e);
        }
        private void CardSubliminal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardSubliminal_Click(sender, e);
        }
        private void CardSystem_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardSystem_Click(sender, e);
        }
        private void CardVideo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardVideo_Click(sender, e);
        }
        private void CardVisuals_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardVisuals_Click(sender, e);
        }
        private void ChkAudioWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAudioWhispers_Changed(sender, e);
        }
        private void ChkAutoPerformance_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAutoPerformance_Changed(sender, e);
        }
        private void ChkClickable_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkClickable_Changed(sender, e);
        }
        private void ChkCorruption_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkCorruption_Changed(sender, e);
        }
        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkDiscordRichPresence_Changed(sender, e);
        }
        private void ChkDualMon_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkDualMon_Changed(sender, e);
        }
        // Phase 2: ChkEnableDeeper_Changed moved with the Deeper master switch to
        // Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
        private void ChkFlashAudio_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkFlashAudio_Changed(sender, e);
        }
        private void ChkFlashEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkFlashEnabled_Changed(sender, e);
        }
        private void ChkFlashGlow_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkFlashGlow_Changed(sender, e);
        }
        private void ChkHydraLinked_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkHydraLinked_Changed(sender, e);
        }
        private void ChkMiniGameEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkMiniGameEnabled_Changed(sender, e);
        }
        // BtnPanicKey_Click / ChkNoPanic_Changed left with their controls in Phase 2 — the panic
        // key is rebound and disabled in Settings → Devices now.
        // Phase 2: ChkOfflineMode_Changed moved with the offline toggle to
        // Views/Controls/AppSettings/DataSettingsSection.xaml.cs.
        private void ChkPerformanceMode_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkPerformanceMode_Changed(sender, e);
        }
        private void CmbMotionLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbMotionLevel_SelectionChanged(sender, e);
        }
        private void ChkRandomizeTargets_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkRandomizeTargets_Changed(sender, e);
        }
        // Phase 2: ChkStartHidden_Click / ChkWinStart_Click moved with the startup group to
        // Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
        private void ChkStrictLock_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkStrictLock_Changed(sender, e);
        }
        private void ChkSubliminalEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkSubliminalEnabled_Changed(sender, e);
        }
        private void ChkVideoEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkVideoEnabled_Changed(sender, e);
        }
        private void ChkVideoHwDecode_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkVideoHwDecode_Changed(sender, e);
        }
        private void ChkUnifiedOverlay_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkUnifiedOverlay_Changed(sender, e);
        }
        private void ImgLogo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ImgLogo_MouseLeftButtonDown(sender, e);
        }
        // Weekly intake pass card face (the flipped-over centre logo tile). Sits INSIDE
        // LogoBrandFrame, so the click would otherwise bubble on into the logo's click-pulse
        // easter egg; MainWindow's handler marks the event Handled to stop that.
        private void IntakePassFace_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.IntakePassFace_MouseLeftButtonDown(sender, e);
        }
        private void SliderAudioSyncIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAudioSyncIntensity_Changed(sender, e);
        }
        private void SliderAudioSyncLatency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderAudioSyncLatency_Changed(sender, e);
        }
        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderDuration_Changed(sender, e);
        }
        private void SliderFade_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderFade_Changed(sender, e);
        }
        private void SliderFlashDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderFlashDuration_Changed(sender, e);
        }
        private void SliderFrames_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderFrames_Changed(sender, e);
        }
        private void SliderImages_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderImages_Changed(sender, e);
        }
        private void SliderMaxOnScreen_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderMaxOnScreen_Changed(sender, e);
        }
        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderOpacity_Changed(sender, e);
        }
        private void SliderPerHour_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderPerHour_Changed(sender, e);
        }
        private void SliderPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderPerMin_Changed(sender, e);
        }
        private void SliderSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderSize_Changed(sender, e);
        }
        private void SliderSubOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderSubOpacity_Changed(sender, e);
        }
        private void SliderSubPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderSubPerMin_Changed(sender, e);
        }
        private void SliderTargetSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderTargetSize_Changed(sender, e);
        }
        private void SliderTargets_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderTargets_Changed(sender, e);
        }
        private void SliderWhisperVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderWhisperVol_Changed(sender, e);
        }
        private void ToggleEnhanceIfPossible_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ToggleEnhanceIfPossible_Changed(sender, e);
        }
        private void ChkForceShowBambiCloud_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkForceShowBambiCloud_Changed(sender, e);
        }
        private void VelvetBtnAppInfo_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.VelvetBtnAppInfo_Click(sender, e);
        }
        private void VelvetBtnSchedulerRamp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.VelvetBtnSchedulerRamp_Click(sender, e);
        }
        private void VelvetBtnWebcam_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.VelvetBtnWebcam_Click(sender, e);
        }
        private void VelvetBtnCatalogue_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCatalogue_Click(sender, e);
        }

        // Training Programs "Today" card (row 0). Loaded is the card's own first-paint hook so
        // the dashboard can show it without the user visiting the Programs tab first.
        private void ProgramTodayCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ProgramTodayCard_Loaded(sender, e);
        }
        private void ProgramTodayCard_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ProgramTodayCard_Click(sender, e);
        }
    }
}
