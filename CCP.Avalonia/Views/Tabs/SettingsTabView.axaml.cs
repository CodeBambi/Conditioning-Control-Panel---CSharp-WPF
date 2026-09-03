using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The Dashboard (tab key <c>settings</c>), PORTED from
    /// ConditioningControlPanel/Views/Tabs/SettingsTabView.xaml.cs.
    ///
    /// <para><b>This file is the host only.</b> On WPF all but three of its members are one-line
    /// shims that look up the hosting MainWindow and forward to a MainWindow partial of the same
    /// name - the premium rail, the mosaic wall, the browser card, the account strip and the
    /// quick-toggle pills every one of them. MainWindow is a WPF type, so on this head each shim
    /// is a stub wired to the same control with the same handler name, so the eventual wiring
    /// diffs cleanly.</para>
    ///
    /// <para><b>The three that are NOT shims are ported for real:</b> the marquee's edge-fade
    /// mask (it needs nothing but its own width), the advanced-audio disclosure (purely local -
    /// nothing below it owns state) and starting the one ambient canvas.</para>
    ///
    /// <para><b>Dropped:</b> the <c>IsVisibleChanged</c> hook that told
    /// <c>MainWindow.OnDashboardTabVisibilityChanged</c> when the Dashboard came and went. The
    /// seam is MainWindow's, not this view's; it is a stub on the Avalonia twin
    /// (<c>AttachedToVisualTree</c>) below.</para>
    /// </summary>
    public partial class SettingsTabView : UserControl
    {
        /// <summary>Width of the marquee's edge fade, in the banner's own units.</summary>
        private const double MarqueeFadePx = 40;

        /// <summary>
        /// Ambient density behind the mosaic. Twin of <c>MainWindow.DashboardFx.cs</c>'s private
        /// <c>MosaicFxIntensity</c> / <c>MosaicFogPuffs</c>, kept to the digit so the wall looks
        /// the same as it does on the WPF head.
        /// </summary>
        private const double MosaicFxIntensity = 0.62;
        private const int MosaicFogPuffs = 3;

        private bool _fxComposed;

        public SettingsTabView()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            MarqueeFadeHost.SizeChanged += MarqueeFadeHost_SizeChanged;
            HomeBtnAudioAdvanced.IsCheckedChanged += HomeBtnAudioAdvanced_Changed;

            // Composed on first ATTACH rather than in the constructor: the canvas needs a live
            // visual tree to size its layers against. WPF hooked Loaded/IsVisibleChanged;
            // Avalonia's twin for "the tree is up" is AttachedToVisualTree. Views stay
            // instantiated for the app's life, so the _fxComposed guard keeps this to once.
            AttachedToVisualTree += OnDashboardAttached;

            WireStubs();
        }

        // ------------------------------------------------------------------------------
        // The real ports.
        // ------------------------------------------------------------------------------

        private void OnDashboardAttached(object? sender, EventArgs e)
        {
            try
            {
                if (_fxComposed) return;
                _fxComposed = true;

                // The one ambient canvas on the whole Dashboard. Config copied verbatim from
                // MainWindow.DashboardFx.cs:165.
                MosaicFx.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField,
                    Intensity = MosaicFxIntensity,
                    FogPuffs = MosaicFogPuffs,
                });

                // ponytail: needs MainWindow.RegisterTabFx("settings", MosaicFx) - the
                // park/resume hook and the motion kill-switch's reach - and
                // MainWindow.OnDashboardTabVisibilityChanged, which owns the one-shot "? box"
                // explainer. Both are MainWindow's; wired when the ambient registry and the tab
                // navigation move to Core. Until then the canvas parks itself on detach
                // (AmbientFxCanvas.Evaluate), which is why running it here is safe.
            }
            catch (Exception)
            {
                // A missing ambient layer must never take the Dashboard down with it.
            }
        }

        /// <summary>
        /// Rebuilds the marquee's edge-fade mask whenever the banner is resized.
        ///
        /// Self-contained on purpose (no MainWindow hop): it needs nothing but its own width, and
        /// the banner resizes with every window resize. The brush uses ABSOLUTE relative units so
        /// its start/end points are this element's own coordinates rather than a bounding box -
        /// the shipped relative-mapped version was stretched across the marquee text's full
        /// (thousands of pixels wide) bounds, which put its 6% fade entirely off-screen and made
        /// the effect invisible. Absolute mapping also means the fade stays a constant ~40px
        /// instead of growing with the window.
        ///
        /// WPF's <c>BrushMappingMode.Absolute</c> is Avalonia's <c>RelativeUnit.Absolute</c> on
        /// the point itself; there is no Freeze() to call.
        /// </summary>
        private void MarqueeFadeHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            try
            {
                if (sender is not Visual host) return;
                double w = e.NewSize.Width;
                if (double.IsNaN(w) || w <= 16) { host.OpacityMask = null; return; }

                // Never eat more than a quarter of the banner from each side on a narrow window.
                double fade = Math.Min(MarqueeFadePx, w / 4.0);
                double stop = fade / w;

                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(w, 0, RelativeUnit.Absolute),
                };
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
                brush.GradientStops.Add(new GradientStop(Colors.Black, stop));
                brush.GradientStops.Add(new GradientStop(Colors.Black, 1.0 - stop));
                brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                host.OpacityMask = brush;
            }
            catch (Exception)
            {
                // Cosmetic. A mask that cannot be built leaves the banner un-faded, not broken.
            }
        }

        /// <summary>The advanced disclosure. Purely local - nothing below it owns state, so the
        /// shell has no reason to know whether the drawer is open.</summary>
        private void HomeBtnAudioAdvanced_Changed(object? sender, RoutedEventArgs e)
        {
            if (HomeAudioAdvanced == null || HomeBtnAudioAdvanced == null) return;
            HomeAudioAdvanced.IsVisible = HomeBtnAudioAdvanced.IsChecked == true;
        }

        // ------------------------------------------------------------------------------
        // The stubs. Every one of these forwards to a MainWindow partial on the WPF head; the
        // handler NAMES are kept verbatim, because they are the behaviour-parity contract and
        // the wiring is a rename away once those partials reach Core.
        //
        // ponytail: needs MainWindow (PremiumRail, DashboardFx, Browser, Login, HomeAudio,
        // ProgramsTab, TeaseCard, TabNavigation), PremiumFeature, TierGate, LinkPhoneDialog and
        // LayeredAudioWindow. None of them is on this head yet.
        // ------------------------------------------------------------------------------
        private void WireStubs()
        {
            // Training Programs "Today" card (row 0). Loaded is the card's own first-paint hook
            // so the dashboard can show it without visiting the Programs tab first.
            ProgramTodayCard.Loaded += ProgramTodayCard_Loaded;
            ProgramTodayCard.Click += ProgramTodayCard_Click;

            // Right-click anywhere in the chip stack: MainWindow works out which chip took the
            // hit and turns that feature on/off. Left-click (the Chip*_Click stubs) opens it.
            PremiumRailContent.PointerReleased += PremiumRailContent_RightClick;
            ChipTakeover.Click += ChipTakeover_Click;
            ChipAwareness.Click += ChipAwareness_Click;
            ChipHaptics.Click += ChipHaptics_Click;
            ChipGradedIntake.Click += ChipGradedIntake_Click;
            ChipVoice.Click += ChipVoice_Click;
            ChipFyp.Click += ChipFyp_Click;
            BtnLockdownMinus.Click += BtnLockdownMinus_Click;
            BtnLockdownPlus.Click += BtnLockdownPlus_Click;
            BtnLockdownGo.Click += BtnLockdownGo_Click;
            BtnBlinkMinus.Click += BtnBlinkMinus_Click;
            BtnBlinkPlus.Click += BtnBlinkPlus_Click;
            BtnBlinkGo.Click += BtnBlinkGo_Click;
            ChipRemote.Click += ChipRemote_Click;
            BtnRemoteStart.Click += BtnRemoteStart_Click;

            // Velvet mosaic. Left-click opens the half's Studio module, right-click toggles it;
            // the three diagonal combo tiles forward per-half. A = the top-left half, B = the
            // bottom-right, as authored in XAML.
            CardFlash.Click += CardFlash_Click;
            CardFlash.ToggleRequested += CardFlash_Toggle;
            CardSubliminal.Click += CardSubliminal_Click;
            CardSubliminal.ToggleRequested += CardSubliminal_Toggle;
            CardBouncingText.Click += CardBouncingText_Click;
            CardBouncingText.ToggleRequested += CardBouncingText_Toggle;
            CardBubblePop.Click += CardBubblePop_Click;
            CardBubblePop.ToggleRequested += CardBubblePop_Toggle;
            CardLockCard.Click += CardLockCard_Click;
            CardLockCard.ToggleRequested += CardLockCard_Toggle;
            ComboVideoBubble.ClickA += ComboVideoBubble_ClickA;
            ComboVideoBubble.ClickB += ComboVideoBubble_ClickB;
            ComboVideoBubble.ToggleA += ComboVideoBubble_ToggleA;
            ComboVideoBubble.ToggleB += ComboVideoBubble_ToggleB;
            ComboSpiralPink.ClickA += ComboSpiralPink_ClickA;
            ComboSpiralPink.ClickB += ComboSpiralPink_ClickB;
            ComboSpiralPink.ToggleA += ComboSpiralPink_ToggleA;
            ComboSpiralPink.ToggleB += ComboSpiralPink_ToggleB;
            ComboMindDrain.ClickA += ComboMindDrain_ClickA;
            ComboMindDrain.ClickB += ComboMindDrain_ClickB;
            ComboMindDrain.ToggleA += ComboMindDrain_ToggleA;
            ComboMindDrain.ToggleB += ComboMindDrain_ToggleB;
            CardMystery.Click += CardMystery_Click;
            MysteryRevealFace.PointerReleased += MysteryRevealFace_Click;
            CardVault.Click += CardVault_Click;
            CardJustDrop.Click += CardJustDrop_Click;
            LogoBrandFrame.PointerPressed += ImgLogo_MouseLeftButtonDown;
            IntakePassFace.PointerPressed += IntakePassFace_MouseLeftButtonDown;

            // Browser card.
            BrowserLoadingText.PointerPressed += BrowserLoadingText_Click;
            RbBambiCloud.Click += BrowserSiteToggle_Click;
            RbHypnoTube.Click += BrowserSiteToggle_Click;
            BtnReloadBrowser.Click += BtnReloadBrowser_Click;
            BtnWebcamTracking.Click += BtnWebcamTracking_Click;
            BtnMuteBrowser.Click += BtnMuteBrowser_Click;
            BtnPopOutBrowser.Click += BtnPopOutBrowser_Click;
            ToggleEnhanceIfPossible.IsCheckedChanged += ToggleEnhanceIfPossible_Changed;
            ChkForceShowBambiCloud.IsCheckedChanged += ChkForceShowBambiCloud_Changed;

            // Home audio card. Pure forwarding, like every re-parented cell: the shell owns the
            // canonical Settings/Audio controls and mirrors both ways.
            HomeSliderMaster.ValueChanged += HomeSliderMaster_Changed;
            HomeChkAudioDuck.IsCheckedChanged += HomeChkAudioDuck_Changed;
            HomeSliderVideoVolume.ValueChanged += HomeSliderVideoVolume_Changed;
            HomeSliderDuck.ValueChanged += HomeSliderDuck_Changed;
            HomeChkExcludeBambiCloudDucking.IsCheckedChanged += HomeChkExcludeBambiCloudDucking_Changed;
            HomeCmbAudioOutputDevice.SelectionChanged += HomeCmbAudioOutputDevice_SelectionChanged;
            HomeBtnAudioOutputRefresh.Click += HomeBtnAudioOutputRefresh_Click;
            HomeBtnTestAudio.Click += HomeBtnTestAudio_Click;
            HomeBtnAudioLayers.Click += HomeBtnAudioLayers_Click;

            // Companion + account strips.
            CompanionStrip.PointerPressed += CompanionStrip_Click;
            BtnUnifiedLogin.Click += BtnUnifiedLogin_Click;
            BtnQuickLogout.Click += BtnQuickLogout_Click;
            BtnLinkPhone.Click += BtnLinkPhone_Click;
            BtnDiscord.Click += BtnDiscord_Click;
            ChkQuickDiscordRichPresence.IsCheckedChanged += ChkDiscordRichPresence_Changed;

            // Quick-toggles row.
            VelvetBtnWebcam.Click += VelvetBtnWebcam_Click;
            VelvetBtnSystem.Click += VelvetBtnSystem_Click;
            VelvetBtnAppInfo.Click += VelvetBtnAppInfo_Click;
            VelvetBtnSchedulerRamp.Click += VelvetBtnSchedulerRamp_Click;
            VelvetBtnCatalogue.Click += VelvetBtnCatalogue_Click;
        }

        // -- premium rail ---------------------------------------------------------------
        private void PremiumRailContent_RightClick(object? sender, PointerReleasedEventArgs e) { }  // mw.PremiumRail_RightClick(...)
        private void ChipTakeover_Click(object? sender, RoutedEventArgs e) { }        // mw.PremiumChip_Click(PremiumFeature.Takeover)
        private void ChipAwareness_Click(object? sender, RoutedEventArgs e) { }       // mw.PremiumChip_Click(PremiumFeature.Awareness)
        private void ChipHaptics_Click(object? sender, RoutedEventArgs e) { }         // mw.PremiumChip_Click(PremiumFeature.Haptics)
        private void ChipGradedIntake_Click(object? sender, RoutedEventArgs e) { }    // mw.PremiumChip_Click(PremiumFeature.GradedIntake)
        private void ChipVoice_Click(object? sender, RoutedEventArgs e) { }           // mw.PremiumChip_Click(PremiumFeature.Voice)
        private void ChipFyp_Click(object? sender, RoutedEventArgs e) { }             // mw.PremiumChip_Click(PremiumFeature.Fyp)
        private void BtnLockdownMinus_Click(object? sender, RoutedEventArgs e) { }    // mw.PremiumLockdownAdjust(-5)
        private void BtnLockdownPlus_Click(object? sender, RoutedEventArgs e) { }     // mw.PremiumLockdownAdjust(5)
        private void BtnLockdownGo_Click(object? sender, RoutedEventArgs e) { }       // mw.PremiumLockdownActivate()
        private void BtnBlinkMinus_Click(object? sender, RoutedEventArgs e) { }       // mw.PremiumBlinkAdjust(-5)
        private void BtnBlinkPlus_Click(object? sender, RoutedEventArgs e) { }        // mw.PremiumBlinkAdjust(5)
        private void BtnBlinkGo_Click(object? sender, RoutedEventArgs e) { }          // mw.PremiumBlinkToggle()
        private void ChipRemote_Click(object? sender, RoutedEventArgs e) { }          // mw.PremiumRemoteOpenFlyout()
        private void BtnRemoteStart_Click(object? sender, RoutedEventArgs e) { }      // mw.PremiumRemoteStart()

        // -- velvet mosaic (4x4 hybrid wall) --------------------------------------------
        private void CardFlash_Click(object? sender, RoutedEventArgs e) { }           // mw.CardFlash_Click(...)
        private void CardFlash_Toggle(object? sender, RoutedEventArgs e) { }          // mw.ToggleWallFeature("flash")
        private void CardSubliminal_Click(object? sender, RoutedEventArgs e) { }      // mw.CardSubliminal_Click(...)
        private void CardSubliminal_Toggle(object? sender, RoutedEventArgs e) { }     // mw.ToggleWallFeature("subliminal")
        private void CardBouncingText_Click(object? sender, RoutedEventArgs e) { }    // mw.CardBouncingText_Click(...)
        private void CardBouncingText_Toggle(object? sender, RoutedEventArgs e) { }   // mw.ToggleWallFeature("bouncingtext")
        private void CardBubblePop_Click(object? sender, RoutedEventArgs e) { }       // mw.CardBubblePop_Click(...)
        private void CardBubblePop_Toggle(object? sender, RoutedEventArgs e) { }      // mw.ToggleWallFeature("bubbles")
        private void CardLockCard_Click(object? sender, RoutedEventArgs e) { }        // mw.CardLockCard_Click(...)
        private void CardLockCard_Toggle(object? sender, RoutedEventArgs e) { }       // mw.ToggleWallFeature("lockcard")
        private void ComboVideoBubble_ClickA(object? sender, RoutedEventArgs e) { }   // mw.OpenStudioModule("video")
        private void ComboVideoBubble_ClickB(object? sender, RoutedEventArgs e) { }   // mw.OpenStudioModule("bubblecount")
        private void ComboVideoBubble_ToggleA(object? sender, RoutedEventArgs e) { }  // mw.ToggleWallFeature("video")
        private void ComboVideoBubble_ToggleB(object? sender, RoutedEventArgs e) { }  // mw.ToggleWallFeature("bubblecount")
        private void ComboSpiralPink_ClickA(object? sender, RoutedEventArgs e) { }    // mw.OpenStudioModule("spiral")
        private void ComboSpiralPink_ClickB(object? sender, RoutedEventArgs e) { }    // mw.OpenStudioModule("pinkfilter")
        private void ComboSpiralPink_ToggleA(object? sender, RoutedEventArgs e) { }   // mw.ToggleWallFeature("spiral")
        private void ComboSpiralPink_ToggleB(object? sender, RoutedEventArgs e) { }   // mw.ToggleWallFeature("pinkfilter")
        private void ComboMindDrain_ClickA(object? sender, RoutedEventArgs e) { }     // mw.OpenStudioModule("mindwipe")
        private void ComboMindDrain_ClickB(object? sender, RoutedEventArgs e) { }     // mw.OpenStudioModule("braindrain")
        private void ComboMindDrain_ToggleA(object? sender, RoutedEventArgs e) { }    // mw.ToggleWallFeature("mindwipe")
        private void ComboMindDrain_ToggleB(object? sender, RoutedEventArgs e) { }    // mw.ToggleWallFeature("braindrain")
        private void CardMystery_Click(object? sender, RoutedEventArgs e) { }         // mw.CardMystery_Click(...)
        /// <summary>Clicking the revealed face is clicking the box - one navigation, two faces.</summary>
        private void MysteryRevealFace_Click(object? sender, PointerReleasedEventArgs e) { }   // mw.CardMystery_Click(...)
        private void CardVault_Click(object? sender, RoutedEventArgs e) { }           // mw.CardVault_Click(...)
        private void CardJustDrop_Click(object? sender, RoutedEventArgs e) { }        // mw.CardJustDrop_Click(...)
        private void ImgLogo_MouseLeftButtonDown(object? sender, PointerPressedEventArgs e) { } // mw.ImgLogo_MouseLeftButtonDown(...)
        /// <summary>Weekly intake pass card face (the flipped-over centre logo tile). Sits INSIDE
        /// LogoBrandFrame, so the click would otherwise bubble on into the logo's click-pulse
        /// easter egg; MainWindow's handler marks the event handled to stop that.</summary>
        private void IntakePassFace_MouseLeftButtonDown(object? sender, PointerPressedEventArgs e) { } // mw.IntakePassFace_MouseLeftButtonDown(...)

        // -- browser card ---------------------------------------------------------------
        private void BrowserLoadingText_Click(object? sender, PointerPressedEventArgs e) { }   // mw.BrowserLoadingText_Click(...)
        private void BrowserSiteToggle_Click(object? sender, RoutedEventArgs e) { }   // mw.BrowserSiteToggle_Click(...)
        private void BtnReloadBrowser_Click(object? sender, RoutedEventArgs e) { }    // mw.BtnReloadBrowser_Click(...)
        private void BtnWebcamTracking_Click(object? sender, RoutedEventArgs e) { }   // mw.BtnWebcamTracking_Click(...)
        private void BtnMuteBrowser_Click(object? sender, RoutedEventArgs e) { }      // mw.BtnMuteBrowser_Click(...)
        private void BtnPopOutBrowser_Click(object? sender, RoutedEventArgs e) { }    // mw.BtnPopOutBrowser_Click(...)
        private void ToggleEnhanceIfPossible_Changed(object? sender, RoutedEventArgs e) { }  // mw.ToggleEnhanceIfPossible_Changed(...)
        private void ChkForceShowBambiCloud_Changed(object? sender, RoutedEventArgs e) { }   // mw.ChkForceShowBambiCloud_Changed(...)

        // -- home audio card ------------------------------------------------------------
        private void HomeSliderMaster_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }      // mw.HomeSliderMaster_Changed(...)
        private void HomeChkAudioDuck_Changed(object? sender, RoutedEventArgs e) { }                     // mw.HomeChkAudioDuck_Changed(...)
        private void HomeSliderVideoVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e) { } // mw.HomeSliderVideoVolume_Changed(...)
        private void HomeSliderDuck_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }        // mw.HomeSliderDuck_Changed(...)
        private void HomeChkExcludeBambiCloudDucking_Changed(object? sender, RoutedEventArgs e) { }      // mw.HomeChkExcludeBambiCloudDucking_Changed(...)
        private void HomeCmbAudioOutputDevice_SelectionChanged(object? sender, SelectionChangedEventArgs e) { } // mw.HomeCmbAudioOutputDevice_SelectionChanged(...)
        private void HomeBtnAudioOutputRefresh_Click(object? sender, RoutedEventArgs e) { }  // mw.BtnAudioOutputRefresh_Click(...)
        private void HomeBtnTestAudio_Click(object? sender, RoutedEventArgs e) { }           // mw.BtnTestAudio_Click(...)
        /// <summary>Self-contained on the old dashboard too - it only opens a window.</summary>
        private void HomeBtnAudioLayers_Click(object? sender, RoutedEventArgs e) { }         // LayeredAudioWindow.Open(this)

        // -- companion + account strips --------------------------------------------------
        /// <summary>Home's companion strip. Pure navigation into the Companion door - the strip
        /// owns no portrait and no clock, so there is nothing to start or stop here.</summary>
        private void CompanionStrip_Click(object? sender, PointerPressedEventArgs e) { }     // mw.ShowTab("companion")
        private void BtnUnifiedLogin_Click(object? sender, RoutedEventArgs e) { }            // mw.BtnUnifiedLogin_Click(...)
        private void BtnQuickLogout_Click(object? sender, RoutedEventArgs e) { }             // mw.BtnQuickLogout_Click(...)
        private void BtnLinkPhone_Click(object? sender, RoutedEventArgs e) { }               // new LinkPhoneDialog().ShowDialog(...)
        private void BtnDiscord_Click(object? sender, RoutedEventArgs e) { }                 // mw.BtnDiscord_Click(...)
        private void ChkDiscordRichPresence_Changed(object? sender, RoutedEventArgs e) { }   // mw.ChkDiscordRichPresence_Changed(...)

        // -- quick-toggles row -----------------------------------------------------------
        private void VelvetBtnWebcam_Click(object? sender, RoutedEventArgs e) { }            // mw.VelvetBtnWebcam_Click(...)
        /// <summary>Quick-toggles row · "System". This pill is the ONLY route to
        /// <c>MainWindow.CardSystem_Click</c> since the mosaic tile was deleted.</summary>
        private void VelvetBtnSystem_Click(object? sender, RoutedEventArgs e) { }            // mw.CardSystem_Click(...)
        private void VelvetBtnAppInfo_Click(object? sender, RoutedEventArgs e) { }           // mw.VelvetBtnAppInfo_Click(...)
        private void VelvetBtnSchedulerRamp_Click(object? sender, RoutedEventArgs e) { }     // mw.VelvetBtnSchedulerRamp_Click(...)
        private void VelvetBtnCatalogue_Click(object? sender, RoutedEventArgs e) { }         // mw.BtnCatalogue_Click(...)

        /// <summary>
        /// Opens the head's diagnostics window. A second window rather than a swap, so the shell
        /// keeps its state and the diagnostics window's own Back button is just a Close.
        /// Re-entrant: a second click focuses the window that is already open instead of stacking
        /// duplicates.
        /// </summary>
        private void BtnOpenDiagnostics_Click(object? sender, RoutedEventArgs e)
        {
            if (_diagnostics is { } open)
            {
                open.Activate();
                return;
            }

            var owner = TopLevel.GetTopLevel(this) as Window;
            _diagnostics = new MainWindow();
            _diagnostics.Closed += (_, _) => _diagnostics = null;

            if (owner is not null) _diagnostics.Show(owner);
            else _diagnostics.Show();
        }

        private MainWindow? _diagnostics;

        // -- training programs "today" card ----------------------------------------------
        private void ProgramTodayCard_Loaded(object? sender, RoutedEventArgs e) { }          // mw.ProgramTodayCard_Loaded(...)
        private void ProgramTodayCard_Click(object? sender, RoutedEventArgs e) { }           // mw.ProgramTodayCard_Click(...)
    }
}
