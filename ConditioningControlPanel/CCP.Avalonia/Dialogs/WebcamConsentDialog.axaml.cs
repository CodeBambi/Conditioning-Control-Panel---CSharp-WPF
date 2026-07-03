using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Webcam;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Multi-step privacy/consent flow for the webcam tracking feature.
/// </summary>
public partial class WebcamConsentDialog : Window
{
    private readonly ILogger<WebcamConsentDialog> _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private const string SourceUrl = "https://github.com/CC-Labs-llc/Conditioning-Control-Panel---CSharp-WPF/blob/main/ConditioningControlPanel/Services/WebcamTrackingService.cs";

    // Single source of truth: the literal lives on the Core seam so the
    // tracker, this dialog, and the Lab / Blink Trainer view-models all agree
    // on what "current" means.
    private const string ConsentVersion = WebcamConsent.ConsentVersion;

    private enum Step { Intro = 1, Privacy = 2, Consent = 3, Calibrate = 4 }
    private Step _step = Step.Intro;

    /// <summary>True when the user completed all consent gates and clicked Enable.</summary>
    public bool ConsentGiven { get; private set; }

    /// <summary>True when the user clicked "Calibrate now" on step 4.</summary>
    public bool WantsCalibrationNow { get; private set; }

    public WebcamConsentDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<WebcamConsentDialog>>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
UpdateUiForStep();
    }

    private void UpdateUiForStep()
    {
        PanelStep1.IsVisible = _step == Step.Intro;
        PanelStep2.IsVisible = _step == Step.Privacy;
        PanelStep3.IsVisible = _step == Step.Consent;
        PanelStep4.IsVisible = _step == Step.Calibrate;

        DotStep1.Fill = StepDotBrush(Step.Intro);
        DotStep2.Fill = StepDotBrush(Step.Privacy);
        DotStep3.Fill = StepDotBrush(Step.Consent);
        DotStep4.Fill = StepDotBrush(Step.Calibrate);

        BtnBack.IsVisible = _step != Step.Intro && _step != Step.Calibrate;
        BtnCancel.IsVisible = _step != Step.Calibrate;

        BtnNext.IsVisible = _step is Step.Intro or Step.Privacy;
        BtnEnable.IsVisible = _step == Step.Consent;
        BtnSkipCal.IsVisible = _step == Step.Calibrate;
        BtnCalNow.IsVisible = _step == Step.Calibrate;

        switch (_step)
        {
            case Step.Intro:
                BtnNext.Content = Loc.Get("dialog_webcam_consent_i_want_know_more_content");
                break;
            case Step.Privacy:
                BtnNext.Content = Loc.Get("dialog_webcam_consent_continue_content");
                break;
            case Step.Consent:
                UpdateEnableButtonState();
                break;
        }
    }

    private IBrush StepDotBrush(Step s)
    {
        if (_step == s) return new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["PinkColor"]!);
        return (int)_step > (int)s
            ? new SolidColorBrush(Color.FromRgb(0x8A, 0x4A, 0x6F))
            : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x52));
    }

    private void BtnNext_Click(object? sender, RoutedEventArgs e)
    {
        _step = _step == Step.Intro ? Step.Privacy : Step.Consent;
        UpdateUiForStep();
    }

    private void BtnBack_Click(object? sender, RoutedEventArgs e)
    {
        if (_step == Step.Privacy) _step = Step.Intro;
        else if (_step == Step.Consent) _step = Step.Privacy;
        UpdateUiForStep();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        ConsentGiven = false;
        Close(false);
    }

    private void ConsentCheckChanged(object? sender, RoutedEventArgs e) => UpdateEnableButtonState();
    private void TxtConfirm_TextChanged(object? sender, TextChangedEventArgs e) => UpdateEnableButtonState();

    private void UpdateEnableButtonState()
    {
        var allChecked = ChkConsent1.IsChecked == true
                      && ChkConsent2.IsChecked == true
                      && ChkConsent3.IsChecked == true;
        var typed = TxtConfirm?.Text?.Trim() == "ENABLE";
        BtnEnable.IsEnabled = allChecked && typed;

        if (TxtConfirmHint != null)
        {
            if (allChecked && typed)
            {
                TxtConfirmHint.Text = Loc.Get("dialog_webcam_consent_all_gates_passed_text");
                TxtConfirmHint.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xE0, 0xA0));
            }
            else
            {
                var missing = "";
                if (!allChecked) missing += Loc.Get("dialog_webcam_consent_waiting_checkboxes");
                if (!allChecked && !typed) missing += Loc.Get("dialog_webcam_consent_waiting_separator");
                if (!typed) missing += Loc.Get("dialog_webcam_consent_waiting_enable_typed");
                TxtConfirmHint.Text = Loc.Get("dialog_webcam_consent_waiting_for_prefix") + missing + ".";
                TxtConfirmHint.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xA0));
            }
        }
    }

    private void BtnEnable_Click(object? sender, RoutedEventArgs e)
    {
        var s =
_settings?.Current;
        if (s != null)
        {
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = ConsentVersion;
            s.WebcamConsentDate = DateTime.UtcNow;
            _settings?.Save();
        }

        _logger?.LogInformation("Webcam consent granted at {Time}, version {Version}",
            DateTime.UtcNow, ConsentVersion);

        ConsentGiven = true;
        _step = Step.Calibrate;
        UpdateUiForStep();
    }

    private void BtnSkipCal_Click(object? sender, RoutedEventArgs e)
    {
        WantsCalibrationNow = false;
        Close(true);
    }

    private void BtnCalNow_Click(object? sender, RoutedEventArgs e)
    {
        WantsCalibrationNow = true;
        Close(true);
    }

    private void LnkSource_Click(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = SourceUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "WebcamConsentDialog: failed to open source URL");
        }
    }
}
