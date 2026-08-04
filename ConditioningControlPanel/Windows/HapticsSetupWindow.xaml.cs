using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel;

/// <summary>
/// Haptics setup wizard v2.
///
/// The v1 window was informational only: three slides of screenshots, a Done button, and
/// not a single setting written — the user still had to go back to the tab, find the right
/// combo, and type the address by hand. This version owns the whole job:
///   1. pick a provider (Lovense LAN / Intiface / Mock demo),
///   2. follow the steps and, for Lovense, type the IP right here,
///   3. press Connect and watch it happen — device list on success, an actionable reason
///      on failure, and a Test buzz button to prove the toy is really ours.
///
/// It writes the v2 per-provider Enabled flags (what HapticDeviceManager actually reads) and
/// the Lovense address, then saves. The caller (MainWindow.BtnHapticsHelp_Click) reloads the
/// tab afterwards.
/// </summary>
public partial class HapticsSetupWindow : Window
{
    private enum WizardProvider { None, Lovense, Buttplug, Mock }
    private enum Page { Provider = 1, Guide = 2, Connect = 3 }

    private WizardProvider _provider = WizardProvider.None;
    private Page _page = Page.Provider;
    private bool _connecting;
    private bool _connected;

    public HapticsSetupWindow()
    {
        InitializeComponent();
        TxtWizardLovenseIp.Text = App.Settings?.Current?.Haptics?.LovenseUrl ?? "";
        UpdateChrome();
    }

    // ------------------------------------------------------------------ page 1

    private void BtnSelectLovense_Click(object sender, RoutedEventArgs e) => Select(WizardProvider.Lovense);
    private void BtnSelectButtplug_Click(object sender, RoutedEventArgs e) => Select(WizardProvider.Buttplug);
    private void BtnSelectMock_Click(object sender, RoutedEventArgs e) => Select(WizardProvider.Mock);

    private void Select(WizardProvider provider)
    {
        _provider = provider;
        _page = Page.Guide;
        UpdateChrome();
    }

    // ------------------------------------------------------------------ nav

    private void BtnPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_page == Page.Guide) { _provider = WizardProvider.None; _page = Page.Provider; }
        else if (_page == Page.Connect) _page = Page.Guide;
        UpdateChrome();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_page != Page.Guide) return;

        // Commit the choice before the connect page so a user who bails out mid-wizard still
        // ends up with the provider they picked enabled on the tab.
        ApplyProviderSettings();
        _page = Page.Connect;
        UpdateChrome();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Writes the v2 provider flags (the truth the device manager reads) plus the
    /// Lovense address, and keeps the legacy single-choice enum in step for old code paths.</summary>
    private void ApplyProviderSettings()
    {
        var settings = App.Settings?.Current?.Haptics;
        if (settings == null) return;
        settings.EnsureV2Migrated();

        var v2 = settings.V2;
        switch (_provider)
        {
            case WizardProvider.Lovense:
                v2.Provider("lovense").Enabled = true;
                settings.Provider = Services.Haptics.HapticProviderType.Lovense;
                var typed = (TxtWizardLovenseIp.Text ?? "").Trim();
                if (typed.Length > 0) settings.LovenseUrl = typed;   // mirrors into v2 provider url
                break;
            case WizardProvider.Buttplug:
                v2.Provider("buttplug").Enabled = true;
                settings.Provider = Services.Haptics.HapticProviderType.Buttplug;
                break;
            case WizardProvider.Mock:
                v2.Provider("mock").Enabled = true;
                settings.Provider = Services.Haptics.HapticProviderType.Mock;
                break;
        }

        try { App.Settings?.Save(); } catch { }
    }

    // ------------------------------------------------------------------ page 3

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_connecting || App.Haptics == null) return;

        if (App.Patreon?.HasPremiumAccess != true)
        {
            ShowResult(false, Loc.Get("gate_premium_locked"), Loc.Get("msg_haptic_feedback_patreon_only"),
                Array.Empty<string>());
            return;
        }

        ApplyProviderSettings();

        _connecting = true;
        BtnConnect.IsEnabled = false;
        ConnectProgress.Visibility = Visibility.Visible;
        ConnectResultBox.Visibility = Visibility.Collapsed;
        BtnWizardTestBuzz.Visibility = Visibility.Collapsed;
        TxtConnectStatus.Text = Loc.Get("login_connecting");

        try
        {
            var ok = await App.Haptics.ConnectAsync();
            var devices = App.Haptics.ConnectedDevices;

            if (ok && devices.Count > 0)
            {
                _connected = true;
                ShowResult(true, Loc.Get("wizard_connect_ok"), Loc.Get("wizard_connect_ok_hint"), devices);
            }
            else if (ok)
            {
                // Lovense Remote answers with zero toys while nothing is paired yet; the
                // provider keeps polling, so this is a "nearly there", not a failure.
                _connected = true;
                ShowResult(true, Loc.Get("wizard_connect_no_toys"), Loc.Get("wizard_connect_no_toys_hint"),
                    Array.Empty<string>());
            }
            else
            {
                _connected = false;
                ShowResult(false, Loc.Get("wizard_connect_failed"), FailureHint(), Array.Empty<string>());
            }
        }
        catch (Exception ex)
        {
            _connected = false;
            ShowResult(false, Loc.Get("wizard_connect_failed"), ex.Message, Array.Empty<string>());
        }
        finally
        {
            _connecting = false;
            ConnectProgress.Visibility = Visibility.Collapsed;
            BtnConnect.IsEnabled = true;
            UpdateChrome();
        }
    }

    private string FailureHint() => _provider switch
    {
        WizardProvider.Lovense => Loc.Get("wizard_fail_hint_lovense"),
        WizardProvider.Buttplug => Loc.Get("wizard_fail_hint_intiface"),
        _ => Loc.Get("wizard_fail_hint_generic")
    };

    private void ShowResult(bool success, string title, string hint, IReadOnlyList<string> devices)
    {
        TxtConnectStatus.Text = title;
        TxtConnectStatus.Foreground = success
            ? (Application.Current.Resources["SuccessGreenBrush"] as Brush ?? Brushes.LimeGreen)
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

        TxtConnectResultTitle.Text = devices.Count > 0 ? Loc.Get("wizard_devices_found") : "";
        TxtConnectResultTitle.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WizardDeviceList.ItemsSource = devices.ToList();
        TxtConnectHint.Text = hint ?? "";
        ConnectResultBox.Visibility = Visibility.Visible;
        BtnWizardTestBuzz.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BtnWizardTestBuzz_Click(object sender, RoutedEventArgs e)
    {
        if (App.Haptics == null) return;
        BtnWizardTestBuzz.IsEnabled = false;
        try
        {
            var result = await App.Haptics.TestAsync();
            if (result != Services.HapticTestResult.Success)
                TxtConnectHint.Text = Loc.Get("wizard_test_failed");
        }
        catch (Exception ex)
        {
            TxtConnectHint.Text = ex.Message;
        }
        finally { BtnWizardTestBuzz.IsEnabled = true; }
    }

    // ------------------------------------------------------------------ chrome

    private void UpdateChrome()
    {
        ProviderPage.Visibility = _page == Page.Provider ? Visibility.Visible : Visibility.Collapsed;
        GuidePage.Visibility = _page == Page.Guide ? Visibility.Visible : Visibility.Collapsed;
        ConnectPage.Visibility = _page == Page.Connect ? Visibility.Visible : Visibility.Collapsed;

        LovenseGuide.Visibility = _provider == WizardProvider.Lovense ? Visibility.Visible : Visibility.Collapsed;
        ButtplugGuide.Visibility = _provider == WizardProvider.Buttplug ? Visibility.Visible : Visibility.Collapsed;
        MockGuide.Visibility = _provider == WizardProvider.Mock ? Visibility.Visible : Visibility.Collapsed;

        var accent = AccentFor(_provider);
        TxtTitle.Text = _provider switch
        {
            WizardProvider.Lovense => Loc.Get("label_lovense_setup_guide"),
            WizardProvider.Buttplug => Loc.Get("label_buttplug_io_setup_guide"),
            WizardProvider.Mock => Loc.Get("wizard_provider_mock"),
            _ => Loc.Get("dialog_haptics_setup_guide")
        };
        TxtTitle.Foreground = accent;
        BtnNext.Background = accent;
        BtnConnect.Background = accent;

        BtnPrevious.Visibility = _page == Page.Provider ? Visibility.Collapsed : Visibility.Visible;
        BtnNext.Visibility = _page == Page.Guide ? Visibility.Visible : Visibility.Collapsed;
        BtnConnect.Visibility = _page == Page.Connect && !_connected ? Visibility.Visible : Visibility.Collapsed;
        BtnDone.Visibility = _page == Page.Connect && _connected ? Visibility.Visible : Visibility.Collapsed;

        var inactive = Application.Current.Resources["PanelAccentBrush"] as Brush ?? Brushes.Gray;
        Dot1.Fill = accent;
        Dot2.Fill = (int)_page >= 2 ? accent : inactive;
        Dot3.Fill = (int)_page >= 3 ? accent : inactive;
    }

    private static Brush AccentFor(WizardProvider provider)
    {
        var hex = provider == WizardProvider.Buttplug
            ? App.Mods?.GetSecondaryColorHex() ?? "#9B59B6"
            : App.Mods?.GetAccentColorHex() ?? "#FF69B4";
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!); }
        catch { return Brushes.HotPink; }
    }
}
