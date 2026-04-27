using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Translucent click-through "where am I looking" dot that follows the
/// calibrated gaze. Used for debug visualization — pulled out of
/// GazeFocusService so a standalone Lab toggle can show it without Focus
/// Gaze being on, and so both subsystems share a single on-screen cursor
/// rather than each rendering their own.
///
/// Reference-counted: callers Show/Hide with a string key; the cursor stays
/// visible while any key is active and disappears once all keys are
/// released. Auto-hides while the face is lost so the dot doesn't park at a
/// stale location.
/// </summary>
public class GazeDebugCursorService : IDisposable
{
    private const double CursorSize = 14;

    private static readonly Brush CursorIdleFill =
        new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0x69, 0xB4));
    private static readonly Brush CursorLockedFill =
        new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xD0, 0x80));
    private static readonly Brush CursorStroke =
        new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    private readonly HashSet<string> _requesters = new(StringComparer.Ordinal);
    private bool _subscribed;
    private bool _faceLost;
    private bool _locked;

    private Window? _cursorWindow;
    private Ellipse? _cursorDot;

    public GazeDebugCursorService()
    {
        // Same lifecycle hook KeywordHighlightService and GazeFocusService
        // use — close the cursor on app exit so the unowned window doesn't
        // keep the process alive under ShutdownMode=OnLastWindowClose.
        if (Application.Current != null)
            Application.Current.Exit += (_, _) => DisposeAll();
    }

    /// <summary>Caller wants the cursor visible while their key is active.</summary>
    public void Show(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _requesters.Add(key);
        UpdateVisibility();
    }

    public void Hide(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _requesters.Remove(key);
        UpdateVisibility();
    }

    public bool IsShowing(string key) => _requesters.Contains(key);

    /// <summary>Tints the cursor (Focus Gaze uses this to indicate lock-on).</summary>
    public void SetLocked(bool locked)
    {
        if (_cursorDot == null) { _locked = locked; return; }
        if (_locked == locked) return;
        _locked = locked;
        try { _cursorDot.Fill = locked ? CursorLockedFill : CursorIdleFill; }
        catch { }
    }

    private void UpdateVisibility()
    {
        if (_requesters.Count > 0)
            EnsureCursorAndSubscribe();
        else
            DisposeAll();
    }

    private void EnsureCursorAndSubscribe()
    {
        if (_cursorWindow == null)
        {
            try
            {
                _cursorDot = new Ellipse
                {
                    Width = CursorSize,
                    Height = CursorSize,
                    Fill = _locked ? CursorLockedFill : CursorIdleFill,
                    Stroke = CursorStroke,
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
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
                    // Park offscreen until the first gaze sample so we don't
                    // flash the cursor at (0, 0) on toggle-on.
                    Left = -10000,
                    Top = -10000,
                };
                _cursorWindow.Show();
                MakeClickThrough(_cursorWindow);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GazeDebugCursorService: cursor create failed: {Error}", ex.Message);
                _cursorWindow = null;
                _cursorDot = null;
                return;
            }
        }

        if (!_subscribed && App.Webcam != null)
        {
            App.Webcam.OnGazeMove += HandleGazeMove;
            App.Webcam.OnFaceLost += HandleFaceLost;
            App.Webcam.OnFaceFound += HandleFaceFound;
            _subscribed = true;
        }
    }

    private void DisposeAll()
    {
        if (_subscribed && App.Webcam != null)
        {
            App.Webcam.OnGazeMove -= HandleGazeMove;
            App.Webcam.OnFaceLost -= HandleFaceLost;
            App.Webcam.OnFaceFound -= HandleFaceFound;
            _subscribed = false;
        }
        if (_cursorWindow != null)
        {
            try { _cursorWindow.Close(); } catch { }
            _cursorWindow = null;
            _cursorDot = null;
        }
        _faceLost = false;
        _locked = false;
    }

    private void HandleGazeMove(Point p)
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

    private void HandleFaceLost()
    {
        _faceLost = true;
        if (_cursorWindow == null) return;
        try { _cursorWindow.Visibility = Visibility.Hidden; } catch { }
    }

    private void HandleFaceFound()
    {
        _faceLost = false;
        if (_cursorWindow == null) return;
        try { _cursorWindow.Visibility = Visibility.Visible; } catch { }
    }

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

    public void Dispose()
    {
        _requesters.Clear();
        DisposeAll();
    }
}
