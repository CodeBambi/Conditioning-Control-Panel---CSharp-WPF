using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Quiz;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;
using ConditioningControlPanel.Models.Quiz;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Lab partial.
/// Exposes lockdown, quiz, chaos mode, wallpaper override, and lab session commands.
/// Lockdown, wallpaper, and pop-quiz test are wired to real Core services; chaos/AI remain parity stubs.
/// </summary>
public partial class LabTabViewModel : TabItemViewModel
{
    private static readonly string[] WallpaperExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IWebcamService? _webcam;
    private readonly IWallpaperProvider? _wallpaperProvider;
    private readonly ILockdownService? _lockdownService;
    private readonly IAppEnvironment? _appEnvironment;
    private readonly IPopQuizService? _popQuiz;
    private readonly IQuizService? _quizService;
    private readonly IFrameSource? _frameSource;
    private readonly ILogger<LabTabViewModel>? _logger;
    private readonly Random _random = new();
    private readonly System.Collections.Generic.Dictionary<string, QuizHistoryEntry> _historyByLabel = new();

    private bool _syncingPlayMode;
    private int _lockdownTimerClickCount;
    private bool _webcamDevicePopulating;
    private List<string> _wallpaperPool = new();
    private string? _currentWallpaperPath;
    private string? _originalWallpaperPath;

    public LabTabViewModel() : base("lab", "Lab", "🧪")
    {
        LockdownDurations = new ObservableCollection<int> { 5, 10, 15, 30, 60 };
        PastQuizzes = new ObservableCollection<PastQuizViewModel>();
        WebcamDevices = new ObservableCollection<WebcamDeviceOption>();
        Monitors = new ObservableCollection<MonitorOption>();
        EffectPermissions = new ObservableCollection<LabEffectPermissionItem>();
        DebugLog = new ObservableCollection<string>();
        InitializeDefaults();
    }

    public LabTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IWebcamService webcam,
        IWallpaperProvider wallpaperProvider,
        ILockdownService lockdownService,
        IAppEnvironment appEnvironment,
        IPopQuizService popQuiz,
        ILogger<LabTabViewModel> logger,
        IQuizService? quizService = null,
        IFrameSource? frameSource = null) : base("lab", "Lab", "🧪")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _webcam = webcam;
        _wallpaperProvider = wallpaperProvider;
        _lockdownService = lockdownService;
        _appEnvironment = appEnvironment;
        _popQuiz = popQuiz;
        _quizService = quizService;
        _frameSource = frameSource;
        _logger = logger;
        LockdownDurations = new ObservableCollection<int> { 5, 10, 15, 30, 60 };
        PastQuizzes = new ObservableCollection<PastQuizViewModel>();
        WebcamDevices = new ObservableCollection<WebcamDeviceOption>();
        Monitors = new ObservableCollection<MonitorOption>();
        EffectPermissions = new ObservableCollection<LabEffectPermissionItem>();
        DebugLog = new ObservableCollection<string>();
        InitializeDefaults();
        SubscribeLockdownEvents();
        LoadFromSettings();
        LoadPastQuizzes();
    }

    #region Lockdown / Quiz / Chaos / Wallpaper (existing bindings)

    [ObservableProperty]
    private bool _isLockdownActive;

    [ObservableProperty]
    private int _selectedLockdownDuration = 10;

    [ObservableProperty]
    private string _lockdownTimerText = "00:00";

    [ObservableProperty]
    private bool _popQuizEnabled;

    [ObservableProperty]
    private int _popQuizFrequency = 3;

    [ObservableProperty]
    private string _popQuizFrequencyText = "3/session hr";

    [ObservableProperty]
    private bool _wallpaperEnabled;

    [ObservableProperty]
    private string _currentWallpaper = "";

    [ObservableProperty]
    private bool _quizFullscreen;

    [ObservableProperty]
    private bool _quizDrone;

    [ObservableProperty]
    private ObservableCollection<int> _lockdownDurations;

    [ObservableProperty]
    private ObservableCollection<PastQuizViewModel> _pastQuizzes;

    [ObservableProperty]
    private PastQuizViewModel? _selectedPastQuiz;

    #endregion

    #region Hero / how-to-play

    [ObservableProperty]
    private bool _playModeFreeDesktop = true;

    [ObservableProperty]
    private bool _playModeStory;

    [ObservableProperty]
    private bool _announcementsEnabled = true;

    #endregion

    #region AI Effects & Memory

    [ObservableProperty]
    private bool _aiEffectsEnabled;

    [ObservableProperty]
    private bool _aiMemoryEnabled;

    [ObservableProperty]
    private ObservableCollection<LabEffectPermissionItem> _effectPermissions;

    #endregion

    #region Webcam engine

    [ObservableProperty]
    private ObservableCollection<WebcamDeviceOption> _webcamDevices;

    [ObservableProperty]
    private WebcamDeviceOption? _selectedWebcamDevice;

    [ObservableProperty]
    private ObservableCollection<MonitorOption> _monitors;

    [ObservableProperty]
    private MonitorOption? _selectedMonitor;

    [ObservableProperty]
    private bool _isTracking;

    [ObservableProperty]
    private string _trackerButtonText = "";

    [ObservableProperty]
    private string _trackerStatusText = "";

    [ObservableProperty]
    private IBrush _trackerStatusColor = GetThemeBrush("PinkBrush", "#FFFF69B4");

    [ObservableProperty]
    private bool _debugCursorEnabled;

    [ObservableProperty]
    private string _countersText = "";

    [ObservableProperty]
    private ObservableCollection<string> _debugLog;

    // Gaze-engine picker (Current / Tier 1 / Deep) surfaced in the tracking popup.
    // This chooses which engine the NEXT calibration trains + switches to; it does
    // NOT flip the live runtime feature (that always follows the loaded calibration),
    // so selecting a not-yet-calibrated engine can never corrupt live tracking. The
    // chosen engine is applied when the user calibrates; after the calibration window
    // closes we re-sync to whatever engine actually got committed.
    [ObservableProperty]
    private int _selectedGazeEngineIndex;

    private static readonly GazeFeatureMode[] GazeEngineOrder =
    {
        GazeFeatureMode.Current, GazeFeatureMode.Tier1, GazeFeatureMode.DeepModel,
    };

    private void SyncGazeEngineFromTracker()
    {
        var active = _webcam?.GazePipelineMode ?? GazeFeatureMode.Current;
        int i = Array.IndexOf(GazeEngineOrder, active);
        SelectedGazeEngineIndex = i >= 0 ? i : 0;
    }

    #endregion

    #region EYES cards

    [ObservableProperty]
    private bool _focusGazeEnabled;

    [ObservableProperty]
    private string _focusGazeStatusText = "";

    [ObservableProperty]
    private IBrush _focusGazeStatusColor = GetThemeBrush("TextMutedBrush", "#FF888888");

    #endregion

    #region Gating

    [ObservableProperty]
    private bool _isBetaLocked;

    #endregion

    private void InitializeDefaults()
    {
        IsBetaLocked = false;
        PlayModeFreeDesktop = true;
        PlayModeStory = false;
        AnnouncementsEnabled = true;
        TrackerButtonText = Loc.Get("lab_start_tracking");
        TrackerStatusText = Loc.Get("lab_tracker_status_stopped");
        SyncGazeEngineFromTracker();
        CountersText = "0 blinks · 0 gaze hits · 0 rabbits";
        FocusGazeStatusText = Loc.Get("lab_focus_gaze_hint");

        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("recap_feat_flash")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("recap_feat_video")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("recap_feat_overlay")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("recap_feat_bubbles")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("label_haptics_enabled")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("label_audio_whispers")));

        WebcamDevices.Add(new WebcamDeviceOption(0, Loc.Get("blink_trainer_default_camera")));
        SelectedWebcamDevice = WebcamDevices[0];

        Monitors.Add(new MonitorOption("Primary", Loc.Get("webcam_monitor_primary")));
        SelectedMonitor = Monitors[0];
    }

    partial void OnPlayModeFreeDesktopChanged(bool value)
    {
        if (_syncingPlayMode) return;
        _syncingPlayMode = true;
        PlayModeStory = !value;
        _syncingPlayMode = false;
    }

    partial void OnPlayModeStoryChanged(bool value)
    {
        if (_syncingPlayMode) return;
        _syncingPlayMode = true;
        PlayModeFreeDesktop = !value;
        _syncingPlayMode = false;
    }

    partial void OnPopQuizEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.PopQuizEnabled = value;
        Save();
    }

    partial void OnPopQuizFrequencyChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.PopQuizFrequency = value;
        PopQuizFrequencyText = $"{value}/session hr";
        Save();
    }

    partial void OnWallpaperEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        if (value)
        {
            if (!TryActivateWallpaper())
            {
                _settingsService.Current.WallpaperEnabled = false;
                WallpaperEnabled = false;
                _ = _dialogService?.ShowMessageAsync(
                    Loc.Get("wallpaper_override_title"),
                    Loc.Get("msg_no_wallpaper_images"));
                return;
            }
        }
        else
        {
            _wallpaperProvider?.RestoreOriginalWallpaper();
            _currentWallpaperPath = null;
            CurrentWallpaper = "";
            _logger?.LogInformation("Wallpaper override deactivated");
        }
        _settingsService.Current.WallpaperEnabled = value;
        Save();
    }

    private bool TryActivateWallpaper()
    {
        try
        {
            var wallpapersDir = Path.Combine(_appEnvironment?.EffectiveAssetsPath ?? AppContext.BaseDirectory, "wallpapers");
            if (!Directory.Exists(wallpapersDir))
            {
                _logger?.LogWarning("[Wallpaper] Wallpapers directory not found: {Dir}", wallpapersDir);
                return false;
            }

            _wallpaperPool = Directory.GetFiles(wallpapersDir)
                .Where(f => WallpaperExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (_wallpaperPool.Count == 0)
            {
                _logger?.LogWarning("[Wallpaper] No supported images found in {Dir}", wallpapersDir);
                return false;
            }

            var image = _wallpaperPool[_random.Next(_wallpaperPool.Count)];
            _wallpaperProvider?.SetWallpaper(image);
            _currentWallpaperPath = image;
            CurrentWallpaper = Path.GetFileName(image);
            _logger?.LogInformation("[Wallpaper] Activated with {File} (pool: {Count} images)", CurrentWallpaper, _wallpaperPool.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wallpaper] Failed to activate");
            return false;
        }
    }

    partial void OnAiEffectsEnabledChanged(bool value)
    {
        _logger?.LogInformation("AI effects master toggle: {Active}", value);
    }

    partial void OnAiMemoryEnabledChanged(bool value)
    {
        _logger?.LogInformation("AI memory toggle: {Active}", value);
    }

    partial void OnIsTrackingChanged(bool value)
    {
        TrackerButtonText = value ? Loc.Get("btn_stop") : Loc.Get("lab_start_tracking");
        if (!value)
        {
            TrackerStatusText = Loc.Get("lab_tracker_status_stopped");
        }
        TrackerStatusColor = value
            ? GetThemeBrush("SuccessBrush", "#FF00C853")
            : GetThemeBrush("PinkBrush", "#FFFF69B4");
        AppendLog(value ? Loc.Get("lab_log_tracking_started") : Loc.Get("lab_log_tracking_stopped"));
    }

    partial void OnSelectedWebcamDeviceChanged(WebcamDeviceOption? value)
    {
        // RefreshDevices sets the combo programmatically; ignore those
        // selection pulses so we only persist genuine user picks (mirrors the
        // WPF _webcamDevicePopulating guard).
        if (_webcamDevicePopulating) return;
        if (value == null || _settingsService?.Current == null) return;
        _settingsService.Current.WebcamDeviceIndex = value.Index;
        _settingsService.Current.WebcamDeviceName = value.Name;
        Save();
        AppendLog(Loc.GetF("blink_trainer_log_camera_set_fmt", value.Name));
    }

    partial void OnDebugCursorEnabledChanged(bool value)
    {
        AppendLog(value ? Loc.Get("lab_log_debug_cursor_enabled") : Loc.Get("lab_log_debug_cursor_hidden"));
    }

    partial void OnFocusGazeEnabledChanged(bool value)
    {
        FocusGazeStatusText = value ? Loc.Get("lab_tracker_status_running") : Loc.Get("lab_focus_gaze_hint");
        FocusGazeStatusColor = value
            ? GetThemeBrush("SuccessBrush", "#FF00C853")
            : GetThemeBrush("TextMutedBrush", "#FF888888");
        _logger?.LogInformation("Focus Gaze toggled: {Active}", value);
    }

    [RelayCommand]
    private async Task ActivateLockdownAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("lab_lockdown_title"),
            string.Format(Loc.Get("lab_lockdown_message_fmt"), SelectedLockdownDuration)) ?? Task.FromResult(false));
        if (!confirmed) return;

        _lockdownService?.Activate(TimeSpan.FromMinutes(SelectedLockdownDuration));
        _logger?.LogInformation("Lockdown activated for {Minutes} minutes", SelectedLockdownDuration);
    }

    [RelayCommand]
    private async Task StartQuizAsync()
    {
        _logger?.LogInformation("Quiz requested");
        await (_dialogService?.ShowMessageAsync(
            "Login Required",
            Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ViewQuizReportAsync()
    {
        var selected = SelectedPastQuiz;
        if (selected == null) return;

        var entry = selected.Entry
            ?? (_historyByLabel.TryGetValue(selected.Label, out var cached) ? cached : null)
            ?? _quizService?.LoadHistory()
                .FirstOrDefault(h => FormatQuizLabel(h) == selected.Label
                                  && h.CategoryName == selected.Category);

        if (entry == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_quiz_report_not_found")) ?? Task.CompletedTask);
            return;
        }

        var owner = GetMainWindow();
        if (owner == null) return;

        var report = new QuizReportWindow(entry);
        await Dispatcher.UIThread.InvokeAsync(() => report.Show(owner));
    }

    [RelayCommand]
    private async Task StartChaosAsync()
    {
        _logger?.LogInformation("Chaos mode requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("lab_chaos_title"),
            Loc.Get("lab_chaos_not_available")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task QuickStartChaosAsync()
    {
        _logger?.LogInformation("Chaos quick start requested");
        await StartChaosAsync();
    }

    [RelayCommand]
    private void TestPopQuiz()
    {
        _logger?.LogInformation("Pop quiz test requested");
        _popQuiz?.TestPopQuiz();
    }

    [RelayCommand]
    private void ShuffleWallpaper()
    {
        try
        {
            if (_wallpaperPool.Count == 0 && !TryActivateWallpaper()) return;

            string image;
            if (_wallpaperPool.Count == 1)
            {
                image = _wallpaperPool[0];
            }
            else
            {
                do
                {
                    image = _wallpaperPool[_random.Next(_wallpaperPool.Count)];
                } while (image == _currentWallpaperPath);
            }

            _wallpaperProvider?.SetWallpaper(image);
            _currentWallpaperPath = image;
            CurrentWallpaper = Path.GetFileName(image);
            _logger?.LogDebug("[Wallpaper] Shuffled to {File}", CurrentWallpaper);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Wallpaper] Failed to shuffle");
        }
    }

    [RelayCommand]
    private void LockdownTimerClick()
    {
        if (_lockdownService?.IsActive != true) return;

        _lockdownTimerClickCount++;
        _logger?.LogInformation("Lockdown timer clicked ({Count} times)", _lockdownTimerClickCount);

        if (_lockdownTimerClickCount >= 5)
        {
            AppendLog(Loc.Get("lab_log_lockdown_exit_revealed"));
        }
    }

    [RelayCommand]
    private void AttemptLockdownExit(string? phrase)
    {
        if (string.IsNullOrEmpty(phrase) || _lockdownService?.IsActive != true) return;

        if (_lockdownService.TryExitWithPhrase(phrase))
        {
            _lockdownTimerClickCount = 0;
            AppendLog(Loc.Get("lab_log_lockdown_exited"));
        }
        else
        {
            AppendLog(Loc.Get("lab_log_lockdown_exit_failed"));
        }
    }

    [RelayCommand]
    private async Task ForgetEverythingAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("lab_forget_title"),
            Loc.Get("lab_forget_message")) ?? Task.FromResult(false));
        if (!confirmed) return;

        AiMemoryEnabled = false;
        _logger?.LogInformation("AI memory cleared (stub)");
        AppendLog(Loc.Get("lab_log_ai_memory_cleared"));
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        var devices = _webcam?.EnumerateDevices() ?? Array.Empty<WebcamDeviceInfo>();
        _webcamDevicePopulating = true;
        try
        {
            WebcamDevices.Clear();
            if (devices.Count == 0)
            {
                WebcamDevices.Add(new WebcamDeviceOption(-1, Loc.Get("lab_no_cameras_detected")));
                SelectedWebcamDevice = WebcamDevices[0];
                return;
            }

            foreach (var d in devices)
                WebcamDevices.Add(new WebcamDeviceOption(d.Index, d.Name));

            int saved = _settingsService?.Current?.WebcamDeviceIndex ?? -1;
            SelectedWebcamDevice = WebcamDevices.FirstOrDefault(x => x.Index == saved) ?? WebcamDevices[0];
        }
        finally
        {
            _webcamDevicePopulating = false;
        }
        AppendLog(Loc.Get("lab_log_camera_list_refreshed"));
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        Monitors.Clear();
        Monitors.Add(new MonitorOption("Primary", Loc.Get("webcam_monitor_primary")));
        SelectedMonitor = Monitors[0];
        AppendLog(Loc.Get("lab_log_monitor_list_refreshed"));
    }

    [RelayCommand]
    private async Task StartTrackingAsync()
    {
        if (IsTracking)
        {
            _webcam?.StopTracking();
            // IsTracking is driven by OnTrackingStateChanged below; no optimistic
            // flip here.
            return;
        }

        if (_webcam == null)
            return;

        var owner = GetMainWindow();
        WebcamLoadingSplash? splash = null;
        if (owner != null)
        {
            splash = new WebcamLoadingSplash();
            await Dispatcher.UIThread.InvokeAsync(() => splash.Show(owner));
        }

        // Track start completion through the real event stream rather than a
        // fixed Task.Delay. OnStartupProgress feeds the splash bar; terminal
        // OnTrackingStateChanged states resolve the wait and surface honest
        // status text (starting / tracking / camera-busy / denied / error).
        var done = new TaskCompletionSource<bool>();
        void OnProgress(double progress, string status)
        {
            splash?.SetProgress(progress, status);
            if (progress >= 1.0)
                done.TrySetResult(true);
        }
        void OnState(WebcamTrackingState state)
        {
            Dispatcher.UIThread.Post(() => ApplyTrackerState(state));
            switch (state)
            {
                case WebcamTrackingState.Tracking:
                    done.TrySetResult(true);
                    break;
                case WebcamTrackingState.CameraInUse:
                    splash?.ShowErrorAndClose(Loc.Get("lab_tracker_error_camera_in_use"));
                    done.TrySetResult(false);
                    break;
                case WebcamTrackingState.CameraDenied:
                    splash?.ShowErrorAndClose(Loc.Get("lab_tracker_error_camera_denied"));
                    done.TrySetResult(false);
                    break;
                case WebcamTrackingState.Error:
                    splash?.ShowErrorAndClose(Loc.Get("lab_tracker_error_generic"));
                    done.TrySetResult(false);
                    break;
                case WebcamTrackingState.Stopped:
                    // Engine never reached Tracking (user/another caller stopped it).
                    done.TrySetResult(false);
                    break;
            }
        }

        _webcam.OnStartupProgress += OnProgress;
        _webcam.OnTrackingStateChanged += OnState;
        TrackerStatusText = Loc.Get("lab_tracker_status_starting");
        TrackerStatusColor = GetThemeBrush("PinkBrush", "#FFFF69B4");
        try
        {
            _webcam.StartTracking();
            await done.Task;
        }
        finally
        {
            _webcam.OnStartupProgress -= OnProgress;
            _webcam.OnTrackingStateChanged -= OnState;
            splash?.CloseSplash();
        }
    }

    /// <summary>
    /// Surfaces the live tracker state honestly. Driving IsTracking from here
    /// (instead of optimistically after StartTracking) keeps the button, color,
    /// and log in sync with what the engine actually did.
    /// </summary>
    private void ApplyTrackerState(WebcamTrackingState state)
    {
        switch (state)
        {
            case WebcamTrackingState.Starting:
                TrackerStatusText = Loc.Get("lab_tracker_status_starting");
                TrackerStatusColor = GetThemeBrush("PinkBrush", "#FFFF69B4");
                IsTracking = false;
                break;
            case WebcamTrackingState.Tracking:
                TrackerStatusText = Loc.Get("lab_tracker_status_running");
                TrackerStatusColor = GetThemeBrush("SuccessBrush", "#FF00C853");
                IsTracking = true;
                break;
            case WebcamTrackingState.FaceLost:
                TrackerStatusText = Loc.Get("lab_tracker_status_face_lost");
                TrackerStatusColor = GetThemeBrush("TextMutedBrush", "#FF888888");
                break;
            case WebcamTrackingState.CameraInUse:
                TrackerStatusText = Loc.Get("lab_tracker_status_camera_busy");
                TrackerStatusColor = GetThemeBrush("TextMutedBrush", "#FF888888");
                IsTracking = false;
                break;
            case WebcamTrackingState.CameraDenied:
                TrackerStatusText = Loc.Get("lab_tracker_status_denied");
                TrackerStatusColor = GetThemeBrush("TextMutedBrush", "#FF888888");
                IsTracking = false;
                break;
            case WebcamTrackingState.Error:
                TrackerStatusText = Loc.Get("lab_tracker_status_error");
                TrackerStatusColor = GetThemeBrush("TextMutedBrush", "#FF888888");
                IsTracking = false;
                break;
            default:
                TrackerStatusText = Loc.Get("lab_tracker_status_stopped");
                TrackerStatusColor = GetThemeBrush("PinkBrush", "#FFFF69B4");
                IsTracking = false;
                break;
        }
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        _webcam?.Calibrate();
        AppendLog(Loc.Get("lab_log_calibration_requested"));
        var engine = GazeEngineOrder[Math.Clamp(SelectedGazeEngineIndex, 0, GazeEngineOrder.Length - 1)];
        await OpenWebcamWindowAsync(() => new WebcamCalibrationWindow(_frameSource, initialEngine: engine));
        // Reflect whatever engine was actually committed (or the baseline if cancelled).
        SyncGazeEngineFromTracker();
    }

    [RelayCommand]
    private async Task QuickRecalAsync()
    {
        _webcam?.Calibrate();
        AppendLog(Loc.Get("lab_log_quick_recal_requested"));
        await OpenWebcamWindowAsync(() => new WebcamQuickRecalWindow(_frameSource, null, _webcam));
    }

    [RelayCommand]
    private async Task TrackerTestAsync()
    {
        _webcam?.TestTracker();
        AppendLog(Loc.Get("lab_log_tracker_test_requested"));
        await OpenWebcamWindowAsync(() => new WebcamGazeTrackerWindow(_frameSource, null, _webcam));
    }

    [RelayCommand]
    private async Task RevokeConsentAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("lab_revoke_consent_title"),
            Loc.Get("lab_revoke_consent_message")) ?? Task.FromResult(false));
        if (!confirmed) return;

        _webcam?.RevokeConsent();
        IsTracking = false;
        DebugCursorEnabled = false;
        AppendLog(Loc.Get("lab_log_consent_revoked"));
    }

    [RelayCommand]
    private void OpenGazeMinigame()
    {
        _logger?.LogInformation("Gaze minigame requested");
        AppendLog(Loc.Get("lab_log_gaze_minigame_opened"));
    }

    [RelayCommand]
    private async Task GoToBlinkTrainerAsync()
    {
        _logger?.LogInformation("Navigate to Blink Trainer requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("lab_blink_trainer_title"),
            Loc.Get("lab_blink_trainer_message")) ?? Task.CompletedTask);
    }

    private void SubscribeLockdownEvents()
    {
        if (_lockdownService == null) return;

        _lockdownService.LockdownActivated += OnLockdownActivated;
        _lockdownService.LockdownDeactivated += OnLockdownDeactivated;
        _lockdownService.CountdownTick += OnLockdownTick;

        IsLockdownActive = _lockdownService.IsActive;
        if (IsLockdownActive)
            LockdownTimerText = FormatLockdownTime(_lockdownService.Remaining);
    }

    private void OnLockdownActivated()
    {
        IsLockdownActive = true;
        _lockdownTimerClickCount = 0;
        AppendLog(Loc.Get("lab_log_lockdown_activated"));
    }

    private void OnLockdownDeactivated()
    {
        IsLockdownActive = false;
        _lockdownTimerClickCount = 0;
        LockdownTimerText = "00:00";
        AppendLog(Loc.Get("lab_log_lockdown_deactivated"));
    }

    private void OnLockdownTick(TimeSpan remaining)
    {
        LockdownTimerText = FormatLockdownTime(remaining);
    }

    private static string FormatLockdownTime(TimeSpan remaining)
    {
        return remaining.TotalHours >= 1
            ? remaining.ToString(@"h\:mm\:ss")
            : remaining.ToString(@"mm\:ss");
    }

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null)
        {
            IsBetaLocked = true;
            return;
        }

        PopQuizEnabled = s.PopQuizEnabled;
        PopQuizFrequency = s.PopQuizFrequency;
        WallpaperEnabled = s.WallpaperEnabled;

        WebcamDevices.Clear();
        WebcamDevices.Add(new WebcamDeviceOption(Math.Max(0, s.WebcamDeviceIndex), s.WebcamDeviceName));
        SelectedWebcamDevice = WebcamDevices[0];

        Monitors.Clear();
        Monitors.Add(new MonitorOption(s.WebcamCalibrationScreen, s.WebcamCalibrationScreen));
        SelectedMonitor = Monitors[0];

        IsBetaLocked = !(s.HasLinkedPatreon || s.HasLinkedDiscord);
    }

    private void LoadPastQuizzes()
    {
        try
        {
            PastQuizzes.Clear();
            _historyByLabel.Clear();
            var history = _quizService?.LoadHistory() ?? new System.Collections.Generic.List<QuizHistoryEntry>();
            foreach (var entry in history)
            {
                var label = FormatQuizLabel(entry);
                _historyByLabel[label] = entry;
                PastQuizzes.Add(new PastQuizViewModel
                {
                    Label = label,
                    Category = entry.CategoryName,
                    Percent = entry.MaxScore > 0
                        ? (int)System.Math.Round((double)entry.TotalScore / entry.MaxScore * 100)
                        : 0,
                    Entry = entry
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load past quizzes");
        }
    }

    private static string FormatQuizLabel(QuizHistoryEntry entry)
        => entry.TakenAt.ToString("g");

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save lab settings"); }
    }

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        DebugLog.Add($"[{stamp}] {line}");
        while (DebugLog.Count > 12) DebugLog.RemoveAt(0);
    }

    private static IBrush GetThemeBrush(string key, string fallbackHex)
    {
        var app = Application.Current;
        if (app != null &&
            app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private async Task OpenWebcamWindowAsync(Func<Window> createWindow)
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var window = createWindow();
        await Dispatcher.UIThread.InvokeAsync(() => window.Show(owner));
    }
}

public sealed partial class PastQuizViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private int _percent;

    /// <summary>Strongly typed backing entry for the quiz report window.</summary>
    public QuizHistoryEntry? Entry { get; set; }
}

/// <summary>
/// Visual-shell item for an AI effect permission toggle in the Lab AI card.
/// </summary>
public sealed partial class LabEffectPermissionItem : ObservableObject
{
    public LabEffectPermissionItem(string label)
    {
        Label = label;
    }

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private bool _isEnabled;
}
