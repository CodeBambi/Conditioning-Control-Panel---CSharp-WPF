using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Haptics;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Haptics setup wizard v2, PORTED from ConditioningControlPanel/Windows/HapticsSetupWindow.xaml.cs.
    ///
    /// Three pages: pick a provider, follow the steps (and type the Lovense IP), then connect and
    /// test. The navigation, the per-provider guide switching, the accent recolouring and the dot
    /// indicators are all view state and are ported for real.
    ///
    /// The settings half is restored against the seams: the Lovense address seeds from and writes
    /// back to <see cref="CoreSettings"/>, <see cref="ApplyProviderSettings"/> writes the same v2
    /// provider flags the WPF original does, and <see cref="AccentFor"/> asks <see cref="CoreMods"/>
    /// for the accent, so the wizard is now themed by the active mod exactly as on Windows. (The
    /// colours therefore differ from the pre-seam render: those were WPF's App.Mods-is-null
    /// fallbacks, which never fired in the real app.)
    ///
    /// What is still NOT ported: the device half. <c>App.Haptics</c> (HapticService) and
    /// <c>PatreonService</c> have no seam in Core, so the connect, the premium gate and the test
    /// buzz are stubs. Each is marked.
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

            TxtWizardLovenseIp.Text = CoreSettings.Current.Haptics?.LovenseUrl ?? "";

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

        /// <summary>
        /// Writes the v2 provider flags (the truth the device manager reads) plus the Lovense
        /// address, and keeps the legacy single-choice enum in step for old code paths. The WPF
        /// body verbatim, with <c>App.Settings</c> read through <see cref="CoreSettings"/>.
        /// </summary>
        private void ApplyProviderSettings()
        {
            var settings = CoreSettings.Current.Haptics;
            if (settings == null) return;
            settings.EnsureV2Migrated();

            var v2 = settings.V2;
            switch (_provider)
            {
                case WizardProvider.Lovense:
                    v2.Provider("lovense").Enabled = true;
                    settings.Provider = HapticProviderType.Lovense;
                    var typed = (TxtWizardLovenseIp.Text ?? "").Trim();
                    if (typed.Length > 0) settings.LovenseUrl = typed;   // mirrors into v2 provider url
                    break;
                case WizardProvider.Buttplug:
                    v2.Provider("buttplug").Enabled = true;
                    settings.Provider = HapticProviderType.Buttplug;
                    break;
                case WizardProvider.Mock:
                    v2.Provider("mock").Enabled = true;
                    settings.Provider = HapticProviderType.Mock;
                    break;
            }

            CoreSettings.Save();
        }

        // ------------------------------------------------------------------ page 3

        private void BtnConnect_Click()
        {
            // ponytail: needs ConditioningControlPanel/Services/Haptics/HapticService.cs
            // (ConnectAsync/ConnectedDevices) and ConditioningControlPanel/Services/Account/
            // PatreonService.cs (HasPremiumAccess) for the premium gate - neither has a seam among
            // the ten in Core, and DailyFreeService alone cannot answer the gate. Until then the
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
            // ponytail: needs HapticService.TestAsync
            // (ConditioningControlPanel/Services/Haptics/HapticService.cs); no seam in Core.
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

        /// <summary>
        /// The active mod's accent, through <see cref="CoreMods"/> - what <c>App.Mods</c> answered
        /// on WPF. Both properties are non-null and swallow provider faults, so there is no null
        /// branch to keep; unseeded they give the CCP-default hexes.
        /// </summary>
        private static IBrush AccentFor(WizardProvider provider)
        {
            var hex = provider == WizardProvider.Buttplug
                ? CoreMods.SecondaryColorHex
                : CoreMods.AccentColorHex;
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch (Exception ex)
            {
                Log.Debug("HapticsSetupWindow: unparseable accent {Hex}: {E}", hex, ex.Message);
                return Brushes.HotPink;
            }
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
