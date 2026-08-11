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

        // PHASE 8 (demolition): 41 shims left this file with LegacyDashboardHost and the Collapsed
        // CardSystem tile - the whole Flash/Visuals/Video/Subliminal dial set, the ChkDualMon and
        // performance twins, and CardSystem_Click. Nothing they reached was lost: the dials live on
        // the Studio rack's *FeatureControl panels, the performance switches on
        // Views/Controls/AppSettings/PerformanceSettingsSection, ChkDualMon on
        // Features/SystemFeatureControl.ChkMultiMon, and the System popup on VelvetBtnSystem_Click
        // below - which is now the ONLY caller of MainWindow.CardSystem_Click.
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
        // Phase 2: BtnAudioOutputRefresh_Click / BtnAudioLayers_Click moved with the Audio
        // section to Views/Controls/AppSettings/AudioSettingsSection.xaml.cs.
        // Phase 2: BtnClearStartupVideo_Click / BtnSelectStartupVideo_Click moved with the startup
        // group to Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDiscord_Click(sender, e);
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
        // Right-click anywhere in the chip stack: MainWindow works out which chip took the hit
        // and turns that feature on/off. Left-click (the Chip*_Click shims below) opens it.
        private void PremiumRailContent_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PremiumRail_RightClick(sender, e);
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
        // Phase 2: BtnExportPhrases_Click / BtnImportPhrases_Click moved with the phrase-backup
        // card to Views/Controls/AppSettings/DataSettingsSection.xaml.cs.
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
        // ---- velvet mosaic (3x3 destinations, 2026-08-11) --------------------------------
        // The twelve FX shims that used to live here (CardFlash / CardSpiral / CardPinkFilter
        // / ...) went with their tiles. Nothing they reached was lost: every one of them
        // called MainWindow.OpenStudioModule(key), and the Studio door's rack still lists all
        // sixteen modules by name. These eight replace them and follow the same shape - the
        // view forwards, MainWindow decides.
        private void CardDtrh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardDtrh_Click(sender, e);
        }
        private void CardGoon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardGoon_Click(sender, e);
        }
        private void CardFyp_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardFyp_Click(sender, e);
        }
        private void CardIntake_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardIntake_Click(sender, e);
        }
        private void CardRemote_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardRemote_Click(sender, e);
        }
        private void CardLoom_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardLoom_Click(sender, e);
        }
        private void CardDeeper_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardDeeper_Click(sender, e);
        }
        private void CardJustDrop_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardJustDrop_Click(sender, e);
        }
        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkDiscordRichPresence_Changed(sender, e);
        }
        // Phase 2: ChkEnableDeeper_Changed moved with the Deeper master switch to
        // Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
        // BtnPanicKey_Click / ChkNoPanic_Changed left with their controls in Phase 2 — the panic
        // key is rebound and disabled in Settings → Devices now.
        // Phase 2: ChkOfflineMode_Changed moved with the offline toggle to
        // Views/Controls/AppSettings/DataSettingsSection.xaml.cs.
        // Phase 2: ChkStartHidden_Click / ChkWinStart_Click moved with the startup group to
        // Views/Controls/AppSettings/GeneralSettingsSection.xaml.cs.
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
        // Phase 3: SliderAudioSyncLatency_Changed / SliderAudioSyncIntensity_Changed moved with
        // the audio-sync tuning panel to Views/Controls/AppSettings/AudioSettingsSection.xaml.cs.
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
        /// <summary>
        /// Quick-toggles row · "System". Phase 3 traded the ⚙ mosaic tile for the Brain Drain
        /// rescue and moved its entry point here; the tile itself is still in the tree (Collapsed)
        /// and both callers reach the SAME <c>MainWindow.CardSystem_Click</c>, so the popup, its
        /// mod-aware title and its <c>NotifyFeatureOpened("System")</c> bark are byte-for-byte
        /// what they were.
        /// </summary>
        private void VelvetBtnSystem_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CardSystem_Click(sender, e);
        }
        /// <summary>
        /// Home's companion strip. Pure navigation into the Companion door - the strip owns no
        /// portrait and no clock, so there is nothing to start or stop here (see the XAML note:
        /// CompanionTheme.xaml budgets ONE Forever storyboard for the companion app-wide, and
        /// CompanionHeroCard already spends it).
        /// </summary>
        private void CompanionStrip_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ShowTab("companion");
        }

        // Home audio card. Pure forwarding, like every re-parented cell: the shell owns the
        // canonical Settings/Audio controls and mirrors both ways. See MainWindow.HomeAudio.cs.
        private void HomeSliderMaster_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeSliderMaster_Changed(sender, e);
        }

        private void HomeChkAudioDuck_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeChkAudioDuck_Changed(sender, e);
        }

        /// <summary>The advanced disclosure. Purely local - nothing below it owns state, so the
        /// shell has no reason to know whether the drawer is open.</summary>
        private void HomeBtnAudioAdvanced_Changed(object sender, RoutedEventArgs e)
        {
            if (HomeAudioAdvanced == null || HomeBtnAudioAdvanced == null) return;
            HomeAudioAdvanced.Visibility = HomeBtnAudioAdvanced.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void HomeSliderVideoVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeSliderVideoVolume_Changed(sender, e);
        }

        private void HomeSliderDuck_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeSliderDuck_Changed(sender, e);
        }

        private void HomeChkExcludeBambiCloudDucking_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeChkExcludeBambiCloudDucking_Changed(sender, e);
        }

        private void HomeCmbAudioOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.HomeCmbAudioOutputDevice_SelectionChanged(sender, e);
        }

        private void HomeBtnAudioOutputRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnAudioOutputRefresh_Click(sender, e);
        }

        private void HomeBtnTestAudio_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnTestAudio_Click(sender, e);
        }

        // Self-contained on the old dashboard too - it only opens a window.
        private void HomeBtnAudioLayers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new LayeredAudioWindow { Owner = Window.GetWindow(this) }.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Home/Audio: Audio Layers window launch failed");
            }
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
