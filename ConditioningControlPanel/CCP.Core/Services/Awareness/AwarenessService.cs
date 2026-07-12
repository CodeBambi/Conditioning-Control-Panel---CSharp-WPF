using System;
using System.Threading;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Core.Services.Awareness;

/// <summary>
/// Portable window-awareness engine, ported from the WPF head's
/// Services/UI/WindowAwarenessService.cs. Polls the platform's foreground window title
/// every 1.5s (WPF :332-356), classifies it via <see cref="AwarenessClassifier"/> only when
/// the title changes (WPF :498-517), detects idle (WPF :489-497), and raises
/// <see cref="ActivityChanged"/> / <see cref="StillOnActivity"/>.
///
/// Threading: unlike WPF's DispatcherTimer, this engine uses <see cref="System.Threading.Timer"/>
/// (CCP.Core stays UI-framework-agnostic here), so BOTH events fire on a background thread-pool
/// thread — consumers must marshal to their UI thread (the interface XML doc carries the same
/// warning; existing consumers already do).
///
/// Privacy (hard contract, WPF :58-62): the raw foreground title lives in memory only for change
/// detection. It is never written to disk, never sent over the network, and never logged — log
/// lines carry the derived detected name only (WPF :539-540). The WPF debug line that logged the
/// full raw title (WPF :483-486) is deliberately NOT ported. This engine performs no network I/O;
/// the derived-payload-to-AI path is a separate consumer concern (AI-2).
///
/// On heads without an <see cref="IForegroundWindowTitleProvider"/> registration (Linux/macOS
/// for now), <see cref="Start"/> no-ops gracefully and the engine stays off.
/// </summary>
public sealed class AwarenessService : IAwarenessService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IForegroundWindowTitleProvider? _titleProvider;
    private readonly ILogger<AwarenessService>? _logger;
    private readonly object _gate = new();

    // State (mirrors WPF Services/UI/WindowAwarenessService.cs:72-84)
    private Timer? _pollTimer;
    private Timer? _stillOnTimer;
    private ActivityCategory _currentCategory = ActivityCategory.Unknown;
    private string _currentDetectedName = "";
    private string _currentServiceName = "";
    private string _currentPageTitle = "";
    private string _lastWindowTitle = "";
    private DateTime _lastActivityChange = DateTime.Now;
    private DateTime _lastReactionTime = DateTime.MinValue;
    private DateTime _lastStillOnTime = DateTime.MinValue;
    private bool _isRunning;
    private bool _isDisposed;

    // Constants (WPF :87)
    private const int IdleThresholdMinutes = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5); // WPF :345 "Fast polling for quick tab/app detection"

    // Still-on milestone tracking: 1 min, 5 min, 10 min (WPF :408-410)
    private static readonly int[] StillOnMilestonesMinutes = { 1, 5, 10 };
    private int _currentMilestoneIndex;
    // Faithful stand-in for "a one-shot DispatcherTimer exists": with System.Threading.Timer a
    // stale callback can race Stop()/re-arm, so ticks are ignored unless a milestone is armed.
    private bool _stillOnArmed;

    public AwarenessService(
        ISettingsService settingsService,
        IForegroundWindowTitleProvider? titleProvider = null,
        ILogger<AwarenessService>? logger = null)
    {
        _settingsService = settingsService;
        _titleProvider = titleProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    /// <inheritdoc />
    public event EventHandler<ActivityChangedEventArgs>? StillOnActivity;

    /// <inheritdoc />
    public ActivityCategory CurrentActivity => _currentCategory;

    /// <inheritdoc />
    public string CurrentDetectedName => _currentDetectedName;

    /// <inheritdoc />
    public string CurrentServiceName => _currentServiceName;

    /// <inheritdoc />
    public string CurrentPageTitle => _currentPageTitle;

    /// <inheritdoc />
    public TimeSpan CurrentActivityDuration => DateTime.Now - _lastActivityChange;

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_isRunning || _isDisposed) return;

            // Platform gap guard: no foreground-title seam registered on this head (Linux/macOS).
            if (_titleProvider is null)
            {
                _logger?.LogInformation("WindowAwareness: no foreground window title provider on this platform - not starting");
                return;
            }

            // Check if feature is enabled and consent given (WPF :336-342)
            if (_settingsService.Current?.AwarenessModeEnabled != true ||
                _settingsService.Current?.AwarenessConsentGiven != true)
            {
                _logger?.LogDebug("WindowAwareness: Not starting - feature disabled or no consent");
                return;
            }

            _pollTimer = new Timer(_ => OnPollTimerCallback(), null, PollInterval, PollInterval); // WPF :344-350
            _isRunning = true;

            _logger?.LogInformation("WindowAwareness: Started monitoring");
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (!_isRunning) return;

            // WPF :358-373: stop + null both timers, category -> Unknown, name -> "".
            _pollTimer?.Dispose();
            _pollTimer = null;
            _stillOnTimer?.Dispose();
            _stillOnTimer = null;
            _stillOnArmed = false;
            _isRunning = false;
            _currentCategory = ActivityCategory.Unknown;
            _currentDetectedName = "";

            _logger?.LogInformation("WindowAwareness: Stopped monitoring");
        }
    }

    /// <inheritdoc />
    public bool CanReact()
    {
        // WPF :376-383 (the ?? 90 fallback only applies when settings are unavailable;
        // the AppSettings property itself defaults to 10 and clamps 10-600, AppSettings.cs:2951-2959).
        var cooldownSeconds = _settingsService.Current?.AwarenessReactionCooldownSeconds ?? 90;
        return (DateTime.Now - _lastReactionTime).TotalSeconds >= cooldownSeconds;
    }

    /// <inheritdoc />
    public bool CanStillOnReact()
    {
        // WPF :385-392
        var cooldownSeconds = _settingsService.Current?.AwarenessReactionCooldownSeconds ?? 90;
        return (DateTime.Now - _lastStillOnTime).TotalSeconds >= cooldownSeconds;
    }

    /// <inheritdoc />
    public void MarkReaction()
    {
        _lastReactionTime = DateTime.Now; // WPF :394-397
    }

    /// <inheritdoc />
    public void MarkStillOnReaction()
    {
        _lastStillOnTime = DateTime.Now; // WPF :402-405
    }

    public void Dispose()
    {
        // WPF :731-738
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
    }

    private void OnPollTimerCallback()
    {
        lock (_gate)
        {
            if (!_isRunning) return; // stale thread-pool callback racing Stop()/Dispose()
            PollTick(DateTime.Now);
        }
    }

    /// <summary>
    /// One poll pass (WPF OnPollTick :477-521). Internal so tests can drive the
    /// idle/debounce/classification contract deterministically without real timers.
    /// </summary>
    internal void PollTick(DateTime now)
    {
        try
        {
            var windowTitle = _titleProvider?.GetForegroundWindowTitle() ?? "";

            // NOTE: WPF :483-486 logged the full raw title here at Debug level. Deliberately NOT
            // ported — the raw title is memory-only and never reaches the log (privacy contract).

            // Check for idle (same window for too long) (WPF :489-497)
            if (windowTitle == _lastWindowTitle)
            {
                var idleMinutes = (now - _lastActivityChange).TotalMinutes;
                if (idleMinutes >= IdleThresholdMinutes && _currentCategory != ActivityCategory.Idle)
                {
                    SetActivity(now, ActivityCategory.Idle, "being idle", "", "");
                }
                return;
            }

            // Window changed - reset activity timer (WPF :499-501)
            _lastWindowTitle = windowTitle;
            _lastActivityChange = now;

            // Categorize by window title only (not background processes)
            // Background process detection was causing false positives (WPF :503-505)
            var (category, detectedName, serviceName, pageTitle) = AwarenessClassifier.Categorize(windowTitle);

            if (category != _currentCategory || detectedName != _currentDetectedName) // debounce (WPF :507)
            {
                // Fine-grained cluster/app classification for the awareness-gated bark rules. Only the
                // resolved ids are surfaced — the raw title is never stored (WPF :509-512).
                var (appCluster, appId) = AppClusterMap.Classify(windowTitle);
                SetActivity(now, category, detectedName, serviceName, pageTitle, appCluster ?? "", appId ?? "");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("WindowAwareness: Poll error - {Error}", ex.Message); // WPF :516-519
        }
    }

    // WPF SetActivity :522-547
    private void SetActivity(DateTime now, ActivityCategory newCategory, string detectedName, string serviceName, string pageTitle, string appCluster = "", string appId = "")
    {
        var previousCategory = _currentCategory;
        var previousServiceName = _currentServiceName;

        // Determine if this is a new service/app (not just a different page) (WPF :527-530)
        var isNewService = !string.Equals(serviceName, previousServiceName, StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrEmpty(serviceName)
                           && !string.IsNullOrEmpty(previousServiceName);

        _currentCategory = newCategory;
        _currentDetectedName = detectedName;
        _currentServiceName = serviceName;
        _currentPageTitle = pageTitle;
        _lastActivityChange = now; // Track when this activity started (WPF :536)

        // Fire event (don't log the window title for privacy, only the detected name) (WPF :538-540)
        _logger?.LogDebug("WindowAwareness: Detected {Name} ({Category}) - Service: {Service}, IsNew: {IsNew}",
            detectedName, newCategory, serviceName, isNewService);

        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(
            newCategory, previousCategory, detectedName, serviceName, pageTitle, isNewService, previousServiceName, appCluster, appId));

        // Restart the still-on timer for periodic comments (WPF :545-546)
        RestartStillOnTimer(now);
    }

    // WPF RestartStillOnTimer :414-425
    private void RestartStillOnTimer(DateTime now)
    {
        _stillOnTimer?.Dispose();
        _stillOnTimer = null;
        _stillOnArmed = false;
        _currentMilestoneIndex = 0; // Reset milestones when activity changes

        // Only start if we have a recognized activity (not Unknown or Idle) (WPF :419-421)
        if (_currentCategory == ActivityCategory.Unknown || _currentCategory == ActivityCategory.Idle)
            return;

        // Start timer for first milestone (1 minute)
        StartNextMilestoneTimer(now);
    }

    // WPF StartNextMilestoneTimer :427-452 (recursion flattened into a loop)
    private void StartNextMilestoneTimer(DateTime now)
    {
        while (_currentMilestoneIndex < StillOnMilestonesMinutes.Length)
        {
            var minutesUntilMilestone = StillOnMilestonesMinutes[_currentMilestoneIndex];
            var elapsedMinutes = (now - _lastActivityChange).TotalMinutes;
            var waitMinutes = minutesUntilMilestone - elapsedMinutes;

            if (waitMinutes <= 0)
            {
                // Already past this milestone, move to next (WPF :436-441)
                _currentMilestoneIndex++;
                continue;
            }

            _stillOnArmed = true;
            if (_isRunning) // real one-shot timer only while monitoring; tests drive StillOnMilestoneTick directly
            {
                _stillOnTimer = new Timer(_ => OnStillOnTimerCallback(), null,
                    TimeSpan.FromMinutes(waitMinutes), Timeout.InfiniteTimeSpan);
            }

            _logger?.LogDebug("WindowAwareness: Still-on timer set for {Minutes}min milestone", minutesUntilMilestone);
            return;
        }
        // No more milestones (WPF :429-430)
        _stillOnArmed = false;
    }

    private void OnStillOnTimerCallback()
    {
        lock (_gate)
        {
            if (!_isRunning) return; // stale thread-pool callback racing Stop()/Dispose()
            StillOnMilestoneTick(DateTime.Now);
        }
    }

    /// <summary>
    /// One "still on" milestone fire (WPF OnStillOnMilestoneTick :454-475). Internal so tests can
    /// drive the {1,5,10}min milestone contract deterministically without real timers.
    /// </summary>
    internal void StillOnMilestoneTick(DateTime now)
    {
        if (!_stillOnArmed) return; // no milestone pending (mirrors "no timer armed" in WPF)
        _stillOnArmed = false;
        _stillOnTimer?.Dispose();
        _stillOnTimer = null;

        // Fire the StillOnActivity event if we're still on the same activity (WPF :459-470)
        if (_currentCategory != ActivityCategory.Unknown && _currentCategory != ActivityCategory.Idle)
        {
            var milestone = _currentMilestoneIndex < StillOnMilestonesMinutes.Length
                ? StillOnMilestonesMinutes[_currentMilestoneIndex]
                : 10;
            _logger?.LogDebug("WindowAwareness: Still on {Name} for {Minutes} minutes", _currentDetectedName, milestone);
            StillOnActivity?.Invoke(this, new ActivityChangedEventArgs(
                _currentCategory, _currentCategory, _currentDetectedName, _currentServiceName, _currentPageTitle));
        }

        // Move to next milestone (WPF :472-474)
        _currentMilestoneIndex++;
        StartNextMilestoneTimer(now);
    }
}
