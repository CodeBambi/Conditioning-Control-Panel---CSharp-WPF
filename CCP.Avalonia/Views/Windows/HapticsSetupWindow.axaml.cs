using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Haptics setup wizard v2, PORTED from ConditioningControlPanel/Windows/HapticsSetupWindow.xaml.cs.
    ///
    /// Three pages: pick a provider, follow the steps (and type the Lovense IP), then connect and
    /// test. The navigation, the per-provider guide switching, the accent recolouring and the dot
    /// indicators are all view state and are ported for real.
    ///
    /// What is NOT ported: everything that reaches a service. <c>App.Settings</c>,
    /// <c>App.Haptics</c>, <c>App.Patreon</c>, <c>App.DailyFree</c> and <c>App.Mods</c> do not
    /// exist on this head, so <see cref="ApplyProviderSettings"/>, the connect and the test buzz
    /// are stubs and <see cref="AccentFor"/> uses the WPF fallback hexes. Each is marked.
    ///
    /// Strings that the WPF code-behind ASSIGNS to a control carrying <c>{loc:Str}</c> are bound
    /// here instead (<see cref="BindLoc"/>): Avalonia keeps the XAML binding alive under a local
    /// value, so a plain <c>.Text =</c> is undone on the next language change (CLAUDE.md).
    /// </summary>
    public partial class HapticsSetupWindow : Window
    {
        private enum WizardProvider { None, Lovense, Buttplug, Mock }
        private enum Page { Provider = 1, Guide = 2, Connect = 3 }

        private WizardProvider _provider = WizardProvider.None;
        private Page _page = Page.Provider;
        private bool _connected;

        public HapticsSetupWindow()
        {
            InitializeComponent();

            // ponytail: needs App.Settings (HapticsSettings.LovenseUrl), wired when it moves to Core.
            TxtWizardLovenseIp.Text = "";

            BtnClose.Click += (_, _) => Close();
            BtnSelectLovense.Click += (_, _) => Select(WizardProvider.Lovense);
            BtnSelectButtplug.Click += (_, _) => Select(WizardProvider.Buttplug);
            BtnSelectMock.Click += (_, _) => Select(WizardProvider.Mock);
            BtnPrevious.Click += (_, _) => BtnPrevious_Click();
            BtnNext.Click += (_, _) => BtnNext_Click();
            BtnConnect.Click += (_, _) => BtnConnect_Click();
            BtnDone.Click += (_, _) => Close();
            BtnWizardTestBuzz.Click += (_, _) => BtnWizardTestBuzz_Click();

            UpdateChrome();
        }

        // ------------------------------------------------------------------ page 1

        private void Select(WizardProvider provider)
        {
            _provider = provider;
            _page = Page.Guide;
            UpdateChrome();
        }

        // ------------------------------------------------------------------ nav

        private void BtnPrevious_Click()
        {
            if (_page == Page.Guide) { _provider = WizardProvider.None; _page = Page.Provider; }
            else if (_page == Page.Connect) _page = Page.Guide;
            UpdateChrome();
        }

        private void BtnNext_Click()
        {
            if (_page != Page.Guide) return;

            // Commit the choice before the connect page so a user who bails out mid-wizard still
            // ends up with the provider they picked enabled on the tab.
            ApplyProviderSettings();
            _page = Page.Connect;
            UpdateChrome();
        }

        /// <summary>Writes the v2 provider flags plus the Lovense address in the WPF head.</summary>
        private void ApplyProviderSettings()
        {
            // ponytail: needs App.Settings (HapticsSettings.V2 provider flags, LovenseUrl) and
            // Services.Haptics.HapticProviderType, wired when they move to Core.
        }

        // ------------------------------------------------------------------ page 3

        private void BtnConnect_Click()
        {
            // ponytail: needs App.Haptics (ConnectAsync/ConnectedDevices), App.Patreon and
            // App.DailyFree for the premium gate, wired when they move to Core. Until then the
            // page shows a placeholder result so the device-list template is actually exercised.
            // The real handler also has a failure path: ShowResult(false, "wizard_connect_failed",
            // <"wizard_fail_hint_lovense" | "wizard_fail_hint_intiface" | "wizard_fail_hint_generic">,
            // empty), and a premium gate on "gate_premium_locked" /
            // "msg_haptic_feedback_patreon_only" — WPF's FailureHint(), not carried as dead code.
            ApplyProviderSettings();
            _connected = true;
            ShowResult(true, "wizard_connect_ok", "wizard_connect_ok_hint",
                       new[] { "Sample Toy (placeholder)", "Sample Toy 2 (placeholder)" });
            UpdateChrome();
        }

        /// <summary>
        /// Keys rather than strings, unlike the WPF original: the two TextBlocks it writes carry
        /// {loc:Str} bindings, so they have to be re-bound, not assigned.
        /// </summary>
        private void ShowResult(bool success, string titleKey, string hintKey, IReadOnlyList<string> devices)
        {
            BindLoc(TxtConnectStatus, titleKey);
            TxtConnectStatus.Foreground = success
                ? (this.TryFindResource("SuccessGreenBrush", out var green) ? green as IBrush ?? Brushes.LimeGreen : Brushes.LimeGreen)
                : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

            if (devices.Count > 0) BindLoc(TxtConnectResultTitle, "wizard_devices_found");
            TxtConnectResultTitle.IsVisible = devices.Count > 0;
            WizardDeviceList.ItemsSource = devices;
            BindLoc(TxtConnectHint, hintKey);
            ConnectResultBox.IsVisible = true;
            BtnWizardTestBuzz.IsVisible = success;
        }

        private void BtnWizardTestBuzz_Click()
        {
            // ponytail: needs App.Haptics (TestAsync), wired when it moves to Core.
            BindLoc(TxtConnectHint, "wizard_test_failed");
        }

        // ------------------------------------------------------------------ chrome

        private void UpdateChrome()
        {
            ProviderPage.IsVisible = _page == Page.Provider;
            GuidePage.IsVisible = _page == Page.Guide;
            ConnectPage.IsVisible = _page == Page.Connect;

            LovenseGuide.IsVisible = _provider == WizardProvider.Lovense;
            ButtplugGuide.IsVisible = _provider == WizardProvider.Buttplug;
            MockGuide.IsVisible = _provider == WizardProvider.Mock;

            var accent = AccentFor(_provider);
            BindLoc(TxtTitle, _provider switch
            {
                WizardProvider.Lovense => "label_lovense_setup_guide",
                WizardProvider.Buttplug => "label_buttplug_io_setup_guide",
                WizardProvider.Mock => "wizard_provider_mock",
                _ => "dialog_haptics_setup_guide"
            });
            TxtTitle.Foreground = accent;
            BtnNext.Background = accent;
            BtnConnect.Background = accent;

            BtnPrevious.IsVisible = _page != Page.Provider;
            BtnNext.IsVisible = _page == Page.Guide;
            BtnConnect.IsVisible = _page == Page.Connect && !_connected;
            BtnDone.IsVisible = _page == Page.Connect && _connected;

            var inactive = this.TryFindResource("PanelAccentBrush", out var pa) ? pa as IBrush ?? Brushes.Gray : Brushes.Gray;
            Dot1.Fill = accent;
            Dot2.Fill = (int)_page >= 2 ? accent : inactive;
            Dot3.Fill = (int)_page >= 3 ? accent : inactive;
        }

        private static IBrush AccentFor(WizardProvider provider)
        {
            // ponytail: needs App.Mods (GetAccentColorHex/GetSecondaryColorHex), wired when it
            // moves to Core. The hexes are the WPF fallbacks.
            var hex = provider == WizardProvider.Buttplug ? "#9B59B6" : "#FF69B4";
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { return Brushes.HotPink; }
        }

        /// <summary>
        /// Re-binds a TextBlock to a loc key, the same binding {loc:Str} builds. Assigning .Text
        /// over a {loc:Str} binding survives until the next language change and then reverts.
        /// </summary>
        private static void BindLoc(TextBlock target, string key) =>
            target.Bind(TextBlock.TextProperty, new Binding($"[{key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
            });
    }
}
