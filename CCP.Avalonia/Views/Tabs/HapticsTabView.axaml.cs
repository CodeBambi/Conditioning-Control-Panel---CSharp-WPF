using System.Collections.Generic;
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
    /// state. That partial has not moved, so the 34 forwards are stubs with the same names.</para>
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

            // Placeholder data. On WPF, MainWindow.Haptics.cs builds these three lists from
            // App.Haptics / HapticDeviceManager and the HapticSettings routing rules; the VM types
            // (Views/Controls/HapticUiModels.cs) use System.Windows.Visibility, so they cannot move
            // as-is. Seeded here so --render-all proves the four DataTemplates and their
            // ControlThemes actually draw - an empty ItemsControl hides a template-less control
            // (CLAUDE.md trap 4). Labels, icons and order copied from MainWindow.Haptics.cs:71-131.
            ProviderChipsList.ItemsSource = new List<HapticProviderChipSample>
            {
                new() { Label = "Lovense",  IsConnected = true,  IsEnabledForConnect = true },
                new() { Label = "Intiface", IsConnected = false, IsEnabledForConnect = true },
                new() { Label = "Mock",     IsConnected = false, IsEnabledForConnect = false },
            };

            ToyCardsList.ItemsSource = new List<HapticToyCardSample>
            {
                new()
                {
                    DeviceKey = "lovense:lush-3", Name = "Lush 3", ProviderLabel = "Lovense",
                    BatteryText = "84%", BatteryVisible = true, ToyEnabled = true,
                    Capabilities = new List<string> { "VIBE x2", "DEPTH" },
                    Nickname = "", RoleIndex = 0, TrimPercent = 100, TrimText = "100%",
                },
                new()
                {
                    DeviceKey = "buttplug:handy", Name = "The Handy", ProviderLabel = "Intiface",
                    BatteryText = "", BatteryVisible = false, ToyEnabled = false,
                    Capabilities = new List<string> { "THRUST", "VIBE" },
                    Nickname = "", RoleIndex = 3, TrimPercent = 60, TrimText = "60%",
                },
            };

            RoutingGroupsList.ItemsSource = new List<HapticRoutingGroupSample>
            {
                new()
                {
                    Icon = "🌀", Title = "Core",
                    Rows = new List<HapticRoutingRowSample>
                    {
                        // One row open, one closed, one disabled: the open row proves the drawer,
                        // the disabled row proves the .rowoff class the WPF DataTriggers became.
                        new() { Icon = "⚡", Label = "Flash click", ValueSummary = "50% · Pulse · All",
                                RowEnabled = true, IsExpanded = true, IntensityPercent = 50,
                                IntensityText = "50%", ModeVisible = true, ModeIndex = 1, RoleIndex = 0 },
                        new() { Icon = "💥", Label = "Flash show", ValueSummary = "70% · Wave · Reward",
                                RowEnabled = true, IsExpanded = false, IntensityPercent = 70,
                                IntensityText = "70%", ModeVisible = true, ModeIndex = 2, RoleIndex = 1 },
                        new() { Icon = "🔑", Label = "Keyword", ValueSummary = "Off",
                                RowEnabled = false, IsExpanded = false, IntensityPercent = 0,
                                IntensityText = "0%", ModeVisible = true, ModeIndex = 0, RoleIndex = 0 },
                    },
                },
                new()
                {
                    Icon = "🎬", Title = "Media",
                    Rows = new List<HapticRoutingRowSample>
                    {
                        // A LAYER row, not an event row: no pattern picker (ModeVisible false).
                        new() { Icon = "🎵", Label = "Audio sync", ValueSummary = "80% · All",
                                RowEnabled = true, IsExpanded = false, IntensityPercent = 80,
                                IntensityText = "80%", ModeVisible = false, ModeIndex = 0, RoleIndex = 0 },
                    },
                },
            };

            // WPF fills this from the live device list; one entry so the themed ComboBox draws text.
            CmbPatternToy.Items.Add(new ComboBoxItem { Content = "Lush 3" });
            CmbPatternToy.SelectedIndex = 0;

            ApplyFeatureArt();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged += OnModChanged;
            ApplyFeatureArt();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
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

        // ponytail: every handler below forwards to MainWindow on WPF
        // (Window.GetWindow(this) as MainWindow -> mw.<same name>). Needs the MainWindow.Haptics
        // partial and HapticService, wired when they move to Core. Names kept identical so the
        // wiring diffs cleanly against ConditioningControlPanel/Views/Tabs/HapticsTabView.xaml.cs.
        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e) { }
        private void ChkHapticsEnabled_Changed(object? sender, RoutedEventArgs e) { }
        private void BtnHapticConnect_Click(object? sender, RoutedEventArgs e) { }
        private void BtnHapticPanic_Click(object? sender, RoutedEventArgs e) { }
        private void BtnHapticTest_Click(object? sender, RoutedEventArgs e) { }
        private void BtnHapticToyTest_Click(object? sender, RoutedEventArgs e) { }
        private void BtnHapticsHelp_Click(object? sender, RoutedEventArgs e) { }
        private void ChkHapticProvider_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkHapticAutoConnect_Changed(object? sender, RoutedEventArgs e) { }
        private void TxtHapticUrl_TextChanged(object? sender, TextChangedEventArgs e) { }
        private void TxtHapticIntifaceUrl_TextChanged(object? sender, TextChangedEventArgs e) { }
        private void SliderHapticIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderHapticMaxPower_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderHapticDtrhAmbient_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void CmbHapticDtrhDensity_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
        private void CmbPatternMode_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
        private void SliderPatternIntensity_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void BtnPatternPlay_Click(object? sender, RoutedEventArgs e) { }
        private void PatternPreviewCanvas_SizeChanged(object? sender, SizeChangedEventArgs e) { }
        private void SliderVideoHapticDelay_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderVideoHapticPower_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }

        // ---- Phase F: temperament, toy input, FunScript, luminance, audio advanced ----

        private void RbHapticTemperament_Checked(object? sender, RoutedEventArgs e) { }
        private void ChkHapticToyInput_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkHapticToyAttentionCheck_Changed(object? sender, RoutedEventArgs e) { }
        private void SliderHapticOverrideCooldown_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void ChkHapticFunScript_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkHapticFunScriptVibe_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkHapticLuminance_Changed(object? sender, RoutedEventArgs e) { }
        private void SliderHapticLuminance_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void ChkHapticBandSplit_Changed(object? sender, RoutedEventArgs e) { }
        private void SliderDspSensitivity_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderDspSmoothing_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderDspBass_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderDspRms_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderDspOnset_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderDspMax_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void BtnDspReset_Click(object? sender, RoutedEventArgs e) { }
    }

    // ---------------------------------------------------------------------------------------
    // Stand-ins for ConditioningControlPanel/Views/Controls/HapticUiModels.cs, which has not
    // moved to Core: those VMs expose System.Windows.Visibility and reach Application.Current
    // for brushes, so neither the types nor their converters can be referenced from this head.
    // Only the members the four DataTemplates bind are here, and every WPF `Visibility` member
    // is a bool named *Visible, because Avalonia binds IsVisible to a bool directly. Swap the
    // x:DataType attributes for the real types when the VMs land in Core.
    // ---------------------------------------------------------------------------------------

    public sealed class HapticProviderChipSample
    {
        public string Label { get; set; } = "";
        public bool IsConnected { get; set; }
        public bool IsEnabledForConnect { get; set; }
    }

    public sealed class HapticToyCardSample
    {
        public string DeviceKey { get; set; } = "";
        public string Name { get; set; } = "";
        public string ProviderLabel { get; set; } = "";
        public string BatteryText { get; set; } = "";
        public bool BatteryVisible { get; set; }
        public bool ToyEnabled { get; set; }
        public List<string> Capabilities { get; set; } = new();
        public string Nickname { get; set; } = "";
        public int RoleIndex { get; set; }
        public double TrimPercent { get; set; }
        public string TrimText { get; set; } = "";
    }

    public sealed class HapticRoutingRowSample
    {
        public string Icon { get; set; } = "";
        public string Label { get; set; } = "";
        public string Hint { get; set; } = "";
        public string ValueSummary { get; set; } = "";
        public bool RowEnabled { get; set; }
        public bool IsExpanded { get; set; }
        public double IntensityPercent { get; set; }
        public string IntensityText { get; set; } = "";
        /// <summary>WPF's <c>ModeVisibility</c>: false on a LAYER row, which has no pattern picker.</summary>
        public bool ModeVisible { get; set; }
        public int ModeIndex { get; set; }
        public int RoleIndex { get; set; }
    }

    public sealed class HapticRoutingGroupSample
    {
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public List<HapticRoutingRowSample> Rows { get; set; } = new();
    }
}
