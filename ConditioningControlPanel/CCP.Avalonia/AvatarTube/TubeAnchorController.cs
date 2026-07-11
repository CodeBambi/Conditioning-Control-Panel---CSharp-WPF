using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    /// <summary>
    /// Single-responsibility windowing controller for the attached avatar tube
    /// (ground-up rewrite, 2026-07: the incremental patches to the old windowing
    /// partial kept regressing "small / too high / static / laggy").
    ///
    /// Ownership contract:
    ///  - This class is the ONLY writer of the tube window's <see cref="Window.Position"/>.
    ///  - This class is the ONLY writer of the ContentViewbox Width/Height.
    ///  - It owns all parent-window follow subscriptions (PositionChanged, SizeChanged,
    ///    WindowState/IsVisible property changes, Activated, Closed) plus, on Windows,
    ///    a WndProc hook on the PARENT window so the tube repositions inside the OS
    ///    move loop (WM_MOVING) instead of one managed event behind it.
    ///
    /// Sizing model (WPF contract + the new scale-with-main-window requirement):
    ///  - screenScale = clamp(min(0.85*workH/1020, 0.3*workW/780), 0.4, 1.0), computed
    ///    once per (re)calibration from the PRIMARY screen working area in LOGICAL units.
    ///    This is both the "default look" anchor and a hard cap.
    ///  - parentRatio = parent.ClientSize.Height / ReferenceParentHeight while attached.
    ///  - finalScale  = clamp(screenScale * parentRatio, 0.30, screenScale).
    ///  - effective   = finalScale * userScale (detached zoom; 1.0 while attached).
    ///  - ContentViewbox is sized to 780x1020 * effective; the window is pinned to the
    ///    viewbox size (never to a transient auto-size ClientSize), so the speech bubble
    ///    can never resize the window.
    ///
    /// Anchor model (physical px): the tube overlaps the parent's left edge by
    /// 350*finalScale and is vertically centered on the parent client area +20*finalScale.
    /// The old WPF logical -500/5000 bounds guard is intentionally GONE - it silently
    /// swallowed valid positions in physical-pixel space and stranded the tube ("static").
    /// The only skip conditions are genuinely transient parent geometry (minimized,
    /// zero client size, or the Win32 -32000 minimized parking sentinel), with a bounded
    /// one-shot retry so a settling parent always gets re-anchored.
    /// </summary>
    public sealed class TubeAnchorController : IDisposable
    {
        // ===== Design constants (WPF ground truth) =====
        private const double DesignWidth = 780;
        private const double DesignHeight = 1020;
        private const double BaseOffsetFromParent = -350; // negative = overlap into the parent
        private const double VerticalOffset = 20;

        /// <summary>
        /// The main window's DEFAULT height. MainWindow.axaml declares Height="1000"
        /// (Views/MainWindow.axaml L15), so at the default main-window size
        /// parentRatio == ~1.0 and finalScale == screenScale - i.e. the tube looks
        /// exactly like the pre-rewrite screen-fit sizing. Growing/shrinking the main
        /// window scales the tube proportionally around that anchor.
        /// </summary>
        private const double ReferenceParentHeight = 1000.0;

        /// <summary>Hard floor so a tiny main window never shrinks the tube into an unreadable sliver.</summary>
        private const double AbsoluteMinScale = 0.30;

        /// <summary>Win32 parks minimized windows at (-32000,-32000); treat anything at or beyond -30000 as transient.</summary>
        private const int MinimizedSentinel = -30000;

        private const int MaxAnchorRetries = 3;

        private const uint WM_MOVING = 0x0216;
        private const uint WM_WINDOWPOSCHANGED = 0x0047;

        // ===== Wiring =====
        private readonly Window _tube;
        private readonly Viewbox _contentViewbox;
        private readonly Window? _parent;
        private readonly ILogger? _logger;

        // ===== Scale state =====
        private double _screenScale = 0.7;
        private double _parentRatio = 1.0;
        private double _finalScale = 0.7;
        private double _userScale = 1.0; // detached zoom; ignored while attached
        private bool _attached = true;

        // ===== Retry (transient parent geometry only) =====
        private DispatcherTimer? _retryTimer;
        private int _retryCount;

        // ===== Win32 parent-move hook =====
        // The delegate is held in a field so it cannot be collected while registered
        // (Win32Properties only keeps the reference we hand it).
        private Win32Properties.CustomWndProcHookCallback? _wndProcHook;
        private bool _moveHookActive;

        private bool _started;
        private bool _disposed;

        // First-fire telemetry flags (phase-0 requirement: every subscription logs its first fire).
        private bool _firstPositionChangedLogged;
        private bool _firstSizeChangedLogged;
        private bool _firstStateChangedLogged;
        private bool _firstVisibleChangedLogged;
        private bool _firstActivatedLogged;
        private bool _firstMoveHookLogged;

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>Raised after the parent fires Activated (controller has already re-anchored).</summary>
        public event EventHandler? ParentActivated;

        /// <summary>Raised when the parent's WindowState changes (controller has already re-anchored when valid).</summary>
        public event EventHandler<WindowState>? ParentWindowStateChanged;

        /// <summary>Raised when the parent's IsVisible changes.</summary>
        public event EventHandler<bool>? ParentIsVisibleChanged;

        /// <summary>Raised when the parent window closes.</summary>
        public event EventHandler? ParentClosed;

        /// <summary>Current composed scale (screen fit x parent ratio, clamped). Excludes detached user zoom.</summary>
        public double FinalScale => _finalScale;

        /// <summary>Screen-fit scale (WPF CalculateScaleFactor parity). Also the upper cap for FinalScale.</summary>
        public double ScreenScale => _screenScale;

        public TubeAnchorController(Window tube, Viewbox contentViewbox, Window? parent, ILogger? logger)
        {
            _tube = tube ?? throw new ArgumentNullException(nameof(tube));
            _contentViewbox = contentViewbox ?? throw new ArgumentNullException(nameof(contentViewbox));
            _parent = parent;
            _logger = logger;
        }

        // =====================================================================
        // Lifecycle
        // =====================================================================

        /// <summary>
        /// Wires all parent follow subscriptions. Idempotent; call once after construction.
        /// </summary>
        public void Start()
        {
            if (_started || _disposed) return;
            _started = true;

            if (_parent == null)
            {
                _logger?.LogDebug("TubeAnchor: no parent window - follow subscriptions skipped (detached-only tube)");
                return;
            }

            _parent.PositionChanged += OnParentPositionChanged;
            _logger?.LogDebug("TubeAnchor: wired parent.PositionChanged");
            _parent.SizeChanged += OnParentSizeChanged;
            _logger?.LogDebug("TubeAnchor: wired parent.SizeChanged");
            _parent.PropertyChanged += OnParentPropertyChanged;
            _logger?.LogDebug("TubeAnchor: wired parent.PropertyChanged (WindowState + IsVisible)");
            _parent.Activated += OnParentActivated;
            _logger?.LogDebug("TubeAnchor: wired parent.Activated");
            _parent.Closed += OnParentClosed;
            _logger?.LogDebug("TubeAnchor: wired parent.Closed");

            TryRegisterParentMoveHook();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _retryTimer?.Stop();
            _retryTimer = null;

            if (_parent != null)
            {
                _parent.PositionChanged -= OnParentPositionChanged;
                _parent.SizeChanged -= OnParentSizeChanged;
                _parent.PropertyChanged -= OnParentPropertyChanged;
                _parent.Activated -= OnParentActivated;
                _parent.Closed -= OnParentClosed;
            }

            if (_moveHookActive && _wndProcHook != null && _parent != null)
            {
                try
                {
                    Win32Properties.RemoveWndProcHookCallback(_parent, _wndProcHook);
                }
                catch (Exception ex)
                {
                    // The _disposed flag already makes the callback a no-op, so a failed
                    // removal is harmless (observe-only hook on a window we don't own).
                    _logger?.LogDebug("TubeAnchor: WndProc hook removal failed ({Error})", ex.Message);
                }
            }
            _moveHookActive = false;
            _wndProcHook = null;
        }

        // =====================================================================
        // Mode / user-zoom inputs
        // =====================================================================

        /// <summary>Attached = follow + parent-ratio sizing; detached = free-floating + user zoom.</summary>
        public void SetAttached(bool attached)
        {
            _attached = attached;
            _logger?.LogDebug("TubeAnchor: SetAttached({Attached})", attached);
            ApplySizing();
            if (attached) UpdatePosition();
        }

        /// <summary>Detached user zoom (window-side grow/shrink menu + Ctrl-wheel). 1.0 while attached.</summary>
        public void SetUserScale(double userScale)
        {
            if (userScale <= 0 || double.IsNaN(userScale)) return;
            _userScale = userScale;
            ApplySizing();
            if (!_attached) ClampToScreen();
        }

        // =====================================================================
        // Sizing (single writer of ContentViewbox.Width/Height)
        // =====================================================================

        /// <summary>
        /// WPF CalculateScaleFactor parity: screen-fit scale from the PRIMARY screen
        /// working area converted to logical units (physical / Screen.Scaling).
        /// </summary>
        public void RecomputeScreenScale()
        {
            try
            {
                var screen = _tube.Screens.Primary ?? _tube.Screens.ScreenFromWindow(_tube);
                if (screen != null)
                {
                    double scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
                    double workH = screen.WorkingArea.Height / scaling;
                    double workW = screen.WorkingArea.Width / scaling;
                    double maxHeightScale = (workH * 0.85) / DesignHeight;
                    double maxWidthScale = (workW * 0.3) / DesignWidth;
                    _screenScale = Math.Max(0.4, Math.Min(1.0, Math.Min(maxHeightScale, maxWidthScale)));
                }
                else
                {
                    _screenScale = 0.7;
                }
            }
            catch
            {
                _screenScale = 0.7;
            }
            _logger?.LogDebug("TubeAnchor: RecomputeScreenScale -> {ScreenScale:F3}", _screenScale);
        }

        /// <summary>
        /// Recomputes finalScale from the live parent client height and writes the
        /// ContentViewbox size. The ONLY writer of ContentViewbox.Width/Height.
        /// </summary>
        public void ApplySizing()
        {
            double parentRatio = 1.0;
            if (_attached && _parent != null
                && _parent.WindowState != WindowState.Minimized
                && _parent.ClientSize.Height > 0)
            {
                parentRatio = _parent.ClientSize.Height / ReferenceParentHeight;
            }
            _parentRatio = parentRatio;
            _finalScale = Math.Clamp(_screenScale * parentRatio, AbsoluteMinScale, _screenScale);

            double effective = _finalScale * (_attached ? 1.0 : _userScale);
            double w = DesignWidth * effective;
            double h = DesignHeight * effective;

            bool changed = double.IsNaN(_contentViewbox.Width) || double.IsNaN(_contentViewbox.Height)
                           || Math.Abs(_contentViewbox.Width - w) > 0.1
                           || Math.Abs(_contentViewbox.Height - h) > 0.1;
            if (changed)
            {
                _contentViewbox.Width = w;
                _contentViewbox.Height = h;
            }

            _logger?.LogDebug(
                "TubeAnchor: ApplySizing attached={Attached} screenScale={ScreenScale:F3} parentRatio={ParentRatio:F3} finalScale={FinalScale:F3} userScale={UserScale:F2} viewbox={W:F1}x{H:F1} changed={Changed}",
                _attached, _screenScale, parentRatio, _finalScale, _userScale, w, h, changed);
        }

        /// <summary>
        /// WPF-parity window pin (ContentViewbox.SizeChanged): the tube window is a FIXED
        /// size equal to the deterministic viewbox size, so bubble/content churn can never
        /// resize it. Only active after the first-render freeze to Manual.
        /// </summary>
        public void PinWindowToContent()
        {
            if (_tube.SizeToContent != SizeToContent.Manual) return;
            double w = _contentViewbox.Width, h = _contentViewbox.Height;
            if (double.IsNaN(w) || w <= 0 || double.IsNaN(h) || h <= 0) return;
            if (double.IsNaN(_tube.Width) || Math.Abs(_tube.Width - w) > 0.5) _tube.Width = w;
            if (double.IsNaN(_tube.Height) || Math.Abs(_tube.Height - h) > 0.5) _tube.Height = h;
        }

        /// <summary>
        /// First-render freeze: switch off auto-size ONCE and pin the window to the
        /// deterministic viewbox size (NOT the transient ClientSize - a greeting bubble
        /// may already have inflated the auto-sized window by first render, which is the
        /// "renders smaller + too high" letterbox bug). SizeToContent must be set to
        /// Manual BEFORE assigning Width/Height (assignments are discarded while
        /// auto-sizing is active).
        /// </summary>
        public void FreezeWindowSize()
        {
            double w = _contentViewbox.Width, h = _contentViewbox.Height;
            if (double.IsNaN(w) || w <= 0 || double.IsNaN(h) || h <= 0)
            {
                w = _tube.ClientSize.Width;
                h = _tube.ClientSize.Height;
            }
            _tube.SizeToContent = SizeToContent.Manual;
            if (w > 0 && h > 0)
            {
                _tube.Width = w;
                _tube.Height = h;
            }
            _logger?.LogDebug("TubeAnchor: FreezeWindowSize -> {W:F1}x{H:F1} (client was {CW:F1}x{CH:F1})",
                w, h, _tube.ClientSize.Width, _tube.ClientSize.Height);
        }

        // =====================================================================
        // Anchoring (single writer of Window.Position)
        // =====================================================================

        /// <summary>Re-anchors the tube to the parent's left edge. Safe to call any time.</summary>
        public void UpdatePosition() => UpdatePositionCore(null, "explicit");

        private void UpdatePositionCore(PixelPoint? parentOriginOverride, string trigger)
        {
            if (_disposed) return;
            if (!_attached || _parent == null)
            {
                _logger?.LogDebug("TubeAnchor: UpdatePosition[{Trigger}] skip guard=not-attached", trigger);
                return;
            }

            var parentPos = parentOriginOverride ?? _parent.Position;
            var parentClient = _parent.ClientSize;

            // GUARDS - only genuinely transient/invalid parent geometry may skip a write.
            // (The WPF logical -500/5000 window was copied into physical space by the old
            // port and silently rejected valid anchors -> "tube goes static". Never again.)
            string? guard = null;
            bool transient = false;
            if (_parent.WindowState == WindowState.Minimized)
            {
                guard = "parent-minimized";
            }
            else if (parentClient.Width <= 0 || parentClient.Height <= 0)
            {
                guard = "parent-clientsize-empty";
                transient = true;
            }
            else if (parentPos.X <= MinimizedSentinel || parentPos.Y <= MinimizedSentinel)
            {
                guard = "parent-position-sentinel";
                transient = true;
            }

            if (guard != null)
            {
                _logger?.LogDebug(
                    "TubeAnchor: UpdatePosition[{Trigger}] skip guard={Guard} parentPos={PPos} parentClient={PW:F0}x{PH:F0} retry={Retry}/{Max}",
                    trigger, guard, parentPos, parentClient.Width, parentClient.Height, _retryCount, MaxAnchorRetries);
                if (transient) ScheduleAnchorRetry();
                return;
            }

            double tubeW = _contentViewbox.Width;
            double tubeH = _contentViewbox.Height;
            if (double.IsNaN(tubeW) || tubeW <= 0 || double.IsNaN(tubeH) || tubeH <= 0)
            {
                // Viewbox not sized yet (pre-first ApplySizing) - size it now, then re-read.
                ApplySizing();
                tubeW = _contentViewbox.Width;
                tubeH = _contentViewbox.Height;
                if (double.IsNaN(tubeW) || tubeW <= 0 || double.IsNaN(tubeH) || tubeH <= 0)
                {
                    _logger?.LogDebug("TubeAnchor: UpdatePosition[{Trigger}] skip guard=viewbox-unsized", trigger);
                    return;
                }
            }

            // Physical px throughout. Window.Position is physical; ClientSize and the
            // viewbox/design values are logical -> convert via the parent's RenderScaling
            // (the attached tube shares the parent's monitor).
            double s = _parent.RenderScaling;
            double scaledOffset = BaseOffsetFromParent * _finalScale;
            int newLeft = parentPos.X - (int)Math.Round((tubeW + scaledOffset) * s);
            int newTop = parentPos.Y + (int)Math.Round(((parentClient.Height - tubeH) / 2 + VerticalOffset * _finalScale) * s);

            _retryCount = 0;
            _retryTimer?.Stop();

            var newPos = new PixelPoint(newLeft, newTop);
            if (_tube.Position != newPos)
                _tube.Position = newPos;

            _logger?.LogDebug(
                "TubeAnchor: UpdatePosition[{Trigger}] attached={Attached} parentPos={PPos} parentClient={PW:F0}x{PH:F0} rs={Rs:F2} screenScale={Ss:F3} parentRatio={Pr:F3} finalScale={Fs:F3} viewbox={VW:F1}x{VH:F1} window={WW:F1}x{WH:F1} new=({NL},{NT})",
                trigger, _attached, parentPos, parentClient.Width, parentClient.Height, s,
                _screenScale, _parentRatio, _finalScale, tubeW, tubeH, _tube.Width, _tube.Height, newLeft, newTop);
        }

        private void ScheduleAnchorRetry()
        {
            if (_disposed || _retryCount >= MaxAnchorRetries) return;
            _retryCount++;
            if (_retryTimer == null)
            {
                _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _retryTimer.Tick += (_, _) =>
                {
                    _retryTimer?.Stop();
                    UpdatePositionCore(null, "retry");
                };
            }
            _retryTimer.Stop();
            _retryTimer.Start();
        }

        // =====================================================================
        // Detached positioning (drag + clamp) - keeps single-writer discipline
        // =====================================================================

        /// <summary>Moves the tube to an explicit position (detached drag), clamped to the working area.</summary>
        public void MoveTo(PixelPoint position)
        {
            if (_disposed) return;
            var clamped = ClampToWorkingArea(position);
            if (_tube.Position != clamped)
                _tube.Position = clamped;
        }

        /// <summary>Clamps the tube's current position into the working area of its screen.</summary>
        public void ClampToScreen()
        {
            if (_disposed) return;
            var clamped = ClampToWorkingArea(_tube.Position);
            if (_tube.Position != clamped)
                _tube.Position = clamped;
        }

        private PixelPoint ClampToWorkingArea(PixelPoint pos)
        {
            try
            {
                var screen = _tube.Screens.ScreenFromWindow(_tube) ?? _tube.Screens.Primary;
                if (screen == null) return pos;
                var wa = screen.WorkingArea; // physical px
                double s = _tube.RenderScaling;
                double wLogical = !double.IsNaN(_tube.Width) && _tube.Width > 0 ? _tube.Width : _tube.ClientSize.Width;
                double hLogical = !double.IsNaN(_tube.Height) && _tube.Height > 0 ? _tube.Height : _tube.ClientSize.Height;
                int w = Math.Max(1, (int)Math.Round(wLogical * s));
                int h = Math.Max(1, (int)Math.Round(hLogical * s));
                int maxX = Math.Max(wa.X, wa.Right - w);
                int maxY = Math.Max(wa.Y, wa.Bottom - h);
                return new PixelPoint(Math.Clamp(pos.X, wa.X, maxX), Math.Clamp(pos.Y, wa.Y, maxY));
            }
            catch
            {
                return pos;
            }
        }

        // =====================================================================
        // Parent follow event handlers (all synchronous - no Dispatcher.Post in the follow path)
        // =====================================================================

        private void OnParentPositionChanged(object? sender, PixelPointEventArgs e)
        {
            if (!_firstPositionChangedLogged)
            {
                _firstPositionChangedLogged = true;
                _logger?.LogDebug("TubeAnchor: first parent.PositionChanged fire ({Pos})", e.Point);
            }
            UpdatePositionCore(null, "position-changed");
        }

        private void OnParentSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (!_firstSizeChangedLogged)
            {
                _firstSizeChangedLogged = true;
                _logger?.LogDebug("TubeAnchor: first parent.SizeChanged fire ({Size})", e.NewSize);
            }
            // Resize -> re-derive the tube scale from the new parent height, then re-anchor.
            ApplySizing();
            UpdatePositionCore(null, "size-changed");
        }

        private void OnParentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty)
            {
                var state = _parent?.WindowState ?? WindowState.Normal;
                if (!_firstStateChangedLogged)
                {
                    _firstStateChangedLogged = true;
                    _logger?.LogDebug("TubeAnchor: first parent WindowState fire ({State})", state);
                }
                if (state != WindowState.Minimized)
                {
                    ApplySizing();
                    UpdatePositionCore(null, "state-changed");
                }
                ParentWindowStateChanged?.Invoke(this, state);
            }
            else if (e.Property == Visual.IsVisibleProperty)
            {
                bool visible = e.NewValue is true;
                if (!_firstVisibleChangedLogged)
                {
                    _firstVisibleChangedLogged = true;
                    _logger?.LogDebug("TubeAnchor: first parent IsVisible fire ({Visible})", visible);
                }
                if (visible)
                    UpdatePositionCore(null, "visible-changed");
                ParentIsVisibleChanged?.Invoke(this, visible);
            }
        }

        private void OnParentActivated(object? sender, EventArgs e)
        {
            if (!_firstActivatedLogged)
            {
                _firstActivatedLogged = true;
                _logger?.LogDebug("TubeAnchor: first parent.Activated fire");
            }
            UpdatePositionCore(null, "activated");
            ParentActivated?.Invoke(this, EventArgs.Empty);
        }

        private void OnParentClosed(object? sender, EventArgs e)
        {
            ParentClosed?.Invoke(this, EventArgs.Empty);
        }

        // =====================================================================
        // Windows fluid follow: WndProc hook on the PARENT window
        // =====================================================================

        /// <summary>
        /// Registers Avalonia's Win32Properties.AddWndProcHookCallback on the parent so
        /// WM_MOVING / WM_WINDOWPOSCHANGED reposition the tube inside the OS move loop
        /// (same compose pass - no managed-event lag). Win32Properties lives in the
        /// cross-platform Avalonia.Controls assembly in v12 (verified against 12.0.5,
        /// src/Avalonia.Controls/Platform/Win32Properties.cs) and internally no-ops when
        /// the platform impl is not Win32, but we still guard with OperatingSystem.IsWindows().
        /// Any failure falls back silently to the PositionChanged path.
        /// </summary>
        private void TryRegisterParentMoveHook()
        {
            if (!OperatingSystem.IsWindows() || _parent == null) return;
            try
            {
                // Held in a field so the callback delegate is never collected while registered.
                _wndProcHook = ParentWndProcHook;
                Win32Properties.AddWndProcHookCallback(_parent, _wndProcHook);
                _moveHookActive = true;
                _logger?.LogDebug("TubeAnchor: wired Win32 WndProc move hook on parent (WM_MOVING/WM_WINDOWPOSCHANGED)");
            }
            catch (Exception ex)
            {
                _wndProcHook = null;
                _moveHookActive = false;
                _logger?.LogDebug("TubeAnchor: WndProc hook registration failed ({Error}) - PositionChanged fallback", ex.Message);
            }
        }

        /// <summary>
        /// Observe-only WndProc hook on the PARENT window (signature matches
        /// Win32Properties.CustomWndProcHookCallback). Always returns Zero and never
        /// sets handled. WM_MOVING carries the PROPOSED window rect in lParam - anchoring
        /// off that rect tracks the drag with zero staleness; WM_WINDOWPOSCHANGED covers
        /// moves/sizes applied outside a modal move loop.
        /// </summary>
        private IntPtr ParentWndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_disposed) return IntPtr.Zero;
            try
            {
                if (msg == WM_MOVING || msg == WM_WINDOWPOSCHANGED)
                {
                    if (!_firstMoveHookLogged)
                    {
                        _firstMoveHookLogged = true;
                        _logger?.LogDebug("TubeAnchor: first WndProc move-hook fire (msg=0x{Msg:X4})", msg);
                    }

                    PixelPoint? origin = null;
                    if (msg == WM_MOVING && lParam != IntPtr.Zero)
                    {
                        var rect = Marshal.PtrToStructure<Win32Rect>(lParam);
                        origin = new PixelPoint(rect.Left, rect.Top);
                    }
                    UpdatePositionCore(origin, msg == WM_MOVING ? "wm-moving" : "wm-windowposchanged");
                }
            }
            catch
            {
                // Never let an exception escape into the parent's WndProc.
            }
            return IntPtr.Zero;
        }
    }
}
