using System.ComponentModel;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Scheduler;

/// <summary>
/// Portable scheduler engine. Ports the WPF scheduler verbatim:
/// 30s check timer armed after a 60s startup grace period (MainWindow.xaml.cs:443-465),
/// startup check (MainWindow.StartStop.cs:507-526), settings-change recheck
/// (MainWindow.Settings.cs:522-528 -> StartStop.cs:528-544), tick auto start/stop
/// (StartStop.cs:546-585) and the pure window predicate (StartStop.cs:587-643).
/// Decision-only: the head owns the actual engine start/stop and tray behavior.
/// </summary>
public sealed class SchedulerService : ISchedulerService
{
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly ISettingsService _settings;
    private DispatcherTimer? _graceTimer;
    private DispatcherTimer? _checkTimer;
    private Func<bool>? _isEngineRunning;
    private bool _lastKnownSchedulerEnabled;
    private bool _started;

    public bool SchedulerAutoStarted { get; private set; }
    public bool ManuallyStoppedDuringSchedule { get; private set; }

    public event EventHandler<SchedulerAutoStartEventArgs>? AutoStartRequested;
    public event EventHandler? AutoStopRequested;

    public SchedulerService(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    private bool EngineRunning => _isEngineRunning?.Invoke() == true;

    public void Start(Func<bool> isEngineRunning)
    {
        if (_started) return;
        _started = true;
        _isEngineRunning = isEngineRunning ?? throw new ArgumentNullException(nameof(isEngineRunning));

        // Settings-change recheck (WPF MainWindow.Settings.cs:522-528). The Avalonia
        // SchedulerFeatureControl writes AppSettings directly, so the enable transition
        // arrives via PropertyChanged rather than a SaveSettings hook.
        _lastKnownSchedulerEnabled = _settings.Current.SchedulerEnabled;
        if (_settings.Current is INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += OnSettingsPropertyChanged;
        }

        _checkTimer = new DispatcherTimer { Interval = CheckInterval };
        _checkTimer.Tick += (_, _) => EvaluateTick(DateTime.Now, EngineRunning);

        // Delay scheduler startup to let the app fully initialize. Prevents issues when
        // restarting after an update while inside a scheduled window (WPF
        // MainWindow.xaml.cs:449-465).
        _graceTimer = new DispatcherTimer { Interval = StartupGracePeriod };
        _graceTimer.Tick += (_, _) =>
        {
            _graceTimer?.Stop();
            _graceTimer = null;
            _checkTimer?.Start();
            CheckOnStartup(DateTime.Now);
            Log.Information("Scheduler grace period complete - scheduler now active");
        };
        _graceTimer.Start();

        Log.Information("Scheduler will start after {Seconds}s grace period", StartupGracePeriod.TotalSeconds);
    }

    /// <summary>
    /// Startup check (WPF CheckSchedulerOnStartup, MainWindow.StartStop.cs:507-526):
    /// launching inside the window auto-starts and minimizes to tray. WPF does not
    /// check whether the engine already runs here; the head's engine-only start is a
    /// no-op when a run is live (e.g. AutoStartEngine fired first), but the scheduler
    /// still claims the run so window exit auto-stops it, matching WPF.
    /// </summary>
    public void CheckOnStartup(DateTime now)
    {
        var settings = _settings.Current;
        Log.Information("Scheduler startup check: Enabled={Enabled}, InWindow={InWindow}",
            settings.SchedulerEnabled, IsInScheduledTimeWindow(settings, now));

        if (!settings.SchedulerEnabled) return;

        if (IsInScheduledTimeWindow(settings, now))
        {
            Log.Information("Scheduler: App started within scheduled time window - auto-starting");
            SchedulerAutoStarted = true;
            AutoStartRequested?.Invoke(this, new SchedulerAutoStartEventArgs(minimizeToTray: true));
        }
    }

    /// <summary>
    /// Recheck after the scheduler was enabled in settings
    /// (WPF CheckSchedulerAfterSettingsChange, MainWindow.StartStop.cs:528-544).
    /// Auto-starts without minimizing when already inside the window.
    /// </summary>
    public void CheckAfterSettingsChange(DateTime now, bool isEngineRunning)
    {
        var settings = _settings.Current;
        if (!settings.SchedulerEnabled) return;

        Log.Information("Scheduler settings changed - checking time window");

        if (IsInScheduledTimeWindow(settings, now) && !isEngineRunning)
        {
            Log.Information("Scheduler: In time window after settings change - auto-starting");
            SchedulerAutoStarted = true;
            AutoStartRequested?.Invoke(this, new SchedulerAutoStartEventArgs(minimizeToTray: false));
        }
    }

    /// <summary>
    /// 30s tick (WPF SchedulerTimer_Tick, MainWindow.StartStop.cs:546-585).
    /// Public with injected time/engine state so the flag machine is unit-testable.
    /// </summary>
    public void EvaluateTick(DateTime now, bool isEngineRunning)
    {
        var settings = _settings.Current;
        if (!settings.SchedulerEnabled) return;

        bool inWindow = IsInScheduledTimeWindow(settings, now);

        if (inWindow && !isEngineRunning && !SchedulerAutoStarted && !ManuallyStoppedDuringSchedule)
        {
            Log.Information("Scheduler: Entering scheduled time window - auto-starting");
            SchedulerAutoStarted = true;
            AutoStartRequested?.Invoke(this, new SchedulerAutoStartEventArgs(minimizeToTray: true));
        }
        else if (!inWindow && isEngineRunning && SchedulerAutoStarted)
        {
            // Only stops runs the scheduler itself started: a manually started run that
            // outlives the window is never killed (WPF MainWindow.StartStop.cs:567).
            Log.Information("Scheduler: Exiting scheduled time window - auto-stopping");
            SchedulerAutoStarted = false;
            AutoStopRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (!inWindow)
        {
            // Outside the window - reset flags for the next window (WPF StartStop.cs:579-584).
            SchedulerAutoStarted = false;
            ManuallyStoppedDuringSchedule = false;
        }
    }

    public void NotifyManualStop() => NotifyManualStop(DateTime.Now);

    /// <summary>
    /// Testable overload of <see cref="NotifyManualStop()"/>.
    /// </summary>
    public void NotifyManualStop(DateTime now)
    {
        if (_settings.Current.SchedulerEnabled && IsInScheduledTimeWindow(_settings.Current, now))
        {
            ManuallyStoppedDuringSchedule = true;
        }
    }

    public void NotifyManualStart()
    {
        ManuallyStoppedDuringSchedule = false;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppSettings.SchedulerEnabled)) return;

        var enabled = _settings.Current.SchedulerEnabled;
        var wasEnabled = _lastKnownSchedulerEnabled;
        _lastKnownSchedulerEnabled = enabled;

        // WPF resets both flags and re-checks when the scheduler flips ON
        // (MainWindow.Settings.cs:522-528). AppSettings setters raise PropertyChanged
        // even for same-value writes, so the transition is tracked explicitly here.
        if (enabled && !wasEnabled)
        {
            SchedulerAutoStarted = false;
            ManuallyStoppedDuringSchedule = false;
            Dispatcher.UIThread.Post(
                () => CheckAfterSettingsChange(DateTime.Now, EngineRunning),
                DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Pure window predicate (WPF IsInScheduledTimeWindow, MainWindow.StartStop.cs:587-643):
    /// per-day-of-week flags, TimeSpan.TryParse with 16:00/22:00 fallbacks, start-inclusive
    /// end-exclusive, and overnight wrap when end &lt; start (e.g. 22:00-02:00).
    /// </summary>
    public static bool IsInScheduledTimeWindow(AppSettings settings, DateTime now)
    {
        bool isDayActive = now.DayOfWeek switch
        {
            DayOfWeek.Monday => settings.SchedulerMonday,
            DayOfWeek.Tuesday => settings.SchedulerTuesday,
            DayOfWeek.Wednesday => settings.SchedulerWednesday,
            DayOfWeek.Thursday => settings.SchedulerThursday,
            DayOfWeek.Friday => settings.SchedulerFriday,
            DayOfWeek.Saturday => settings.SchedulerSaturday,
            DayOfWeek.Sunday => settings.SchedulerSunday,
            _ => false
        };

        if (!isDayActive)
        {
            Log.Debug("Scheduler: {Day} is not an active day", now.DayOfWeek);
            return false;
        }

        if (!TimeSpan.TryParse(settings.SchedulerStartTime, out var startTime))
        {
            Log.Warning("Scheduler: Could not parse start time '{Time}', using default 16:00", settings.SchedulerStartTime);
            startTime = new TimeSpan(16, 0, 0);
        }

        if (!TimeSpan.TryParse(settings.SchedulerEndTime, out var endTime))
        {
            Log.Warning("Scheduler: Could not parse end time '{Time}', using default 22:00", settings.SchedulerEndTime);
            endTime = new TimeSpan(22, 0, 0);
        }

        var currentTime = now.TimeOfDay;

        bool inWindow;
        if (endTime < startTime)
        {
            // Overnight schedule (e.g. 22:00 - 02:00).
            inWindow = currentTime >= startTime || currentTime < endTime;
        }
        else
        {
            // Same-day schedule.
            inWindow = currentTime >= startTime && currentTime < endTime;
        }

        Log.Debug("Scheduler: Current={Current}, Start={Start}, End={End}, InWindow={InWindow}",
            currentTime.ToString(@"hh\:mm"), startTime.ToString(@"hh\:mm"), endTime.ToString(@"hh\:mm"), inWindow);

        return inWindow;
    }

    public void Dispose()
    {
        _graceTimer?.Stop();
        _graceTimer = null;
        _checkTimer?.Stop();
        _checkTimer = null;
        if (_settings.Current is INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged -= OnSettingsPropertyChanged;
        }
    }
}
