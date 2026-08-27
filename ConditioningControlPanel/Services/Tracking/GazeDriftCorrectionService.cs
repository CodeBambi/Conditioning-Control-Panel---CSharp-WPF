using System;
using System.Collections.Generic;
using System.Windows;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Continuous implicit recalibration: keeps the gaze mapping honest as the
/// user's posture drifts, without them ever opening Quick Recal.
///
/// Principle: when a person clicks something, they are almost always looking
/// at it. Each left-click is therefore a free ground-truth sample. If the
/// projected gaze was fixating near the click point, the gap between them is
/// calibration drift — and we fold a small fraction of it into the runtime
/// offset (the same translational nudge Quick Recal captures). Individual
/// nudges are tiny and gated hard (fixation required, residual must be small
/// enough to plausibly be drift), so a click the user wasn't looking at can't
/// yank the calibration; repeated ordinary clicks converge it back onto truth
/// within a handful of interactions.
///
/// Scope and privacy: the low-level mouse hook is installed ONLY while webcam
/// tracking is running with a calibration loaded and the
/// WebcamAutoDriftCorrection setting is on. Click positions are consumed
/// in-memory for the residual math and never logged or persisted — only the
/// resulting offset (numbers, same as Quick Recal) is saved.
/// </summary>
public class GazeDriftCorrectionService : IDisposable
{
    private const double MaxResidualDips = 220;    // farther than this from gaze → user wasn't looking at the click
    private const double MinResidualDips = 15;     // smaller than this → nothing worth fixing
    private const double NudgeGain = 0.15;         // fraction of the residual folded in per click
    private const double MaxTotalOffsetDips = 300; // cumulative runtime-offset clamp per axis
    private const int GazeFreshMs = 300;           // newest gaze sample must be this recent
    private const int FixationWindowMs = 350;      // gaze must have been stable over this window…
    private const double FixationMaxSpreadDips = 90; // …within this spread, else user was mid-saccade
    private const int MinFixationSamples = 5;
    private const int NudgeCooldownMs = 500;
    private const int PersistThrottleMs = 30000;   // batch disk writes; in-memory offset applies instantly

    // ─────────────────────────────────────────────────────────────────────────
    //  Head-reposition fast adaptation
    // ─────────────────────────────────────────────────────────────────────────
    //  The single biggest driver of "it worked yesterday, it's garbage today"
    //  is uncompensated head motion: the gaze feature is the iris centre
    //  normalised against the eye corners, so a few cm of seating change walks
    //  the whole per-user mapping off by roughly 1-3 degrees. The nudge loop
    //  above already recovers from that — but at NudgeGain 0.15 it needs on the
    //  order of twenty ordinary clicks, which the user experiences as minutes
    //  of "it's just broken".
    //
    //  IMPORTANT — this is NOT head-pose compensation. Head-pose comp was built
    //  and retired TWICE (see the tombstone in WebcamTrackingService, ~:2021).
    //  Pose is never multiplied into the projection and never persisted here;
    //  WebcamCalibrationData.HeadPoseComp/BaselineHeadPose stay tombstoned and
    //  the baseline below lives in memory only (which also keeps us clear of
    //  the privacy contract's "persisted calibration data" clause — no
    //  ConsentVersion bump needed).
    //
    //  Pose is used ONLY as a trigger: we can't model *how* pose changes the
    //  mapping, but we can reliably detect *that* it changed, and respond by
    //  letting the proven residual-folding machinery adapt faster for a while.
    // ─────────────────────────────────────────────────────────────────────────

    // 0.09 rad ≈ 5.2°. Below this is fidget, not a reposition: the yaw/pitch we
    // consume is already a 12-frame (~400 ms) rolling mean, and a seated user
    // holding a screen still wanders roughly ±1-2° on that smoothed signal,
    // with solvePnP-on-6-landmarks contributing about another degree of bias
    // noise. 5° sits clear of both. A genuine reposition — leaning back,
    // slouching, turning toward a second monitor — is 5-15°.
    private const double PoseTriggerRad = 0.09;

    // 0.02 rad ≈ 1.15° peak-to-peak across the stability window. On a signal
    // that is already a 400 ms mean, that means genuinely parked.
    private const double PoseStableSpreadRad = 0.02;
    private const int PoseStableWindowMs = 700;
    private const int PoseStableMinSamples = 10;   // guards against declaring "stable" from a stalled/low-fps stream

    // If the pose deviates and never settles within this long, the user is in
    // continuous motion (constant fidgeting, eating, talking to someone).
    // Adapting to a moving target amplifies noise for nothing, so we give up,
    // re-anchor the baseline to wherever they are now, and let the default slow
    // gain carry it. This is the primary constant-fidget guard.
    private const int PoseArmTimeoutMs = 20000;

    // 0.45 vs the default 0.15. Convergence: the remaining bias after n clicks
    // is (1-g)^n, so 5 clicks leaves 44% at 0.15 but only 5% at 0.45 — seconds
    // instead of minutes. Cost: an EMA with gain g driven by residual noise σ
    // settles to a jitter of σ·sqrt(g/(2-g)), i.e. ~0.29σ at 0.15 and ~0.54σ at
    // 0.45 — roughly double, which is tolerable for a bounded window and is
    // exactly why it must decay. (0.8 would give ~0.82σ, comparable in size to
    // the error being corrected — that is the runaway regime.)
    private const double FastNudgeGain = 0.45;

    // Raised from 220 for the same window. The reposition bias (up to ~3°,
    // ~120 DIPs) stacks on top of whatever baseline error was already there —
    // which is what 220 was sized for — so right after a reposition the honest
    // drift residuals land in the 220-320 band and the default cap would reject
    // exactly the samples carrying the most information. 300 stays at/below the
    // ±300-per-axis MaxTotalOffsetDips clamp, so a single fold can never
    // out-run the cumulative limit.
    private const double FastMaxResidualDips = 300;

    // At ordinary click rates this is 5-10 ground-truth samples — plenty for
    // (1-g)^n to kill the bias — while being short enough that one activation
    // can't bleed into the next posture change.
    private const int FastModeMs = 45000;
    private const int FastModeMaxNudges = 15;      // hard runaway stop, independent of the clock

    // A Fast window is a promise about *current* geometry: the user settled at
    // a new pose and we believe the mapping is stale by a known-ish amount. The
    // moment the pose stream stops (face lost, user left, tracking hitched) we
    // no longer know that, and the 45 s window would otherwise keep the raised
    // gain armed on evidence that has expired — including for whoever/whatever
    // the stream comes back on. So: no pose sample for this long ends the
    // window, and re-entry must go the normal Armed→settle route.
    //
    // 1.2 s matches the face-lost grace GazeFocusService uses for the refine
    // panel (RefineFaceLostGraceMs), for the same reason: at the ~30 Hz pose
    // rate it is ~36 consecutive missing samples, far past a blink or a dropped
    // frame, while still being 2.7% of the window — so a false positive costs
    // almost nothing (the head is by definition back at baseline, where the
    // conservative gain is the right answer anyway).
    private const int FastPoseStaleMs = 1200;

    // Three consecutive well-placed clicks (residual inside a normal click
    // target) means the mapping is back on truth: drop to the conservative gain
    // early rather than burning the rest of the window amplifying noise.
    private const double FastConvergedResidualDips = 45;
    private const int FastConvergedStreak = 3;

    // Leaky-bucket duty cycle: at most 90 s of fast mode per 5 minutes. Belt
    // and braces behind the settle gate — a user who repeatedly leans in and
    // back *does* settle each time, and without this could sit in fast mode
    // indefinitely.
    private const double FastBudgetCapMs = 90000;
    private const double FastBudgetWindowMs = 300000;
    private const double FastBudgetMinToEnterMs = 8000;

    // Very slow baseline re-anchor while idle and parked (tau 60 s). Absorbs a
    // gradual slouch so creep never fires the step detector — slow drift is
    // precisely the case the default 0.15 gain already handles well. A real
    // step crosses PoseTriggerRad long before this can eat it.
    private const double PoseBaselineTauSec = 60.0;

    private readonly GlobalMouseHook _hook = new();
    private readonly Queue<(DateTime At, Point P)> _recentGaze = new();
    private readonly object _gazeLock = new();

    // Pose state is UI-thread-confined: OnHeadPose is dispatched by
    // WebcamTrackingService and ProcessClick is dispatched from the hook, so no
    // lock is needed. In-memory only — never written to WebcamCalibrationData.
    private readonly Queue<(DateTime At, double Yaw, double Pitch)> _poseWindow = new();
    private bool _poseBaselineSet;
    private double _poseBaseYaw;
    private double _poseBasePitch;
    private DateTime _lastPoseAt = DateTime.MinValue;
    private bool _poseArmed;
    private DateTime _poseArmedAt = DateTime.MinValue;

    private DateTime _fastUntil = DateTime.MinValue;
    private DateTime _fastEnteredAt = DateTime.MinValue;
    private int _fastNudges;
    private int _fastConverged;
    private double _fastBudgetMs = FastBudgetCapMs;
    private DateTime _budgetTickAt = DateTime.MinValue;

    private bool _hookActive;
    private bool _subscribed;
    private bool _disposed;
    private DateTime _lastNudgeAt = DateTime.MinValue;
    private DateTime _lastPersistAt = DateTime.MinValue;

    public GazeDriftCorrectionService()
    {
        if (App.Webcam != null)
        {
            App.Webcam.OnTrackingStateChanged += _ => EnsureHookState();
        }
        var settings = App.Settings?.Current;
        if (settings != null)
        {
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Models.AppSettings.WebcamAutoDriftCorrection))
                    EnsureHookState();
            };
        }
        _hook.LeftDown = OnLeftDown;
        EnsureHookState();

        if (Application.Current != null)
            Application.Current.Exit += (_, _) => Dispose();
    }

    /// <summary>
    /// Installs/removes the mouse hook and gaze subscription to match the
    /// current state: enabled setting + tracking running + calibration loaded.
    /// Safe to call redundantly; must run on the UI thread (hook needs a
    /// message pump).
    /// </summary>
    private void EnsureHookState()
    {
        if (_disposed) return;
        bool shouldRun = App.Settings?.Current?.WebcamAutoDriftCorrection == true
            && App.Webcam is { IsRunning: true, Calibration: not null };

        if (shouldRun && !_hookActive)
        {
            try
            {
                _hook.Start();
                _hookActive = true;
                if (!_subscribed && App.Webcam != null)
                {
                    App.Webcam.OnGazeMove += OnGazeMove;
                    App.Webcam.OnHeadPose += OnHeadPoseSample;
                    _subscribed = true;
                }
                // Fresh session, fresh baseline — the previous one described a
                // seating position that no longer exists.
                ResetPoseState();
                App.Logger?.Information("GazeDriftCorrectionService: active (click-driven drift correction)");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazeDriftCorrectionService: hook start failed");
            }
        }
        else if (!shouldRun && _hookActive)
        {
            try { _hook.Stop(); } catch { }
            _hookActive = false;
            if (_subscribed && App.Webcam != null)
            {
                App.Webcam.OnGazeMove -= OnGazeMove;
                App.Webcam.OnHeadPose -= OnHeadPoseSample;
                _subscribed = false;
            }
            lock (_gazeLock) { _recentGaze.Clear(); }
            ResetPoseState();
            App.Logger?.Information("GazeDriftCorrectionService: inactive");
        }
    }

    private void OnGazeMove(Point p)
    {
        var now = DateTime.UtcNow;
        lock (_gazeLock)
        {
            _recentGaze.Enqueue((now, p));
            while (_recentGaze.Count > 0 && (now - _recentGaze.Peek().At).TotalMilliseconds > FixationWindowMs * 2)
                _recentGaze.Dequeue();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Pose trigger state machine
    //
    //    Idle ──(|Δpose| > trigger)──> Armed ──(pose re-settles)──> Fast
    //      ^                             │                            │
    //      └──(Δ falls back / 20 s ──────┘                            │
    //          arm timeout, re-anchor)                                │
    //      └────────────(45 s elapsed | 3 converged clicks |──────────┘
    //                    15 nudges | budget exhausted |
    //                    1.2 s with no pose sample)
    //
    //  Fast mode is only ever entered from a *settled* new pose. That single
    //  gate is what makes constant fidgeting safe: while the head is still
    //  moving there is no stable geometry to adapt to, so we simply don't
    //  adapt faster.
    // ─────────────────────────────────────────────────────────────────────────

    private void ResetPoseState()
    {
        _poseWindow.Clear();
        _poseBaselineSet = false;
        _poseArmed = false;
        _fastUntil = DateTime.MinValue;
        _fastNudges = 0;
        _fastConverged = 0;
        _fastBudgetMs = FastBudgetCapMs;
        _budgetTickAt = DateTime.MinValue;
        _lastPoseAt = DateTime.MinValue;
    }

    private bool InFastMode(DateTime now)
        => now < _fastUntil && _fastNudges < FastModeMaxNudges;

    /// <summary>
    /// Leaky-bucket accounting for the fast-mode duty cycle. Drains while fast
    /// mode is live, refills at FastBudgetCap/FastBudgetWindow the rest of the
    /// time. Driven off the pose stream (and each click), which is the same
    /// stream that can start fast mode.
    ///
    /// Refill is computed from the elapsed wall-clock delta, NOT per tick, so a
    /// long quiet gap still counts: the stream that drives this stops whenever
    /// the face is lost or the machine sleeps, and time spent not adapting is
    /// exactly the time the duty cycle is meant to be earning back. A gap
    /// longer than the window is clamped to one window's worth of credit —
    /// which by construction refills the bucket to its cap and no further, so a
    /// long absence can never bank more than a full bucket.
    /// </summary>
    private void TickFastBudget(DateTime now)
    {
        if (_budgetTickAt == DateTime.MinValue) { _budgetTickAt = now; return; }
        double dt = (now - _budgetTickAt).TotalMilliseconds;
        _budgetTickAt = now;
        // Clock stepped backwards (DST, NTP correction) — nothing honest to do.
        if (dt <= 0) return;
        // One window is already worth a whole bucket; anything past that is
        // credit we would only clamp away, and clamping dt here also bounds the
        // drain branch.
        if (dt > FastBudgetWindowMs) dt = FastBudgetWindowMs;

        if (now < _fastUntil) _fastBudgetMs -= dt;
        else _fastBudgetMs += dt * (FastBudgetCapMs / FastBudgetWindowMs);
        _fastBudgetMs = Math.Clamp(_fastBudgetMs, 0, FastBudgetCapMs);
    }

    private bool IsPoseParked()
    {
        if (_poseWindow.Count < PoseStableMinSamples) return false;
        double minY = double.MaxValue, maxY = double.MinValue;
        double minP = double.MaxValue, maxP = double.MinValue;
        foreach (var s in _poseWindow)
        {
            if (s.Yaw < minY) minY = s.Yaw;
            if (s.Yaw > maxY) maxY = s.Yaw;
            if (s.Pitch < minP) minP = s.Pitch;
            if (s.Pitch > maxP) maxP = s.Pitch;
        }
        return (maxY - minY) <= PoseStableSpreadRad && (maxP - minP) <= PoseStableSpreadRad;
    }

    private void AnchorPoseBaseline(double yaw, double pitch)
    {
        _poseBaseYaw = yaw;
        _poseBasePitch = pitch;
        _poseBaselineSet = true;
        _poseArmed = false;
    }

    /// <summary>
    /// UI-thread callback from WebcamTrackingService.OnHeadPose (~30 Hz,
    /// already 12-frame smoothed). Cheap by construction: one enqueue, one
    /// bounded scan. Never touches the projection — pose is a trigger only.
    /// </summary>
    private void OnHeadPoseSample(double yaw, double pitch)
    {
        try
        {
            if (_disposed || !_hookActive) return;
            if (double.IsNaN(yaw) || double.IsNaN(pitch)) return;

            var now = DateTime.UtcNow;
            // Before the budget tick, so the quiet gap is credited as refill
            // rather than drained as fast-mode time we never actually spent.
            EndFastModeIfPoseStale(now);
            TickFastBudget(now);

            // A gap in the stream (face lost, tracking paused) invalidates the
            // window — we can't tell a settle from a jump across the gap.
            if (_lastPoseAt != DateTime.MinValue && (now - _lastPoseAt).TotalMilliseconds > 1500)
            {
                _poseWindow.Clear();
                _poseArmed = false;
            }
            var prevPoseAt = _lastPoseAt;
            _lastPoseAt = now;

            _poseWindow.Enqueue((now, yaw, pitch));
            while (_poseWindow.Count > 0 && (now - _poseWindow.Peek().At).TotalMilliseconds > PoseStableWindowMs)
                _poseWindow.Dequeue();

            bool parked = IsPoseParked();

            if (!_poseBaselineSet)
            {
                // First anchor only once the user is actually holding still,
                // so the baseline describes a real seating position.
                if (parked) AnchorPoseBaseline(yaw, pitch);
                return;
            }

            // While fast mode is live the baseline is already anchored at the
            // new pose; don't re-arm or re-anchor underneath it. Exit early if
            // the duty-cycle budget ran dry.
            if (now < _fastUntil)
            {
                if (_fastBudgetMs <= 0) ExitFastMode("budget exhausted");
                return;
            }

            double delta = Math.Max(Math.Abs(yaw - _poseBaseYaw), Math.Abs(pitch - _poseBasePitch));

            if (delta <= PoseTriggerRad)
            {
                // Back where they started — that was a transient swing, not a
                // reposition, and the mapping never left truth.
                _poseArmed = false;
                if (parked)
                {
                    // Slow re-anchor (tau 60 s) so gradual creep never fires
                    // the step detector.
                    double dtSec = prevPoseAt == DateTime.MinValue
                        ? 0.033
                        : Math.Clamp((now - prevPoseAt).TotalSeconds, 0, 1.0);
                    double a = Math.Clamp(dtSec / PoseBaselineTauSec, 0, 1);
                    _poseBaseYaw += (yaw - _poseBaseYaw) * a;
                    _poseBasePitch += (pitch - _poseBasePitch) * a;
                }
                return;
            }

            if (!_poseArmed)
            {
                _poseArmed = true;
                _poseArmedAt = now;
                return;
            }

            if (parked)
            {
                EnterFastMode(now, yaw, pitch, delta);
                return;
            }

            if ((now - _poseArmedAt).TotalMilliseconds >= PoseArmTimeoutMs)
            {
                // Continuous motion. Fast adaptation on a moving target is
                // pure noise amplification — accept the new pose as the
                // baseline and let the conservative gain do the work.
                AnchorPoseBaseline(yaw, pitch);
                App.Logger?.Debug("GazeDriftCorrectionService: pose never settled in {Ms} ms — re-anchored, staying at default gain",
                    PoseArmTimeoutMs);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeDriftCorrectionService.OnHeadPoseSample: {Error}", ex.Message);
        }
    }

    private void EnterFastMode(DateTime now, double yaw, double pitch, double delta)
    {
        // Re-anchor first either way: the user is settled somewhere new and
        // that is the pose we measure future deltas against.
        AnchorPoseBaseline(yaw, pitch);

        if (_fastBudgetMs < FastBudgetMinToEnterMs)
        {
            App.Logger?.Debug("GazeDriftCorrectionService: reposition detected but fast-adapt budget spent ({Ms:F0} ms) — default gain",
                _fastBudgetMs);
            return;
        }

        _fastUntil = now.AddMilliseconds(FastModeMs);
        _fastEnteredAt = now;
        _fastNudges = 0;
        _fastConverged = 0;

        // Degrees only — no positions, no per-frame data (privacy contract).
        App.Logger?.Information("GazeDriftCorrectionService: head reposition {Deg:F1}° settled — fast drift adaptation for {Sec:F0}s",
            delta * 180.0 / Math.PI, FastModeMs / 1000.0);
    }

    /// <summary>
    /// Ends an in-flight fast window if the head-pose stream has gone quiet for
    /// <see cref="FastPoseStaleMs"/>. Called from both entry points that can
    /// observe the clock moving — the pose callback (which sees the gap on the
    /// far side of it) and ProcessClick (which sees it *during* the gap, and is
    /// the one that would otherwise apply an elevated-gain nudge on stale
    /// evidence). Both are UI-thread, which is where all this state lives.
    ///
    /// Deliberately not "resume": the window is over, the pose window is
    /// dropped, and the arm flag is cleared, so getting back to fast adaptation
    /// takes a fresh deviation past PoseTriggerRad followed by a fresh settle.
    /// </summary>
    private void EndFastModeIfPoseStale(DateTime now)
    {
        if (_fastUntil == DateTime.MinValue) return;
        if (_lastPoseAt == DateTime.MinValue) return;
        if ((now - _lastPoseAt).TotalMilliseconds < FastPoseStaleMs) return;
        ExitFastMode("pose stream gap");
        _poseWindow.Clear();
        _poseArmed = false;
    }

    private void ExitFastMode(string reason)
    {
        if (_fastUntil == DateTime.MinValue) return;
        _fastUntil = DateTime.MinValue;
        _fastConverged = 0;
        App.Logger?.Debug("GazeDriftCorrectionService: fast drift adaptation ended ({Reason})", reason);
    }

    /// <summary>
    /// Hook-thread callback — must stay cheap. Snapshot the click point,
    /// bounce the real work to the dispatcher, never swallow the click.
    /// </summary>
    private bool OnLeftDown(Point physicalPx)
    {
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
                dispatcher.BeginInvoke(() => ProcessClick(physicalPx));
        }
        catch { }
        return false;
    }

    private void ProcessClick(Point physicalPx)
    {
        try
        {
            if (!_hookActive) return;
            var webcam = App.Webcam;
            var cal = webcam?.Calibration;
            if (webcam == null || cal == null) return;

            // Don't fight the explicit calibration flows — they own the
            // offset while open.
            if (WebcamCalibrationWindow.IsShowing) return;
            foreach (Window w in Application.Current.Windows)
            {
                if (w is WebcamQuickRecalWindow) return;
            }

            var now = DateTime.UtcNow;
            // A click can land in the middle of a pose-stream gap, where the
            // pose callback isn't running to notice. Check here too, before
            // anything reads the fast gain.
            EndFastModeIfPoseStale(now);
            TickFastBudget(now);
            if ((now - _lastNudgeAt).TotalMilliseconds < NudgeCooldownMs) return;

            // Fixation check: the gaze must have been parked (not mid-saccade)
            // when the click landed, else "gaze at click time" is meaningless.
            List<(DateTime At, Point P)> snapshot;
            lock (_gazeLock) { snapshot = new List<(DateTime, Point)>(_recentGaze); }
            if (snapshot.Count < MinFixationSamples) return;
            if ((now - snapshot[^1].At).TotalMilliseconds > GazeFreshMs) return;

            var window = snapshot.FindAll(s => (now - s.At).TotalMilliseconds <= FixationWindowMs);
            if (window.Count < MinFixationSamples) return;

            double sx = 0, sy = 0;
            foreach (var s in window) { sx += s.P.X; sy += s.P.Y; }
            double gx = sx / window.Count, gy = sy / window.Count;
            foreach (var s in window)
            {
                double dx0 = s.P.X - gx, dy0 = s.P.Y - gy;
                if (Math.Sqrt(dx0 * dx0 + dy0 * dy0) > FixationMaxSpreadDips) return;
            }

            // Click physical px → the calibrated monitor's local DIP space
            // (the space OnGazeMove emits in: window-local DIPs of the
            // calibration window, which was borderless-maximized on that
            // monitor).
            var bounds = cal.MonitorBounds;
            double dpi = bounds?.DpiScale is > 0.25 and < 8.0 ? bounds.DpiScale : 1.0;
            double originX = bounds?.DeviceName != null ? bounds.X : 0;
            double originY = bounds?.DeviceName != null ? bounds.Y : 0;
            double clickX = (physicalPx.X - originX) / dpi;
            double clickY = (physicalPx.Y - originY) / dpi;

            // Ignore clicks off the calibrated monitor entirely.
            if (bounds != null
                && (clickX < 0 || clickY < 0 || clickX > bounds.Width || clickY > bounds.Height)) return;

            double rx = clickX - gx;
            double ry = clickY - gy;
            double residual = Math.Sqrt(rx * rx + ry * ry);

            // Fast adaptation window: raise the fold gain and the residual cap,
            // then ramp both linearly back to the conservative defaults across
            // the window. The ramp matters as much as the peak — it means the
            // loud, informative first seconds after a reposition get the
            // aggressive gain and the quiet tail settles at the noise-tolerant
            // one, with no step change at the boundary.
            bool fast = InFastMode(now);
            double gain = NudgeGain;
            double residualCap = MaxResidualDips;
            if (fast)
            {
                double ramp = 1.0 - Math.Clamp((now - _fastEnteredAt).TotalMilliseconds / FastModeMs, 0, 1);
                gain = NudgeGain + (FastNudgeGain - NudgeGain) * ramp;
                residualCap = MaxResidualDips + (FastMaxResidualDips - MaxResidualDips) * ramp;
            }

            if (residual > residualCap) return;

            if (fast)
            {
                // Converged = the click landed inside a normal target's worth
                // of the projected gaze. Three in a row and the reposition has
                // been absorbed; stop early rather than spending the rest of
                // the window amplifying noise.
                if (residual < FastConvergedResidualDips)
                {
                    if (++_fastConverged >= FastConvergedStreak) ExitFastMode("converged");
                }
                else _fastConverged = 0;
            }

            if (residual < MinResidualDips) return;

            var prev = cal.RuntimeOffset;
            double newDx = Math.Clamp((prev?.Dx ?? 0) + rx * gain, -MaxTotalOffsetDips, MaxTotalOffsetDips);
            double newDy = Math.Clamp((prev?.Dy ?? 0) + ry * gain, -MaxTotalOffsetDips, MaxTotalOffsetDips);
            if (fast && ++_fastNudges >= FastModeMaxNudges) ExitFastMode("nudge budget");

            bool persist = (now - _lastPersistAt).TotalMilliseconds >= PersistThrottleMs;
            webcam.SetRuntimeOffset(new RuntimeOffsetData
            {
                Dx = newDx,
                Dy = newDy,
                CapturedAt = now,
            }, persist);
            if (persist) _lastPersistAt = now;
            _lastNudgeAt = now;

            // Magnitude only — no positions in the log (privacy contract).
            App.Logger?.Debug("GazeDriftCorrectionService: drift nudge {Mag:F0} DIPs applied (gain={Gain:F2} fast={Fast} persist={Persist})",
                residual * gain, gain, fast, persist);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeDriftCorrectionService.ProcessClick: {Error}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _hook.Stop(); } catch { }
        _hook.Dispose();
        if (_subscribed && App.Webcam != null)
        {
            App.Webcam.OnGazeMove -= OnGazeMove;
            App.Webcam.OnHeadPose -= OnHeadPoseSample;
            _subscribed = false;
        }
        _hookActive = false;
        ResetPoseState();
    }
}
