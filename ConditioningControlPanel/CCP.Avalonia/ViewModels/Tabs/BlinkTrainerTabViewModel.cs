using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BlinkTrainer;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.LabTab partial / Blink Trainer flagship tab.
/// Wires the real Blink Trainer, Focus Gaze, and debug-cursor services.
/// </summary>
public partial class BlinkTrainerTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IBlinkTrainerService? _blinkTrainer;
    private readonly IGazeFocusService? _gazeFocus;
    private readonly IGazeDebugCursorService? _gazeCursor;
    private readonly IWebcamService? _webcam;
    private readonly IScreenProvider? _screens;
    private readonly IHapticsService? _haptics;
    private readonly IQuestService? _quests;
    private readonly ILogger<BlinkTrainerTabViewModel>? _logger;

    private DispatcherTimer? _statusTimer;

    // ── Demo preview loop (WPF MainWindow.BlinkTrainer.cs:33-175 parity) ──
    // Cycles 4 SFW abstract gradient PNGs every 2s with a ~200ms cross-fade
    // between two overlapping Image controls (StageImageA/StageImageB in the
    // AXAML). DEMO MODE ONLY — the live-preview OnBlink seam
    // (IBlinkTrainerService) is a separate board follow-up. The cross-fade is
    // driven by bound opacity props; an Avalonia DoubleTransition on each
    // Image animates the value change over ~200ms.
    private DispatcherTimer? _demoTimer;
    private List<IImage> _demoAssets = new();
    private int _demoIndex;
    private bool _demoUsingA = true;
    private bool _isTabSelected;
    private bool _demoAssetsLoaded;

    public BlinkTrainerTabViewModel() : base("blinktrainer", "Blink Trainer", "💫")
    {
        WebcamDevices = new ObservableCollection<WebcamDeviceOption>();
        Monitors = new ObservableCollection<MonitorOption>();
        DebugLog = new ObservableCollection<string>();
        AssetFolders = new ObservableCollection<AssetFolderItem>();
        InitializeDefaults();
    }

    public BlinkTrainerTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IBlinkTrainerService blinkTrainer,
        IGazeFocusService gazeFocus,
        IGazeDebugCursorService gazeCursor,
        IWebcamService webcam,
        IScreenProvider screens,
        IHapticsService? haptics = null,
        IQuestService? quests = null,
        ILogger<BlinkTrainerTabViewModel>? logger = null) : base("blinktrainer", "Blink Trainer", "💫")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _blinkTrainer = blinkTrainer;
        _gazeFocus = gazeFocus;
        _gazeCursor = gazeCursor;
        _webcam = webcam;
        _screens = screens;
        _haptics = haptics;
        _quests = quests;
        _logger = logger;

        WebcamDevices = new ObservableCollection<WebcamDeviceOption>();
        Monitors = new ObservableCollection<MonitorOption>();
        DebugLog = new ObservableCollection<string>();
        AssetFolders = new ObservableCollection<AssetFolderItem>();

        InitializeDefaults();
        LoadFromSettings();
        SubscribeToService();
    }

    private void InitializeDefaults()
    {
        IsPremiumLocked = false;
        IsSessionRunning = false;
        SessionButtonText = Loc.Get("blink_trainer_start_session");
        StatusText = Loc.Get("blink_trainer_status_ready");
        StatusColor = new SolidColorBrush(Color.Parse("#FFFF69B4"));
        ConsentGranted = false;
        ConsentStatusText = Loc.Get("blink_trainer_consent_required");
        CalibrationStatusText = Loc.Get("blink_trainer_calibration_none");

        AssetFolders.Add(new AssetFolderItem("Default", Loc.Get("blink_trainer_folder_empty_or_invalid"), string.Empty));
    }

    private void SubscribeToService()
    {
        if (_blinkTrainer == null) return;
        _blinkTrainer.StateChanged += OnBlinkTrainerStateChanged;
    }

    private void OnBlinkTrainerStateChanged()
    {
        var running = _blinkTrainer?.IsRunning ?? false;
        var lastError = _blinkTrainer?.LastError;
        Dispatcher.UIThread.Post(() =>
        {
            IsSessionRunning = running;
            if (!running && !string.IsNullOrEmpty(lastError))
            {
                StatusText = lastError;
                UpdateStatusColor();
            }
        });
    }

    [ObservableProperty]
    private bool _isTracking;

    [ObservableProperty]
    private string _trackerButtonText = Loc.Get("blink_trainer_tracker_start");

    [ObservableProperty]
    private string _statusText = Loc.Get("blink_trainer_status_stopped");

    [ObservableProperty]
    private IBrush _statusColor = new SolidColorBrush(Color.Parse("#FFFF69B4"));

    [ObservableProperty]
    private string _counterText = Loc.Get("blink_trainer_counter_format");

    [ObservableProperty]
    private bool _focusGazeActive;

    [ObservableProperty]
    private string _focusGazeStatus = "";

    [ObservableProperty]
    private bool _blinkRecalibrateShortcutEnabled;

    [ObservableProperty]
    private bool _restrictGazeToCalibratedScreen;

    [ObservableProperty]
    private bool _debugCursorEnabled;

    [ObservableProperty]
    private ObservableCollection<WebcamDeviceOption> _webcamDevices;

    [ObservableProperty]
    private WebcamDeviceOption? _selectedWebcamDevice;

    [ObservableProperty]
    private ObservableCollection<MonitorOption> _monitors;

    [ObservableProperty]
    private MonitorOption? _selectedMonitor;

    [ObservableProperty]
    private ObservableCollection<string> _debugLog;

    [ObservableProperty]
    private bool _isPremiumLocked;

    [ObservableProperty]
    private bool _isSessionRunning;

    [ObservableProperty]
    private string _sessionButtonText = Loc.Get("blink_trainer_start_session");

    [ObservableProperty]
    private double _sessionDuration = 10;

    [ObservableProperty]
    private double _overlayOpacity = 0.7;

    [ObservableProperty]
    private bool _isMixMode;

    [ObservableProperty]
    private bool _includeVideos;

    // Stage preview images for the demo cross-fade slideshow. Source is an
    // IImage (decoded Bitmap) loaded from avares; Opacity is flipped between
    // 1/0 by AdvanceBlinkTrainerDemo and animated by the AXAML DoubleTransition.
    [ObservableProperty]
    private IImage? _stageImageASource;

    [ObservableProperty]
    private double _stageImageAOpacity = 1.0;

    [ObservableProperty]
    private IImage? _stageImageBSource;

    [ObservableProperty]
    private double _stageImageBOpacity = 0.0;

    [ObservableProperty]
    private bool _consentGranted;

    [ObservableProperty]
    private string _consentStatusText = "";

    [ObservableProperty]
    private string _calibrationStatusText = "";

    [ObservableProperty]
    private ObservableCollection<AssetFolderItem> _assetFolders;

    partial void OnFocusGazeActiveChanged(bool value)
    {
        FocusGazeStatus = value ? Loc.Get("label_focus_gaze_active") : "";
        _logger?.LogInformation("Focus Gaze toggled: {Active}", value);

        if (value)
        {
            // Arm the consumer-driven master toggle so the shared dwell engine stays
            // alive under the auto-start logic even if the camera later cycles
            // (WPF parity: GazeFocusService MasterEnabled, GazeFocusService.cs:90).
            // MasterEnabled on its own NEVER powers the camera — it only rides along.
            if (_gazeFocus != null)
                _gazeFocus.MasterEnabled = true;

            // Start() is the ONLY path that powers the camera, and it returns the
            // bool that drives the calibration/starting feedback UX — preserved as-is.
            if (_gazeFocus?.Start() == true)
            {
                AppendLog(Loc.Get("label_focus_gaze_active"));
                if (DebugCursorEnabled)
                    _gazeCursor?.Show("blinktrainer");
            }
            else
            {
                AppendLog(Loc.Get("label_focus_gaze_calibrate_first"));
            }
        }
        else
        {
            // Disarm the master consumer and let EvaluateDesiredState decide whether
            // to stop — a still-enabled per-feature consumer (flash gaze-pop / linger /
            // video gaze-click) keeps the engine running, matching WPF intent. Do NOT
            // call Stop() directly here (WPF parity: GazeFocusService.cs:90-98 setter).
            if (_gazeFocus != null)
                _gazeFocus.MasterEnabled = false;
            _gazeCursor?.Hide("blinktrainer");
        }
    }

    partial void OnDebugCursorEnabledChanged(bool value)
    {
        AppendLog(value ? Loc.Get("blink_trainer_log_debug_cursor_enabled") : Loc.Get("blink_trainer_log_debug_cursor_hidden"));
        if (!FocusGazeActive) return;
        if (value) _gazeCursor?.Show("blinktrainer");
        else _gazeCursor?.Hide("blinktrainer");
    }

    partial void OnBlinkRecalibrateShortcutEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BlinkRecalibrateShortcutEnabled = value;
        Save();
    }

    partial void OnRestrictGazeToCalibratedScreenChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.RestrictGazeContentToCalibratedScreen = value;
        Save();
    }

    partial void OnSelectedWebcamDeviceChanged(WebcamDeviceOption? value)
    {
        if (value == null || _settingsService?.Current == null) return;
        _settingsService.Current.WebcamDeviceIndex = value.Index;
        _settingsService.Current.WebcamDeviceName = value.Name;
        Save();
        AppendLog(Loc.GetF("blink_trainer_log_camera_set_fmt", value.Name));
    }

    partial void OnSelectedMonitorChanged(MonitorOption? value)
    {
        if (value == null || _settingsService?.Current == null) return;
        _settingsService.Current.WebcamCalibrationScreen = value.DeviceName;
        Save();
        AppendLog(Loc.GetF("blink_trainer_log_monitor_set_fmt", value.Label));
    }

    partial void OnIsSessionRunningChanged(bool value)
    {
        SessionButtonText = value ? Loc.Get("blink_trainer_stop_session") : Loc.Get("blink_trainer_start_session");
        StatusText = value
            ? Loc.GetF("blink_trainer_status_running", $"{SessionDuration:0} min")
            : Loc.Get("blink_trainer_status_ready");
        UpdateStatusColor();
        StartOrStopStatusTimer(value);
        // Stop the demo under a live session; resume it when the session ends
        // (only if the tab is visible). Avoids cycling images that the live
        // preview would otherwise fight, and avoids off-screen CPU burn.
        UpdateDemoTimer();
    }

    partial void OnSessionDurationChanged(double value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BlinkTrainerDurationMinutes = (int)value;
        Save();
        if (IsSessionRunning)
            StatusText = Loc.GetF("blink_trainer_status_running", $"{value:0} min");
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BlinkTrainerOpacity = (int)Math.Round(value * 100.0);
        Save();
    }

    partial void OnIncludeVideosChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BlinkTrainerIncludeVideos = value;
        Save();
        _logger?.LogInformation("Include videos toggled: {Value}", value);
    }

    partial void OnIsMixModeChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BlinkTrainerMixImages = value;
        Save();
    }

    partial void OnConsentGrantedChanged(bool value)
    {
        ConsentStatusText = value ? Loc.Get("blink_trainer_consent_granted") : Loc.Get("blink_trainer_consent_required");
    }

    [RelayCommand]
    private async Task ToggleTrackingAsync()
    {
        if (IsTracking)
        {
            _webcam?.StopTracking();
            IsTracking = false;
            TrackerButtonText = Loc.Get("blink_trainer_tracker_start");
            StatusText = Loc.Get("blink_trainer_status_ready");
            UpdateStatusColor();
            AppendLog(Loc.Get("blink_trainer_log_stop_requested"));
            return;
        }

        // Gate on consent flag AND version, mirroring the WPF
        // WebcamTrackingService.IsConsentCurrent check. A stale version (from
        // before a contract bump) is treated as "not consented" so the user
        // re-runs the multi-gate flow. The dialog is the only path that writes
        // the consent flag + version + date, so there is no direct flag write
        // here.
        if (!IsConsentCurrent())
        {
            var granted = await ShowConsentDialogAsync();
            ConsentGranted = granted;
            if (!granted)
            {
                AppendLog(Loc.Get("blink_trainer_consent_required"));
                return;
            }
        }

        _webcam?.StartTracking();
        IsTracking = true;
        TrackerButtonText = Loc.Get("blink_trainer_tracker_stop");
        StatusText = Loc.Get("blink_trainer_status_starting");
        UpdateStatusColor();
        AppendLog(Loc.Get("blink_trainer_log_start_result"));
    }

    /// <summary>
    /// Consent is current only when the flag is set AND the recorded version
    /// matches the live contract version. Mirrors WPF's
    /// WebcamTrackingService.IsConsentCurrent.
    /// </summary>
    private bool IsConsentCurrent()
    {
        var s = _settingsService?.Current;
        return s?.WebcamConsentGiven == true
            && s.WebcamConsentVersion == WebcamConsent.ConsentVersion;
    }

    /// <summary>
    /// Shows the multi-gate WebcamConsentDialog and awaits its result. The
    /// dialog itself writes WebcamConsentGiven / WebcamConsentVersion /
    /// WebcamConsentDate + saves on acceptance — callers must never write
    /// those flags directly.
    /// </summary>
    private async Task<bool> ShowConsentDialogAsync()
    {
        // L5-04: the webcam consent gate must be a modal, owner-owned dialog — not an
        // ownerless, non-modal Show(). Resolve the desktop main window as owner; if there is no
        // owner we cannot host the modal gate, so treat consent as NOT granted rather than
        // proceeding as though it were given.
        var owner = GetMainWindow();
        if (owner is null)
        {
            _logger?.LogWarning("Webcam consent dialog skipped: no owner window available");
            return false;
        }

        var dialog = new WebcamConsentDialog();
        await dialog.ShowDialog(owner);
        // The dialog remains the sole writer of WebcamConsentGiven / WebcamConsentVersion /
        // WebcamConsentDate; we only read its result flag so the consent-result handling stays
        // identical to before.
        return dialog.ConsentGiven;
    }

    private static global::Avalonia.Controls.Window? GetMainWindow()
        => (global::Avalonia.Application.Current?.ApplicationLifetime
            as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (_blinkTrainer == null) return;

        if (IsSessionRunning)
        {
            await Task.Run(() => _blinkTrainer.Stop());
            IsSessionRunning = false;
            AppendLog(Loc.Get("blink_trainer_log_session_stopped"));
        }
        else
        {
            var started = await Task.Run(() => _blinkTrainer.Start());
            if (started)
            {
                IsSessionRunning = true;
                AppendLog(Loc.Get("blink_trainer_log_session_started"));
            }
            else
            {
                var err = _blinkTrainer.LastError;
                StatusText = err;
                UpdateStatusColor();
                AppendLog(err);
            }
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var path = await (_dialogService?.ShowOpenFolderDialogAsync(Loc.Get("blink_trainer_add_folder")) ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(path)) return;

        var settings = _settingsService?.Current;
        if (settings == null) return;

        if (settings.BlinkTrainerFolders == null)
            settings.BlinkTrainerFolders = new List<string>();

        if (!settings.BlinkTrainerFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            settings.BlinkTrainerFolders.Add(path);
            Save();
            AppendLog(Loc.GetF("blink_trainer_log_add_folder_requested", path));
        }

        RefreshAssetFolders();
    }

    [RelayCommand]
    private async Task GrantConsentAsync()
    {
        // Route the one-click grant through the full multi-gate consent dialog
        // rather than writing the consent flag directly. The dialog writes the
        // flag + current version + date and saves; we only mirror its result.
        var granted = await ShowConsentDialogAsync();
        ConsentGranted = granted;
        AppendLog(granted
            ? Loc.Get("blink_trainer_log_consent_granted")
            : Loc.Get("blink_trainer_consent_required"));
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        _logger?.LogInformation("Premium unlock requested from blink trainer gate");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("gate_premium_locked"),
            Loc.Get("blink_trainer_gate_body")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ShowHelpAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("blink_trainer_help_title"),
            Loc.Get("blink_trainer_help_body")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        AppendLog(Loc.Get("blink_trainer_log_calibration_opening"));
        _webcam?.Calibrate();
        await OpenCalibrationWindowAsync();
        AppendLog(Loc.Get("blink_trainer_log_calibration_cancelled"));
    }

    [RelayCommand]
    private void TrackerTestAsync()
    {
        AppendLog(Loc.Get("blink_trainer_log_tracker_test_opening"));
        _webcam?.TestTracker();
        AppendLog(Loc.Get("blink_trainer_log_tracker_test_closed"));
    }

    [RelayCommand]
    private async Task QuickRecalAsync()
    {
        AppendLog(Loc.Get("blink_trainer_log_quick_recal_opening"));
        var result = await OpenQuickRecalWindowAsync();
        AppendLog(result == true
            ? Loc.Get("blink_trainer_log_quick_recal_complete")
            : Loc.Get("blink_trainer_log_quick_recal_cancelled"));
    }

    private async Task<bool?> OpenQuickRecalWindowAsync()
    {
        var tcs = new TaskCompletionSource<bool?>();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new WebcamQuickRecalWindow(null, null, _webcam);
                window.Closed += (_, _) => tcs.TrySetResult(window.DialogResult);
                window.Show();
            });
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open quick-recal window");
            return false;
        }
    }

    private async Task OpenCalibrationWindowAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                var window = new WebcamCalibrationWindow();
                window.Show();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to open calibration window");
            }
        });
    }

    [RelayCommand]
    private async Task ReviewPrivacyAsync()
    {
        AppendLog(Loc.Get("blink_trainer_log_privacy_reviewed"));
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("blink_trainer_privacy_dialog_title"),
            Loc.Get("blink_trainer_privacy_dialog_body")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task RevokeConsentAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("blink_trainer_consent_revoke_confirm_title"),
            Loc.Get("blink_trainer_revoke_confirm_body_short")) ?? Task.FromResult(false));
        if (!confirmed) return;

        _webcam?.RevokeConsent();
        _gazeFocus?.Stop();
        _gazeCursor?.Hide("blinktrainer");
        FocusGazeActive = false;
        DebugCursorEnabled = false;
        IsTracking = false;
        TrackerButtonText = Loc.Get("blink_trainer_tracker_start");
        StatusText = Loc.Get("blink_trainer_status_ready");
        ConsentGranted = false;
        UpdateStatusColor();
        AppendLog(Loc.Get("blink_trainer_log_consent_revoked"));
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        _webcam?.RefreshDevices();
        WebcamDevices.Clear();
        WebcamDevices.Add(new WebcamDeviceOption(0, Loc.Get("blink_trainer_default_camera")));
        SelectedWebcamDevice = WebcamDevices[0];
        AppendLog(Loc.GetF("blink_trainer_log_camera_scan_result_fmt", 1));
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        Monitors.Clear();
        if (_screens == null)
        {
            Monitors.Add(new MonitorOption("Primary", Loc.Get("webcam_monitor_primary")));
            SelectedMonitor = Monitors[0];
            return;
        }

        var all = _screens.GetAllScreens();
        var primary = _screens.GetPrimaryScreen();
        var index = 1;
        foreach (var screen in all)
        {
            var label = string.Format(Loc.Get("webcam_monitor_item_fmt"),
                index++,
                string.IsNullOrEmpty(screen.Name) ? $"{screen.Bounds.X},{screen.Bounds.Y}" : screen.Name,
                (int)screen.Bounds.Width,
                (int)screen.Bounds.Height);
            Monitors.Add(new MonitorOption(screen.Name, label));
        }

        if (Monitors.Count == 0)
            Monitors.Add(new MonitorOption("Primary", Loc.Get("webcam_monitor_primary")));

        var settings = _settingsService?.Current;
        var preferredName = settings?.WebcamCalibrationScreen;
        SelectedMonitor = Monitors.FirstOrDefault(m =>
            !string.IsNullOrEmpty(preferredName)
            && string.Equals(m.DeviceName, preferredName, StringComparison.OrdinalIgnoreCase))
            ?? Monitors[0];

        AppendLog(Loc.Get("blink_trainer_log_monitors_refreshed"));
    }

    [RelayCommand]
    private void OpenGazeMinigame()
    {
        _logger?.LogInformation("Gaze minigame requested");
    }

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        BlinkRecalibrateShortcutEnabled = s.BlinkRecalibrateShortcutEnabled;
        RestrictGazeToCalibratedScreen = s.RestrictGazeContentToCalibratedScreen;
        SessionDuration = s.BlinkTrainerDurationMinutes;
        OverlayOpacity = s.BlinkTrainerOpacity / 100.0;
        IncludeVideos = s.BlinkTrainerIncludeVideos;
        IsMixMode = s.BlinkTrainerMixImages;
        ConsentGranted = s.WebcamConsentGiven;
        IsPremiumLocked = !(s.HasLinkedPatreon || s.HasLinkedDiscord);

        RefreshAssetFolders();
        RefreshDevices();
        RefreshMonitors();

        SelectedMonitor = Monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, s.WebcamCalibrationScreen, StringComparison.OrdinalIgnoreCase))
            ?? Monitors.FirstOrDefault();
    }

    private void RefreshAssetFolders()
    {
        AssetFolders.Clear();
        var folders = _settingsService?.Current?.BlinkTrainerFolders;
        if (folders == null || folders.Count == 0)
        {
            AssetFolders.Add(new AssetFolderItem("Default", Loc.Get("blink_trainer_folder_empty_or_invalid"), string.Empty));
            return;
        }

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var name = System.IO.Path.GetFileName(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            // L2-09: distinguish a valid folder (image count) from a missing/empty one. The previous
            // ternary returned the same "empty or invalid" label on both branches, so a real folder
            // was always mislabeled. A folder that exists but holds no images is still "empty".
            string status;
            if (System.IO.Directory.Exists(folder))
            {
                var imageCount = CountImageFiles(folder);
                status = imageCount > 0
                    ? Loc.GetF("blink_trainer_folder_image_count_fmt", imageCount)
                    : Loc.Get("blink_trainer_folder_empty_or_invalid");
            }
            else
            {
                status = Loc.Get("blink_trainer_folder_empty_or_invalid");
            }
            AssetFolders.Add(new AssetFolderItem(name, status, folder));
        }
    }
    /// <summary>
    /// Removes a Blink Trainer asset folder by path (WPF
    /// BtnBlinkTrainerRemoveFolderCard_Click parity). Ordinal-ignore-case match so a
    /// path chosen with different casing still clears; persists via Save() and
    /// refreshes the card list. No-op when settings, the folder list, or the path is
    /// null/empty (e.g. the placeholder "Default" row carries an empty Path).
    /// </summary>
    [RelayCommand]
    private void RemoveFolder(string path)
    {
        var s = _settingsService?.Current;
        if (s?.BlinkTrainerFolders == null || string.IsNullOrWhiteSpace(path)) return;
        s.BlinkTrainerFolders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        Save();
        RefreshAssetFolders();
    }

    /// <summary>
    /// Count image files (jpg/png/gif/bmp/webp) directly inside a folder. Returns 0 on any I/O error
    /// so a permission-denied or transient failure degrades to the "empty or invalid" label rather
    /// than throwing during a UI refresh.
    /// </summary>
    private static int CountImageFiles(string folder)
    {
        try
        {
            var exts = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            return System.IO.Directory.EnumerateFiles(folder)
                .Count(f => exts.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return 0;
        }
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save blink trainer settings"); }
    }

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        DebugLog.Add($"[{stamp}] {line}");
        while (DebugLog.Count > 12) DebugLog.RemoveAt(0);
    }

    private void UpdateStatusColor()
    {
        var color = IsTracking || IsSessionRunning ? "#FF00C853" : "#FFFF69B4";
        StatusColor = new SolidColorBrush(Color.Parse(color));
    }

    private void StartOrStopStatusTimer(bool running)
    {
        _statusTimer?.Stop();
        _statusTimer = null;
        if (!running) return;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) =>
        {
            var remaining = _blinkTrainer?.Remaining ?? TimeSpan.Zero;
            CounterText = remaining > TimeSpan.Zero
                ? Loc.GetF("blink_trainer_status_running", $"{remaining.TotalMinutes:0}:{remaining.Seconds:00}")
                : Loc.Get("blink_trainer_counter_format");
        };
        _statusTimer.Start();
    }

    // ── Demo preview loop (WPF MainWindow.BlinkTrainer.cs:33-175 parity) ──

    /// <summary>
    /// Called when this tab becomes the selected tab. Starts the demo loop so
    /// the stage preview animates while visible (and no session is running).
    /// </summary>
    public override void OnSelected()
    {
        base.OnSelected();
        _isTabSelected = true;
        UpdateDemoTimer();
    }

    /// <summary>
    /// Called when the user navigates away from this tab. Stops the demo loop
    /// to avoid cycling images (and burning CPU) while off-screen.
    /// </summary>
    public override void OnDeselected()
    {
        base.OnDeselected();
        _isTabSelected = false;
        UpdateDemoTimer();
    }

    /// <summary>
    /// Starts or stops the demo loop based on tab visibility and session state.
    /// The demo runs only while the tab is visible AND no session is running —
    /// mirrors WPF's ApplyBlinkTrainerStageMode(Demo) gating without the
    /// live-preview tiers (a separate board follow-up).
    /// </summary>
    private void UpdateDemoTimer()
    {
        if (_isTabSelected && !IsSessionRunning)
            StartBlinkTrainerDemo();
        else
            StopBlinkTrainerDemo();
    }

    /// <summary>
    /// Lazily loads the 4 demo PNGs from avares URIs and shuffles them
    /// (Fisher-Yates) so the play order isn't predictable. Cached for the app
    /// session; the decoded Bitmaps outlive every tab visit (WPF parity).
    /// </summary>
    private void EnsureDemoAssetsLoaded()
    {
        if (_demoAssetsLoaded) return;
        _demoAssetsLoaded = true;

        var loaded = new List<IImage>(4);
        for (int i = 1; i <= 4; i++)
        {
            try
            {
                var uri = new Uri($"avares://CCP.Avalonia/Assets/BlinkTrainer/Demo/demo_{i:00}.png");
                if (!global::Avalonia.Platform.AssetLoader.Exists(uri))
                {
                    _logger?.LogWarning("BlinkTrainer demo asset demo_{Index:00}.png not found in resources", i);
                    continue;
                }
                using var stream = global::Avalonia.Platform.AssetLoader.Open(uri);
                loaded.Add(new Bitmap(stream));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BlinkTrainer demo asset demo_{Index:00}.png failed to load", i);
            }
        }

        // Fisher-Yates shuffle so the first run isn't always demo_01 -> 02 -> ...
        var rng = Random.Shared;
        for (int i = loaded.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (loaded[i], loaded[j]) = (loaded[j], loaded[i]);
        }

        _demoAssets = loaded;
    }

    /// <summary>
    /// Starts the 2s cross-fade cycle on the stage preview. Idempotent — if
    /// already running, returns immediately. Sets the initial frame
    /// synchronously so the user never sees an empty stage. Mirrors WPF
    /// StartBlinkTrainerDemoLoop (MainWindow.BlinkTrainer.cs:91-130).
    /// </summary>
    private void StartBlinkTrainerDemo()
    {
        if (_demoTimer != null) return; // already running

        EnsureDemoAssetsLoaded();
        if (_demoAssets.Count == 0)
        {
            _logger?.LogWarning("BlinkTrainer demo loop skipped — no demo assets loaded");
            return;
        }

        _demoIndex = 0;
        _demoUsingA = true;
        StageImageASource = _demoAssets[0];
        StageImageAOpacity = 1;
        StageImageBSource = null;
        StageImageBOpacity = 0;

        _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
        _demoTimer.Tick += (_, _) => AdvanceBlinkTrainerDemo();
        _demoTimer.Start();
    }

    /// <summary>
    /// Stops the demo timer. Idempotent. Keeps the cached assets for the next
    /// tab visit (WPF parity: StopBlinkTrainerDemoLoop).
    /// </summary>
    private void StopBlinkTrainerDemo()
    {
        if (_demoTimer == null) return;
        _demoTimer.Stop();
        _demoTimer = null;
    }

    /// <summary>
    /// Advances to the next demo asset, loading it into the inactive Image and
    /// cross-fading opacity. The ~200ms fade is handled by the Avalonia
    /// DoubleTransition declared on each Image's Opacity in the AXAML, so
    /// flipping the bound opacity values here is all that's needed. Mirrors WPF
    /// AdvanceBlinkTrainerDemo (MainWindow.BlinkTrainer.cs:153-175).
    /// </summary>
    private void AdvanceBlinkTrainerDemo()
    {
        if (_demoAssets.Count == 0) return;

        _demoIndex = (_demoIndex + 1) % _demoAssets.Count;
        var next = _demoAssets[_demoIndex];

        if (_demoUsingA)
        {
            // Incoming = B, outgoing = A.
            StageImageBSource = next;
            StageImageBOpacity = 1;
            StageImageAOpacity = 0;
        }
        else
        {
            // Incoming = A, outgoing = B.
            StageImageASource = next;
            StageImageAOpacity = 1;
            StageImageBOpacity = 0;
        }

        _demoUsingA = !_demoUsingA;
    }
}

public sealed record WebcamDeviceOption(int Index, string Name);
public sealed record MonitorOption(string DeviceName, string Label);
public sealed record AssetFolderItem(string Name, string Status, string Path);
