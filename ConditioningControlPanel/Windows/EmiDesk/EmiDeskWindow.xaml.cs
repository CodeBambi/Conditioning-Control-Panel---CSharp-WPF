using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP, and it is not a style preference. Every window under Windows/ lives in the
// FLAT ConditioningControlPanel namespace - not one of them declares ConditioningControlPanel.Windows.
// Declaring it here compiles for a moment and then breaks ScreenOcrService, whose
// `Windows.Graphics.Imaging.BitmapDecoder` is resolved relative to the enclosing
// ConditioningControlPanel namespace: the instant a ConditioningControlPanel.Windows exists, it
// shadows the WinRT `Windows` root and the OCR service stops finding its decoder. Keep it flat.
namespace ConditioningControlPanel;

/// <summary>
/// EMI Desk's widget window: the body, the face, drag, resize, the hover x, the pet, the idle
/// beats and the summon / dismiss FX. This file is chunk B1's; the ring lives in
/// <c>EmiDeskWindow.Ring.cs</c>, the glass in <c>EmiDeskWindow.Glass.cs</c> and the speech bubble
/// in <c>EmiDeskWindow.Bubble.cs</c>, all partials of this class. Those three files do not exist
/// yet: every hook they need is declared here as a <c>partial void ...Core(...)</c> seam, so B2
/// and B3 can plug in WITHOUT editing this file. See <c>docs/emi-desk/SEAMS.md</c>.
///
/// Windowing recipe, lifted from the avatar tube (<c>AvatarTube/AvatarTubeWindow.Windowing.cs</c>
/// and <c>AvatarRandomBubble.cs</c>): AllowsTransparency + WindowStyle None, Topmost,
/// ShowActivated false, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE (no taskbar entry, no focus theft),
/// and WM_DPICHANGED SWALLOWED behind a 450 ms quiesce because WPF's automatic rescale of a
/// layered window is a synchronous CompleteRender that deadlocks the shared render thread
/// (#451 / #477). She therefore keeps rendering in her birth DPI space and all persistence runs in
/// PHYSICAL PIXELS plus a monitor device name (the DIPs-vs-pixels trap).
/// </summary>
public partial class EmiDeskWindow : Window
{
    // ---------------------------------------------------------------- geometry

    /// <summary>Smallest body width in DIPs.</summary>
    public const double MinBodyWidth = 152.0;

    /// <summary>Largest body width in DIPs.</summary>
    public const double MaxBodyWidth = 420.0;

    /// <summary>The body PNG is 859 x 869, so height = width * 869/859.</summary>
    public const double BodyAspect = 869.0 / 859.0;

    /// <summary>
    /// Transparent air ABOVE AND BELOW her, in DIPs. The window is bigger than the silhouette so
    /// the summon smoke, the sparkle scatter and the speech bubble have somewhere to go. Padding is
    /// Background-less, so WPF does not hit-test it and clicks fall through to whatever is behind.
    /// </summary>
    public const double OverlayPad = 120.0;

    /// <summary>
    /// Transparent air to her LEFT AND RIGHT, in DIPs, and it is deliberately much wider than
    /// <see cref="OverlayPad"/>.
    ///
    /// <para>Her bubble hangs off her shoulder at <c>BubbleLeftFrac</c> (58 % of her width) and may
    /// be <c>BubbleMaxWidth</c> (380) DIPs wide, so at her narrowest the bubble's right edge wants
    /// to sit <c>0.58 x 152 + 380 = 468</c> DIPs from her left - 316 past her own 152. With a
    /// symmetric 120 pad the bubble ran off the end of the window and WPF clipped it MID-WORD
    /// ("no hands." / "work.", owner screenshot 2026-08-29). The pad is now the widest bubble that
    /// can ever be built, on either side, so the flip has the same room as the rest position.</para>
    ///
    /// <para>Cost of the wider window: none that a user can see. The extra area is
    /// <c>Background="{x:Null}"</c>, which is not hit-tested and not drawn, the window is placed by
    /// her BODY rect (<see cref="ClampIntoWorkArea"/> clamps the silhouette, never the window), and
    /// the pad is allowed to hang off the edge of the desktop.</para>
    /// </summary>
    public const double OverlayPadX = 330.0;

    // The glass rect as a fraction of the body image. Copied from Resources/web/arcademy/emi/emi.css
    // (.emi-screen / .emi-glass). Any change here has to be mirrored in that file, and vice versa.
    private const double GlassLeftFrac = 0.3446;
    private const double GlassTopFrac = 0.2946;
    private const double GlassWidthFrac = 0.4168;
    private const double GlassHeightFrac = 0.3763;

    // A pet is armed by hovering the HEAD: everything above the glass, plus a little of the bezel.
    private const double HeadBottomFrac = 0.30;
    private const int PetHoverMs = 1200;
    private const int PetCooldownMs = 6000;   // widget.js DIALS.PET_COOLDOWN_MS

    // Travel under this much is a CLICK, not a drag. In DIPs, and it has to stay that way: the
    // ring's own drag watch (EmiDeskWindow.Ring.cs) measures in DIPs off the same constant, and
    // when this side measured PHYSICAL pixels the two disagreed by the DPI scale - at the owner's
    // 125% a 7 px hand tremor was under the ring's threshold but over this one, so the ring never
    // toggled and she crept across the desktop instead (QA 2026-08-29). One constant, one space.
    private const double DragThresholdDip = 6.0;   // widget.js DIALS.DRAG_PX

    // The blink clock is EmiAlive.BlinkEveryMs (5200) jittered by EmiAlive.BlinkJitterMs, and the
    // blink itself is a raw lid swap rather than the 2.7 s `blink` CHAIN the pitch stage played on
    // a coin flip every 4200 ms. See PlayIdleBlink in EmiDeskWindow.Alive.cs for why that one read
    // as dead.

    // ---------------------------------------------------------------- seams (B2 / B3)

    #region seams

    /// <summary>
    /// The glass canvas, exactly over the bezel rect, ON TOP of the face. Chunk B3 paints channels
    /// into it and hides it at rest; the face keeps painting underneath the whole time, which is
    /// the safety argument (killing a channel is hiding one node, the locked renderer is never
    /// touched). Sized by <see cref="ApplyBodyWidth"/>; never resize it yourself.
    /// </summary>
    public Panel GlassHost => GlassCanvas;

    /// <summary>
    /// The full-window FX layer, over her body and under the bubble. B1 fires the summon smoke and
    /// the dismiss sparkles in here; chunk B2 can hang the ring's own visuals off it. Not
    /// hit-testable: put interactive ring cards in their own window and anchor them with
    /// <see cref="BodyScreenRect"/>, because this window is only a pad wider than she is.
    /// </summary>
    public Panel OverlayHost => OverlayCanvas;

    /// <summary>The topmost layer, for chunk B3's speech bubble. Not hit-testable.</summary>
    public Panel BubbleHost => BubbleCanvas;

    /// <summary>The face renderer on the glass. Prefer <see cref="DrawFace"/> over touching it.</summary>
    public EmiFace Face => FaceView;

    /// <summary>
    /// True while she must ignore pointer input: during the summon / dismiss transitions. B2 and
    /// B3 must honour it (no ring open, no glass tap) so a click cannot land mid-CRT.
    /// </summary>
    public bool InputLocked { get; private set; }

    /// <summary>Any pointer input on her: a press, a drag, a resize, a hover that armed the pet.</summary>
    public event EventHandler? PointerActivity;

    /// <summary>Her body width changed (DIPs). Fires after the layout has been applied.</summary>
    public event EventHandler<double>? Resized;

    /// <summary>She was moved. Fires once per drop, not per mouse-move.</summary>
    public event EventHandler? Moved;

    /// <summary>
    /// Chunk B2: a click on her body that was not a drag. Set <paramref name="handled"/> to true to
    /// claim it (opening or closing the ring). Unhandled clicks do nothing.
    /// </summary>
    partial void OnBodyClickedCore(ref bool handled);

    /// <summary>
    /// Chunk B3: a click that landed inside the glass rect while a channel was up. Set
    /// <paramref name="handled"/> to true to fire the channel; unhandled falls through to
    /// <see cref="OnBodyClickedCore"/>.
    /// </summary>
    partial void OnGlassClickedCore(ref bool handled);

    /// <summary>
    /// Chunk B3: is a glass channel on screen right now? Answered by setting
    /// <paramref name="live"/>. B1 asks before routing a click to <see cref="OnGlassClickedCore"/>
    /// and before letting an idle beat play.
    /// </summary>
    partial void OnGlassLiveQuery(ref bool live);

    /// <summary>
    /// Chunk B2: is the ring open right now? Answered by setting <paramref name="open"/>.
    ///
    /// Added for chunk B3: the glass may only wander off to a channel while she is genuinely idle,
    /// and an open ring is the loudest "the user is mid-thought" signal there is. Asked as a query
    /// rather than tracked as a flag so B2 stays the only owner of the ring's state.
    /// </summary>
    partial void OnRingOpenQuery(ref bool open);

    /// <summary>
    /// Chunk B2 / B3: the widget has been built and its first layout applied. The counterpart to
    /// <see cref="OnTearDownCore"/>: start the ambient loops here (chunk B3 starts the glass idle
    /// watch), never in a static initialiser, so a second widget could never inherit the first
    /// one's timers.
    /// </summary>
    partial void OnReadyCore();

    /// <summary>
    /// Chunk B2 / B3: the widget is about to go away (dismiss, or the app is closing). Tear the
    /// ring, the glass and any open ask down here. Called before the outro chain starts.
    /// </summary>
    partial void OnTearDownCore();

    /// <summary>
    /// Chunk B3: the bubble text changed. Null clears it. Driven by every chain frame that carries
    /// a bubble instruction, so the locked . / .. / ... cadence comes for free.
    /// </summary>
    partial void OnBubbleTextCore(string? text);

    /// <summary>
    /// Chunk B2 / B3: a chain asked for a one-shot particle burst (hearts, sparks, tears, storm,
    /// bang). B1 draws nothing for these; they are decoration the later chunks own.
    /// </summary>
    partial void OnChainFxCore(string kind);

    /// <summary>
    /// A chain asked for a one-shot body move (bounce, nod, droop, shiver, thud). B1 runs the
    /// canonical ones itself; the seam exists so a later chunk can add art-driven moves.
    /// </summary>
    partial void OnBodyMoveCore(string move, ref bool handled);

    #endregion

    // ---------------------------------------------------------------- state

    private readonly EmiChains.Player _player;
    private readonly Dictionary<string, ImageSource> _bodyCache = new(StringComparer.Ordinal);

    // THE OUTFIT OVERLAY (THE SKIN LAW; see the block in EmiChains.cs). `_outfit` is null on every
    // road today - nothing on the desk picks a garment - so all of this rests at zero cost.
    // `_overArmed` is the ONE probe per outfit: null means "not asked yet", and once it is a bool
    // the answer is final for the sitting, so a sheet without an overlay costs one File.Exists and
    // never touches the disk again however many times she sways.
    private readonly Dictionary<string, ImageSource> _overCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _overArmed = new(StringComparer.Ordinal);
    private string? _outfit;
    private string? _overPose;

    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _swayTimer;
    private DispatcherTimer? _petTimer;
    private DispatcherTimer? _fxSweepTimer;
    private DispatcherTimer? _dpiQuiesceTimer;

    private double _bodyWidth = 220;
    private string _pose = "idle";
    private int _swayAt;
    private DateTime _petCooldownUntil = DateTime.MinValue;
    private bool _petArmed;
    private bool _transiting;
    private bool _closingForGood;

    private Point _dragStartScreen;
    private double _dragStartLeft, _dragStartTop;
    private bool _dragging, _dragMoved;
    private bool _resizing;
    private Point _resizeStartScreen;
    private double _resizeStartWidth;

    private static readonly Random Rng = new();

    /// <summary>True while a chain is on screen.</summary>
    public bool ChainLive => _player.IsLive;

    /// <summary>True while a summon or dismiss transition is running.</summary>
    public bool Transiting => _transiting;

    /// <summary>Her body width in DIPs.</summary>
    public double BodyWidth => _bodyWidth;

    // ---------------------------------------------------------------- ctor

    /// <summary>Builds the widget. It is created hidden; <c>EmiDeskService</c> summons it.</summary>
    public EmiDeskWindow()
    {
        InitializeComponent();
        _player = new EmiChains.Player(Dispatcher);

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosedCleanup;

        // ALIVE wave A rides ONE 100 ms poll, and it lives exactly as long as she is on screen.
        // Hung off IsVisibleChanged rather than off the summon so every road that shows or hides
        // her - the summon FX, the dismiss, a bare Hide during teardown - moves the clock with her.
        IsVisibleChanged += (_, _) =>
        {
            try
            {
                if (IsVisible) StartAlive();
                else StopAlive();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] alive visibility hook failed"); }
        };

        BodyRoot.MouseLeftButtonDown += OnBodyMouseDown;
        BodyRoot.MouseMove += OnBodyMouseMove;
        BodyRoot.MouseLeftButtonUp += OnBodyMouseUp;
        BodyRoot.MouseRightButtonUp += OnBodyRightClick;
        BodyRoot.MouseEnter += OnBodyMouseEnter;
        BodyRoot.MouseLeave += OnBodyMouseLeave;

        BtnClose.Click += OnCloseClick;
        BtnClose.MouseLeftButtonDown += (_, e) => e.Handled = true;

        BtnCards.Click += OnCardsClick;
        BtnCards.MouseLeftButtonDown += (_, e) => e.Handled = true;

        try
        {
            BtnClose.ToolTip = Loc.Get("emi_desk_tip_close");
            BtnCards.ToolTip = Loc.Get("emi_desk_tip_cards");
            ResizeGrip.ToolTip = Loc.Get("emi_desk_tip_grip");
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chrome tooltips failed"); }

        ResizeGrip.MouseLeftButtonDown += OnGripMouseDown;
        ResizeGrip.MouseMove += OnGripMouseMove;
        ResizeGrip.MouseLeftButtonUp += OnGripMouseUp;

        try { _bodyWidth = ClampWidth(App.Settings?.Current?.EmiDeskWidth ?? 220); }
        catch { _bodyWidth = 220; }

        ApplyBodyWidth(_bodyWidth);
        SetPose("idle");
        DrawFace(EmiChains.RestFace);

        try { OnReadyCore(); }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] ready seam threw"); }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] window ex-style / hook install failed");
        }
    }

    // ---------------------------------------------------------------- win32

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_DPICHANGED = 0x02E0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    /// <summary>
    /// Swallow WM_DPICHANGED. WPF's automatic rescale of a layered window is a synchronous
    /// CompleteRender delivered inside the drag's modal move loop, and it deadlocks against this
    /// surface's own writers (the chain timer, the FX sweep). She keeps her birth DPI and gets one
    /// controlled re-clamp on the 450 ms settle instead.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DPICHANGED)
        {
            try
            {
                if (_dpiQuiesceTimer == null)
                {
                    _dpiQuiesceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(450)
                    };
                    _dpiQuiesceTimer.Tick += (_, _) =>
                    {
                        try
                        {
                            _dpiQuiesceTimer?.Stop();
                            ClampIntoWorkArea();
                            SavePlacement();
                        }
                        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] DPI settle refit failed"); }
                    };
                }
                _dpiQuiesceTimer.Stop();
                _dpiQuiesceTimer.Start();
                handled = true;
            }
            catch { /* never let a hook throw */ }
        }
        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- layout

    private static double ClampWidth(double w)
    {
        if (double.IsNaN(w) || double.IsInfinity(w)) return 220;
        return Math.Max(MinBodyWidth, Math.Min(MaxBodyWidth, w));
    }

    /// <summary>
    /// Resize her. Width is clamped to 152..420 DIPs; the height, the window, the glass rect and
    /// the two corner affordances all follow. Raises <see cref="Resized"/>.
    /// </summary>
    public void ApplyBodyWidth(double width)
    {
        try
        {
            _bodyWidth = ClampWidth(width);
            double bw = _bodyWidth;
            double bh = bw * BodyAspect;

            BodyRoot.Width = bw;
            BodyRoot.Height = bh;
            Width = bw + OverlayPadX * 2;
            Height = bh + OverlayPad * 2;

            double gl = bw * GlassLeftFrac;
            double gt = bh * GlassTopFrac;
            double gw = bw * GlassWidthFrac;
            double gh = bh * GlassHeightFrac;

            FaceView.Width = gw;
            FaceView.Height = gh;
            Canvas.SetLeft(FaceView, gl);
            Canvas.SetTop(FaceView, gt);

            GlassCanvas.Width = gw;
            GlassCanvas.Height = gh;
            Canvas.SetLeft(GlassCanvas, gl);
            Canvas.SetTop(GlassCanvas, gt);

            // The drag wobble swings her from the HEAD, not from her middle: a mascot held by the
            // scruff. BodyRoot's RenderTransformOrigin is 0.55 of her height, so a centre of
            // -0.25 x height puts the pivot at 0.30 - the top of the bezel. It is set here because
            // it is the only place that knows her current height.
            WobbleRotate.CenterX = 0;
            WobbleRotate.CenterY = -bh * 0.25;

            // The two affordances scale a little with her so they stay reachable but never eat the
            // face: 8 % of the body width, floored at their authored size.
            double chip = Math.Max(18, bw * 0.08);
            BtnClose.Width = chip;
            BtnClose.Height = chip;
            BtnClose.Margin = new Thickness(0, bh * 0.02, bw * 0.02, 0);

            // The cards glyph is the x's mirror: same chip, same inset, other corner.
            BtnCards.Width = chip;
            BtnCards.Height = chip;
            BtnCards.Margin = new Thickness(bw * 0.02, bh * 0.02, 0, 0);

            // The grip's HIT AREA, floored at 22 DIP so the cursor catches it at every body width
            // (owner call, QA 2026-08-29). The glyph inside it does not grow with it.
            double grip = Math.Max(GripHitSize, bw * 0.09);
            ResizeGrip.Width = grip;
            ResizeGrip.Height = grip;
            ResizeGrip.Margin = new Thickness(0, 0, bw * 0.01, bh * 0.01);

            Resized?.Invoke(this, _bodyWidth);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ApplyBodyWidth failed");
        }
    }

    /// <summary>
    /// Put her in the nearest corner of the work area she is currently on, with a small inset.
    /// Used by the shrink offer, where she makes herself small and tidies herself out of the way in
    /// one move; a shrink that left her floating mid-screen would read as a glitch, not a favour.
    /// </summary>
    public void SnapToNearestCorner()
    {
        try
        {
            double s = DipScale;
            var body = BodyScreenRect;
            var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(
                (int)body.X, (int)body.Y, Math.Max(1, (int)body.Width), Math.Max(1, (int)body.Height)));
            var wa = screen.WorkingArea;

            double insetPx = 12 * s;
            double cx = body.X + body.Width / 2.0;
            double cy = body.Y + body.Height / 2.0;

            double bodyLeftPx = cx < wa.Left + wa.Width / 2.0
                ? wa.Left + insetPx
                : wa.Right - insetPx - body.Width;
            double bodyTopPx = cy < wa.Top + wa.Height / 2.0
                ? wa.Top + insetPx
                : wa.Bottom - insetPx - body.Height;

            // Left/Top are the WINDOW's, and the window is a pad bigger than she is on
            // every side. Physical pixels in, DIPs out (THE COORDINATE TRAP).
            Left = bodyLeftPx / s - OverlayPadX;
            Top = bodyTopPx / s - OverlayPad;

            ClampIntoWorkArea();
            SavePlacement();
            try { Moved?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] Moved handler threw"); }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] SnapToNearestCorner failed");
        }
    }

    /// <summary>The glass rect in this window's own coordinates (for chunk B3's hit maths).</summary>
    public Rect GlassRect
    {
        get
        {
            double bw = _bodyWidth, bh = bw * BodyAspect;
            return new Rect(bw * GlassLeftFrac, bh * GlassTopFrac, bw * GlassWidthFrac, bh * GlassHeightFrac);
        }
    }

    /// <summary>Her silhouette in PHYSICAL screen pixels. Chunk B2 anchors the ring off this.</summary>
    public Rect BodyScreenRect
    {
        get
        {
            double s = DipScale;
            double bw = _bodyWidth, bh = bw * BodyAspect;
            return new Rect((Left + OverlayPadX) * s, (Top + OverlayPad) * s, bw * s, bh * s);
        }
    }

    /// <summary>The point a fan of ring cards should orbit, in PHYSICAL screen pixels.</summary>
    public Point RingAnchorScreenPoint
    {
        get
        {
            var r = BodyScreenRect;
            return new Point(r.X + r.Width / 2.0, r.Y + r.Height * 0.48);
        }
    }

    private double DipScale
    {
        get
        {
            try
            {
                var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
                if (m.HasValue && m.Value.M11 > 0) return m.Value.M11;
            }
            catch { /* no source yet */ }
            try
            {
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96.0;
            }
            catch { return 1.0; }
        }
    }

    // ---------------------------------------------------------------- placement

    /// <summary>
    /// Put her back where she was: the persisted PHYSICAL rect on the persisted monitor, clamped
    /// into that monitor's work area. A monitor that is gone, or no saved rect at all, parks her
    /// bottom right on the main window's monitor.
    /// </summary>
    public void RestorePlacement()
    {
        try
        {
            var st = EmiState.Current;
            double s = DipScale;

            // Her width has ONE home and it is the setting (BRIEF 5: the rect, the pins and the
            // usage counters live in EmiState, the width does not). Re-read it on every summon so a
            // change made while she was away - the shrink offer, the settings slider - is on her
            // the next time she comes out. ApplyBodyWidth clamps into 152..420 itself.
            double wantW = App.Settings?.Current?.EmiDeskWidth ?? 220;
            if (Math.Abs(wantW - _bodyWidth) > 0.5) ApplyBodyWidth(wantW);

            System.Drawing.Rectangle? work = null;
            if (!string.IsNullOrWhiteSpace(st.Monitor))
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                if (screens != null && screens.Length > 0)
                {
                    foreach (var sc in screens)
                    {
                        if (string.Equals(sc.DeviceName, st.Monitor, StringComparison.OrdinalIgnoreCase))
                        {
                            work = sc.WorkingArea;
                            break;
                        }
                    }
                }
            }

            if (work != null && !double.IsNaN(st.WinLeftPx) && !double.IsNaN(st.WinTopPx))
            {
                SetBodyPhysical(st.WinLeftPx, st.WinTopPx);
                ClampIntoWorkArea();
                return;
            }

            ParkBottomRightOfMain();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] RestorePlacement failed, parking her by default");
            try { ParkBottomRightOfMain(); } catch { /* out of options */ }
        }
    }

    private void SetBodyPhysical(double bodyLeftPx, double bodyTopPx)
    {
        double s = DipScale;
        if (s <= 0) s = 1.0;
        Left = bodyLeftPx / s - OverlayPadX;
        Top = bodyTopPx / s - OverlayPad;
    }

    /// <summary>Bottom right of the monitor the main window is on, with a comfortable margin.</summary>
    public void ParkBottomRightOfMain()
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens == null || screens.Length == 0) return;

            System.Drawing.Rectangle work = screens[0].WorkingArea;
            try
            {
                var main = App.MainWindowRef;
                if (main != null)
                {
                    var h = new WindowInteropHelper(main).Handle;
                    if (h != IntPtr.Zero) work = System.Windows.Forms.Screen.FromHandle(h).WorkingArea;
                }
                else
                {
                    work = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? work;
                }
            }
            catch { /* keep screens[0] */ }

            double s = DipScale;
            if (s <= 0) s = 1.0;
            double bw = _bodyWidth * s;
            double bh = _bodyWidth * BodyAspect * s;
            double margin = 24 * s;

            SetBodyPhysical(work.Right - bw - margin, work.Bottom - bh - margin);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ParkBottomRightOfMain failed");
        }
    }

    /// <summary>Pull her fully back onto the nearest monitor's work area.</summary>
    public void ClampIntoWorkArea()
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens == null || screens.Length == 0) return;

            double s = DipScale;
            if (s <= 0) s = 1.0;
            var body = BodyScreenRect;
            var centre = new System.Drawing.Point(
                (int)Math.Round(body.X + body.Width / 2),
                (int)Math.Round(body.Y + body.Height / 2));

            var screen = System.Windows.Forms.Screen.FromPoint(centre);
            var work = screen.WorkingArea;

            double x = body.X, y = body.Y;
            if (body.Width < work.Width) x = Math.Max(work.Left, Math.Min(work.Right - body.Width, x));
            else x = work.Left;
            if (body.Height < work.Height) y = Math.Max(work.Top, Math.Min(work.Bottom - body.Height, y));
            else y = work.Top;

            SetBodyPhysical(x, y);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ClampIntoWorkArea failed");
        }
    }

    /// <summary>Persist where she is and how big, in physical pixels plus the monitor's device name.</summary>
    public void SavePlacement()
    {
        try
        {
            var body = BodyScreenRect;
            var st = EmiState.Current;
            st.WinLeftPx = body.X;
            st.WinTopPx = body.Y;

            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens != null && screens.Length > 0)
            {
                var centre = new System.Drawing.Point(
                    (int)Math.Round(body.X + body.Width / 2),
                    (int)Math.Round(body.Y + body.Height / 2));
                st.Monitor = System.Windows.Forms.Screen.FromPoint(centre).DeviceName;
            }
            EmiState.SaveSoon();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] SavePlacement failed");
        }
    }

    // ---------------------------------------------------------------- face + pose

    /// <summary>Paint one face frame. The chain player's draw hook; safe to call directly.</summary>
    public void DrawFace(string? text, bool small = false, bool flat = false)
    {
        try { FaceView.Draw(text, small, flat); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] DrawFace failed"); }
    }

    /// <summary>Swap the body pose PNG. A no-op when that pose is already up (this runs per sway step).</summary>
    public void SetPose(string? frame)
    {
        try
        {
            var key = EmiChains.FrameKey(frame) ?? "idle";
            if (key == _pose && BodyImage.Source != null) return;
            _pose = key;
            if (!_bodyCache.TryGetValue(key, out var img))
            {
                var path = EmiChains.BodyPath(key);
                if (path == null)
                {
                    // Art is allowed to arrive after the code does: keep whatever is up rather than
                    // blanking her into an invisible click target.
                    Log.Debug("[EmiDesk] body art missing for pose {Pose}", key);
                    return;
                }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
                _bodyCache[key] = img;
            }
            BodyImage.Source = img;
            PaintOutfitOver();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] SetPose failed for {Frame}", frame);
        }
    }

    /// <summary>
    /// The garment she is wearing, or null for the standard art. Null on every road today: nothing
    /// on the desk chooses an outfit yet (see <see cref="EmiChains.Outfits"/>).
    /// </summary>
    public string? Outfit => _outfit;

    /// <summary>
    /// Put a wardrobe sheet's OVERLAY on her, or take it off with null. THE SKIN LAW's one seam.
    ///
    /// <para>This is the neutral setter the campus calls <c>setOutfit</c>. It is deliberately not
    /// wired to anything: the desk has no outfit picker, no wardrobe and no outfit BODY sheets, and
    /// this method invents none of them. What it guarantees is that when a sheet does arrive, the
    /// part of it that crosses her glass resolves through
    /// <see cref="EmiChains.OverPath"/> and lands in <c>OutfitOverImage</c> - which the XAML
    /// authors ABOVE the face and the glass, so the garment is on top and stays there.</para>
    ///
    /// <para>Silent about missing art, exactly like the web: an outfit with no overlay sheet asks
    /// once, is answered once, and leaves one collapsed Image behind.</para>
    /// </summary>
    /// <param name="outfit">A name from <see cref="EmiChains.Outfits"/>, or null for standard.</param>
    public void SetOutfit(string? outfit)
    {
        try
        {
            var want = string.IsNullOrWhiteSpace(outfit) ? null : outfit!.Trim();
            if (string.Equals(want, _outfit, StringComparison.Ordinal)) return;
            _outfit = want;
            _overPose = null;
            PaintOutfitOver();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] SetOutfit failed for {Outfit}", outfit);
        }
    }

    /// <summary>
    /// Lay the overlay for the pose that is up, or take it off. The body's shadow: no timer, no
    /// pose logic and no geometry of its own - it is repainted from <see cref="SetPose"/>, which is
    /// the one place the body PNG changes, so every sway step and every face-driven pose carries it
    /// for free.
    /// </summary>
    private void PaintOutfitOver()
    {
        try
        {
            var outfit = _outfit;
            if (outfit == null || !ArmOutfitOver(outfit))
            {
                if (OutfitOverImage.Visibility != Visibility.Collapsed)
                {
                    OutfitOverImage.Visibility = Visibility.Collapsed;
                    OutfitOverImage.Source = null;
                }
                _overPose = null;
                return;
            }

            var key = outfit + "/" + _pose;
            if (key == _overPose && OutfitOverImage.Source != null) return;

            if (!_overCache.TryGetValue(key, out var img))
            {
                var path = EmiChains.OverPath(outfit, _pose);
                if (path == null)
                {
                    // A HALF-PRESENT SHEET IS NO SHEET: one pose's goggles on every other pose read
                    // as a broken sprite, so the whole layer stands down for the sitting.
                    Log.Debug("[EmiDesk] outfit overlay incomplete for {Outfit}: {Pose} missing", outfit, _pose);
                    _overArmed[outfit] = false;
                    OutfitOverImage.Visibility = Visibility.Collapsed;
                    OutfitOverImage.Source = null;
                    _overPose = null;
                    return;
                }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
                _overCache[key] = img;
            }

            _overPose = key;
            OutfitOverImage.Source = img;
            OutfitOverImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] outfit overlay paint failed");
            try
            {
                OutfitOverImage.Visibility = Visibility.Collapsed;
                OutfitOverImage.Source = null;
            }
            catch (Exception inner) { Log.Debug(inner, "[EmiDesk] outfit overlay stand-down failed"); }
        }
    }

    /// <summary>
    /// Does this sheet come with an overlay at all? ONE probe per outfit, ever; the verdict is
    /// cached for the sitting, so the three sheets that have no overlay cost one file check each
    /// and nothing after it.
    /// </summary>
    private bool ArmOutfitOver(string outfit)
    {
        if (_overArmed.TryGetValue(outfit, out var armed)) return armed;
        armed = EmiChains.OverPath(outfit, "idle") != null;
        _overArmed[outfit] = armed;
        return armed;
    }

    // ---------------------------------------------------------------- chains

    private EmiChainHooks BuildHooks(Action? done = null) => new()
    {
        Draw = (t, small, flat) => DrawFace(t, small, flat),
        Bubble = text =>
        {
            try { OnBubbleTextCore(text); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bubble seam threw"); }
        },
        BodyFrame = SetPose,
        Fx = kind =>
        {
            try { OnChainFxCore(kind); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] fx seam threw"); }
        },
        Move = RunBodyMove,
        Done = () =>
        {
            try
            {
                // chains.js settles every finished chain back to the resting face and pose.
                SetPose("idle");
                DrawFace(EmiChains.RestFace);
                RestartIdleBeats();
                done?.Invoke();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chain done failed"); }
        }
    };

    /// <summary>Play a canon chain by id (see <see cref="EmiChains.Chains"/>). Unknown ids are ignored.</summary>
    public void PlayChain(string chainId, Action? done = null, string? bodyFrameOverride = null)
        => PlayChain(EmiChains.Get(chainId), done, bodyFrameOverride);

    /// <summary>Play a chain. Cancels whatever was running; stops the idle beats for its duration.</summary>
    public void PlayChain(EmiChain? chain, Action? done = null, string? bodyFrameOverride = null)
    {
        if (chain == null) return;
        try
        {
            // A chain with a pose of its own is also a chain with a MOOD of its own; one without
            // keeps whatever the caller set (which is how Say's reaction face survives the trip).
            var voice = EmiChains.FrameKey(bodyFrameOverride) ?? EmiChains.FrameKey(chain.BodyFrame);
            if (voice != null) _voxMood = voice;

            StopIdleBeats();
            _player.Play(chain, BuildHooks(done), bodyFrameOverride);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] PlayChain failed for {Chain}", chain.Id);
        }
    }

    /// <summary>Say a line on the locked . / .. / ... cadence.</summary>
    public void Say(string? line, string reactionFace = "^_^", Action? done = null)
    {
        // The say chain carries no pose of its own (the dots stay at idle and the reaction lands
        // on the last frame), so its VOICE has to be resolved from the face here, before the
        // chain starts handing the bubble seam text.
        _voxMood = EmiChains.FrameForFace(reactionFace);
        PlayChain(EmiChains.MakeSay(line, reactionFace, EmiChains.SayHoldMs(line)), done);
    }

    /// <summary>Kill the running chain without firing its done hook.</summary>
    public void CancelChain()
    {
        try
        {
            _player.Cancel();
            OnBubbleTextCore(null);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] CancelChain failed");
        }
    }

    private void RunBodyMove(string move)
    {
        bool handled = false;
        try { OnBodyMoveCore(move, ref handled); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] body-move seam threw"); }
        if (handled) return;

        try
        {
            // The canon moves from emi.css, as WPF animations on the body's own transform group.
            // They ride BodyRoot's RenderTransform slot alongside the CRT scale, so each one is a
            // short animation on a dedicated transform rather than a class swap.
            var tt = EnsureMoveTransform();
            switch (move)
            {
                case "bounce":
                    AnimateScalePulse(0.34, 1.08, 0.94);
                    break;
                case "thud":
                    AnimateScalePulse(0.28, 1.10, 0.88);
                    break;
                case "nod":
                    AnimateOffset(tt, 0, 4, 0.9, 2);
                    break;
                case "droop":
                    AnimateOffset(tt, 0, 6, 0.6, 1);
                    break;
                case "shiver":
                    AnimateOffset(tt, 2, 0, 0.32, 4);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] body move {Move} failed", move);
        }
    }

    private TranslateTransform EnsureMoveTransform() => MoveShift;

    private void AnimateScalePulse(double seconds, double sx, double sy)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var ax = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds) };
        ax.KeyFrames.Add(new EasingDoubleKeyFrame(sx, KeyTime.FromPercent(0.4), ease));
        ax.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), ease));
        var ay = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds) };
        ay.KeyFrames.Add(new EasingDoubleKeyFrame(sy, KeyTime.FromPercent(0.4), ease));
        ay.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), ease));
        CrtScale.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
        CrtScale.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
    }

    private static void AnimateOffset(TranslateTransform tt, double dx, double dy, double seconds, int repeats)
    {
        if (dx != 0)
        {
            var a = new DoubleAnimation(-dx, dx, TimeSpan.FromSeconds(seconds))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(repeats), FillBehavior = FillBehavior.Stop };
            tt.BeginAnimation(TranslateTransform.XProperty, a);
        }
        if (dy != 0)
        {
            var a = new DoubleAnimation(0, dy, TimeSpan.FromSeconds(seconds / 2))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(repeats), FillBehavior = FillBehavior.Stop };
            tt.BeginAnimation(TranslateTransform.YProperty, a);
        }
    }

    // ---------------------------------------------------------------- idle beats

    /// <summary>True when something is on screen that an idle beat must not interrupt.</summary>
    private bool Busy()
    {
        if (_transiting || _player.IsLive || InputLocked) return true;
        bool glass = false;
        try { OnGlassLiveQuery(ref glass); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] glass-live seam threw"); }
        return glass;
    }

    /// <summary>Start (or restart) the idle blink cycle and the idle sway.</summary>
    public void RestartIdleBeats()
    {
        StopIdleBeats();
        if (_closingForGood || Visibility != Visibility.Visible) return;
        try
        {
            _idleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(EmiAlive.BlinkDelayMs(Rng))
            };
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();

            _swayAt = 0;
            _swayTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(SwayHold("idle"))
            };
            _swayTimer.Tick += OnSwayTick;
            _swayTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] RestartIdleBeats failed");
        }
    }

    /// <summary>Stop the idle blink cycle and the sway.</summary>
    public void StopIdleBeats()
    {
        try
        {
            if (_idleTimer != null) { _idleTimer.Stop(); _idleTimer.Tick -= OnIdleTick; _idleTimer = null; }
            if (_swayTimer != null) { _swayTimer.Stop(); _swayTimer.Tick -= OnSwayTick; _swayTimer = null; }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] StopIdleBeats failed");
        }
    }

    private void OnIdleTick(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.Dispatcher == null) return;
            if (Application.Current.Dispatcher.HasShutdownStarted) return;

            // THE CLOCK WANDERS, THE BLINK DOES NOT SKIP. The jitter is re-rolled every tick, so
            // the cadence is 5200 +/- 600 ms and never a metronome - which is what the old coin
            // flip was reaching for and missed, because a lost flip is twelve seconds of nothing.
            if (_idleTimer != null) _idleTimer.Interval = TimeSpan.FromMilliseconds(EmiAlive.BlinkDelayMs(Rng));

            if (Busy()) return;
            PlayIdleBlink();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] idle tick failed");
        }
    }

    private static int SwayHold(string key)
    {
        if (key != "idle") return EmiChains.SwayStepMs;
        int lo = EmiChains.SwayCentreMinMs;
        int hi = Math.Max(lo, EmiChains.SwayCentreMaxMs);
        return lo + Rng.Next(hi - lo + 1);
    }

    private void OnSwayTick(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (_swayTimer == null) return;
            if (Busy() || _dragging || _resizing)
            {
                // Stay on the clock but do not move: a sway that resumed mid-chain would flick her
                // pose out from under the chain's own.
                _swayTimer.Interval = TimeSpan.FromMilliseconds(SwayHold("idle"));
                return;
            }
            var key = EmiChains.SwayCycle[_swayAt % EmiChains.SwayCycle.Count];
            _swayAt++;
            SetPose(key);
            _swayTimer.Interval = TimeSpan.FromMilliseconds(SwayHold(key));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] sway tick failed");
        }
    }

    // ---------------------------------------------------------------- pointer

    private void RaiseActivity()
    {
        try { PointerActivity?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] PointerActivity handler threw"); }
    }

    private void OnBodyMouseEnter(object sender, MouseEventArgs e)
    {
        FadeChrome(0.95);
    }

    private void OnBodyMouseLeave(object sender, MouseEventArgs e)
    {
        FadeChrome(0);
        DisarmPet();
    }

    /// <summary>The resize grip's resting opacity: faint, but never invisible (owner call).</summary>
    private const double GripRestOpacity = 0.35;

    /// <summary>The grip's minimum hit area in DIPs. The glyph drawn inside it stays 12.</summary>
    private const double GripHitSize = 22;

    private void FadeChrome(double to)
    {
        try
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140));
            BtnClose.BeginAnimation(OpacityProperty, a);

            // The cards glyph rides the x exactly: same fade, same 140 ms, opposite corner. One
            // animation object cannot drive two elements, so it gets its own with the same value.
            var c = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140));
            BtnCards.BeginAnimation(OpacityProperty, c);

            // The x is hover-only; the grip is not. It rests faint and goes solid under the
            // pointer, so a user who has never dragged her still sees where the corner is.
            var g = new DoubleAnimation(to <= 0 ? GripRestOpacity : 1.0, TimeSpan.FromMilliseconds(140));
            ResizeGrip.BeginAnimation(OpacityProperty, g);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] chrome fade failed");
        }
    }

    private void OnBodyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (InputLocked || _transiting) { e.Handled = true; return; }
        try
        {
            RaiseActivity();
            DisarmPet();
            _dragging = true;
            _dragMoved = false;
            _dragStartScreen = PointToScreen(e.GetPosition(this));
            _dragStartLeft = Left;
            _dragStartTop = Top;
            BeginWobble();
            BodyRoot.CaptureMouse();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] body mouse down failed");
            _dragging = false;
        }
    }

    private void OnBodyMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            if (!_dragging)
            {
                UpdatePetHover(e.GetPosition(BodyRoot));
                return;
            }
            // PointToScreen gives PHYSICAL pixels; Left/Top and the threshold are DIPs. Scale
            // first, then measure, so the "is this a drag yet?" test is in the same space as the
            // ring's.
            var now = PointToScreen(e.GetPosition(this));
            double s = DipScale;
            if (s <= 0) s = 1.0;
            double dx = (now.X - _dragStartScreen.X) / s;
            double dy = (now.Y - _dragStartScreen.Y) / s;
            if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) > DragThresholdDip) _dragMoved = true;
            if (!_dragMoved) return;

            Left = _dragStartLeft + dx;
            Top = _dragStartTop + dy;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] body mouse move failed");
        }
    }

    private void OnBodyMouseUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (!_dragging) return;
            _dragging = false;
            BodyRoot.ReleaseMouseCapture();
            EndWobble();
            e.Handled = true;

            if (_dragMoved)
            {
                ClampIntoWorkArea();
                SavePlacement();
                try { Moved?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] Moved handler threw"); }
                return;
            }

            if (InputLocked || _transiting) return;

            // FEEDBACK FIRST, and unconditionally. Whatever this click turns out to mean - the
            // ring, the glass, a pet, or nothing at all because a chain is running - she visibly
            // takes it. A reaction that depends on the outcome is a reaction that is missing
            // exactly when the app is busy, which is when "she is dead" gets said.
            PlayClickSquash();

            // A click inside the glass while a channel is up belongs to the glass. Resolved
            // geometrically, not with a hit-testable overlay, so the drag above still works.
            var p = e.GetPosition(BodyRoot);
            bool glassLive = false;
            try { OnGlassLiveQuery(ref glassLive); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] glass-live seam threw"); }

            bool handled = false;
            if (glassLive && GlassRect.Contains(p))
            {
                try { OnGlassClickedCore(ref handled); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] glass-click seam threw"); }
                if (handled) return;
            }

            // QA ONLY, and deliberately awkward to hit by accident: Ctrl+Shift+Alt+click replays
            // the gesture tutorial. Checked BEFORE the pat so the pat cannot eat it.
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt))
            {
                App.EmiDesk?.ResetOnboarding();
                return;
            }

            // LEFT CLICK IS THE PAT, everywhere on her. The ring is the RIGHT button now, or the
            // cards glyph on hover. See PetFromClick for why.
            PetFromClick();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] body mouse up failed");
        }
    }

    /// <summary>
    /// RIGHT CLICK ON HER BODY OPENS THE RING (owner, 2026-08-29). It used to be the left click,
    /// which is now the pat.
    ///
    /// <para>No drag bookkeeping here on purpose: dragging is a left-button gesture, so a right
    /// click can never be the tail of a move and never needs the 6 DIP threshold. The squash still
    /// plays, so the button that does NOT pat her still visibly registers.</para>
    /// </summary>
    private void OnBodyRightClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (InputLocked || _transiting) return;

            RaiseActivity();
            PlayClickSquash();
            ToggleRingFromGesture();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] body right-click failed");
        }
    }

    /// <summary>The hover glyph: a plain left click on it opens the same ring.</summary>
    private void OnCardsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (InputLocked || _transiting) return;
            RaiseActivity();
            ToggleRingFromGesture();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] cards glyph failed");
        }
    }

    /// <summary>
    /// The one road to the ring, so the right click and the glyph cannot drift apart. Goes through
    /// the same partial seam the left click used to, which keeps the ring's own file the only place
    /// that knows what a ring is.
    /// </summary>
    private void ToggleRingFromGesture()
    {
        bool handled = false;
        try { OnBodyClickedCore(ref handled); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] body-click seam threw"); }
    }

    // ---- pet ------------------------------------------------------------------

    /// <summary>
    /// The second way to pat her: rest the pointer on her HEAD for 1.2 s. Since wave 3 the LEFT
    /// CLICK does it too, anywhere on her, and that is the gesture she teaches; this one survives
    /// because it is how you pat her without meaning to open anything, and because it is the only
    /// pat available while a card fan is up under the pointer.
    ///
    /// <para>The cooldown and the pose are the campus ones (widget.js <c>pet()</c>): inside the
    /// cooldown she only winks, so spam cannot loop the show.</para>
    /// </summary>
    private void UpdatePetHover(Point p)
    {
        try
        {
            if (!IsOnHead(p)) { DisarmPet(); return; }
            if (_petArmed || _petTimer != null) return;

            _petTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(PetHoverMs)
            };
            _petTimer.Tick += OnPetTick;
            _petTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] pet hover failed");
        }
    }

    private void DisarmPet()
    {
        try
        {
            if (_petTimer != null) { _petTimer.Stop(); _petTimer.Tick -= OnPetTick; _petTimer = null; }
            _petArmed = false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] DisarmPet failed");
        }
    }

    private void OnPetTick(object? sender, EventArgs e)
    {
        try
        {
            if (_petTimer != null) { _petTimer.Stop(); _petTimer.Tick -= OnPetTick; _petTimer = null; }
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (_transiting || InputLocked) return;
            // LAW 3: a line in flight is never cut for a head-pat. Ignore, do not queue.
            if (_player.IsLive) return;

            _petArmed = true;
            RaiseActivity();

            if (DateTime.UtcNow < _petCooldownUntil)
            {
                PlayChain("wink", bodyFrameOverride: "pet");
            }
            else
            {
                _petCooldownUntil = DateTime.UtcNow.AddMilliseconds(PetCooldownMs);
                PlayChain("pet");
                CountPat();
            }
            App.EmiDesk?.Fire("petted");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] pet failed");
        }
    }

    // ---- resize ---------------------------------------------------------------

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (InputLocked || _transiting) { e.Handled = true; return; }
        try
        {
            RaiseActivity();
            _resizing = true;
            _resizeStartScreen = PointToScreen(e.GetPosition(this));
            _resizeStartWidth = _bodyWidth;
            ResizeGrip.CaptureMouse();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] grip mouse down failed");
            _resizing = false;
        }
    }

    private void OnGripMouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        try
        {
            var now = PointToScreen(e.GetPosition(this));
            double s = DipScale;
            if (s <= 0) s = 1.0;
            double w = _resizeStartWidth + (now.X - _resizeStartScreen.X) / s;
            ApplyBodyWidth(w);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] grip mouse move failed");
        }
    }

    private void OnGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        try
        {
            _resizing = false;
            ResizeGrip.ReleaseMouseCapture();
            e.Handled = true;
            ClampIntoWorkArea();
            SavePlacement();
            var s = App.Settings?.Current;
            if (s != null && Math.Abs(s.EmiDeskWidth - _bodyWidth) > 0.5)
            {
                s.EmiDeskWidth = _bodyWidth;
                App.Settings?.Save();
            }
            App.EmiDesk?.Fire("resized");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] grip mouse up failed");
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            RaiseActivity();
            App.EmiDesk?.Dismiss();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] hover-x dismiss failed");
        }
    }

    // ---------------------------------------------------------------- teardown

    /// <summary>Let her go for good: stop every timer and drop the window. Called at app shutdown.</summary>
    public void ShutDown()
    {
        try
        {
            _closingForGood = true;
            StopIdleBeats();
            StopAlive();
            DisarmPet();
            TearDownReactions();
            TearDownVox();
            CancelChain();
            _player.Dispose();
            SweepFx(all: true);
            Close();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ShutDown failed");
        }
    }

    private void OnClosedCleanup(object? sender, EventArgs e)
    {
        try
        {
            _closingForGood = true;
            StopIdleBeats();
            StopAlive();
            DisarmPet();
            TearDownReactions();
            TearDownVox();
            if (_fxSweepTimer != null) { _fxSweepTimer.Stop(); _fxSweepTimer = null; }
            if (_dpiQuiesceTimer != null) { _dpiQuiesceTimer.Stop(); _dpiQuiesceTimer = null; }
            _player.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] close cleanup failed");
        }
    }
}
