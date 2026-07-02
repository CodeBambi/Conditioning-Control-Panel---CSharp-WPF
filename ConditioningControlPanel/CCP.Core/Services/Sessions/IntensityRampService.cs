using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Settings;
using Serilog;

namespace ConditioningControlPanel.Core.Services.Sessions;

/// <summary>
/// Portable implementation of the manual intensity ramp
/// (WPF MainWindow.StartStop.cs:355-501). Every 2 seconds:
/// progress = elapsed / RampDurationMinutes (capped at 1.0),
/// currentMult = 1.0 + (SchedulerMultiplier - 1.0) * progress,
/// applied to the RampLink*-enabled settings with WPF's caps
/// (flash &lt;= 100, spiral &lt;= 50, pink &lt;= 50, master/subliminal audio &lt;= 100).
/// Settings writes only; sliders and effect services react through AppSettings
/// PropertyChanged, so no UI pokes are needed (unlike the WPF original).
/// </summary>
public sealed class IntensityRampService : IIntensityRampService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settings;
    private readonly ISessionService? _sessionService;

    private DispatcherTimer? _timer;
    private DateTime _rampStartTime;
    private bool _sessionActiveAtStart;
    private bool _completedRaised;

    // Baselines snapshotted at ramp start (WPF MainWindow.StartStop.cs:360-364).
    private int _flashOpacityBase;
    private int _spiralOpacityBase;
    private int _pinkOpacityBase;
    private int _masterVolumeBase;
    private int _subAudioVolumeBase;

    // Per-field mutation tracking: only fields the ramp actually wrote are restored,
    // so mid-run manual tweaks of unlinked settings survive engine stop.
    private bool _flashMutated;
    private bool _spiralMutated;
    private bool _pinkMutated;
    private bool _masterMutated;
    private bool _subAudioMutated;

    public bool IsRunning => _timer != null;

    public event EventHandler? RampCompleted;

    /// <summary>
    /// Test seam: when set, replaces the ISessionService session-active probe.
    /// </summary>
    public Func<bool>? SessionActiveOverride { get; set; }

    public IntensityRampService(ISettingsService settings, ISessionService? sessionService = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sessionService = sessionService;
    }

    // WPF checks _sessionEngine?.IsRunning, which stays true while paused
    // (SessionEngine.cs:80); State != Idle matches that contract.
    private bool SessionActive =>
        SessionActiveOverride?.Invoke() ?? (_sessionService?.State ?? SessionState.Idle) != SessionState.Idle;

    public void StartRamp() => StartRamp(DateTime.Now);

    /// <summary>
    /// Testable overload of <see cref="StartRamp()"/> with an injected start time.
    /// </summary>
    public void StartRamp(DateTime now)
    {
        if (_timer != null) return;

        var settings = _settings.Current;

        _flashOpacityBase = settings.FlashOpacity;
        _spiralOpacityBase = settings.SpiralOpacity;
        _pinkOpacityBase = settings.PinkFilterOpacity;
        _masterVolumeBase = settings.MasterVolume;
        _subAudioVolumeBase = settings.SubAudioVolume;
        _flashMutated = _spiralMutated = _pinkMutated = _masterMutated = _subAudioMutated = false;
        _completedRaised = false;
        _sessionActiveAtStart = SessionActive;
        _rampStartTime = now;

        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => EvaluateTick(DateTime.Now);
        _timer.Start();

        Log.Information("Ramp timer started - Duration: {Duration}min, Multiplier: {Mult}x",
            settings.RampDurationMinutes, settings.SchedulerMultiplier);
    }

    /// <summary>
    /// One ramp tick (WPF RampTimer_Tick, MainWindow.StartStop.cs:425-501).
    /// Public with injected time so the multiplier math is unit-testable.
    /// </summary>
    public void EvaluateTick(DateTime now)
    {
        if (_timer == null) return;

        var settings = _settings.Current;
        var elapsed = (now - _rampStartTime).TotalMinutes;
        var duration = settings.RampDurationMinutes; // setter clamps to 10-180, never 0
        var multiplier = settings.SchedulerMultiplier;

        // Visual ramps are suppressed while a preset session is active - the Core
        // session Lerp ramps (SessionService.UpdateRampingValues) own the visuals then,
        // and two writers would make the values jump around (WPF StartStop.cs:432-434).
        var sessionActive = SessionActive;

        var progress = Math.Min(elapsed / duration, 1.0);
        var currentMult = 1.0 + (multiplier - 1.0) * progress;

        if (!sessionActive && settings.RampLinkFlashOpacity)
        {
            settings.FlashOpacity = (int)Math.Min(_flashOpacityBase * currentMult, 100);
            _flashMutated = true;
        }

        if (!sessionActive && settings.RampLinkSpiralOpacity)
        {
            settings.SpiralOpacity = (int)Math.Min(_spiralOpacityBase * currentMult, 50);
            _spiralMutated = true;
        }

        if (!sessionActive && settings.RampLinkPinkFilterOpacity)
        {
            settings.PinkFilterOpacity = (int)Math.Min(_pinkOpacityBase * currentMult, 50);
            _pinkMutated = true;
        }

        if (settings.RampLinkMasterAudio)
        {
            settings.MasterVolume = (int)Math.Min(_masterVolumeBase * currentMult, 100);
            _masterMutated = true;
        }

        if (settings.RampLinkSubliminalAudio)
        {
            settings.SubAudioVolume = (int)Math.Min(_subAudioVolumeBase * currentMult, 100);
            _subAudioMutated = true;
        }

        // Auto-stop only when no preset session is active: the preset owns its master
        // timer and must run its full length; a shorter ramp ending the run mid-session
        // tore down services while the session timer kept counting (WPF #444 fix,
        // MainWindow.StartStop.cs:487-500). Latched so the head's async stop path is
        // not re-entered every 2s.
        if (progress >= 1.0 && settings.EndSessionOnRampComplete && !sessionActive && !_completedRaised)
        {
            _completedRaised = true;
            Log.Information("Ramp complete - ending session");
            RampCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopRamp()
    {
        if (_timer == null) return;

        _timer.Stop();
        _timer = null;

        var settings = _settings.Current;

        // Restore rules - a deliberate refinement of WPF's restore-all (StartStop.cs:379-423):
        // WPF snapshots baselines in StartEngine BEFORE SessionEngine overwrites the live
        // settings with the preset, so its unconditional restore always writes pre-session
        // user values. Here the ramp starts from the SessionStarted event, AFTER
        // SessionSettingsScope.Apply ran, so baselines captured during a preset run hold
        // SESSION values for scope-managed fields. SessionSettingsScope.Restore runs
        // before this method (inside SessionService.StopSession, before SessionStopped
        // fires); re-writing session-derived baselines here would clobber the user's
        // restored settings. Therefore:
        //  - only fields the ramp actually wrote are restored;
        //  - scope-managed fields (flash/spiral/pink opacity, subliminal volume) are
        //    restored only when the ramp started OUTSIDE a session, i.e. its baselines
        //    are genuine user values (engine-only runs, or runs a preset joined later);
        //  - MasterVolume is not scope-managed and is always safe to restore.
        if (_flashMutated && !_sessionActiveAtStart) settings.FlashOpacity = _flashOpacityBase;
        if (_spiralMutated && !_sessionActiveAtStart) settings.SpiralOpacity = _spiralOpacityBase;
        if (_pinkMutated && !_sessionActiveAtStart) settings.PinkFilterOpacity = _pinkOpacityBase;
        if (_masterMutated) settings.MasterVolume = _masterVolumeBase;
        if (_subAudioMutated && !_sessionActiveAtStart) settings.SubAudioVolume = _subAudioVolumeBase;

        _flashMutated = _spiralMutated = _pinkMutated = _masterMutated = _subAudioMutated = false;

        Log.Information("Ramp timer stopped - values reset to base");
    }

    public void Dispose() => StopRamp();
}
