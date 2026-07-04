using System.Collections.Generic;
using System.Collections.ObjectModel;
using ConditioningControlPanel.Avalonia.Dialogs;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Speech;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// "She's Listening" — the voice-control surface (offline mic features: spoken mantras + the
/// "Hey Bambi" voice commands). Binds the same voice AppSettings the Takeover tab uses and re-arms
/// the wake loop via <see cref="IAutonomyService.RefreshVoiceInputModes"/> whenever a toggle flips.
/// Port of the WPF SheListeningTabView (whose code-behind delegated to MainWindow handlers).
/// </summary>
public partial class SheListeningTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settings;
    private readonly ISpeechRecognitionService? _speech;
    private readonly IAutonomyService? _autonomy;
    private readonly ISpeechWakeService? _wakeWord;
    private readonly ILogger<SheListeningTabViewModel>? _logger;

    private bool _syncing;
    /// <summary>Re-entrancy guard for the async mic-consent flow (turning the checkbox ON).</summary>
    private bool _consentGateBusy;

    [ObservableProperty] private bool _micConsentGiven;
    [ObservableProperty] private bool _wakeWordEnabled;
    [ObservableProperty] private string _wakeWords = "hey bambi";
    [ObservableProperty] private bool _pushToTalkEnabled;
    [ObservableProperty] private SpeechInputDevice? _selectedDevice;
    [ObservableProperty] private bool _engineAvailable;
    [ObservableProperty] private string _statusText = "";
    /// <summary>Reliable-wake (sherpa-onnx KWS) status line.</summary>
    [ObservableProperty] private bool _wakeEngineAvailable;
    [ObservableProperty] private string _wakeEngineStatusText = "";
    [ObservableProperty] private bool _isCalibrating;
    [ObservableProperty] private string _calibrateCaption = "Calibrate to my voice";
    /// <summary>Mic sensitivity 0-100% (inverted loudness gate). Bound to SpeechLoudnessThreshold.</summary>
    [ObservableProperty] private int _micSensitivity;

    public ObservableCollection<SpeechInputDevice> Devices { get; } = new();

    /// <summary>A short, on-screen command cheat-sheet (a slice of the full grammar).</summary>
    public IReadOnlyList<string> CommandHints { get; } = new[]
    {
        "“bubbles” / “stop bubbles”", "“show me a video”", "“flash me”", "“the spiral”",
        "“make it pink”", "“deeper”", "“quiz me”", "“lock me”", "“mute” / “louder”",
        "“take over”", "“red” (stops everything)", "“stop listening”",
    };

    public SheListeningTabViewModel() : base("shelistening", "She's Listening", "🎤")
    {
    }

    public SheListeningTabViewModel(
        ISettingsService settings,
        ISpeechRecognitionService speech,
        IAutonomyService autonomy,
        ILogger<SheListeningTabViewModel> logger,
        ISpeechWakeService? wakeWord = null) : base("shelistening", "She's Listening", "🎤")
    {
        _settings = settings;
        _speech = speech;
        _autonomy = autonomy;
        _wakeWord = wakeWord;
        _logger = logger;
        SyncFromSettings();
        RefreshDevices();
    }

    public override void OnSelected()
    {
        base.OnSelected();
        SyncFromSettings();
        RefreshDevices();
    }

    private void SyncFromSettings()
    {
        var s = _settings?.Current;
        if (s == null) return;
        _syncing = true;
        try
        {
            MicConsentGiven = s.MicConsentGiven;
            WakeWordEnabled = s.SpeechWakeWordEnabled;
            WakeWords = string.IsNullOrWhiteSpace(s.SpeechWakeWords) ? "hey bambi" : s.SpeechWakeWords;
            PushToTalkEnabled = s.SpeechPushToTalkEnabled;
            MicSensitivity = ThresholdToSens(s.SpeechLoudnessThreshold);
            EngineAvailable = _speech?.IsAvailable ?? false;
            StatusText = EngineAvailable
                ? "Offline voice engine ready."
                : "Voice engine unavailable — drop a Vosk model into Resources/Models/vosk.";
            RefreshWakeStatus();
        }
        finally { _syncing = false; }
    }

    /// <summary>Reliable-wake (sherpa-onnx KWS) status: model present + ready, or what's missing.</summary>
    private void RefreshWakeStatus()
    {
        WakeEngineAvailable = _wakeWord?.IsAvailable ?? false;
        if (_wakeWord == null)
            WakeEngineStatusText = "";
        else if (_wakeWord.IsAvailable)
            WakeEngineStatusText = "Reliable wake ready (sherpa-onnx KWS).";
        else if (_wakeWord.IsConfigured)
            WakeEngineStatusText = "Reliable wake model present but unavailable (no mic?).";
        else
            WakeEngineStatusText = "Tip: drop a sherpa-kws model into Resources/Models for reliable wake.";
    }

    /// <summary>
    /// Tune the reliable-wake threshold to this user's voice + mic by saying "Hey Bambi" a few times.
    /// The recognizer is single-session: stops the wake loop first, re-arms after with the new threshold.
    /// </summary>
    [RelayCommand]
    private async Task CalibrateWakeAsync()
    {
        var wake = _wakeWord;
        if (wake?.IsAvailable != true || IsCalibrating) return;

        // P0 S-1: opening the capture session for calibration requires mic consent. Mirror the WPF
        // gate at MainWindow.SheListening.cs (SL_Calibrate_Click:124-128): show MicConsentDialog when
        // consent is absent and abort if declined.
        if (!(_settings?.Current?.MicConsentGiven == true))
        {
            if (!await ShowMicConsentDialogAsync()) return;
            // Consent was granted via the dialog — reflect it on the bound checkbox without re-firing.
            _syncing = true;
            try { MicConsentGiven = true; }
            finally { _syncing = false; }
        }

        try
        {
            IsCalibrating = true;
            CalibrateCaption = "Say “Hey Bambi” 5×…";
            // The recognizer is single-session — stop the wake loop first so calibration can open the mic.
            _autonomy?.StopVoiceInput();
            for (int i = 0; i < 20 && wake.IsListening; i++) await Task.Delay(25);
            var progress = new Progress<WakeCalibrationProgress>(p =>
                CalibrateCaption = p.Phase == "analyze" ? "Analyzing…" : $"Say “Hey Bambi” ({p.Captured}/{p.Target})");
            var result = await wake.CalibrateAsync(5, progress);
            WakeEngineStatusText = result.Message;
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "SheListeningTab: wake calibration failed"); WakeEngineStatusText = "Calibration failed — try again."; }
        finally
        {
            IsCalibrating = false;
            CalibrateCaption = "Calibrate to my voice";
            _autonomy?.RefreshVoiceInputModes(); // re-arm the wake loop (with the new threshold if it changed)
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        Devices.Clear();
        var list = _speech?.EnumerateInputDevices() ?? new List<SpeechInputDevice>();
        foreach (var d in list) Devices.Add(d);

        var idx = _settings?.Current?.SpeechInputDeviceIndex ?? -1;
        SelectedDevice = Devices.FirstOrDefault(d => d.Index == idx);
        if (SelectedDevice is null && Devices.Count > 0) SelectedDevice = Devices[0];
    }

    [RelayCommand]
    private void StopMic() => _autonomy?.StopVoiceInput();

    // ── settings write-back + re-arm on each toggle ──

    partial void OnMicConsentGivenChanged(bool value)
    {
        if (_syncing) return;
        if (!value)
        {
            // Turning OFF stays a direct toggle — no consent is needed to disable. Mirrors WPF.
            Apply(s => s.MicConsentGiven = false);
            return;
        }

        // Turning ON routes through the consent dialog and only persists if accepted — mirror the
        // WPF gate at MainWindow.SheListening.cs:38-40. Defer to an async flow because the checkbox
        // already flipped MicConsentGiven to true; if the user declines we revert it.
        if (_consentGateBusy) return;
        _ = RequestMicConsentAsync();
    }

    /// <summary>
    /// Async half of the consent gate. If consent is already on record this is a no-op re-arm;
    /// otherwise the mic consent dialog is shown and the flag is reverted when the user declines.
    /// </summary>
    private async Task RequestMicConsentAsync()
    {
        _consentGateBusy = true;
        try
        {
            if (_settings?.Current?.MicConsentGiven == true)
            {
                Apply(s => s.MicConsentGiven = true); // already consented — persist the flag + re-arm
                return;
            }

            var granted = await ShowMicConsentDialogAsync();
            if (granted)
            {
                // The dialog persisted MicConsentGiven=true + logged the grant; just re-arm the loop.
                _autonomy?.RefreshVoiceInputModes();
            }
            else
            {
                // Declined → revert the checkbox without re-entering the ON path.
                _syncing = true;
                try { MicConsentGiven = false; }
                finally { _syncing = false; }
                Apply(s => s.MicConsentGiven = false);
            }
        }
        finally { _consentGateBusy = false; }
    }

    /// <summary>
    /// Show the mic consent dialog and await its outcome, mirroring how WebcamConsentDialog is shown
    /// from BlinkTrainerTabViewModel. Returns true only when every consent gate was completed.
    /// </summary>
    private async Task<bool> ShowMicConsentDialogAsync()
    {
        // L5-04: show the mic consent gate as a modal, owner-owned dialog instead of an
        // ownerless, non-modal Show(). If no owner window is available we cannot host the modal
        // gate, so treat consent as NOT granted rather than proceeding as though it were given.
        var owner = GetMainWindow();
        if (owner is null)
        {
            _logger?.LogWarning("Mic consent dialog skipped: no owner window available");
            return false;
        }

        var dialog = new MicConsentDialog();
        await dialog.ShowDialog(owner);
        // The dialog is the sole writer of MicConsentGiven; we only read its result flag so the
        // consent-result handling stays identical to before.
        return dialog.ConsentGiven;
    }

    private static global::Avalonia.Controls.Window? GetMainWindow()
        => (global::Avalonia.Application.Current?.ApplicationLifetime
            as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    partial void OnWakeWordEnabledChanged(bool value) => Apply(s => s.SpeechWakeWordEnabled = value);
    partial void OnPushToTalkEnabledChanged(bool value) => Apply(s => s.SpeechPushToTalkEnabled = value);
    partial void OnWakeWordsChanged(string value) => Apply(s => s.SpeechWakeWords = value?.Trim() ?? "");
    partial void OnMicSensitivityChanged(int value)
        => Apply(s => s.SpeechLoudnessThreshold = SensToThreshold(value));
    partial void OnSelectedDeviceChanged(SpeechInputDevice? value)
    {
        if (value is not { } d) return;
        // P2 S-4: persist BOTH the ordinal and the name so ResolveDeviceNumber can re-find the device by
        // name even if NAudio reshuffles ordinals when virtual audio devices appear/disappear (#441b).
        // Mirrors WPF MainWindow.SheListening SL_MicDevice_SelectionChanged (writes SpeechInputDeviceName).
        Apply(s =>
        {
            s.SpeechInputDeviceIndex = d.Index;
            s.SpeechInputDeviceName = d.Index < 0 ? "" : (d.Name ?? "");
        });
    }

    private void Apply(System.Action<ConditioningControlPanel.Models.AppSettings>? write)
    {
        if (_syncing) return;
        var s = _settings?.Current;
        if (s == null || write == null) return;
        try
        {
            write(s);
            _settings!.Save();
            _autonomy?.RefreshVoiceInputModes();
        }
        catch (System.Exception ex) { _logger?.LogDebug(ex, "SheListeningTab: failed to apply a voice setting"); }
    }

    // ── mic-sensitivity <-> loudness-threshold conversion (WPF parity) ──
    // The slider shows a percentage (higher = more sensitive = lower threshold).
    private const double LoudThrAtMinSens = 0.045; // slider 0%  (least sensitive)
    private const double LoudThrAtMaxSens = 0.004; // slider 100% (most sensitive)
    private static double SensToThreshold(double sens)
        => LoudThrAtMinSens - (LoudThrAtMinSens - LoudThrAtMaxSens) * (System.Math.Clamp(sens, 0, 100) / 100.0);
    private static int ThresholdToSens(double thr)
        => (int)System.Math.Round(System.Math.Clamp((LoudThrAtMinSens - thr) / (LoudThrAtMinSens - LoudThrAtMaxSens) * 100.0, 0, 100));
}
