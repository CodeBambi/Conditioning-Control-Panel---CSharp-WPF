using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Translucent click-through "where am I looking" cursor that follows the
/// calibrated gaze. Used for debug visualization — pulled out of
/// GazeFocusService so a standalone Lab toggle can show it without Focus
/// Gaze being on, and so both subsystems share a single on-screen cursor
/// rather than each rendering their own.
///
/// Rendered on a single full-stage Skia overlay (same CPU SKElement pattern
/// as ChaosSkiaFxOverlay — SKElement rasterizes into a WriteableBitmap that
/// composites fine on an AllowsTransparency window): a soft-bloom dot plus
/// an additive comet trail of the recent gaze path. This replaces the old
/// tiny per-frame-repositioned Ellipse window — moving a layered window
/// every frame was a SetWindowPos per sample; the overlay stays put and
/// only repaints.
///
/// Reference-counted: callers Show/Hide with a string key; the cursor stays
/// visible while any key is active and disappears once all keys are
/// released. Auto-hides while the face is lost so the dot doesn't park at a
/// stale location.
/// </summary>
public class GazeDebugCursorService : IDisposable
{
    private const float DotRadius = 8f;          // core dot radius (DIPs)
    private const float TrailLifeSec = 0.55f;    // trail node lifetime
    private const int MaxTrailNodes = 64;
    private const float TrailMinStepDips = 3f;   // new node only after this much travel

    // Idle pink / locked warm gold — same palette the old Ellipse fills used.
    private static readonly SKColor IdleBody = new(0xFF, 0x69, 0xB4);
    private static readonly SKColor LockedBody = new(0xFF, 0xD0, 0x80);
    private static readonly SKColor CoreWhite = new(0xFF, 0xFF, 0xFF);

    private readonly HashSet<string> _requesters = new(StringComparer.Ordinal);
    private bool _subscribed;
    private bool _faceLost;
    private bool _locked;

    private Window? _overlay;
    private IntPtr _hwnd;
    private SKElement? _sk;
    private bool _rendering;
    private TimeSpan _lastRender = TimeSpan.MinValue;
    private TimeSpan _lastTopmostAssert = TimeSpan.MinValue;
    private double _dpiX = 1, _dpiY = 1;

    private Point _pos;
    private bool _hasPos;
    private Point _lastPaintedPos;
    private bool _needsPaint;

    private struct TrailNode { public float X, Y, Age; }
    private readonly List<TrailNode> _trail = new(MaxTrailNodes);

    // Soft radial sprite for the dot bloom (shared, built once) + cached
    // tint filters so the paint path never allocates per frame.
    //
    // #912: every one of these used to be a field initializer, so a missing or unloadable
    // libSkiaSharp.dll threw out of the constructor and killed the launch — for a DEBUG cursor.
    // They are built lazily behind EnsureSkiaResources() instead, and the service degrades to
    // "no cursor" when Skia is unavailable.
    private static SKImage? _dotSprite;
    private static bool _dotSpriteFailed;
    private static SKColorFilter? _idleCF;
    private static SKColorFilter? _lockedCF;
    private static SKColorFilter? _whiteCF;
    private SKPaint? _spritePaint;
    private SKPaint? _trailGlow;
    private SKPaint? _trailCore;
    private SKPaint? _rimPaint;
    private bool _skiaUnavailable;

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
        if (_locked == locked) return;
        _locked = locked;
        _needsPaint = true;
    }

    private void UpdateVisibility()
    {
        if (_requesters.Count > 0)
            EnsureOverlayAndSubscribe();
        else
            DisposeAll();
    }

    /// <summary>
    /// Builds the Skia paints/filters on first use. Returns false — permanently, for this session —
    /// when the native Skia library isn't usable, so the cursor simply never appears instead of
    /// throwing out of a constructor or a render callback (#912).
    /// </summary>
    private bool EnsureSkiaResources()
    {
        if (_skiaUnavailable) return false;
        if (_spritePaint != null) return true;

        try
        {
            _spritePaint = new SKPaint { BlendMode = SKBlendMode.Plus, IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
            _trailGlow = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f) };
            _trailCore = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            _rimPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.6f };
            _idleCF ??= SKColorFilter.CreateBlendMode(IdleBody, SKBlendMode.Modulate);
            _lockedCF ??= SKColorFilter.CreateBlendMode(LockedBody, SKBlendMode.Modulate);
            _whiteCF ??= SKColorFilter.CreateBlendMode(CoreWhite, SKBlendMode.Modulate);
            return true;
        }
        catch (Exception ex)
        {
            _skiaUnavailable = true;
            // A throw part-way through leaves the paints built so far holding native SKPaint
            // (and a blur SKMaskFilter) handles — dropping the references without disposing
            // them leaks that memory for the session.
            DisposePaints();
            App.Logger?.Warning(
                "GazeDebugCursorService: Skia is unavailable ({Error}) — the gaze debug cursor is disabled this session",
                ex.Message);
            return false;
        }
    }

    private void EnsureOverlayAndSubscribe()
    {
        if (!EnsureSkiaResources()) return;

        if (_overlay == null)
        {
            try
            {
                _sk = new SKElement { IsHitTestVisible = false };
                _sk.PaintSurface += OnPaintSurface;
                _overlay = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Focusable = false,
                    IsHitTestVisible = false,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = SystemParameters.VirtualScreenLeft,
                    Top = SystemParameters.VirtualScreenTop,
                    Width = Math.Max(1, SystemParameters.VirtualScreenWidth),
                    Height = Math.Max(1, SystemParameters.VirtualScreenHeight),
                    Content = _sk,
                };
                _overlay.SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(_overlay).Handle; MakeClickThrough(_overlay); CacheDpi(); };
                _overlay.Show();
                _hasPos = false;
                _trail.Clear();
                StartRendering();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("GazeDebugCursorService: overlay create failed: {Error}", ex.Message);
                _overlay = null;
                _sk = null;
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
        StopRendering();
        if (_overlay != null)
        {
            try { _overlay.Close(); } catch { }
            _overlay = null;
            _sk = null;
            _hwnd = IntPtr.Zero;
            _lastTopmostAssert = TimeSpan.MinValue;
        }
        _trail.Clear();
        _hasPos = false;
        _faceLost = false;
        _locked = false;
        // Native Skia objects, and this is the only teardown the service has: the cursor can be
        // shown and released many times a session. EnsureSkiaResources rebuilds them on the next
        // Show (the cached colour filters are static and deliberately outlive this).
        DisposePaints();
    }

    private void DisposePaints()
    {
        try { _spritePaint?.Dispose(); } catch { }
        try { _trailGlow?.Dispose(); } catch { }
        try { _trailCore?.Dispose(); } catch { }
        try { _rimPaint?.Dispose(); } catch { }
        _spritePaint = null;
        _trailGlow = null;
        _trailCore = null;
        _rimPaint = null;
    }

    private void HandleGazeMove(Point p)
    {
        _pos = p;
        _hasPos = true;
    }

    private void HandleFaceLost()
    {
        _faceLost = true;
        _trail.Clear();
        _needsPaint = true;
    }

    private void HandleFaceFound()
    {
        _faceLost = false;
        _needsPaint = true;
    }

    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _lastRender = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        _rendering = false;
        try { CompositionTarget.Rendering -= OnRendering; } catch { }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        try
        {
            if (_overlay == null || _sk == null) return;

            float dt = 1f / 60f;
            if (e is RenderingEventArgs r)
            {
                if (_lastRender == TimeSpan.MinValue) { _lastRender = r.RenderingTime; }
                else
                {
                    dt = (float)(r.RenderingTime - _lastRender).TotalSeconds;
                    _lastRender = r.RenderingTime;
                }
                if (dt <= 0) return;
                if (dt > 0.1f) dt = 0.1f;

                // Re-assert topmost periodically. The overlay sets Topmost once,
                // but the video windows (and their attention-check targets) call
                // SetWindowPos(HWND_TOPMOST) continuously, which shuffles them
                // above us in the topmost band and buries the debug dot. A light
                // ~200ms nudge back to the top keeps the cursor visible over
                // video without high-frequency z-order thrash. SWP_NOACTIVATE so
                // we never steal focus.
                if (_hwnd != IntPtr.Zero
                    && (_lastTopmostAssert == TimeSpan.MinValue
                        || (r.RenderingTime - _lastTopmostAssert).TotalMilliseconds >= 200))
                {
                    _lastTopmostAssert = r.RenderingTime;
                    try { SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); } catch { }
                }
            }

            // Age + cull trail nodes.
            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                var n = _trail[i];
                n.Age += dt;
                if (n.Age >= TrailLifeSec) _trail.RemoveAt(i);
                else _trail[i] = n;
            }

            // Feed the trail from the latest cursor position (overlay-local).
            if (_hasPos && !_faceLost)
            {
                float lx = (float)(_pos.X - _overlay.Left);
                float ly = (float)(_pos.Y - _overlay.Top);
                bool add = _trail.Count == 0;
                if (!add)
                {
                    var last = _trail[_trail.Count - 1];
                    float dx = lx - last.X, dy = ly - last.Y;
                    add = dx * dx + dy * dy >= TrailMinStepDips * TrailMinStepDips;
                }
                if (add)
                {
                    if (_trail.Count >= MaxTrailNodes) _trail.RemoveAt(0);
                    _trail.Add(new TrailNode { X = lx, Y = ly, Age = 0f });
                    _needsPaint = true;
                }
            }

            // Repaint only when something changed — a static dot with a dead
            // trail costs nothing while the user fixates.
            if (_trail.Count > 0) _needsPaint = true;
            if (_hasPos && (_pos != _lastPaintedPos)) _needsPaint = true;

            if (_needsPaint)
            {
                _needsPaint = false;
                _lastPaintedPos = _pos;
                _sk.InvalidateVisual();
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("GazeDebugCursorService render tick error: {Error}", ex.Message);
        }
    }

    /// <summary>Soft white radial sprite, tinted per draw (ChaosSkiaFxOverlay.Dot pattern).</summary>
    private static SKImage? DotSprite()
    {
        if (_dotSprite != null) return _dotSprite;
        if (_dotSpriteFailed) return null;
        try
        {
            return _dotSprite = BuildDotSprite();
        }
        catch (Exception ex)
        {
            _dotSpriteFailed = true;
            App.Logger?.Warning("GazeDebugCursorService: dot sprite could not be built: {Error}", ex.Message);
            return null;
        }
    }

    private static SKImage BuildDotSprite()
    {
        const int s = 128;
        var info = new SKImageInfo(s, s, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surf = SKSurface.Create(info);
        var c = surf.Canvas;
        c.Clear(SKColors.Transparent);
        float r = s / 2f;
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(r, r), r,
            new[] { new SKColor(255, 255, 255, 255), new SKColor(255, 255, 255, 150), new SKColor(255, 255, 255, 0) },
            new[] { 0f, 0.35f, 1f }, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader, IsAntialias = true };
        c.DrawCircle(r, r, r, p);
        return surf.Snapshot();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (!_hasPos || _faceLost || _overlay == null) return;
        if (!EnsureSkiaResources()) return;

        canvas.Save();
        canvas.Scale((float)_dpiX, (float)_dpiY);   // draw in window-local DIPs

        var body = _locked ? LockedBody : IdleBody;

        // Comet trail: consecutive nodes as additive strokes, width and
        // alpha tapering with age (newest = fat and bright, oldest = wisp).
        if (_trail.Count >= 2)
        {
            for (int i = 1; i < _trail.Count; i++)
            {
                var a = _trail[i - 1];
                var b = _trail[i];
                float t = 1f - b.Age / TrailLifeSec;    // 1 fresh → 0 dead
                if (t <= 0f) continue;
                float ease = t * t;
                _trailGlow!.Color = body.WithAlpha((byte)(70 * ease));
                _trailGlow.StrokeWidth = 2.5f + 7.5f * ease;
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _trailGlow);
                _trailCore!.Color = body.WithAlpha((byte)(140 * ease));
                _trailCore.StrokeWidth = 1f + 2.4f * ease;
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _trailCore);
            }
        }

        // The cursor itself: wide bloom + bright body + hot core + a thin
        // white rim so it stays readable over busy/light content (the rim
        // is normal alpha blending, not additive — additive vanishes on
        // white backgrounds).
        float cx = (float)(_pos.X - _overlay.Left);
        float cy = (float)(_pos.Y - _overlay.Top);
        var sprite = DotSprite();
        var bodyCF = _locked ? _lockedCF : _idleCF;
        if (sprite != null)
        {
            DrawSprite(canvas, sprite, cx, cy, DotRadius * 3.2f, 60, bodyCF);
            DrawSprite(canvas, sprite, cx, cy, DotRadius * 1.6f, 190, bodyCF);
            DrawSprite(canvas, sprite, cx, cy, DotRadius * 0.7f, 220, _whiteCF);
        }
        _rimPaint!.Color = CoreWhite.WithAlpha(210);
        canvas.DrawCircle(cx, cy, DotRadius, _rimPaint);

        canvas.Restore();
    }

    private void DrawSprite(SKCanvas canvas, SKImage img, float cx, float cy, float radius, byte alpha, SKColorFilter? tint)
    {
        if (_spritePaint == null) return;
        _spritePaint.ColorFilter = tint;
        _spritePaint.Color = new SKColor(255, 255, 255, alpha);
        var dest = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        canvas.DrawImage(img, dest, _spritePaint);
    }

    private void CacheDpi()
    {
        try
        {
            var m = _overlay != null
                ? PresentationSource.FromVisual(_overlay)?.CompositionTarget?.TransformToDevice
                : null;
            if (m is Matrix mm && mm.M11 > 0 && mm.M22 > 0) { _dpiX = mm.M11; _dpiY = mm.M22; }
        }
        catch { }
    }

    private static void MakeClickThrough(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
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

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public void Dispose()
    {
        _requesters.Clear();
        DisposeAll();
    }
}
