using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Services.AvatarTube;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    /// <summary>
    /// Single-writer geometry controller for the avatar tube window
    /// (2026-07-11 core rebuild, 3rd attempt — replaces TubeAnchorController, whose
    /// four synchronous parent-follow triggers plus a leaked per-speech z-order timer
    /// produced a positive-feedback reposition storm: 5,196 UpdatePosition calls in one
    /// session, finalScale oscillating 0.527&lt;-&gt;0.738 = freeze + drift-to-top;
    /// Engram obs #6 avatartube-rootcause-2026-07-11).
    ///
    /// Ownership contract (board REBUILD SPEC 2026-07-11, invariant 1):
    ///  - This class is the ONLY writer of the tube <see cref="Window.Position"/>.
    ///  - This class is the ONLY writer of the tube Window.Width/Height and of the
    ///    ContentViewbox Width/Height. The window runs SizeToContent=Manual from birth
    ///    with an analytically computed size, so a transparent window is NEVER
    ///    auto-sized (the WPF layered-window CompleteRender freeze lesson,
    ///    WPF AvatarTubeWindow.xaml.cs:483-520).
    ///
    /// Discipline (invariants 2-4):
    ///  - Repositioning is event-driven and COALESCED to at most one pass per frame via
    ///    a queued Dispatcher.Post; nothing repositions synchronously inside a WndProc
    ///    hook. Logs are write-only (a pass that changes nothing is silent).
    ///  - The attached scale derives from SETTLED parent geometry only: parent
    ///    SizeChanged feeds a throttled leading recompute (event NewSize, not a live
    ///    read) plus a trailing settle timer, quantized and dead-banded
    ///    (<see cref="TubeGeometryMath.ShouldApplyScale"/>) so a fixed main-window size
    ///    yields exactly ONE scale. Move/activate/state paths never touch scale.
    ///  - Z-order is strictly separated from position: the parent WM_WINDOWPOSCHANGED
    ///    hook may only request a raise (WPF Windowing.cs:379-408 parity), and all
    ///    raises funnel through ONE throttled+coalesced path with a re-entrancy guard.
    ///
    /// Sizing model (owner-confirmed contract, obs #7):
    ///  - ATTACHED: tube+avatar scale WITH the main window —
    ///    finalScale = clamp(screenScale * parentClientHeight/1000, 0.30, screenScale).
    ///  - DETACHED: free user resize, independent of the main window —
    ///    effective = screenScale * userScale (0.25..2.5), capped to the work area.
    ///  All math lives in <see cref="TubeGeometryMath"/> (CCP.Core, unit-tested).
    /// </summary>
    public sealed class TubeGeometryController : IDisposable
    {
        private const uint WM_MOVING = 0x0216;
        private const uint WM_WINDOWPOSCHANGED = 0x0047;
        private const uint WM_ENTERSIZEMOVE = 0x0231;
        private const uint WM_EXITSIZEMOVE = 0x0232;
        private const uint SWP_NOZORDER = 0x0004;

        /// <summary>Trailing settle delay after the last parent SizeChanged before the scale recomputes from live geometry.</summary>
        private const int ScaleSettleMs = 110;

        /// <summary>Leading-edge throttle: during a continuous resize the scale recomputes at most this often.</summary>
        private const int ScaleLeadingThrottleMs = 150;

        /// <summary>Minimum interval between passive z-order raises (force bypasses).</summary>
        private const int RaiseThrottleMs = 200;

        private const int AnchorRetryMs = 150;
        private const int MaxAnchorRetries = 3;

        /// <summary>
        /// True per-frame coalescing floor: a geometry pass never executes more often than
        /// this. Dispatcher.Post alone is NOT enough — inside the Win32 modal move loop
        /// the message queue drains between WM_MOVING deliveries, so posted passes run
        /// per-event (~480/s measured on a high-poll mouse, including same-ms pairs).
        /// The trailing throttle timer guarantees the final rect always lands.
        /// </summary>
        private const int MinPassIntervalMs = 16;

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32WindowPos
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        // ===== Wiring =====
        private readonly Window _tube;
        private readonly Viewbox _contentViewbox;
        private readonly Window? _parent;
        private readonly ILogger? _logger;

        // ===== Scale state =====
        private double _screenScale = TubeGeometryMath.FallbackScreenScale;
        private double _parentRatio = 1.0;
        private double _finalScale = double.NaN;       // NaN = never computed; first ApplySizing always applies
        private double _appliedEffective = double.NaN; // the scale the viewbox/window are ACTUALLY sized at
        private double _userScale = 1.0;               // detached zoom; ignored while attached
        private bool _attached = true;
        private double _pendingParentHeight = double.NaN; // last SizeChanged NewSize.Height (settled read source)
        private DispatcherTimer? _scaleSettleTimer;
        private DateTime _lastScaleApplyUtc = DateTime.MinValue;

        // ===== Coalesced geometry pass =====
        private bool _passQueued;
        private string _passTrigger = "explicit";
        private DateTime _lastPassUtc = DateTime.MinValue;
        private DispatcherTimer? _passThrottleTimer;

        // Sticky drag-tracking state: while the parent sits in its Win32 modal move loop
        // (WM_ENTERSIZEMOVE..WM_EXITSIZEMOVE) the managed Position property is STALE —
        // Avalonia only raises PositionChanged after the loop ends. Every pass during the
        // move (including explicit/activated ones) must anchor off the freshest WM_MOVING
        // rect or it yanks the tube back to the pre-drag spot (a visible fight).
        private bool _inParentSizeMove;
        private PixelPoint? _movingOrigin; // freshest proposed parent rect from WM_MOVING

        // ===== Transient-geometry retry =====
        private DispatcherTimer? _retryTimer;
        private int _retryCount;

        // ===== Z-order raise funnel =====
        private bool _raiseQueued;
        private bool _pendingRaiseForce;
        private bool _raising; // re-entrancy guard: our own SetWindowPos re-enters WM_WINDOWPOSCHANGED synchronously
        private DateTime _lastRaiseUtc = DateTime.MinValue;

        // ===== Win32 parent hook (held in a field so the delegate is never collected) =====
        private Win32Properties.CustomWndProcHookCallback? _wndProcHook;
        private bool _hookActive;

        private bool _started;
        private bool _disposed;

        /// <summary>Raised after the parent fires Activated (position pass already queued).</summary>
        public event EventHandler? ParentActivated;

        /// <summary>Raised when the parent's WindowState changes.</summary>
        public event EventHandler<WindowState>? ParentWindowStateChanged;

        /// <summary>Raised when the parent's IsVisible changes.</summary>
        public event EventHandler<bool>? ParentIsVisibleChanged;

        /// <summary>Raised when the parent window closes.</summary>
        public event EventHandler? ParentClosed;

        /// <summary>
        /// The ONE shared z-order raise path (throttled + coalesced + re-entrancy guarded).
        /// The window subscribes and performs the platform pair-raise
        /// (WPF Windowing.cs:1031-1067). The bool argument is the WPF force flag.
        /// </summary>
        public event EventHandler<bool>? RaiseRequested;

        /// <summary>Current composed attached scale (screen fit x parent ratio, clamped). NaN until first sizing.</summary>
        public double FinalScale => _finalScale;

        /// <summary>Screen-fit scale (WPF CalculateScaleFactor parity, Windowing.cs:425-461). Upper cap for FinalScale.</summary>
        public double ScreenScale => _screenScale;

        public TubeGeometryController(Window tube, Viewbox contentViewbox, Window? parent, ILogger? logger)
        {
            _tube = tube ?? throw new ArgumentNullException(nameof(tube));
            _contentViewbox = contentViewbox ?? throw new ArgumentNullException(nameof(contentViewbox));
            _parent = parent;
            _logger = logger;
        }

        // ================= Lifecycle =================

        /// <summary>Wires all parent subscriptions. Idempotent; call once after construction.</summary>
        public void Start()
        {
            if (_started || _disposed) return;
            _started = true;

            if (_parent == null)
            {
                _logger?.LogDebug("TubeGeometry: no parent window - follow subscriptions skipped (detached-only tube)");
                return;
            }

            _parent.PositionChanged += OnParentPositionChanged;
            _parent.SizeChanged += OnParentSizeChanged;
            _parent.PropertyChanged += OnParentPropertyChanged;
            _parent.Activated += OnParentActivatedHandler;
            _parent.Closed += OnParentClosedHandler;
            TryRegisterParentHook();
            _logger?.LogDebug("TubeGeometry: parent subscriptions wired (hook={Hook})", _hookActive);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _retryTimer?.Stop();
            _retryTimer = null;
            _scaleSettleTimer?.Stop();
            _scaleSettleTimer = null;
            _passThrottleTimer?.Stop();
            _passThrottleTimer = null;

            if (_parent != null)
            {
                _parent.PositionChanged -= OnParentPositionChanged;
                _parent.SizeChanged -= OnParentSizeChanged;
                _parent.PropertyChanged -= OnParentPropertyChanged;
                _parent.Activated -= OnParentActivatedHandler;
                _parent.Closed -= OnParentClosedHandler;

                if (_hookActive && _wndProcHook != null)
                {
                    try
                    {
                        Win32Properties.RemoveWndProcHookCallback(_parent, _wndProcHook);
                    }
                    catch (Exception ex)
                    {
                        // The _disposed flag already makes the callback a no-op.
                        _logger?.LogDebug("TubeGeometry: WndProc hook removal failed ({Error})", ex.Message);
                    }
                }
            }
            _hookActive = false;
            _wndProcHook = null;
        }

        // ================= Mode / zoom inputs =================

        /// <summary>Attached = scale-with-main-window + anchored; detached = free-floating + user zoom.</summary>
        public void SetAttached(bool attached)
        {
            if (_disposed) return;
            _attached = attached;
            _logger?.LogDebug("TubeGeometry: SetAttached({Attached})", attached);
            ApplySizing();
            if (attached) RequestReanchor("attach");
            else ClampToScreen();
        }

        /// <summary>Detached user zoom (Ctrl+wheel / grow / shrink). Ignored while attached.</summary>
        public void SetUserScale(double userScale)
        {
            if (_disposed || userScale <= 0 || double.IsNaN(userScale)) return;
            _userScale = Math.Clamp(userScale, TubeGeometryMath.MinUserScale, TubeGeometryMath.MaxUserScale);
            if (!_attached)
            {
                ApplySizing();
                ClampToScreen();
            }
        }

        // ================= Sizing (single writer of viewbox + window size) =================

        /// <summary>
        /// WPF CalculateScaleFactor parity (Windowing.cs:425-461): screen-fit scale from
        /// the PRIMARY screen working area in logical units.
        /// </summary>
        public void RecomputeScreenScale()
        {
            double workW = 0, workH = 0;
            try
            {
                var screen = _tube.Screens.Primary ?? _tube.Screens.ScreenFromWindow(_tube);
                if (screen != null)
                {
                    double scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
                    workW = screen.WorkingArea.Width / scaling;
                    workH = screen.WorkingArea.Height / scaling;
                }
            }
            catch
            {
                // fall through to the math fallback
            }
            _screenScale = TubeGeometryMath.ComputeScreenScale(workW, workH);
            _logger?.LogDebug("TubeGeometry: RecomputeScreenScale -> {ScreenScale:F3}", _screenScale);
        }

        /// <summary>
        /// Recomputes the effective scale for the current mode and writes the viewbox and
        /// window size when it actually changed. Attached recomputes go through the
        /// quantize + dead-band gate so a fixed parent size settles to ONE value.
        /// </summary>
        public void ApplySizing() => ApplySizingCore("explicit");

        private void ApplySizingCore(string reason)
        {
            if (_disposed) return;

            double candidateFinal;
            double candidateRatio;
            double effective;
            if (_attached)
            {
                double parentHeight = double.NaN;
                if (_parent != null && _parent.WindowState != WindowState.Minimized)
                {
                    // Prefer the height reported by the last SizeChanged event (a settled
                    // layout value) over an ad-hoc live read; ApplySizing is never driven
                    // from move/activate paths, so a transient mid-move client size can
                    // no longer flip the ratio (the 0.714<->1.000 oscillation, obs #6).
                    parentHeight = !double.IsNaN(_pendingParentHeight)
                        ? _pendingParentHeight
                        : _parent.ClientSize.Height;
                }

                candidateRatio = TubeGeometryMath.QuantizeParentRatio(parentHeight);
                candidateFinal = TubeGeometryMath.ComposeAttachedScale(_screenScale, candidateRatio);
                effective = candidateFinal;
            }
            else
            {
                // Detached: anchor the zoom at the screen-fit scale; no parent coupling.
                candidateFinal = _screenScale;
                candidateRatio = 1.0;
                effective = TubeGeometryMath.ComputeDetachedScale(_screenScale, _userScale, WorkAreaHeightLogical());
            }

            // Dead-band on the EFFECTIVE scale that actually drives the size, so mode
            // transitions (attach after a detached zoom) and real resizes always apply
            // while sub-pixel jitter never does (no A<->B flip, obs #6).
            if (!TubeGeometryMath.ShouldApplyScale(_appliedEffective, effective))
                return;

            _parentRatio = candidateRatio;
            _finalScale = candidateFinal;
            _appliedEffective = effective;
            _lastScaleApplyUtc = DateTime.UtcNow;

            double w = TubeGeometryMath.DesignWidth * effective;
            double h = TubeGeometryMath.DesignHeight * effective;

            bool changed = double.IsNaN(_contentViewbox.Width) || double.IsNaN(_contentViewbox.Height)
                           || Math.Abs(_contentViewbox.Width - w) > 0.1
                           || Math.Abs(_contentViewbox.Height - h) > 0.1;
            if (changed)
            {
                // Viewbox and window are pinned to the SAME analytic size; the window is
                // never auto-sized (SizeToContent=Manual from birth), so bubble/content
                // churn can never resize it (WPF xaml.cs:483-520 anti-freeze invariant).
                _contentViewbox.Width = w;
                _contentViewbox.Height = h;
                _tube.Width = w;
                _tube.Height = h;

                _logger?.LogDebug(
                    "TubeGeometry: ApplySizing[{Reason}] attached={Attached} screenScale={ScreenScale:F3} parentRatio={ParentRatio:F3} finalScale={FinalScale:F3} userScale={UserScale:F2} size={W:F1}x{H:F1}",
                    reason, _attached, _screenScale, _parentRatio, _finalScale, _userScale, w, h);

                RequestReanchor("sizing");
            }
        }

        private double WorkAreaHeightLogical()
        {
            try
            {
                var screen = _tube.Screens.ScreenFromWindow(_tube) ?? _tube.Screens.Primary;
                if (screen == null) return double.NaN;
                double scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
                return screen.WorkingArea.Height / scaling;
            }
            catch
            {
                return double.NaN;
            }
        }

        // ================= Anchoring (single writer of Window.Position) =================

        /// <summary>
        /// Public re-anchor entry point. Idempotent, coalesced to at most one executed
        /// pass per frame, and a no-op unless attached (public-API contract).
        /// </summary>
        public void UpdatePosition() => RequestReanchor("explicit");

        /// <summary>
        /// Queues ONE geometry pass; further requests coalesce into it, and passes are
        /// floored at <see cref="MinPassIntervalMs"/> apart (trailing edge guaranteed),
        /// so no burst of triggers can ever produce sub-frame or same-ms repositions.
        /// </summary>
        private void RequestReanchor(string trigger)
        {
            if (_disposed) return;
            _passTrigger = trigger;
            if (_passQueued) return;
            _passQueued = true;

            double sinceLast = (DateTime.UtcNow - _lastPassUtc).TotalMilliseconds;
            if (sinceLast >= MinPassIntervalMs)
            {
                Dispatcher.UIThread.Post(RunGeometryPass, DispatcherPriority.Render);
            }
            else
            {
                // Too soon: run on the trailing edge of the frame interval instead.
                if (_passThrottleTimer == null)
                {
                    _passThrottleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MinPassIntervalMs) };
                    _passThrottleTimer.Tick += (_, _) =>
                    {
                        _passThrottleTimer?.Stop();
                        RunGeometryPass();
                    };
                }
                if (!_passThrottleTimer.IsEnabled)
                    _passThrottleTimer.Start();
            }
        }

        private void RunGeometryPass()
        {
            _passQueued = false;
            if (_disposed) return;
            _lastPassUtc = DateTime.UtcNow;
            if (!_attached || _parent == null)
                return;

            var trigger = _passTrigger;
            // While the parent is inside its modal move loop, the sticky WM_MOVING rect is
            // the ONLY fresh origin (Position is stale until the loop exits) — it applies
            // to every trigger, not just the hook's own. Outside the loop, Position rules.
            var parentPos = _inParentSizeMove && _movingOrigin.HasValue ? _movingOrigin.Value : _parent.Position;
            var parentClient = _parent.ClientSize;

            var state = TubeGeometryMath.ClassifyParentGeometry(
                _parent.WindowState == WindowState.Minimized,
                parentClient.Width, parentClient.Height,
                parentPos.X, parentPos.Y);

            if (state != TubeParentGeometryState.Valid)
            {
                // Skips are logged with a DIFFERENT literal than the write log so the
                // telemetry acceptance grep counts actual repositions only.
                _logger?.LogDebug(
                    "TubeGeometry: anchor skip[{Trigger}] state={State} parentPos={PPos} parentClient={PW:F0}x{PH:F0} retry={Retry}/{Max}",
                    trigger, state, parentPos, parentClient.Width, parentClient.Height, _retryCount, MaxAnchorRetries);
                if (state == TubeParentGeometryState.SkipTransient)
                    ScheduleAnchorRetry();
                return;
            }

            double tubeW = _contentViewbox.Width;
            double tubeH = _contentViewbox.Height;
            if (double.IsNaN(tubeW) || tubeW <= 0 || double.IsNaN(tubeH) || tubeH <= 0)
            {
                // Pre-first-sizing pass: size now, then re-read.
                ApplySizingCore("pre-anchor");
                tubeW = _contentViewbox.Width;
                tubeH = _contentViewbox.Height;
                if (double.IsNaN(tubeW) || tubeW <= 0 || double.IsNaN(tubeH) || tubeH <= 0)
                    return;
            }

            var (left, top) = TubeGeometryMath.ComputeAttachedAnchor(
                parentPos.X, parentPos.Y, parentClient.Height,
                tubeW, tubeH, _finalScale, _parent.RenderScaling);

            _retryCount = 0;
            _retryTimer?.Stop();

            var newPos = new PixelPoint(left, top);
            if (_tube.Position != newPos)
            {
                _tube.Position = newPos;

                // WRITE-ONLY telemetry: one line per actual reposition. The acceptance
                // gate greps 'UpdatePosition' + 'finalScale=' (board REBUILD SPEC).
                _logger?.LogDebug(
                    "TubeGeometry: UpdatePosition[{Trigger}] parentPos={PPos} parentClient={PW:F0}x{PH:F0} rs={Rs:F2} finalScale={FinalScale:F3} tube={TW:F1}x{TH:F1} new=({NL},{NT})",
                    trigger, parentPos, parentClient.Width, parentClient.Height,
                    _parent.RenderScaling, _finalScale, tubeW, tubeH, left, top);
            }
        }

        private void ScheduleAnchorRetry()
        {
            if (_disposed || _retryCount >= MaxAnchorRetries) return;
            _retryCount++;
            if (_retryTimer == null)
            {
                _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnchorRetryMs) };
                _retryTimer.Tick += (_, _) =>
                {
                    _retryTimer?.Stop();
                    RequestReanchor("retry");
                };
            }
            _retryTimer.Stop();
            _retryTimer.Start();
        }

        // ================= Detached positioning (drag + clamp) =================

        /// <summary>Moves the tube to an explicit position (detached drag), clamped to the working area.</summary>
        public void MoveTo(PixelPoint position)
        {
            if (_disposed) return;
            var clamped = ClampToWorkArea(position);
            if (_tube.Position != clamped)
                _tube.Position = clamped;
        }

        /// <summary>Clamps the tube's current position into the working area of its screen.</summary>
        public void ClampToScreen()
        {
            if (_disposed) return;
            var clamped = ClampToWorkArea(_tube.Position);
            if (_tube.Position != clamped)
                _tube.Position = clamped;
        }

        private PixelPoint ClampToWorkArea(PixelPoint pos)
        {
            try
            {
                var screen = _tube.Screens.ScreenFromWindow(_tube) ?? _tube.Screens.Primary;
                if (screen == null) return pos;
                var wa = screen.WorkingArea; // physical px
                double s = _tube.RenderScaling;
                double wLogical = !double.IsNaN(_tube.Width) && _tube.Width > 0 ? _tube.Width : _tube.ClientSize.Width;
                double hLogical = !double.IsNaN(_tube.Height) && _tube.Height > 0 ? _tube.Height : _tube.ClientSize.Height;
                var (x, y) = TubeGeometryMath.ClampToWorkArea(
                    pos.X, pos.Y, wa.X, wa.Y, wa.Right, wa.Bottom,
                    (int)Math.Round(wLogical * s), (int)Math.Round(hLogical * s));
                return new PixelPoint(x, y);
            }
            catch
            {
                return pos;
            }
        }

        // ================= Z-order raise funnel (strictly separated from position) =================

        /// <summary>
        /// The ONE shared z-order raise path (board REBUILD SPEC invariant 3): throttled,
        /// coalesced to one dispatcher post, and wrapped in a re-entrancy guard so the
        /// WM_WINDOWPOSCHANGED our own SetWindowPos generates can never loop back in.
        /// <paramref name="force"/> mirrors WPF's deliberate-foregrounding flag
        /// (Windowing.cs:1022-1029) and bypasses the throttle.
        /// </summary>
        public void RequestRaise(bool force = false)
        {
            if (_disposed || !_attached) return;
            if (force) _pendingRaiseForce = true;
            else if ((DateTime.UtcNow - _lastRaiseUtc).TotalMilliseconds < RaiseThrottleMs) return;
            if (_raiseQueued) return;
            _raiseQueued = true;

            Dispatcher.UIThread.Post(() =>
            {
                _raiseQueued = false;
                if (_disposed || !_attached) return;
                bool f = _pendingRaiseForce;
                _pendingRaiseForce = false;
                _lastRaiseUtc = DateTime.UtcNow;
                _raising = true;
                try
                {
                    RaiseRequested?.Invoke(this, f);
                }
                finally
                {
                    _raising = false;
                }
            }, DispatcherPriority.Render);
        }

        // ================= Parent follow handlers (schedule-only; no synchronous writes) =================

        private void OnParentPositionChanged(object? sender, PixelPointEventArgs e)
        {
            // The managed Position property is fresh again — drop any sticky drag rect.
            _movingOrigin = null;
            RequestReanchor("position-changed");
        }

        private void OnParentSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // Scale-with-main-window feed: the event's NewSize is a settled layout value.
            _pendingParentHeight = e.NewSize.Height;

            // Leading edge (throttled) so the tube visibly tracks during a continuous resize...
            if ((DateTime.UtcNow - _lastScaleApplyUtc).TotalMilliseconds >= ScaleLeadingThrottleMs)
                ApplySizingCore("resize");

            // ...and a trailing settle pass so the final size always lands exactly.
            if (_scaleSettleTimer == null)
            {
                _scaleSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScaleSettleMs) };
                _scaleSettleTimer.Tick += (_, _) =>
                {
                    _scaleSettleTimer?.Stop();
                    _pendingParentHeight = double.NaN; // settled: read live geometry once
                    ApplySizingCore("resize-settled");
                };
            }
            _scaleSettleTimer.Stop();
            _scaleSettleTimer.Start();

            RequestReanchor("size-changed");
        }

        private void OnParentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty)
            {
                var state = _parent?.WindowState ?? WindowState.Normal;
                if (state != WindowState.Minimized)
                {
                    // Maximize/restore change the client size; SizeChanged delivers the
                    // scale update. Position must re-anchor either way.
                    RequestReanchor("state-changed");
                }
                ParentWindowStateChanged?.Invoke(this, state);
            }
            else if (e.Property == Visual.IsVisibleProperty)
            {
                bool visible = e.NewValue is true;
                if (visible)
                    RequestReanchor("visible-changed");
                ParentIsVisibleChanged?.Invoke(this, visible);
            }
        }

        private void OnParentActivatedHandler(object? sender, EventArgs e)
        {
            RequestReanchor("activated");
            ParentActivated?.Invoke(this, EventArgs.Empty);
        }

        private void OnParentClosedHandler(object? sender, EventArgs e)
        {
            ParentClosed?.Invoke(this, EventArgs.Empty);
        }

        // ================= Windows parent hook (observe-only, schedule-only) =================

        /// <summary>
        /// Registers Avalonia's Win32Properties.AddWndProcHookCallback on the parent.
        /// Win32Properties lives in the cross-platform Avalonia.Controls assembly in v12
        /// (verified against 12.0.5, src/Avalonia.Controls/Platform/Win32Properties.cs)
        /// and no-ops off-Windows, but we still guard with OperatingSystem.IsWindows().
        /// Contract (board REBUILD SPEC invariants 3+4):
        ///  - WM_MOVING: stash the proposed rect and SCHEDULE a coalesced pass — the tube
        ///    tracks the OS move loop without ever repositioning inside the hook.
        ///  - WM_WINDOWPOSCHANGED: z-order only (WPF Windowing.cs:379-408) — when the
        ///    parent's z-order actually changed and we are not raising ourselves,
        ///    request the shared debounced raise. NEVER a reposition.
        /// </summary>
        private void TryRegisterParentHook()
        {
            if (!OperatingSystem.IsWindows() || _parent == null) return;
            try
            {
                _wndProcHook = ParentWndProcHook;
                Win32Properties.AddWndProcHookCallback(_parent, _wndProcHook);
                _hookActive = true;
            }
            catch (Exception ex)
            {
                _wndProcHook = null;
                _hookActive = false;
                _logger?.LogDebug("TubeGeometry: WndProc hook registration failed ({Error}) - PositionChanged fallback", ex.Message);
            }
        }

        private IntPtr ParentWndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_disposed) return IntPtr.Zero;
            try
            {
                if (msg == WM_ENTERSIZEMOVE)
                {
                    _inParentSizeMove = true;
                }
                else if (msg == WM_EXITSIZEMOVE)
                {
                    // Move/size loop ended (including ESC cancel): the final placement has
                    // been applied, so the managed Position is authoritative again.
                    _inParentSizeMove = false;
                    _movingOrigin = null;
                    RequestReanchor("move-end");
                }
                else if (msg == WM_MOVING && lParam != IntPtr.Zero)
                {
                    // WM_MOVING only occurs inside a modal move loop; flag it too in case
                    // the hook was registered after WM_ENTERSIZEMOVE already fired.
                    _inParentSizeMove = true;
                    var rect = Marshal.PtrToStructure<Win32Rect>(lParam);
                    _movingOrigin = new PixelPoint(rect.Left, rect.Top);
                    RequestReanchor("wm-moving");
                }
                else if (msg == WM_WINDOWPOSCHANGED && lParam != IntPtr.Zero && !_raising)
                {
                    // Only react when the z-order actually changed (ignore pure move/resize
                    // — those arrive via the managed events). WPF Windowing.cs:387-390.
                    var wp = Marshal.PtrToStructure<Win32WindowPos>(lParam);
                    if ((wp.flags & SWP_NOZORDER) == 0)
                        RequestRaise(false);
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
