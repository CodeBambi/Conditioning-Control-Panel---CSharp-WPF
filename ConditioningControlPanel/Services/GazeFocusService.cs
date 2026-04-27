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

    // Hit-test slack — how far outside a target's bounds the gaze can land
    // and still count as a hit. Closest target within slack wins, so a
    // generous slack mostly just means "always lock on to *something*
    // nearby" rather than misfire on the wrong target.
    private const double BubbleSlackDips = 120;
    private const double FlashSlackDips = 40;

    private DispatcherTimer? _timer;
    private Point? _lastGazePoint;
    private bool _faceLost;
    private DateTime _dwellStartedAt;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private bool _subscribed;

    // Mutually exclusive — only one target is being dwelt on at a time.
    private Bubble? _currentBubble;
    private FlashWindow? _currentFlash;

    // Key the shared GazeDebugCursorService uses to remember Focus Gaze
    // wants the dot visible. The Lab webcam-debug toggle uses its own key,
    // so the cursor stays up while either client wants it.
    private const string CursorKey = "focus-gaze";

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
        App.GazeCursor?.Show(CursorKey);

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
        App.GazeCursor?.Hide(CursorKey);
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

            var hit = FindClosestTarget(_lastGazePoint.Value);
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
                try { App.Flash?.GazePop(fw); }
                catch (Exception ex) { App.Logger?.Debug("Gaze blink-pop flash failed: {Error}", ex.Message); }
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
            var hit = FindClosestTarget(p);

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
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeFocusService tick error: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Picks the closest target to the gaze point within slack tolerance.
    /// Flashes are checked first; if any flash contains the point (slack 0),
    /// it wins. Otherwise the closest bubble within BubbleSlackDips wins.
    /// Returns null when nothing is in range.
    /// </summary>
    private GazeHit? FindClosestTarget(Point p)
    {
        // Flashes first — direct hits beat bubbles (foreground intent).
        var flashes = App.Flash?.GetGazeTargets();
        if (flashes != null)
        {
            FlashWindow? bestFlash = null;
            double bestDist = double.MaxValue;
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var fw = flashes[i];
                Rect rect;
                try { rect = new Rect(fw.Left, fw.Top, fw.Width, fw.Height); }
                catch { continue; }
                var dist = DistanceFromRectEdge(rect, p);
                if (dist <= FlashSlackDips && dist < bestDist)
                {
                    bestDist = dist;
                    bestFlash = fw;
                }
            }
            if (bestFlash != null) return new GazeHit(null, bestFlash);
        }

        var bubbles = App.Bubbles?.GetGazeTargets();
        if (bubbles != null)
        {
            Bubble? bestBubble = null;
            double bestDist = double.MaxValue;
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var b = bubbles[i];
                var rect = b.GetGazeBounds();
                if (rect.IsEmpty) continue;
                var dist = DistanceFromRectEdge(rect, p);
                if (dist <= BubbleSlackDips && dist < bestDist)
                {
                    bestDist = dist;
                    bestBubble = b;
                }
            }
            if (bestBubble != null) return new GazeHit(bestBubble, null);
        }

        return null;
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
        public GazeHit(Bubble? bubble, FlashWindow? flash) { Bubble = bubble; Flash = flash; }
        public Bubble? Bubble { get; }
        public FlashWindow? Flash { get; }
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

    private void AdvanceFlashDwell(FlashWindow fw)
    {
        if (!ReferenceEquals(_currentFlash, fw))
        {
            ClearTarget();
            _currentFlash = fw;
            _dwellStartedAt = DateTime.UtcNow;
        }

        var elapsedMs = (DateTime.UtcNow - _dwellStartedAt).TotalMilliseconds;
        var t = elapsedMs / DwellMs;
        fw.SetGazeDwellProgress(t);

        if (elapsedMs >= DwellMs)
        {
            try { App.Flash?.GazePop(fw); }
            catch (Exception ex) { App.Logger?.Debug("Gaze flash pop failed: {Error}", ex.Message); }
            _currentFlash = null;
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
    }

    public void Dispose() => Stop();
}
