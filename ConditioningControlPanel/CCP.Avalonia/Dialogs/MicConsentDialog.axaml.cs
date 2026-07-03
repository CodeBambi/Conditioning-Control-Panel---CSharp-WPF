using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Multi-step privacy/consent flow for the OFFLINE microphone ("repeat after me" / "Hey Bambi").
/// Steps: 1) what it enables, 2) privacy contract, 3) explicit consent. On approval flips
/// <see cref="ConditioningControlPanel.Core.Models.AppSettings.MicConsentGiven"/>. Does NOT open the
/// mic — the mic only opens later during an explicit listen window, and only while Takeover is running.
/// Mirrors <see cref="WebcamConsentDialog"/> so the two consent flows feel identical.
/// </summary>
public partial class MicConsentDialog : Window
{
    private readonly ILogger<MicConsentDialog>? _logger;
    private readonly ISettingsService? _settings;

    // Source code for the offline speech capture (the privacy contract links here for verification).
    private const string SourceUrl =
        "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/blob/main/ConditioningControlPanel/Services/Speech/SpeechService.cs";

    private enum Step { Intro = 1, Privacy = 2, Consent = 3 }
    private Step _step = Step.Intro;

    /// <summary>True when the user completed all consent gates and clicked Enable.</summary>
    public bool ConsentGiven { get; private set; }

    public MicConsentDialog()
    {
        InitializeComponent();

        // Resolve dependencies defensively — the dialog is also design-time/parameterless constructed.
        try
        {
            _logger = App.Services?.GetService<ILogger<MicConsentDialog>>();
            _settings = App.Services?.GetService<ISettingsService>();
        }
        catch { /* design-time / no provider */ }

        UpdateUiForStep();
    }

    private void UpdateUiForStep()
    {
        PanelStep1.IsVisible = _step == Step.Intro;
        PanelStep2.IsVisible = _step == Step.Privacy;
        PanelStep3.IsVisible = _step == Step.Consent;

        DotStep1.Fill = StepDotBrush(Step.Intro);
        DotStep2.Fill = StepDotBrush(Step.Privacy);
        DotStep3.Fill = StepDotBrush(Step.Consent);

        BtnBack.IsVisible = _step != Step.Intro;

        switch (_step)
        {
            case Step.Intro:
                BtnNext.IsVisible = true;
                BtnEnable.IsVisible = false;
                BtnNext.Content = Loc.Get("dialog_webcam_consent_i_want_know_more_content");
                break;
            case Step.Privacy:
                BtnNext.IsVisible = true;
                BtnEnable.IsVisible = false;
                BtnNext.Content = Loc.Get("dialog_webcam_consent_continue_content");
                break;
            case Step.Consent:
                BtnNext.IsVisible = false;
                BtnEnable.IsVisible = true;
                UpdateEnableButtonState();
                break;
        }
    }

    private IBrush StepDotBrush(Step s)
    {
        if (_step == s)
            return new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["PinkColor"]!);
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
        // Persist consent. The mic stays closed — it only opens during an explicit listen window
        // while Takeover is running. Mirrors the WPF MicConsentDialog grant path.
        var s = _settings?.Current;
        if (s != null)
        {
            s.MicConsentGiven = true;
            _settings?.Save();
        }

        _logger?.LogInformation("Mic consent granted at {Time}", DateTime.UtcNow);

        ConsentGiven = true;
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
            _logger?.LogWarning(ex, "MicConsentDialog: failed to open source URL");
        }
    }
}
