using ConditioningControlPanel.Views.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// Ported from ConditioningControlPanel/Views/Tabs/HapticsTabView.xaml.cs.
    ///
    /// <para>On WPF this file is a forwarding shim: every handler hands straight to the MainWindow
    /// partial (ConditioningControlPanel/MainWindow/MainWindow.Haptics.cs), which owns all the
    /// state. Here the view owns it (Connection.cs + Dials.cs), because this view calls
    /// InitializeComponent and its x:Name fields are real (CLAUDE.md trap 7).</para>
    ///
    /// <para><b>The art hooks are real again.</b> WPF's Loaded/Unloaded pair repainted the vibe.png
    /// plates through ModResourceResolver; here <see cref="Helpers.ModArt.TryLoad"/> answers the
    /// same question (mod override first, this head's avares:// copy second) and
    /// <see cref="CoreMods.ModChanged"/> is the repaint signal. Neither the resolver nor the art
    /// is the blocker any more - Assets/features/vibe.png is linked into this head.</para>
    /// </summary>
    public partial class HapticsTabView : UserControl
    {
        public HapticsTabView()
        {
            InitializeComponent();

            // The real VMs (moved to Core with WPF's MainWindow.Haptics.cs:61-140 builders): chips,
            // the four routing groups sharing one expansion scope, and the device-fed toy cards.
            ProviderChipsList.ItemsSource = _providerChips;
            RoutingGroupsList.ItemsSource = _routingGroups =
                HapticRoutingGroupVm.BuildDefault(Cfg, _rowScope, (_, _) => RefreshAudioSyncCardVisibility());
            ToyCardsList.ItemsSource = _toyCards;
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) SyncLiveStatusTimer(); };

            ApplyFeatureArt();
            LoadHapticsSettingsToUi();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged += OnModChanged;
            ApplyFeatureArt();
            _attached = true;
            HookHapticService(true);
            RefreshHapticToys();
            SyncLiveStatusTimer();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
            _attached = false;
            HookHapticService(false);
            SyncLiveStatusTimer();
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>ModChanged can be raised off the UI thread, so the repaint is marshalled.</summary>
        private void OnModChanged(object? sender, ModPackage mod) =>
            Dispatcher.UIThread.Post(ApplyFeatureArt);

        /// <summary>
        /// The two vibe.png plates, mod override first. A null answer means neither the mod nor this
        /// head has the picture; the authored bare surface then stands, which is what WPF's resolver
        /// falls back to as well.
        /// </summary>
        private void ApplyFeatureArt()
        {
            var art = Helpers.ModArt.TryLoad("features/vibe.png");
            if (art == null) return;

            HapticsHeroArt.Background = new ImageBrush(art)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Right,
            };
            ImgVideoHapticSync.Source = art;
        }

        // Every handler is wired (HapticsTabView.Connection.cs + HapticsTabView.Dials.cs), mirroring
        // WPF MainWindow.Haptics.cs, which owns them there.
        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.BtnGateUnlock_Click(sender, e);
        private void ChkHapticsEnabled_Changed(object? sender, RoutedEventArgs e) => OnHapticsEnabledChanged();
        private void BtnHapticConnect_Click(object? sender, RoutedEventArgs e) => OnHapticConnectClicked();
        private void BtnHapticPanic_Click(object? sender, RoutedEventArgs e) => OnHapticPanicClicked();
        private void BtnHapticTest_Click(object? sender, RoutedEventArgs e) => OnHapticTestClicked();
        private void BtnHapticToyTest_Click(object? sender, RoutedEventArgs e) => OnHapticToyTestClicked(sender);
        private async void BtnHapticsHelp_Click(object? sender, RoutedEventArgs e)
        {
            // WPF: the wizard rewrites provider flags and addresses, so re-read afterwards.
            if (TopLevel.GetTopLevel(this) is Windows.MainShellWindow shell) await shell.ShowHapticsSetupAsync();
            LoadHapticsSettingsToUi();
        }
        private void ChkHapticProvider_Changed(object? sender, RoutedEventArgs e) => OnHapticProviderChanged(sender);
        private void ChkHapticAutoConnect_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading || Cfg.AutoConnect == (ChkHapticAutoConnect.IsChecked == true)) return;
            Cfg.AutoConnect = ChkHapticAutoConnect.IsChecked == true;
            CoreSettings.Save();
        }
        private void TxtHapticUrl_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_loading || (Cfg.LovenseUrl ?? "") == (TxtHapticUrl.Text ?? "")) return;   // compare-before-write
            Cfg.LovenseUrl = TxtHapticUrl.Text ?? "";   // mirrors into V2.Provider("lovense").Url
            CoreSettings.Save();
        }
        private void TxtHapticIntifaceUrl_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_loading || (Cfg.ButtplugUrl ?? "") == (TxtHapticIntifaceUrl.Text ?? "")) return;
            Cfg.ButtplugUrl = TxtHapticIntifaceUrl.Text ?? "";   // what ButtplugProviderV2 reads
            CoreSettings.Save();
        }
        private void SliderHapticIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) { if (TxtHapticIntensity != null) OnHapticIntensityChanged(); }
        private void SliderHapticMaxPower_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) { if (TxtHapticMaxPower != null) OnHapticMaxPowerChanged(); }
        private void SliderHapticDtrhAmbient_Changed(object? sender, RangeBaseValueChangedEventArgs e) { if (TxtHapticDtrhAmbient != null) OnDtrhAmbientChanged(); }
        private void CmbHapticDtrhDensity_SelectionChanged(object? sender, SelectionChangedEventArgs e) => OnDtrhDensityChanged();
        private void CmbPatternMode_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateHapticPatternPreview();
        private void SliderPatternIntensity_Changed(object? sender, RangeBaseValueChangedEventArgs e) { if (TxtPatternIntensity == null) return; TxtPatternIntensity.Text = $"{(int)SliderPatternIntensity.Value}%"; UpdateHapticPatternPreview(); }
        private void BtnPatternPlay_Click(object? sender, RoutedEventArgs e) => OnPatternPlayClicked();
        private void PatternPreviewCanvas_SizeChanged(object? sender, SizeChangedEventArgs e) => UpdateHapticPatternPreview();
        private void SliderVideoHapticDelay_Changed(object? sender, RangeBaseValueChangedEventArgs e) { if (TxtVideoHapticDelay != null) OnSyncDelayChanged(); }
        private void SliderVideoHapticPower_Changed(object? sender, RangeBaseValueChangedEventArgs e) { if (TxtVideoHapticPower != null) OnSyncPowerChanged(); }

        // ---- Phase F: temperament, toy input, FunScript, luminance, audio advanced ----

        private void RbHapticTemperament_Checked(object? sender, RoutedEventArgs e) => OnTemperamentChecked(sender);
        private void ChkHapticToyInput_Changed(object? sender, RoutedEventArgs e) => OnToyInputChanged();
        private void ChkHapticToyAttentionCheck_Changed(object? sender, RoutedEventArgs e) => OnToyAttentionChanged();
        private void SliderHapticOverrideCooldown_Changed(object? sender, RangeBaseValueChangedEventArgs e) { if (TxtHapticOverrideCooldown != null) OnOverrideCooldownChanged(); }
        private void ChkHapticFunScript_Changed(object? sender, RoutedEventArgs e) => OnFunScriptChanged();
        private void ChkHapticFunScriptVibe_Changed(object? sender, RoutedEventArgs e) => OnFunScriptVibeChanged();
        private void ChkHapticLuminance_Changed(object? sender, RoutedEventArgs e) => OnLuminanceChanged();
        private void SliderHapticLuminance_Changed(object? sender, RangeBaseValueChangedEventArgs e) { if (TxtHapticLuminance != null) OnLuminanceLevelChanged(); }
        private void ChkHapticBandSplit_Changed(object? sender, RoutedEventArgs e) => OnBandSplitChanged();
        private void SliderDspSensitivity_Changed(object? sender, RangeBaseValueChangedEventArgs e) => OnDspSliderChanged(SliderDspSensitivity, TxtDspSensitivity, "x", v => Cfg.AudioSync.Sensitivity = v);
        private void SliderDspSmoothing_Changed(object? sender, RangeBaseValueChangedEventArgs e) => OnDspSliderChanged(SliderDspSmoothing, TxtDspSmoothing, "%", v => Cfg.AudioSync.Smoothing = v);
        private void SliderDspBass_Changed(object? sender, RangeBaseValueChangedEventArgs e) => OnDspSliderChanged(SliderDspBass, TxtDspBass, "%", v => Cfg.AudioSync.BassWeight = v);
        private void SliderDspRms_Changed(object? sender, RangeBaseValueChangedEventArgs e) => OnDspSliderChanged(SliderDspRms, TxtDspRms, "%", v => Cfg.AudioSync.RmsWeight = v);
        private void SliderDspOnset_Changed(object? sender, RangeBaseValueChangedEventArgs e) => OnDspSliderChanged(SliderDspOnset, TxtDspOnset, "%", v => Cfg.AudioSync.OnsetWeight = v);
        private void SliderDspMax_Changed(object? sender, RangeBaseValueChangedEventArgs e) => OnDspSliderChanged(SliderDspMax, TxtDspMax, "%", v => Cfg.AudioSync.MaxIntensity = v);
        private void BtnDspReset_Click(object? sender, RoutedEventArgs e) => OnDspReset();
    }
}
