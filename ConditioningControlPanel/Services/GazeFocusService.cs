using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
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

    // Debug gaze cursor (translucent click-through dot following the gaze).
    private Window? _cursorWindow;
    private Ellipse? _cursorDot;
    private bool _cursorLocked; // last-known lock state, drives color

    private static readonly Brush CursorIdleFill =
        new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0x69, 0xB4));
    private static readonly Brush CursorLockedFill =
        new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xD0, 0x80));
    private static readonly Brush CursorStroke =
        new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    public bool IsActive { get; private set; }
    public int DwellMs { get; set; } = DefaultDwellMs;

    /// <summary>Fires when IsActive flips, on the UI thread.</summary>
    public event Action<bool>? OnActiveChanged;

    public GazeFocusService()
    {
        // ShutdownMode=OnLastWindowClose means our cursor window keeps the
        // process alive after MainWindow closes. Close it on app-exit so the
        // window count drops to zero and shutdown can complete. Mirrors
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
        EnsureCursor();

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
        DisposeCursor();
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
        UpdateCursorPosition(p);
    }

    private void HandleFaceLost()
    {
        _faceLost = true;
        SetCursorVisible(false);
    }

    private void HandleFaceFound()
    {
        _faceLost = false;
        SetCursorVisible(true);
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
                SetCursorLocked(false);
                return;
            }

            if (_faceLost || !_lastGazePoint.HasValue)
            {
                ClearTarget();
                SetCursorLocked(false);
                return;
            }

            var p = _lastGazePoint.Value;
            var hit = FindClosestTarget(p);

            if (hit == null)
            {
                ClearTarget();
                SetCursorLocked(false);
                return;
            }

            SetCursorLocked(true);

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

    // ─── Debug gaze cursor ───────────────────────────────────────────────

    private const double CursorSize = 14;

    private void EnsureCursor()
    {
        if (_cursorWindow != null) return;
        try
        {
            _cursorDot = new Ellipse
            {
                Width = CursorSize,
                Height = CursorSize,
                Fill = CursorIdleFill,
                Stroke = CursorStroke,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            _cursorWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                Width = CursorSize,
                Height = CursorSize,
                Content = _cursorDot,
                // Park offscreen until the first gaze sample so we don't flash
                // the cursor at (0, 0) on toggle-on.
                Left = -10000,
                Top = -10000
            };
            _cursorWindow.Show();
            MakeClickThrough(_cursorWindow);
            _cursorLocked = false;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeFocusService cursor create failed: {Error}", ex.Message);
            _cursorWindow = null;
            _cursorDot = null;
        }
    }

    private void DisposeCursor()
    {
        if (_cursorWindow == null) return;
        try { _cursorWindow.Close(); } catch { }
        _cursorWindow = null;
        _cursorDot = null;
        _cursorLocked = false;
    }

    private void UpdateCursorPosition(Point p)
    {
        if (_cursorWindow == null) return;
        try
        {
            _cursorWindow.Left = p.X - CursorSize / 2;
            _cursorWindow.Top = p.Y - CursorSize / 2;
            if (_cursorWindow.Visibility != Visibility.Visible)
                _cursorWindow.Visibility = Visibility.Visible;
        }
        catch { }
    }

    private void SetCursorVisible(bool visible)
    {
        if (_cursorWindow == null) return;
        try { _cursorWindow.Visibility = visible ? Visibility.Visible : Visibility.Hidden; }
        catch { }
    }

    private void SetCursorLocked(bool locked)
    {
        if (_cursorDot == null) return;
        if (_cursorLocked == locked) return;
        _cursorLocked = locked;
        try { _cursorDot.Fill = locked ? CursorLockedFill : CursorIdleFill; }
        catch { }
    }

    // ─── Win32 click-through (mirrors BubbleService non-clickable path) ──

    private static void MakeClickThrough(Window w)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public void Dispose() => Stop();
}
