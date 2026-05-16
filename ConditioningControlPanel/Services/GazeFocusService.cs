using System;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Lab feature: lets the user pop floating bubbles and dismiss flash images
/// by looking at them for ~1 second, or by blinking once while looking near
/// them. Subscribes to the shared WebcamTrackingService gaze stream while
/// active and drives a small inflate animation on the dwell target before
/// invoking its existing pop / click pipeline (sound, XP, hydra,
/// achievements, haptics).
///
/// Coordinate space: WebcamTrackingService.OnGazeMove emits points in WPF
/// DIPs of the primary screen (the calibration window's ActualWidth/Height),
/// which matches Window.Left/Top/Width/Height directly — no DPI conversion
/// needed for hit-testing on the primary monitor.
///
/// Hit-test tolerance: bubbles are small and homography has a few percent of
/// residual error, so we use distance-from-rect-edge with a slack margin
/// (BubbleSlackDips). The closest target within slack wins — flashes get
/// priority when both are simultaneously hit (foreground content beats
/// drifting bubbles).
/// </summary>
public class GazeFocusService : IDisposable
{
    private const int DefaultDwellMs = 1000;
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
    private const double BubbleScoreSigma = 100; // ≈ 2.45σ (≈245 dips) at threshold
    private const double FlashScoreSigma = 60;
    private const double StickyBonus = 0.20;
    private const double FlashTypeBonus = 0.15;
    private const double ScoreThreshold = 0.05;

    private DispatcherTimer? _timer;
    private Point? _lastGazePoint;
    private bool _faceLost;
    private DateTime _dwellStartedAt;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private bool _subscribed;

    // Mutually exclusive — only one target is being dwelt on at a time.
    private Bubble? _currentBubble;
    private FlashWindow? _currentFlash;
    private FloatingText? _currentFloating;

    // Throttle clock for stare-linger boosts. Reset to MinValue whenever the
    // dwell target changes (or no target is held) so the first boost on
    // re-acquisition fires immediately.
    private DateTime _lastLingerBoostAt = DateTime.MinValue;

    public bool IsActive { get; private set; }
    public int DwellMs { get; set; } = DefaultDwellMs;

    /// <summary>Fires when IsActive flips, on the UI thread.</summary>
    public event Action<bool>? OnActiveChanged;

    public GazeFocusService()
    {
        // ShutdownMode=OnLastWindowClose means subsystems holding hidden
        // windows can keep the process alive after MainWindow closes —
        // close ourselves on app-exit so we drop those references. Mirrors
        // KeywordHighlightService.cs:30-31.
        if (Application.Current != null)
            Application.Current.Exit += (_, _) => Stop();
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
        if (!App.Webcam.IsRunning && !App.Webcam.Start()) return false;
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

        ClearTarget();
        App.GazeCursor?.SetLocked(false);
        _lastGazePoint = null;
        _faceLost = false;
        _cooldownUntil = DateTime.MinValue;

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

    private void HandleGazeMove(Point p)
    {
        _lastGazePoint = p;
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

            var hit = FindBestTarget(_lastGazePoint.Value);
            if (hit == null) return;

            // Cancel any in-progress dwell scaling on a different target.
            ClearTarget();

            if (hit.Value.Bubble is Bubble b)
            {
                try { b.Pop(); }
                catch (Exception ex) { App.Logger?.Debug("Gaze blink-pop bubble failed: {Error}", ex.Message); }
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
            // Cooldown after a successful pop — short window during which a
            // single sustained look can't chain-pop another nearby target.
            if (DateTime.UtcNow < _cooldownUntil)
            {
                ClearTarget();
                App.GazeCursor?.SetLocked(false);
                return;
            }

            if (_faceLost || !_lastGazePoint.HasValue)
            {
                ClearTarget();
                App.GazeCursor?.SetLocked(false);
                return;
            }

            var p = _lastGazePoint.Value;
            var hit = FindBestTarget(p);

            if (hit == null)
            {
                ClearTarget();
                App.GazeCursor?.SetLocked(false);
                return;
            }

            App.GazeCursor?.SetLocked(true);

            if (hit.Value.Bubble is Bubble b)
            {
                AdvanceBubbleDwell(b);
            }
            else if (hit.Value.Flash is FlashWindow fw)
            {
                AdvanceFlashDwell(fw);
            }
            else if (hit.Value.Floating is FloatingText ft)
            {
                AdvanceFloatingTextDwell(ft);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeFocusService tick error: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Picks the highest-scoring target across all candidates. Score is a
    /// Gaussian distance falloff plus additive bonuses for sticky lock and
    /// flash type. See the constant block at the top of the class for the
    /// model. Returns null if no target clears ScoreThreshold.
    /// </summary>
    private GazeHit? FindBestTarget(Point p)
    {
        Bubble? bestBubble = null;
        FlashWindow? bestFlash = null;
        FloatingText? bestFloating = null;
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
                var score = GaussianScore(dist, FlashScoreSigma) + FlashTypeBonus;
                if (ReferenceEquals(_currentFlash, fw)) score += StickyBonus;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFlash = fw;
                    bestBubble = null;
                    bestFloating = null;
                }
            }
        }

        var bubbles = App.Bubbles?.GetGazeTargets();
        if (bubbles != null)
        {
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var b = bubbles[i];
                var rect = b.GetGazeBounds();
                if (rect.IsEmpty) continue;
                if (!TargetOnCalScreen(rect, calScreen)) continue;
                var dist = DistanceFromRectEdge(rect, p);
                var score = GaussianScore(dist, BubbleScoreSigma);
                if (ReferenceEquals(_currentBubble, b)) score += StickyBonus;
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
                var score = GaussianScore(dist, BubbleScoreSigma);
                if (ReferenceEquals(_currentFloating, ft)) score += StickyBonus;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFloating = ft;
                    bestFlash = null;
                    bestBubble = null;
                }
            }
        }

        if (bestFlash != null) return new GazeHit(null, bestFlash, null);
        if (bestBubble != null) return new GazeHit(bestBubble, null, null);
        if (bestFloating != null) return new GazeHit(null, null, bestFloating);
        return null;
    }

    // Defensive clamp helper for the gaze read. Uses the rect's center for
    // screen lookup. WPF Window coordinates are in DIPs but Screen.FromPoint
    // takes pixels; on same-DPI multi-monitor setups these align, and on
    // mixed-DPI setups the center-based check is robust enough for monitors
    // that are visually distinct. Fail-open on errors (target considered
    // valid) since this is a defensive backstop, not the primary clamp.
    //
    // The PRIMARY defense is the spawn-write clamps via
    // GazeContentScreenPolicy and FlashService.PickMonitor — those execute
    // before any gaze content is positioned, so on multi-monitor systems
    // misplaced spawns should be impossible. This backstop catches the case
    // where a future consumer (or refactor) bypasses the policy helpers.
    // Known edge case: on mixed-DPI multi-monitor setups, rect-center
    // coordinates carry small drift between DIPs and pixels, which CAN cause
    // a misclassification when a target is positioned near a monitor edge.
    // If users on mixed-DPI hardware report stray gaze-clicks on off-screen
    // content, this is the suspect — but the spawn-write clamp should make
    // such a target rare to begin with.
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
        public GazeHit(Bubble? bubble, FlashWindow? flash, FloatingText? floating)
        {
            Bubble = bubble;
            Flash = flash;
            Floating = floating;
        }
        public Bubble? Bubble { get; }
        public FlashWindow? Flash { get; }
        public FloatingText? Floating { get; }
    }

    private void AdvanceBubbleDwell(Bubble b)
    {
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
            try { b.Pop(); }
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
            try { App.Flash?.GazePop(fw); }
            catch (Exception ex) { App.Logger?.Debug("Gaze flash pop failed: {Error}", ex.Message); }
            _currentFlash = null;
            _cooldownUntil = DateTime.UtcNow.AddMilliseconds(CooldownMs);
        }
    }

    private void AdvanceFloatingTextDwell(FloatingText ft)
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

    public void Dispose() => Stop();
}
