using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Lab feature: lets the user pop floating bubbles and dismiss flash images
/// by looking at them for ~600ms, or by blinking once while looking near
/// them. Subscribes to the shared WebcamTrackingService gaze stream while
/// active and drives a small inflate animation on the dwell target before
/// invoking its existing pop / click pipeline (sound, XP, hydra,
/// achievements, haptics).
///
/// Coordinate space: TWO spaces meet in this class and they are NOT the same
/// (see the CalOriginDips block below for the proof and the conversion).
/// OnGazeMove emits GAZE space — DIPs local to the CALIBRATED monitor — while
/// every target rect here is DESKTOP space (Window.Left/Top on the virtual
/// desktop). They coincide only when calibration ran on the primary monitor.
///
/// Hit-test tolerance: bubbles are small and homography has a few percent of
/// residual error, so we use distance-from-rect-edge with a slack margin
/// (BubbleSlackDips). The closest target within slack wins — flashes get
/// priority when both are simultaneously hit (foreground content beats
/// drifting bubbles).
///
/// Two-stage selection: a raw gaze point is physically imprecise, so when a
/// completed dwell lands somewhere two targets share one gaze error blob with
/// near-identical scores, this raises GazeRefineOverlay — a magnified inset of
/// that neighbourhood — and the user's second dwell picks for real. See the
/// Refine* constant block.
/// </summary>
public class GazeFocusService : IDisposable
{
    // 600ms, not the 1000ms this shipped with. The dwell-time literature puts
    // the sweet spot around 500-700ms: 1000ms reads as sluggish and, worse, it
    // is long enough that the user's gaze routinely drifts off the target
    // before the dwell completes, which they experience as "it didn't fire".
    // The two-stage refine below is what buys back the precision the shorter
    // dwell would otherwise cost — dwell fast, disambiguate when it matters.
    private const int DefaultDwellMs = 600;
    private const int CooldownMs = 250;
    private const int TickMs = 33; // ~30 FPS

    // Stare-linger boost cadence. The dwell tick runs every ~33ms; we only
    // call BoostLifetime every ~250ms (8 ticks) so CancelAfter isn't
    // re-scheduled on every frame. The window's death deadline still tracks
    // "alive for FlashGazeLingerExtensionMs from the last boost" closely
    // because each boost replaces the previous timer.
    private const int LingerBoostThrottleMs = 250;

    // Predictive target scoring — replaces the old hard-radius slack hit-test.
    // For each candidate target we compute:
    //   score = exp(-d² / 2σ²)  (Gaussian falloff with distance d from rect edge)
    //         + StickyBonus     (if target is the one we were already dwelling on)
    //         + FlashTypeBonus  (flashes outrank background bubbles, foreground intent)
    // The single highest score above ScoreThreshold wins. The Gaussian replaces
    // the binary "inside slack / outside slack" cliff with a soft falloff —
    // small jitter at the boundary no longer toggles the lock. The sticky
    // bonus prevents ping-pong when two targets are equidistant: noise has
    // to push the cursor meaningfully closer to a *different* target before
    // the lock switches. This is the "feels glued" behavior — the same trick
    // iPhone keyboards use (bias the candidate set with a prior, don't just
    // pick the literal hit point).
    // Sigmas sized for quadrant-level reach ("if we are in that quadrant we
    // are looking at that target"): a bubble stays the acquired target out
    // to ≈390 dips — roughly a quarter of a 1080p screen — so the cursor
    // lock-on (SetCursorLock) can draw the cursor in from its whole region.
    private const double BubbleScoreSigma = 160; // ≈ 2.45σ (≈390 dips) at threshold
    private const double FlashScoreSigma = 90;
    private const double StickyBonus = 0.20;
    private const double FlashTypeBonus = 0.15;
    private const double ScoreThreshold = 0.05;

    // ---- Two-stage zoom refine (see GazeRefineOverlay) --------------------
    // A raw gaze point can never be precise: the fovea is ~1 degree and the eye
    // micro-moves through every fixation, so the true point of regard lives
    // somewhere inside an error blob (~90-200px for a webcam rig). When two
    // targets both sit in that blob with near-identical scores, taking the top
    // score is a coin flip — and losing that flip is exactly what users report
    // as "it selected the wrong thing". So instead of guessing we magnify.
    //
    // Every threshold here is set to under-trigger on purpose. A refine panel
    // the user didn't need is far more annoying than an occasional wrong pick
    // they can just re-dwell, so the rule demands ALL of:
    //   * the winner is genuinely engaged (not a wisp at the threshold),
    //   * the runner-up is genuinely engaged too,
    //   * their scores are within RefineCloseRatio of each other,
    //   * they are close enough together to plausibly share one error blob,
    //   * and the winner did NOT need the sticky bonus to win (if it did, the
    //     user has already been holding it for a whole dwell — that's evidence,
    //     not ambiguity).
    // Ambiguity is judged on scores WITHOUT the sticky bonus: sticky exists to
    // stop lock ping-pong, not to answer "which target did they mean".
    private const double RefineCloseRatio = 0.85;        // runner-up within 15% of the winner
    private const double RefineMinWinnerScore = 0.30;    // ≈1.55σ — clearly in a target's region
    private const double RefineMinRunnerUpScore = 0.20;  // ≈1.79σ
    private const double RefineMaxSpreadDips = 280;      // centre-to-centre; wider than this is not one blob
    private const int MaxRefineCandidates = 4;

    // Refine session limits. The panel must be impossible to get stuck in:
    // it is click-through (so it never traps input), it dies on look-away, it
    // dies on a hard timeout, and it dies if its candidates do.
    private const int RefineDwellMs = 550;          // second dwell — targets are big now, so it can be brisk
    private const int RefineTimeoutMs = 6000;       // hard ceiling on a refine session
    private const int RefineLookAwayMs = 700;       // continuous gaze outside EscapeBounds = cancel
    private const int RefineFaceLostGraceMs = 1200; // a blink/turn shouldn't kill the panel; a walk-away should
    private const int RefineSuppressMs = 4000;      // after a cancel, let the next dwell just fire

    private DispatcherTimer? _timer;
    private Point? _lastGazePoint;
    private bool _faceLost;
    private DateTime _dwellStartedAt;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private bool _subscribed;

    // Mutually exclusive — only one target is being dwelt on at a time.
    private Bubble? _currentBubble;
    private FlashWindow? _currentFlash;
    private IAttentionTarget? _currentFloating;

    // Throttle clock for stare-linger boosts. Reset to MinValue whenever the
    // dwell target changes (or no target is held) so the first boost on
    // re-acquisition fires immediately.
    private DateTime _lastLingerBoostAt = DateTime.MinValue;

    // ---- Refine stage state ----------------------------------------------
    // Ambiguity is recomputed by FindBestTarget every tick and consumed at the
    // moment a dwell completes. Held as fields rather than widened into
    // GazeHit so the hot path stays a struct with no allocation.
    private readonly List<ScoredCandidate> _scored = new(16);
    private bool _ambiguous;

    private GazeRefineOverlay? _refine;
    private List<GazeRefineCandidate>? _refineCandidates;
    private DateTime _refineOpenedAt;
    private int _refineChipIndex = -1;
    private DateTime _refineChipDwellStartedAt;
    private DateTime _refineOutsideSince = DateTime.MinValue;
    private DateTime _refineFaceLostSince = DateTime.MinValue;
    private DateTime _refineSuppressedUntil = DateTime.MinValue;

    public bool IsActive { get; private set; }
    public int DwellMs { get; set; } = DefaultDwellMs;

    /// <summary>
    /// Master switch for the two-stage zoom refine. On by default; exposed so
    /// a Lab toggle (or a bug report) can turn the second stage off and get
    /// the old always-fire-the-top-score behavior back without a rebuild.
    /// </summary>
    public bool RefineEnabled { get; set; } = true;

    /// <summary>True while the magnified refine panel is on screen.</summary>
    public bool IsRefining => _refine?.IsShowing == true;

    // The explicit Lab "Focus Gaze" toggle. It is now just one of several
    // "consumers" that want the shared dwell engine alive (the others are the
    // per-feature Flash gaze-pop / Flash linger / Video gaze-click settings).
    // The engine runs whenever ANY consumer wants it, so ticking "Flash gaze
    // pop" in the Flashes tab is enough on its own — the user no longer has to
    // separately arm this master toggle. See EvaluateDesiredState.
    private bool _masterEnabled;
    public bool MasterEnabled
    {
        get => _masterEnabled;
        set
        {
            if (_masterEnabled == value) return;
            _masterEnabled = value;
            EvaluateDesiredState();
        }
    }


    /// <summary>Fires when IsActive flips, on the UI thread.</summary>
    public event Action<bool>? OnActiveChanged;

    /// <summary>Fires when a BUBBLE is popped by gaze (dwell or blink). UI thread.</summary>
    public event Action? GazePopped;

    public GazeFocusService()
    {
        // ShutdownMode=OnLastWindowClose means subsystems holding hidden
        // windows can keep the process alive after MainWindow closes —
        // close ourselves on app-exit so we drop those references. Mirrors
        // KeywordHighlightService.cs:30-31.
        if (Application.Current != null)
            Application.Current.Exit += (_, _) => Stop();

        // Auto-start hook: the shared engine should come alive the moment any
        // per-feature gaze toggle (Flash gaze-pop / linger, Video gaze-click)
        // is enabled, not only when the Lab "Focus Gaze" master is armed.
        var settings = App.Settings?.Current;
        if (settings != null)
            settings.PropertyChanged += OnSettingsChanged;

        // ...and it should also come alive/fall away as the shared webcam is
        // started or stopped by ANY feature (Webcam Triggers, debug cursor,
        // Blink Trainer). This is what makes "turn the camera on, look at a
        // flash, it pops" work without separately arming Focus Gaze — the
        // engine follows the camera rather than powering it.
        if (App.Webcam != null)
            App.Webcam.OnTrackingStateChanged += OnWebcamStateChanged;
    }

    private void OnWebcamStateChanged(WebcamTrackingState _) => EvaluateDesiredState();

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Models.AppSettings.FlashGazePopEnabled):
            case nameof(Models.AppSettings.FlashGazeLingerEnabled):
            case nameof(Models.AppSettings.BubbleGazePopEnabled):
            case nameof(Models.AppSettings.VideoGazeClickEnabled):
                EvaluateDesiredState();
                break;
        }
    }

    private static bool AnyConsumerOn()
    {
        var s = App.Settings?.Current;
        if (s == null) return false;
        return s.FlashGazePopEnabled || s.FlashGazeLingerEnabled
               || s.BubbleGazePopEnabled || s.VideoGazeClickEnabled;
    }

    /// <summary>
    /// May gaze pop a BUBBLE right now? The Lab/Play "Focus Gaze" master arms
    /// every target type at once; BubbleGazePopEnabled is the bubble's own
    /// per-feature flag (the twin of FlashGazePopEnabled), surfaced on the
    /// Bubble Pop panel. Either one is enough, which is what makes "turn the
    /// camera on, look at a bubble, it pops" work without hunting down the
    /// master switch on another tab. Every bubble gate reads THIS, so the
    /// three enumerate/dwell/blink paths can never disagree.
    /// </summary>
    private bool BubblesGazeEnabled
        => _masterEnabled || App.Settings?.Current?.BubbleGazePopEnabled == true;

    /// <summary>
    /// Single source of truth for whether the shared dwell engine should be
    /// running. Starts or stops the engine to match. It should run when a
    /// consumer wants it (the Lab master toggle OR any per-feature gaze flag)
    /// AND the shared webcam can actually feed it.
    ///
    /// Crucially, this NEVER powers the camera on: the per-feature gaze flags
    /// default to ON, so warming the webcam whenever they're set would
    /// silently light the camera at startup for any calibrated user. Instead
    /// we require the camera to already be running (turned on by the master
    /// toggle's own prewarm, Webcam Triggers, the debug cursor, etc.) and ride
    /// along — OnWebcamStateChanged re-runs this when the camera comes up or
    /// goes away, so the engine tracks the camera's lifetime. Auto-start also
    /// never prompts for consent (the explicit master toggle owns that dialog).
    /// Idempotent and UI-thread-marshalled (Start spins up a DispatcherTimer).
    /// </summary>
    public void EvaluateDesiredState()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        if (!disp.CheckAccess()) { disp.BeginInvoke(new Action(EvaluateDesiredState)); return; }

        bool wants = _masterEnabled || AnyConsumerOn();
        bool canRun = App.Webcam != null
                      && App.Webcam.IsRunning
                      && App.Webcam.Calibration != null
                      && WebcamTrackingService.IsConsentCurrent();

        if (wants && canRun)
        {
            if (!IsActive) Start();
        }
        else
        {
            if (IsActive) Stop();
        }
    }

    /// <summary>
    /// Try to start dwell processing. Requires the webcam to be running and
    /// calibrated. Returns false if either prerequisite is missing — caller
    /// should reflect that in their UI (toggle bounces back, status message).
    /// </summary>
    public bool Start()
    {
        if (IsActive) return true;
        if (App.Webcam == null) return false;
        // #743: this method is deliberately UI-thread-marshalled (see EvaluateDesiredState), and
        // WebcamTrackingService.Start() blocks synchronously for up to the 90s camera-open timeout
        // while it opens the device and builds three ONNX sessions. Calling it here froze the
        // window until the OS "not responding" reaper killed the app. The webcam must already be
        // up - brought there by an awaited StartAsync() on a UI path that can show progress.
        if (!App.Webcam.IsRunning) return false;
        if (App.Webcam.Calibration == null) return false;

        Subscribe();
        // Cursor visibility is controlled solely by the explicit "Show debug
        // gaze cursor" checkbox in the Lab webcam-debug card. Focus Gaze runs
        // silently — turning it on shouldn't paint a dot on the user's screen.

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(TickMs)
        };
        _timer.Tick += OnTick;
        _timer.Start();

        IsActive = true;
        try { OnActiveChanged?.Invoke(true); } catch { }
        App.Logger?.Information("GazeFocusService: active");
        return true;
    }

    /// <summary>
    /// Stop dwell processing. Leaves the webcam running — other Lab features
    /// share App.Webcam and follow a no-stop convention; app shutdown disposes it.
    /// </summary>
    public void Stop()
    {
        if (!IsActive) return;
        Unsubscribe();
        try { _timer?.Stop(); } catch { }
        if (_timer != null) _timer.Tick -= OnTick;
        _timer = null;

        CloseRefine("engine-stop", suppress: false);
        ClearTarget();
        SetCursorLock(null);
        App.GazeCursor?.SetLocked(false);
        _lastGazePoint = null;
        _faceLost = false;
        _cooldownUntil = DateTime.MinValue;
        _refineSuppressedUntil = DateTime.MinValue;
        _scored.Clear();
        _ambiguous = false;

        IsActive = false;
        try { OnActiveChanged?.Invoke(false); } catch { }
        App.Logger?.Information("GazeFocusService: inactive");
    }

    private void Subscribe()
    {
        if (_subscribed || App.Webcam == null) return;
        App.Webcam.OnGazeMove += HandleGazeMove;
        App.Webcam.OnFaceLost += HandleFaceLost;
        App.Webcam.OnFaceFound += HandleFaceFound;
        App.Webcam.OnBlink += HandleBlink;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || App.Webcam == null) return;
        App.Webcam.OnGazeMove -= HandleGazeMove;
        App.Webcam.OnFaceLost -= HandleFaceLost;
        App.Webcam.OnFaceFound -= HandleFaceFound;
        App.Webcam.OnBlink -= HandleBlink;
        _subscribed = false;
    }

    // ===================== Coordinate spaces ==============================
    // GAZE space    — what WebcamTrackingService.OnGazeMove emits and what
    //                 SetGazeAttractor consumes (documented as such at
    //                 WebcamTrackingService.cs:759). DIPs LOCAL to the
    //                 calibrated monitor: origin = that monitor's top-left,
    //                 extent = Calibration.MonitorBounds.Width/Height. Proof,
    //                 from code rather than comments: the projection is fit
    //                 against dot positions placed in the calibration window's
    //                 own ActualWidth/Height (WebcamCalibrationWindow.xaml.cs
    //                 :266-287 and :614, borderless-maximized on that monitor),
    //                 and the runtime soft-clamps the projected point to
    //                 [0, bounds.Width] × [0, bounds.Height]
    //                 (WebcamTrackingService.cs:2232-2239).
    //
    // DESKTOP space — every target rect in this class: Window.Left/Top DIPs on
    //                 the virtual desktop (FlashWindow.Left/Top,
    //                 Bubble.GetGazeBounds, IAttentionTarget.GetGazeBounds,
    //                 and GazeRefineOverlay's panel/chip/escape bounds).
    //
    // The two coincide ONLY when calibration ran on the primary monitor, whose
    // origin is (0,0) by definition — which is exactly why this mismatch stayed
    // invisible. Converting is a pure TRANSLATION, so every sigma, radius and
    // spread constant in this file carries across unscaled and none of them
    // need re-tuning.
    //
    // Same conversion as the house pattern at FypHostService.cs:1315-1320 and
    // GazeDriftCorrectionService.cs:499-508, with one deliberate difference:
    // the origin is read from the LIVE screen via TryGetCalibratedBounds rather
    // than the stored MonitorBounds.X/Y, so unplugging the calibrated monitor
    // degrades to "no shift" instead of offsetting every hit-test by a dead
    // monitor's coordinates.

    private const int CalOriginCacheMs = 1000;
    private static Vector _calOriginDips;
    private static DateTime _calOriginAt = DateTime.MinValue;

    /// <summary>
    /// Origin of the calibrated monitor expressed in DESKTOP DIPs, or (0,0)
    /// when it cannot be established (no calibration, a pre-hotfix save with no
    /// DeviceName, or the monitor is no longer connected). (0,0) reproduces the
    /// pre-fix behaviour exactly and is also the correct answer whenever
    /// calibration ran on the primary monitor, so the common case is untouched
    /// at any DPI scale. Never throws: a coordinate conversion must not be able
    /// to kill the 30 FPS gaze tick. Cached for a second because
    /// Screen.AllScreens re-enumerates the display set on every call and this
    /// runs on the hot path.
    /// </summary>
    private static Vector CalOriginDips()
    {
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _calOriginAt).TotalMilliseconds < CalOriginCacheMs) return _calOriginDips;

            var result = default(Vector);
            var webcam = App.Webcam;
            // DeviceName == null is a pre-hotfix calibration whose X/Y are
            // meaningless zeros — same guard the house pattern uses.
            if (webcam != null
                && webcam.Calibration?.MonitorBounds is { DeviceName: not null } mb
                && webcam.TryGetCalibratedBounds(out var physical)   // PHYSICAL px
                && (physical.X != 0 || physical.Y != 0))             // primary → no shift at all
            {
                double dpi = mb.DpiScale is > 0.25 and < 8.0 ? mb.DpiScale : 1.0;
                result = new Vector(physical.X / dpi, physical.Y / dpi);
            }

            _calOriginDips = result;
            _calOriginAt = now;
            return result;
        }
        catch
        {
            // Fall back to the last good value (initially (0,0) = old behaviour).
            return _calOriginDips;
        }
    }

    /// <summary>GAZE space → DESKTOP space (monitor-local DIPs → virtual-desktop DIPs).</summary>
    private static Point GazeToDesktop(Point p)
    {
        var o = CalOriginDips();
        return o.X == 0 && o.Y == 0 ? p : new Point(p.X + o.X, p.Y + o.Y);
    }

    /// <summary>DESKTOP space → GAZE space. Inverse of <see cref="GazeToDesktop"/>.</summary>
    private static Point DesktopToGaze(Point p)
    {
        var o = CalOriginDips();
        return o.X == 0 && o.Y == 0 ? p : new Point(p.X - o.X, p.Y - o.Y);
    }

    private void HandleGazeMove(Point p)
    {
        // p arrives in GAZE space. Everything that reads _lastGazePoint compares
        // it against DESKTOP-space rects (FindBestTarget's target rects,
        // GazeRefineOverlay.HitTest, EscapeBounds.Contains), so translate ONCE
        // here rather than at each of those easily-missed comparison sites.
        // Identity when calibration ran on the primary monitor.
        _lastGazePoint = GazeToDesktop(p);
        // Cursor visualization is owned by GazeDebugCursorService — it
        // subscribes to OnGazeMove independently when any client (us or
        // the Lab debug toggle) has Show()'d its key.
    }

    private void HandleFaceLost()
    {
        _faceLost = true;
    }

    private void HandleFaceFound()
    {
        _faceLost = false;
    }

    private void HandleBlink()
    {
        try
        {
            if (DateTime.UtcNow < _cooldownUntil) return;
            if (_faceLost || !_lastGazePoint.HasValue) return;
            // The refine panel owns selection while it is up — a blink must not
            // reach through it and fire whatever is underneath.
            if (_refine != null) return;

            var hit = FindBestTarget(_lastGazePoint.Value);
            if (hit == null) return;

            // Cancel any in-progress dwell scaling on a different target.
            ClearTarget();

            if (hit.Value.Bubble is Bubble b)
            {
                // Same gate as FindBestTarget/AdvanceBubbleDwell: bubbles only
                // react to gaze while the "Focus Gaze" master or the bubble's
                // own "Stare to pop" toggle is armed.
                if (BubblesGazeEnabled)
                {
                    try { b.Pop(); GazePopped?.Invoke(); }
                    catch (Exception ex) { App.Logger?.Debug("Gaze blink-pop bubble failed: {Error}", ex.Message); }
                }
            }
            else if (hit.Value.Flash is FlashWindow fw)
            {
                // Match AdvanceFlashDwell's gating exactly: blink-pop is the
                // alternate codepath to dwell-pop and must respect the same
                // FlashGazePopEnabled toggle, otherwise a user who turned
                // gaze-pop off still gets blinked out of flashes.
                if (App.Settings?.Current?.FlashGazePopEnabled == true)
                {
                    try { App.Flash?.GazePop(fw); }
                    catch (Exception ex) { App.Logger?.Debug("Gaze blink-pop flash failed: {Error}", ex.Message); }
                }
            }
            _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownMs);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeFocusService blink handler error: {Error}", ex.Message);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            // Stage two owns the whole tick while it is up: no first-stage
            // dwell runs underneath it, so a refine can never fire a selection
            // behind the user's back.
            if (_refine != null)
            {
                TickRefine();
                return;
            }

            // Cooldown after a successful pop — short window during which a
            // single sustained look can't chain-pop another nearby target.
            if (DateTime.UtcNow < _cooldownUntil)
            {
                ClearTarget();
                SetCursorLock(null);
                App.GazeCursor?.SetLocked(false);
                return;
            }

            if (_faceLost || !_lastGazePoint.HasValue)
            {
                ClearTarget();
                SetCursorLock(null);
                App.GazeCursor?.SetLocked(false);
                return;
            }

            var p = _lastGazePoint.Value;
            var hit = FindBestTarget(p);

            if (hit == null)
            {
                ClearTarget();
                SetCursorLock(null);
                App.GazeCursor?.SetLocked(false);
                return;
            }

            App.GazeCursor?.SetLocked(true);

            if (hit.Value.Bubble is Bubble b)
            {
                SetCursorLock(b.GetGazeBounds());
                AdvanceBubbleDwell(b);
            }
            else if (hit.Value.Flash is FlashWindow fw)
            {
                Rect? fr = null;
                try { fr = new Rect(fw.Left, fw.Top, fw.Width, fw.Height); } catch { }
                SetCursorLock(fr);
                AdvanceFlashDwell(fw);
            }
            else if (hit.Value.Floating is IAttentionTarget ft)
            {
                SetCursorLock(ft.GetGazeBounds());
                AdvanceFloatingTextDwell(ft);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeFocusService tick error: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Points the tracking service's stateful gaze lock-on at the current
    /// dwell target's bounds (null = no target → release). The cursor then
    /// wobbles toward the target and stays with it in proportion to how
    /// steady the user's gaze actually is — the tracking service drains the
    /// lock gradually when the gaze sways too much, so there's no hard glue.
    /// Applies to ALL live targets (bubbles, flash GIFs, video attention
    /// targets): if the user's gaze is roughly in a target's region they
    /// almost certainly want to hit it, so tending there IS the intended
    /// behavior (round-7 user direction). Big targets get their radius
    /// capped so the pull toward the center of a large flash stays sane.
    /// Skipped while the calibration window owns the attractor (its bubble
    /// test declares its own targets).
    /// </summary>
    private static void SetCursorLock(Rect? bounds)
    {
        if (WebcamCalibrationWindow.IsShowing) return;
        if (bounds == null || bounds.Value.IsEmpty)
        {
            App.Webcam?.ClearGazeAttractor();
            return;
        }
        var rect = bounds.Value;
        // Radius = target visual radius + slack for mapping residual,
        // capped so the lock's capture/taper zones (which scale off it)
        // don't cover half the screen for a large flash window.
        double radius = Math.Clamp(Math.Max(rect.Width, rect.Height) / 2.0 + 30, 50, 240);
        // rect is DESKTOP space (window Left/Top DIPs); SetGazeAttractor takes
        // GAZE space ("coordinates in the OnGazeMove DIP space",
        // WebcamTrackingService.cs:759) — translate back, or the attractor sits
        // one monitor-origin away from the target and the lock pulls the cursor
        // off the thing the user is looking at. Radius is a distance and the two
        // spaces differ by a translation only, so it needs no conversion.
        var center = DesktopToGaze(new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0));
        App.Webcam?.SetGazeAttractor(center.X, center.Y, radius);
    }

    // ===================== Two-stage zoom refine ==========================

    /// <summary>
    /// Called at the instant a first-stage dwell completes. If this tick's
    /// scoring said the neighbourhood was ambiguous, puts up the magnified
    /// panel and returns true — the caller must then NOT fire its selection.
    /// Returns false for every other case (refine off, not ambiguous, panel
    /// couldn't be built), so the normal selection still happens: a failure
    /// here degrades to the old behavior, never to a swallowed input.
    /// </summary>
    private bool TryRaiseRefine()
    {
        try
        {
            if (!RefineEnabled || !_ambiguous || _refine != null) return false;
            if (DateTime.UtcNow < _refineSuppressedUntil) return false;

            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return false;

            // Best candidate first, then everyone else that is genuinely in
            // the same blob. The panel shows the real choice set, not just the
            // top two — if three bubbles overlapped, hiding the third would
            // make the refine itself feel wrong.
            var ordered = new List<ScoredCandidate>(_scored);
            ordered.Sort((x, y) => y.BaseScore.CompareTo(x.BaseScore));
            if (ordered.Count < 2) return false;

            var head = ordered[0];
            var hc = new Point(head.Bounds.X + head.Bounds.Width / 2.0, head.Bounds.Y + head.Bounds.Height / 2.0);

            var picked = new List<GazeRefineCandidate>(MaxRefineCandidates);
            foreach (var s in ordered)
            {
                if (picked.Count >= MaxRefineCandidates) break;
                if (s.BaseScore < RefineMinRunnerUpScore) continue;
                if (s.Bounds.IsEmpty) continue;
                var c = new Point(s.Bounds.X + s.Bounds.Width / 2.0, s.Bounds.Y + s.Bounds.Height / 2.0);
                var dx = c.X - hc.X; var dy = c.Y - hc.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > RefineMaxSpreadDips) continue;
                var cand = BuildRefineCandidate(s);
                if (cand != null) picked.Add(cand);
            }
            if (picked.Count < 2) return false;

            // The calibration window runs its own bubble test with its own
            // attractor, and SetCursorLock() defers to it - so a panel raised
            // over calibration would ask the user to pick a chip while the
            // cursor is magnetised to the calibration bubble underneath.
            if (WebcamCalibrationWindow.IsShowing) return false;

            var overlay = new GazeRefineOverlay();
            if (!overlay.Show(picked)) { overlay.Dispose(); return false; }

            // Assigned IMMEDIATELY after Show() succeeds. Anything throwing
            // between a successful Show() and this assignment would strand a
            // visible, topmost, click-through, never-activated window with no
            // surviving reference - unclosable short of killing the app.
            _refine = overlay;

            // Stop the first-stage dwell visuals before the panel takes over,
            // otherwise a half-filled ring sits under it on the real target.
            ClearTarget();
            _refineCandidates = picked;
            _refineOpenedAt = DateTime.UtcNow;
            _refineChipIndex = -1;
            _refineOutsideSince = DateTime.MinValue;
            _refineFaceLostSince = DateTime.MinValue;
            App.GazeCursor?.SetLocked(false);
            SetCursorLock(null);
            App.Logger?.Debug("GazeFocusService: refine raised with {Count} candidates", picked.Count);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeFocusService: refine raise failed: {Error}", ex.Message);
            CloseRefine("error", suppress: true);
            return false;
        }
    }

    // Not static: the bubble branch reads the instance-level BubblesGazeEnabled gate.
    private GazeRefineCandidate? BuildRefineCandidate(ScoredCandidate s)
    {
        try
        {
            if (s.Bubble is Bubble b)
            {
                // Mirror of the flash branch below: the panel can only POP a
                // bubble, so a user with both bubble gates off must never be
                // offered a chip that pops one.
                if (!BubblesGazeEnabled) return null;
                return new GazeRefineCandidate
                {
                    SourceBounds = s.Bounds,
                    Kind = GazeRefineKind.Bubble,
                    IsAlive = () => b.CanGazePop,
                    Activate = () => b.Pop(),
                };
            }
            if (s.Flash is FlashWindow fw)
            {
                // GetGazeTargets() returns flashes when EITHER gaze-pop OR
                // gaze-linger is enabled, but the ONLY thing the refine panel
                // can do to a flash is pop it. A linger-only user (pop off,
                // linger on = "stare to keep it alive, never destroy it")
                // must never be offered a chip that destroys their flash.
                if (App.Settings?.Current?.FlashGazePopEnabled != true) return null;
                return new GazeRefineCandidate
                {
                    SourceBounds = s.Bounds,
                    Kind = GazeRefineKind.Flash,
                    Preview = SafeFlashFrame(fw),
                    IsAlive = () => !fw.IsFadingOut,
                    Activate = () => App.Flash?.GazePop(fw),
                };
            }
            if (s.Floating is IAttentionTarget ft)
            {
                return new GazeRefineCandidate
                {
                    SourceBounds = s.Bounds,
                    Kind = GazeRefineKind.Floating,
                    IsAlive = () => !ft.GetGazeBounds().IsEmpty,
                    Activate = () => App.Video?.GazeClick(ft),
                };
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// The flash's current frame, so the refine chip shows the actual image
    /// rather than a generic token. Only frozen bitmaps are used — an unfrozen
    /// one may belong to the decode thread and would throw on touch.
    /// </summary>
    private static System.Windows.Media.Imaging.BitmapSource? SafeFlashFrame(FlashWindow fw)
    {
        try
        {
            var frames = fw.Frames;
            if (frames == null || frames.Count == 0) return null;
            var idx = Math.Clamp(fw.CurrentFrameIndex, 0, frames.Count - 1);
            var f = frames[idx];
            return f != null && f.IsFrozen ? f : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// One tick of the refine stage. Runs instead of the normal dwell while
    /// the panel is up. Four independent ways out, so being stuck is not a
    /// reachable state: pick a chip, look away, time out, or lose the
    /// candidates. (A fifth: the panel is click-through, so the mouse still
    /// reaches whatever is underneath it at all times.)
    /// </summary>
    private void TickRefine()
    {
        var overlay = _refine;
        if (overlay == null) return;

        var now = DateTime.UtcNow;
        overlay.KeepOnTop();

        // Hard ceiling.
        if ((now - _refineOpenedAt).TotalMilliseconds >= RefineTimeoutMs)
        {
            CloseRefine("timeout", suppress: true);
            return;
        }

        // Candidates died underneath us (bubble drifted off, flash expired).
        var cands = _refineCandidates;
        if (cands == null) { CloseRefine("no-candidates", suppress: true); return; }
        int alive = 0;
        for (int i = 0; i < cands.Count; i++)
        {
            try { if (cands[i].IsAlive?.Invoke() != false) alive++; }
            catch { }
        }
        if (alive < 2) { CloseRefine("candidates-expired", suppress: false); return; }

        // Face lost. A blink or a quick head turn must not kill the panel, but
        // walking away should.
        if (_faceLost || !_lastGazePoint.HasValue)
        {
            if (_refineFaceLostSince == DateTime.MinValue) _refineFaceLostSince = now;
            else if ((now - _refineFaceLostSince).TotalMilliseconds >= RefineFaceLostGraceMs)
            {
                CloseRefine("face-lost", suppress: true);
                return;
            }
            overlay.SetProgress(-1, 0);
            _refineChipIndex = -1;
            return;
        }
        _refineFaceLostSince = DateTime.MinValue;

        var p = _lastGazePoint.Value;
        int chip = overlay.HitTest(p);

        if (chip < 0)
        {
            overlay.SetProgress(-1, 0);
            _refineChipIndex = -1;
            App.GazeCursor?.SetLocked(false);
            SetCursorLock(null);

            // Look-away escape: only counts while the gaze is fully outside
            // the panel's generous escape margin, and only after it has stayed
            // there. Glancing between chips passes through the gaps and must
            // not cancel.
            var esc = overlay.EscapeBounds;
            bool outside = esc.IsEmpty || !esc.Contains(p);
            if (!outside) { _refineOutsideSince = DateTime.MinValue; return; }

            if (_refineOutsideSince == DateTime.MinValue) _refineOutsideSince = now;
            else if ((now - _refineOutsideSince).TotalMilliseconds >= RefineLookAwayMs)
                CloseRefine("look-away", suppress: true);
            return;
        }

        _refineOutsideSince = DateTime.MinValue;
        App.GazeCursor?.SetLocked(true);
        // Soft attractor only — the existing stateful lock-on, which builds and
        // drains gradually. Never a hard snap: an I-DT-style hard fixation lock
        // was tried on the cursor path and reverted (see the tombstone in
        // WebcamTrackingService by SetGazeAttractor) because it parked the
        // cursor at wrong positions.
        SetCursorLock(overlay.GetChipBounds(chip));

        if (chip != _refineChipIndex)
        {
            _refineChipIndex = chip;
            _refineChipDwellStartedAt = now;
        }

        var elapsed = (now - _refineChipDwellStartedAt).TotalMilliseconds;
        overlay.SetProgress(chip, elapsed / RefineDwellMs);

        if (elapsed >= RefineDwellMs)
        {
            GazeRefineCandidate? chosen = chip >= 0 && chip < cands.Count ? cands[chip] : null;
            // Tear the panel down BEFORE activating: the activation can spawn
            // UI of its own, and a stale click-through overlay sitting over it
            // would be the one thing here that could look like a stuck state.
            CloseRefine("selected", suppress: false);
            if (chosen != null)
            {
                try
                {
                    chosen.Activate?.Invoke();
                    if (chosen.Kind == GazeRefineKind.Bubble) GazePopped?.Invoke();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Gaze refine activation failed: {Error}", ex.Message);
                }
            }
            _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownMs);
        }
    }

    /// <summary>
    /// Tears the panel down. <paramref name="suppress"/> arms a short
    /// no-refine window: without it, a cancelled refine would be re-raised by
    /// the very next dwell over the same pair and the user could never simply
    /// take the top-scored target. This is the anti-loop guarantee.
    /// </summary>
    private void CloseRefine(string reason, bool suppress)
    {
        var overlay = _refine;
        _refine = null;
        _refineCandidates = null;
        _refineChipIndex = -1;
        _refineOutsideSince = DateTime.MinValue;
        _refineFaceLostSince = DateTime.MinValue;
        _ambiguous = false;
        if (suppress) _refineSuppressedUntil = DateTime.UtcNow.AddMilliseconds(RefineSuppressMs);

        if (overlay != null)
        {
            try { overlay.Dispose(); }
            catch (Exception ex) { App.Logger?.Debug("GazeFocusService: refine close failed: {Error}", ex.Message); }
            App.Logger?.Debug("GazeFocusService: refine closed ({Reason})", reason);
        }

        App.GazeCursor?.SetLocked(false);
        SetCursorLock(null);
    }

    /// <summary>
    /// Picks the highest-scoring target across all candidates. Score is a
    /// Gaussian distance falloff plus additive bonuses for sticky lock and
    /// flash type. See the constant block at the top of the class for the
    /// model. Returns null if no target clears ScoreThreshold.
    /// </summary>
    private GazeHit? FindBestTarget(Point p)
    {
        _scored.Clear();
        _ambiguous = false;

        Bubble? bestBubble = null;
        FlashWindow? bestFlash = null;
        IAttentionTarget? bestFloating = null;
        double bestScore = ScoreThreshold;

        // Defensive multi-monitor clamp at the gaze read: drop targets that
        // aren't on the calibrated screen, even if their spawn path missed
        // GazeContentScreenPolicy. Returns null (no-clamp) when there's no
        // calibration loaded — uniform fail-open behavior across all clamp
        // sites.
        System.Windows.Forms.Screen? calScreen = null;
        var settings = App.Settings?.Current;
        if (settings?.RestrictGazeContentToCalibratedScreen == true)
            calScreen = App.Webcam?.GetCalibratedScreen();

        var flashes = App.Flash?.GetGazeTargets();
        if (flashes != null)
        {
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var fw = flashes[i];
                Rect rect;
                try { rect = new Rect(fw.Left, fw.Top, fw.Width, fw.Height); }
                catch { continue; }
                if (!TargetOnCalScreen(rect, calScreen)) continue;
                var dist = DistanceFromRectEdge(rect, p);
                // Base score = geometry + the type prior. The sticky bonus is
                // deliberately EXCLUDED from the base: the refine stage asks
                // "which target did they mean", and sticky is a lock-stability
                // trick, not evidence of intent.
                var baseScore = GaussianScore(dist, FlashScoreSigma) + FlashTypeBonus;
                var score = baseScore;
                if (ReferenceEquals(_currentFlash, fw)) score += StickyBonus;
                _scored.Add(new ScoredCandidate(fw, null, null, rect, baseScore));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFlash = fw;
                    bestBubble = null;
                    bestFloating = null;
                }
            }
        }

        // Bubbles are gated exactly like flashes (FlashGazePopEnabled) and
        // video targets (VideoGazeClickEnabled): on their own per-feature flag,
        // or on the "Focus Gaze" master. Enumerating them with neither armed
        // would gaze-pop bubbles the user never opted into.
        var bubbles = BubblesGazeEnabled ? App.Bubbles?.GetGazeTargets() : null;
        if (bubbles != null)
        {
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var b = bubbles[i];
                var rect = b.GetGazeBounds();
                if (rect.IsEmpty) continue;
                if (!TargetOnCalScreen(rect, calScreen)) continue;
                var dist = DistanceFromRectEdge(rect, p);
                var baseScore = GaussianScore(dist, BubbleScoreSigma);
                var score = baseScore;
                if (ReferenceEquals(_currentBubble, b)) score += StickyBonus;
                _scored.Add(new ScoredCandidate(null, b, null, rect, baseScore));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestBubble = b;
                    bestFlash = null;
                    bestFloating = null;
                }
            }
        }

        // Video attention-game targets (Phase 3.2). Same scoring shape as
        // bubbles — no type bonus, so flashes still win when both are
        // simultaneously hit.
        var floats = App.Video?.GetGazeTargets();
        if (floats != null)
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var ft = floats[i];
                var rect = ft.GetGazeBounds();
                if (rect.IsEmpty) continue;
                if (!TargetOnCalScreen(rect, calScreen)) continue;
                var dist = DistanceFromRectEdge(rect, p);
                var baseScore = GaussianScore(dist, BubbleScoreSigma);
                var score = baseScore;
                if (ReferenceEquals(_currentFloating, ft)) score += StickyBonus;
                _scored.Add(new ScoredCandidate(null, null, ft, rect, baseScore));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFloating = ft;
                    bestFlash = null;
                    bestBubble = null;
                }
            }
        }

        if (bestFlash == null && bestBubble == null && bestFloating == null) return null;

        EvaluateAmbiguity(bestBubble, bestFlash, bestFloating);

        if (bestFlash != null) return new GazeHit(null, bestFlash, null);
        if (bestBubble != null) return new GazeHit(bestBubble, null, null);
        return new GazeHit(null, null, bestFloating);
    }

    /// <summary>
    /// Sets <see cref="_ambiguous"/> for this tick. See the Refine* constant
    /// block for the rule and why each clause is there. Sorting is by BASE
    /// score (no sticky bonus) so an in-progress dwell doesn't mask a genuine
    /// tie; the winner is still whoever the normal (sticky-included) scoring
    /// picked, and if sticky is what made them the winner we decline to refine.
    /// </summary>
    private void EvaluateAmbiguity(Bubble? winBubble, FlashWindow? winFlash, IAttentionTarget? winFloating)
    {
        if (!RefineEnabled) return;
        if (_scored.Count < 2) return;
        if (DateTime.UtcNow < _refineSuppressedUntil) return;

        // Highest and second-highest BASE score, distinct candidates.
        int iA = -1, iB = -1;
        for (int i = 0; i < _scored.Count; i++)
        {
            if (iA < 0 || _scored[i].BaseScore > _scored[iA].BaseScore) { iB = iA; iA = i; }
            else if (iB < 0 || _scored[i].BaseScore > _scored[iB].BaseScore) { iB = i; }
        }
        if (iA < 0 || iB < 0) return;

        var a = _scored[iA];
        var b = _scored[iB];

        if (a.BaseScore < RefineMinWinnerScore) return;
        if (b.BaseScore < RefineMinRunnerUpScore) return;
        if (a.BaseScore <= 0) return;
        if (b.BaseScore / a.BaseScore < RefineCloseRatio) return;

        // Both must plausibly live inside one gaze error blob. Two targets on
        // opposite sides of the screen scoring alike is a scoring artifact, not
        // an ambiguity the user is experiencing.
        var ca = new Point(a.Bounds.X + a.Bounds.Width / 2.0, a.Bounds.Y + a.Bounds.Height / 2.0);
        var cb = new Point(b.Bounds.X + b.Bounds.Width / 2.0, b.Bounds.Y + b.Bounds.Height / 2.0);
        var dx = ca.X - cb.X; var dy = ca.Y - cb.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > RefineMaxSpreadDips) return;

        // If the sticky bonus is what decided the winner, the user has already
        // been holding that target for a full dwell. Trust them.
        if (!a.Matches(winBubble, winFlash, winFloating)) return;

        _ambiguous = true;
    }

    /// <summary>One candidate's geometry-only standing for this tick.</summary>
    private readonly struct ScoredCandidate
    {
        public ScoredCandidate(FlashWindow? flash, Bubble? bubble, IAttentionTarget? floating, Rect bounds, double baseScore)
        {
            Flash = flash;
            Bubble = bubble;
            Floating = floating;
            Bounds = bounds;
            BaseScore = baseScore;
        }
        public FlashWindow? Flash { get; }
        public Bubble? Bubble { get; }
        public IAttentionTarget? Floating { get; }
        public Rect Bounds { get; }
        public double BaseScore { get; }

        public bool Matches(Bubble? bubble, FlashWindow? flash, IAttentionTarget? floating)
            => (Bubble != null && ReferenceEquals(Bubble, bubble))
               || (Flash != null && ReferenceEquals(Flash, flash))
               || (Floating != null && ReferenceEquals(Floating, floating));
    }

    // Defensive clamp helper for the gaze read. Uses the rect's center for
    // screen lookup. WPF Window coordinates are in DIPs but Screen.FromPoint
    // takes pixels; on same-DPI multi-monitor setups these align, and on
    // mixed-DPI setups the center-based check is robust enough for monitors
    // that are visually distinct. Fail-open on errors (target considered
    // valid) since this is a defensive backstop, not the primary clamp.
    //
    // As of v5.9.9, this is the only read-side clamp; spawn-side clamps were
    // removed. Only gaze-reactive content (BlinkTrainer overlay) clamps at
    // spawn now. Flashes and bubbles spawn freely across all monitors;
    // off-cal-screen instances are simply filtered out of gaze interaction
    // here, while mouse-click still works everywhere.
    // Known edge case: on mixed-DPI multi-monitor setups, rect-center
    // coordinates carry small drift between DIPs and pixels, which CAN cause
    // a misclassification when a target is positioned near a monitor edge.
    // If users on mixed-DPI hardware report stray gaze-clicks on off-screen
    // content, this is the suspect.
    private static bool TargetOnCalScreen(Rect r, System.Windows.Forms.Screen? cal)
    {
        if (cal == null) return true;
        try
        {
            var cx = (int)(r.X + r.Width / 2.0);
            var cy = (int)(r.Y + r.Height / 2.0);
            var s = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cx, cy));
            return s != null && string.Equals(s.DeviceName, cal.DeviceName, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static double GaussianScore(double dist, double sigma)
    {
        var d = dist / sigma;
        return Math.Exp(-0.5 * d * d);
    }

    /// <summary>
    /// Perpendicular distance from p to the nearest edge of r. Returns 0 if
    /// p is inside r.
    /// </summary>
    private static double DistanceFromRectEdge(Rect r, Point p)
    {
        var dx = Math.Max(0, Math.Max(r.X - p.X, p.X - (r.X + r.Width)));
        var dy = Math.Max(0, Math.Max(r.Y - p.Y, p.Y - (r.Y + r.Height)));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private readonly struct GazeHit
    {
        public GazeHit(Bubble? bubble, FlashWindow? flash, IAttentionTarget? floating)
        {
            Bubble = bubble;
            Flash = flash;
            Floating = floating;
        }
        public Bubble? Bubble { get; }
        public FlashWindow? Flash { get; }
        public IAttentionTarget? Floating { get; }
    }

    private void AdvanceBubbleDwell(Bubble b)
    {
        // Belt-and-braces gate: FindBestTarget already skips bubbles when both
        // bubble gates are off, but if one slips through (e.g. a toggle flips
        // mid-tick) release any in-progress bubble dwell instead of popping
        // something the user turned off.
        if (!BubblesGazeEnabled)
        {
            ClearTarget();
            return;
        }

        if (!ReferenceEquals(_currentBubble, b))
        {
            ClearTarget();
            _currentBubble = b;
            _dwellStartedAt = DateTime.UtcNow;
        }

        var elapsedMs = (DateTime.UtcNow - _dwellStartedAt).TotalMilliseconds;
        var t = elapsedMs / DwellMs;
        b.SetGazeDwellProgress(t);

        if (elapsedMs >= DwellMs)
        {
            // Ambiguous neighbourhood? Magnify instead of guessing. Returns
            // false whenever the refine can't or shouldn't run, so the pop
            // below is still the default path.
            if (TryRaiseRefine()) return;
            try { b.Pop(); GazePopped?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("Gaze bubble pop failed: {Error}", ex.Message); }
            _currentBubble = null;
            _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownMs);
        }
    }

    // Phase 3.3: dwell tick branches pop and linger independently. The two
    // behaviors share the dwell-time tracker but their fire conditions are
    // separate, so (Pop=OFF, Linger=ON) extends a flash's lifetime without
    // ever auto-dismissing it, and (Pop=ON, Linger=OFF) restores the pre-3.3
    // gaze-pop behavior with no lifetime extension.
    private void AdvanceFlashDwell(FlashWindow fw)
    {
        var settings = App.Settings?.Current;
        var popEnabled = settings?.FlashGazePopEnabled == true;
        var lingerEnabled = settings?.FlashGazeLingerEnabled == true;
        var lingerExtensionMs = settings?.FlashGazeLingerExtensionMs ?? 1500;

        if (!ReferenceEquals(_currentFlash, fw))
        {
            ClearTarget();
            _currentFlash = fw;
            _dwellStartedAt = DateTime.UtcNow;
            _lastLingerBoostAt = DateTime.MinValue;
        }

        var elapsedMs = (DateTime.UtcNow - _dwellStartedAt).TotalMilliseconds;

        // Pop progress visual: only show the dwell-filling ring when pop
        // is actually going to fire. With pop disabled, the ring would
        // mislead the user into expecting a click that never happens.
        if (popEnabled)
        {
            var t = elapsedMs / DwellMs;
            fw.SetGazeDwellProgress(t);
        }

        // Linger boost. Independent of popEnabled. Throttled to ~250ms so
        // CancelAfter isn't re-scheduled every 33ms tick. Each boost pushes
        // the death deadline lingerExtensionMs into the future from now;
        // when gaze leaves, the deadline stops moving and elapses
        // naturally.
        if (lingerEnabled
            && (DateTime.UtcNow - _lastLingerBoostAt).TotalMilliseconds >= LingerBoostThrottleMs)
        {
            try { fw.BoostLifetime(lingerExtensionMs); }
            catch (Exception ex) { App.Logger?.Debug("Flash linger boost failed: {Error}", ex.Message); }
            _lastLingerBoostAt = DateTime.UtcNow;
        }

        // Pop. Independent of lingerEnabled. When pop is off, dwell can
        // accumulate past DwellMs indefinitely without firing — linger
        // alone holds the window alive. When pop is on, this fires once
        // dwell reaches DwellMs and the cooldown prevents chain-popping.
        if (popEnabled && elapsedMs >= DwellMs)
        {
            if (TryRaiseRefine()) return;
            try { App.Flash?.GazePop(fw); }
            catch (Exception ex) { App.Logger?.Debug("Gaze flash pop failed: {Error}", ex.Message); }
            _currentFlash = null;
            _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownMs);
        }
    }

    private void AdvanceFloatingTextDwell(IAttentionTarget ft)
    {
        if (!ReferenceEquals(_currentFloating, ft))
        {
            ClearTarget();
            _currentFloating = ft;
            _dwellStartedAt = DateTime.UtcNow;
        }

        var elapsedMs = (DateTime.UtcNow - _dwellStartedAt).TotalMilliseconds;

        if (elapsedMs >= DwellMs)
        {
            if (TryRaiseRefine()) return;
            try { App.Video?.GazeClick(ft); }
            catch (Exception ex) { App.Logger?.Debug("Gaze video target click failed: {Error}", ex.Message); }
            _currentFloating = null;
            _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownMs);
        }
    }

    private void ClearTarget()
    {
        if (_currentBubble != null)
        {
            try { _currentBubble.SetGazeDwellProgress(0); } catch { }
            _currentBubble = null;
        }
        if (_currentFlash != null)
        {
            try { _currentFlash.SetGazeDwellProgress(0); } catch { }
            _currentFlash = null;
        }
        if (_currentFloating != null)
        {
            _currentFloating = null;
        }
        _lastLingerBoostAt = DateTime.MinValue;
    }

    public void Dispose()
    {
        var settings = App.Settings?.Current;
        if (settings != null) settings.PropertyChanged -= OnSettingsChanged;
        if (App.Webcam != null) App.Webcam.OnTrackingStateChanged -= OnWebcamStateChanged;
        Stop();
    }
}
