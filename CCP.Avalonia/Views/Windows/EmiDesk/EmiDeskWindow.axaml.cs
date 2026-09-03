using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// EMI Desk's widget window: the body, the face, drag, resize, the hover x, the pet and the
    /// chrome region.
    ///
    /// <para>PORTED from <c>ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.xaml.cs</c> -
    /// and that file is ONE of eight partials of the WPF class. The other seven
    /// (<c>.Alive</c>, <c>.Bubble</c>, <c>.Fx</c>, <c>.Glass</c>, <c>.Props</c>, <c>.React</c>,
    /// <c>.Ring</c>) are not part of this layer, so every member this file calls into them is a
    /// one-line <c>ponytail:</c> stub below rather than an invention. The
    /// <c>partial void ...Core(...)</c> seams are kept VERBATIM: with no implementing partial they
    /// compile to nothing, which is exactly what B2 and B3 need to plug into later without editing
    /// this file.</para>
    ///
    /// <para><b>Windowing.</b> WPF's recipe was AllowsTransparency + WindowStyle None + Topmost +
    /// ShowActivated false + <c>WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c> via
    /// <c>GetWindowLong</c>/<c>SetWindowLong</c>, plus an <c>HwndSource</c> hook that swallowed
    /// <c>WM_DPICHANGED</c>. On this head the two ex-styles ARE
    /// <c>ShowInTaskbar="False"</c> + <c>ShowActivated="False"</c> in the markup, so both P/Invokes
    /// are gone rather than stubbed. The DPI hook is dropped: it existed because WPF's automatic
    /// rescale of a LAYERED window is a synchronous CompleteRender that deadlocked the shared
    /// render thread (#451 / #477), which is a WPF-compositor bug with no X11 twin.
    /// <c>ScalingChanged</c> now does the useful half - one re-clamp and a save when the scale
    /// settles.</para>
    ///
    /// <para><b>THE COORDINATE TRAP INVERTS.</b> WPF's <c>Left</c>/<c>Top</c> are DIPs, so the
    /// original divides by the DPI scale everywhere. Avalonia's <see cref="Window.Position"/> and
    /// <c>PointToScreen</c> are already PHYSICAL PIXELS, so those divisions are gone and the
    /// multiplications moved to the pad instead. Persistence stays in physical pixels plus a
    /// monitor name, exactly as before.</para>
    ///
    /// <para><b>What is NOT here.</b> No WebView2: the WPF original hosts none (only a doc-comment
    /// mentions the runtime, on the road into her book). No X11Overlay call: <c>Topmost</c> maps
    /// straight to <c>_NET_WM_STATE_ABOVE</c> and needs no shim.</para>
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
        /// Her width when nothing has been persisted - the fallback behind
        /// <c>CoreSettings.Current.EmiDeskWidth</c>, which is where her width actually lives.
        /// </summary>
        public const double DefaultBodyWidth = 220.0;

        /// <summary>
        /// Transparent air ABOVE AND BELOW her, in DIPs. The window is bigger than the silhouette so
        /// the summon smoke, the sparkle scatter and the speech bubble have somewhere to go.
        /// </summary>
        public const double OverlayPad = 120.0;

        /// <summary>
        /// Transparent air to her LEFT AND RIGHT, in DIPs, and it is deliberately much wider than
        /// <see cref="OverlayPad"/>.
        ///
        /// <para>Her bubble hangs off her shoulder at 58 % of her width and may be 380 DIPs wide, so
        /// at her narrowest the bubble's right edge wants to sit <c>0.58 x 152 + 380 = 468</c> DIPs
        /// from her left - 316 past her own 152. With a symmetric 120 pad the bubble ran off the end
        /// of the window and was clipped MID-WORD (owner screenshot 2026-08-29). The pad is now the
        /// widest bubble that can ever be built, on either side.</para>
        /// </summary>
        public const double OverlayPadX = 330.0;

        // The glass rect as a fraction of the body image. Copied from
        // Resources/web/arcademy/emi/emi.css (.emi-screen / .emi-glass). Any change here has to be
        // mirrored in that file, and vice versa.
        private const double GlassLeftFrac = 0.3446;
        private const double GlassTopFrac = 0.2946;
        private const double GlassWidthFrac = 0.4168;
        private const double GlassHeightFrac = 0.3763;

        // A pet is armed by hovering the HEAD: everything above the glass, plus a little of the bezel.
        private const double HeadBottomFrac = 0.30;
        private const int PetHoverMs = 1200;
        private const int PetCooldownMs = 6000;   // widget.js DIALS.PET_COOLDOWN_MS

        // Travel under this much is a CLICK, not a drag. In DIPs, and it has to stay that way: the
        // ring's own drag watch measures in DIPs off the same constant, and when this side measured
        // PHYSICAL pixels the two disagreed by the DPI scale - at the owner's 125% a 7 px hand
        // tremor was under the ring's threshold but over this one, so the ring never toggled and she
        // crept across the desktop instead (QA 2026-08-29). One constant, one space.
        private const double DragThresholdDip = 6.0;   // widget.js DIALS.DRAG_PX

        /// <summary>The face string she rests on. <c>EmiChains.RestFace</c> in the WPF head.</summary>
        private const string RestFace = "0_0";

        // ---------------------------------------------------------------- seams (B2 / B3)

        #region seams

        /// <summary>
        /// The glass canvas, exactly over the bezel rect, ON TOP of the face. Chunk B3 paints
        /// channels into it and hides it at rest; the face keeps painting underneath the whole time,
        /// which is the safety argument. Sized by <see cref="ApplyBodyWidth"/>; never resize it
        /// yourself.
        /// </summary>
        public Panel GlassHost => _glassCanvas;

        /// <summary>
        /// The full-window FX layer, over her body and under the bubble. Not hit-testable: put
        /// interactive ring cards in their own window and anchor them with
        /// <see cref="BodyScreenRect"/>, because this window is only a pad wider than she is.
        /// </summary>
        public Panel OverlayHost => _overlayCanvas;

        /// <summary>The topmost layer, for chunk B3's speech bubble. Not hit-testable.</summary>
        public Panel BubbleHost => _bubbleCanvas;

        /// <summary>
        /// True while she must ignore pointer input: during the summon / dismiss transitions. B2 and
        /// B3 must honour it (no ring open, no glass tap) so a click cannot land mid-CRT.
        /// </summary>
        public bool InputLocked { get; private set; }

        /// <summary>Any pointer input on her: a press, a drag, a resize, a hover that armed the pet.</summary>
        public event EventHandler? PointerActivity;

        /// <summary>Her body width changed (DIPs). Fires after the layout has been applied.</summary>
        /// <remarks>
        /// <c>new</c>, deliberately: Avalonia's <c>WindowBase</c> has a <c>Resized</c> event of its
        /// own that WPF's did not, and it reports the WINDOW's size in DIPs. Hers reports her BODY
        /// width, which is the number every consumer of this window actually anchors off, so the
        /// name stays and the base event is hidden rather than the port inventing a second one.
        /// </remarks>
        public new event EventHandler<double>? Resized;

        /// <summary>She was moved. Fires once per drop, not per pointer-move.</summary>
        public event EventHandler? Moved;

        /// <summary>
        /// Chunk B2: a click on her body that was not a drag. Set <paramref name="handled"/> to true
        /// to claim it (opening or closing the ring). Unhandled clicks do nothing.
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
        /// Chunk B2: is the ring open right now? Asked as a query rather than tracked as a flag so
        /// B2 stays the only owner of the ring's state.
        /// </summary>
        partial void OnRingOpenQuery(ref bool open);

        /// <summary>
        /// Chunk B2 / B3: the widget has been built and its first layout applied. Start the ambient
        /// loops here, never in a static initialiser, so a second widget could never inherit the
        /// first one's timers.
        /// </summary>
        partial void OnReadyCore();

        /// <summary>
        /// Chunk B2 / B3: the widget is about to go away (dismiss, or the app is closing). Tear the
        /// ring, the glass and any open ask down here.
        /// </summary>
        partial void OnTearDownCore();

        /// <summary>Chunk B3: the bubble text changed. Null clears it.</summary>
        partial void OnBubbleTextCore(string? text);

        /// <summary>
        /// Chunk B2 / B3: a chain asked for a one-shot particle burst (hearts, sparks, tears, storm,
        /// bang). B1 draws nothing for these; they are decoration the later chunks own.
        /// </summary>
        partial void OnChainFxCore(string kind);

        /// <summary>
        /// A chain asked for a one-shot body move (bounce, nod, droop, shiver, thud). The seam
        /// exists so a later chunk can add art-driven moves.
        /// </summary>
        partial void OnBodyMoveCore(string move, ref bool handled);

        #endregion

        // ---------------------------------------------------------------- controls

        private readonly Grid _bodyRoot;
        private readonly Border _bodyPlaceholder;
        private readonly Image _bodyImage;
        private readonly Canvas _faceLayer;
        private readonly TextBlock _faceView;
        private readonly Canvas _glassCanvas;
        private readonly Image _outfitOverImage;
        private readonly Image _propImage;
        private readonly Button _btnClose, _btnGear, _btnHelp;
        private readonly Border _resizeGrip;
        private readonly Canvas _overlayCanvas, _bubbleCanvas;

        // The four body transform slots. Built here rather than named in the markup because
        // FindControl is constrained to Control and a Transform is not one. Order matters: the
        // rotate sits ABOVE the scales, or a squash applied after a rotation shears her.
        private readonly ScaleTransform _crtScale = new(1, 1);
        private readonly ScaleTransform _squashScale = new(1, 1);
        private readonly RotateTransform _wobbleRotate = new(0);
        private readonly TranslateTransform _moveShift = new();

        // THE GAZE LEAN's own transform, private to the face so it composes with the body's rather
        // than fighting it.
        private readonly TranslateTransform _gazeShift = new();

        // ---------------------------------------------------------------- state

        private DispatcherTimer? _petTimer;

        private double _bodyWidth = DefaultBodyWidth;
        private string _pose = "idle";
        private DateTime _petCooldownUntil = DateTime.MinValue;
        private bool _petArmed;
        // ponytail: set by EmiDeskWindow.Fx.cs around the summon / dismiss transitions, which is
        // not part of this layer - hence the explicit initialiser. Every gate that reads it is
        // ported, so the moment that partial lands the locks work.
        private bool _transiting = false;
        private bool _closingForGood;
        private string? _outfit;

        private PixelPoint _dragStartScreen;
        private PixelPoint _dragStartPosition;
        private bool _dragging, _dragMoved;
        private bool _resizing;
        private PixelPoint _resizeStartScreen;
        private double _resizeStartWidth;

        /// <summary>True while a chain is on screen.</summary>
        // ponytail: needs EmiChains.Player (Services/EmiDesk/EmiChains.cs), wired when the chain
        // player moves to Core. Nothing on this head can start a chain, so nothing is ever live.
        public bool ChainLive => false;

        /// <summary>True while a summon or dismiss transition is running.</summary>
        public bool Transiting => _transiting;

        /// <summary>Her body width in DIPs.</summary>
        public double BodyWidth => _bodyWidth;

        /// <summary>The garment she is wearing, or null for the standard art.</summary>
        public string? Outfit => _outfit;

        // ---------------------------------------------------------------- ctor

        /// <summary>
        /// Builds the widget. It is created hidden; <c>EmiDeskService</c> summons it. Also the
        /// render constructor - it takes no arguments and touches no screen, so
        /// <c>--render-view EmiDeskWindow</c> can construct it headless.
        /// </summary>
        public EmiDeskWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _bodyRoot = this.FindControl<Grid>("BodyRoot")!;
            _bodyPlaceholder = this.FindControl<Border>("BodyPlaceholder")!;
            _bodyImage = this.FindControl<Image>("BodyImage")!;
            _faceLayer = this.FindControl<Canvas>("FaceLayer")!;
            _faceView = this.FindControl<TextBlock>("FaceView")!;
            _glassCanvas = this.FindControl<Canvas>("GlassCanvas")!;
            _outfitOverImage = this.FindControl<Image>("OutfitOverImage")!;
            _propImage = this.FindControl<Image>("PropImage")!;
            _btnClose = this.FindControl<Button>("BtnClose")!;
            _btnGear = this.FindControl<Button>("BtnGear")!;
            _btnHelp = this.FindControl<Button>("BtnHelp")!;
            _resizeGrip = this.FindControl<Border>("ResizeGrip")!;
            _overlayCanvas = this.FindControl<Canvas>("OverlayCanvas")!;
            _bubbleCanvas = this.FindControl<Canvas>("BubbleCanvas")!;

            _bodyRoot.RenderTransform = new TransformGroup
            {
                Children = { _crtScale, _squashScale, _wobbleRotate, _moveShift }
            };
            _faceView.RenderTransform = _gazeShift;

            _bodyRoot.PointerPressed += OnBodyPointerPressed;
            _bodyRoot.PointerMoved += OnBodyPointerMoved;
            _bodyRoot.PointerReleased += OnBodyPointerReleased;
            _bodyRoot.PointerCaptureLost += (_, _) => EndDragHold();

            // THE HOVER REGION, not one element's hover. Her body and all three chips report in
            // separately, so the chrome stays lit while the pointer is on ANY of them and the trip
            // between two of them cannot start a fade. See EmiChromeHover, in Core.
            WireChromePart(_bodyRoot, EmiChromePart.Body);
            WireChromePart(_btnClose, EmiChromePart.Close);
            WireChromePart(_btnGear, EmiChromePart.Gear);
            WireChromePart(_btnHelp, EmiChromePart.Help);
            WireChromePart(_resizeGrip, EmiChromePart.Grip);

            _btnClose.Click += OnCloseClick;
            _btnGear.Click += OnGearClick;
            _btnHelp.Click += OnHelpClick;

            // A press on a chip is a HOLD: the button has capture, so a pointer that slides off it
            // mid-press raises a leave the region would otherwise act on, and the chrome would fade
            // out from under a button that is still held down. Cleared on the release and again on
            // lost capture, because a press that ends outside the window never sees the up.
            WireChromePress(_btnClose);
            WireChromePress(_btnGear);
            WireChromePress(_btnHelp);

            _resizeGrip.PointerPressed += OnGripPointerPressed;
            _resizeGrip.PointerMoved += OnGripPointerMoved;
            _resizeGrip.PointerReleased += OnGripPointerReleased;
            _resizeGrip.PointerCaptureLost += (_, _) => EndResizeHold();

            // At rest the chips are invisible AND inert (see FadeChrome). Set here rather than in
            // the markup so the two halves of "lit" are decided in one place.
            _btnClose.IsHitTestVisible = false;
            _btnGear.IsHitTestVisible = false;
            _btnHelp.IsHitTestVisible = false;

            // Her width has ONE home and it is the setting (BRIEF 5: the rect, the pins and the
            // usage counters live in EmiState, the width does not).
            try { _bodyWidth = ClampWidth(CoreSettings.Current.EmiDeskWidth); }
            catch { _bodyWidth = DefaultBodyWidth; }

            // One controlled re-clamp when the monitor scale settles. This is the useful half of
            // WPF's WM_DPICHANGED hook; the deadlock the other half guarded against was a WPF
            // layered-window compositor bug and has no X11 twin.
            ScalingChanged += OnScalingSettled;
            Closed += OnClosedCleanup;

            ApplyBodyWidth(_bodyWidth);
            SetPose("idle");
            DrawFace(RestFace);

            try { OnReadyCore(); }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] ready seam threw"); }
        }

        /// <summary>
        /// ALIVE wave A rides one poll that lives exactly as long as she is on screen, and the
        /// chrome region has to forget her when she goes: a hide takes the window out from under the
        /// pointer WITHOUT a leave, so the body would stay latched "hovered" and she would come back
        /// next summon with her x and her gear already lit and nobody near her.
        ///
        /// <para>WPF hung this off <c>IsVisibleChanged</c>; Avalonia has no such event, and the
        /// property change is the same signal without an extra subscription to dispose.</para>
        /// </summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsVisibleProperty) return;
            try
            {
                if (IsVisible) { StartAlive(); return; }
                StopAlive();
                CloseOptionsPanel();
                ResetChrome();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] alive visibility hook failed"); }
        }

        private void OnScalingSettled(object? sender, EventArgs e)
        {
            try
            {
                ClampIntoWorkArea();
                SavePlacement();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] scaling settle refit failed"); }
        }

        // ---------------------------------------------------------------- layout

        private static double ClampWidth(double w)
        {
            if (double.IsNaN(w) || double.IsInfinity(w)) return DefaultBodyWidth;
            return Math.Max(MinBodyWidth, Math.Min(MaxBodyWidth, w));
        }

        /// <summary>
        /// Resize her. Width is clamped to 152..420 DIPs; the height, the window, the glass rect and
        /// the three corner affordances all follow. Raises <see cref="Resized"/>.
        /// </summary>
        public void ApplyBodyWidth(double width)
        {
            try
            {
                _bodyWidth = ClampWidth(width);
                double bw = _bodyWidth;
                double bh = bw * BodyAspect;

                _bodyRoot.Width = bw;
                _bodyRoot.Height = bh;
                Width = bw + OverlayPadX * 2;
                Height = bh + OverlayPad * 2;

                double gl = bw * GlassLeftFrac;
                double gt = bh * GlassTopFrac;
                double gw = bw * GlassWidthFrac;
                double gh = bh * GlassHeightFrac;

                _faceView.Width = gw;
                _faceView.Height = gh;
                // The pixel face fills the bezel; the placeholder's type size is what actually
                // reads at that scale, so it tracks the glass rather than sitting at a fixed size.
                _faceView.FontSize = Math.Max(10, gh * 0.55);
                Canvas.SetLeft(_faceView, gl);
                Canvas.SetTop(_faceView, gt);

                _glassCanvas.Width = gw;
                _glassCanvas.Height = gh;
                Canvas.SetLeft(_glassCanvas, gl);
                Canvas.SetTop(_glassCanvas, gt);

                // The drag wobble swings her from the HEAD, not from her middle: a mascot held by
                // the scruff. BodyRoot's RenderTransformOrigin is 0.55 of her height, so a centre of
                // -0.25 x height puts the pivot at 0.30 - the top of the bezel. It is set here
                // because it is the only place that knows her current height.
                _wobbleRotate.CenterX = 0;
                _wobbleRotate.CenterY = -bh * 0.25;

                // The affordances scale a little with her so they stay reachable but never eat the
                // face: 8 % of the body width, floored at their authored size. That is the DRAWN
                // chip; the button around it is bigger.
                double chip = Math.Max(18, bw * 0.08);
                double insetX = bw * 0.02;
                double insetY = bh * 0.02;

                // THE FORGIVING RING (owner, 2026-08-30). Each button grows to chip + 2 x ChipPad of
                // invisible hit area with the drawn chip held in place by the button's Padding, so a
                // near miss still lands. The split is ASYMMETRIC because the pad must not overhang
                // BodyRoot: the two OUTWARD sides can only take the inset that is really there
                // (~4 DIPs at her default width), so whatever they cannot use is handed to the two
                // inward sides, which have her whole silhouette behind them. The margins below are
                // non-negative by construction, and the drawn chip does not move a pixel.
                double closeTop = Math.Min(ChipPad, insetY);
                double closeRight = Math.Min(ChipPad, insetX);
                _btnClose.Padding = new Thickness(2 * ChipPad - closeRight, closeTop,
                                                  closeRight, 2 * ChipPad - closeTop);
                _btnClose.Width = chip + 2 * ChipPad;
                _btnClose.Height = chip + 2 * ChipPad;
                _btnClose.Margin = new Thickness(0, insetY - closeTop, insetX - closeRight, 0);

                // The gear is the x's mirror: same chip, same inset, other corner, outward sides
                // swapped to top and left.
                double gearTop = Math.Min(ChipPad, insetY);
                double gearLeft = Math.Min(ChipPad, insetX);
                _btnGear.Padding = new Thickness(gearLeft, gearTop,
                                                 2 * ChipPad - gearLeft, ChipStackPad);
                _btnGear.Width = chip + 2 * ChipPad;
                _btnGear.Height = chip + gearTop + ChipStackPad;
                _btnGear.Margin = new Thickness(insetX - gearLeft, insetY - gearTop, 0, 0);

                // The ? rides directly under the gear, same column, same drawn size. Its top margin
                // is the gear's margin plus the gear's FULL height, which is the hit area and not
                // the chip, so the two forgiving squares sit edge to edge and can never both claim a
                // click.
                //
                // THE FACING SIDES ARE THIN (owner, 2026-08-30: "raise a little bit that ? button,
                // seems a bit too far away from the gear"). ChipStackPad is what the two sides that
                // FACE EACH OTHER take instead, and it is the whole of the visible gap. No hit area
                // is lost: the pair still tile the same column edge to edge, so a low miss on the
                // gear now lands on the ? instead of on nothing.
                _btnHelp.Padding = new Thickness(gearLeft, ChipStackPad,
                                                 2 * ChipPad - gearLeft, 2 * ChipPad - ChipStackPad);
                _btnHelp.Width = _btnGear.Width;
                _btnHelp.Height = chip + 2 * ChipPad;
                _btnHelp.Margin = new Thickness(_btnGear.Margin.Left,
                                                _btnGear.Margin.Top + _btnGear.Height, 0, 0);

                // The grip's HIT AREA, floored at 22 DIP so the cursor catches it at every body
                // width (owner call, QA 2026-08-29). The glyph inside it does not grow with it.
                double grip = Math.Max(GripHitSize, bw * 0.09);
                _resizeGrip.Width = grip;
                _resizeGrip.Height = grip;
                _resizeGrip.Margin = new Thickness(0, 0, bw * 0.01, bh * 0.01);

                // A plate in her hand mid-resize moves with her. No-ops when she is holding nothing,
                // which is always on this head.
                LayoutProp();

                Resized?.Invoke(this, _bodyWidth);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ApplyBodyWidth failed");
            }
        }

        /// <summary>
        /// Put her in the nearest corner of the work area she is currently on, with a small inset.
        /// Used by the shrink offer, where she makes herself small and tidies herself out of the way
        /// in one move; a shrink that left her floating mid-screen would read as a glitch.
        /// </summary>
        public void SnapToNearestCorner()
        {
            try
            {
                double s = DipScale;
                var body = BodyScreenRect;
                var work = WorkAreaAt(body);
                if (work is null) return;
                var wa = work.Value;

                double insetPx = 12 * s;
                double cx = body.X + body.Width / 2.0;
                double cy = body.Y + body.Height / 2.0;

                double bodyLeftPx = cx < wa.X + wa.Width / 2.0
                    ? wa.X + insetPx
                    : wa.Right - insetPx - body.Width;
                double bodyTopPx = cy < wa.Y + wa.Height / 2.0
                    ? wa.Y + insetPx
                    : wa.Bottom - insetPx - body.Height;

                SetBodyPhysical(bodyLeftPx, bodyTopPx);
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
                // Position is ALREADY physical pixels here, unlike WPF's Left/Top - only the pad
                // needs scaling into that space.
                var p = Position;
                return new Rect(p.X + OverlayPadX * s, p.Y + OverlayPad * s, bw * s, bh * s);
            }
        }

        /// <summary>The point a fan of ring cards should orbit, in PHYSICAL screen pixels.</summary>
        public Point RingAnchorScreenPoint
        {
            get
            {
                var r = BodyScreenRect;
                // Orbit her SCREEN, not the PNG's midline. The art is not centred in its own canvas:
                // the glass sits at 0.553 across and 0.483 down. Both come off the SAME rect so they
                // cannot drift apart again.
                return new Point(r.X + r.Width * (GlassLeftFrac + GlassWidthFrac / 2.0),
                                 r.Y + r.Height * (GlassTopFrac + GlassHeightFrac / 2.0));
            }
        }

        /// <summary>
        /// Device pixels per DIP. WPF dug this out of the PresentationSource's TransformToDevice;
        /// Avalonia hands it over directly, and falls back to 1 before the window has a surface.
        /// </summary>
        private double DipScale
        {
            get
            {
                try
                {
                    double s = RenderScaling;
                    if (s > 0) return s;
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] no render scaling yet"); }
                return 1.0;
            }
        }

        /// <summary>
        /// The work area of the monitor a physical-pixel rect sits on, or null when this process has
        /// no screens at all (headless CI, which is where <c>--render-all</c> runs).
        /// </summary>
        private PixelRect? WorkAreaAt(Rect bodyPx)
        {
            try
            {
                var screens = Screens;
                if (screens is null || screens.ScreenCount == 0) return null;
                var centre = new PixelPoint(
                    (int)Math.Round(bodyPx.X + bodyPx.Width / 2),
                    (int)Math.Round(bodyPx.Y + bodyPx.Height / 2));
                var screen = screens.ScreenFromPoint(centre) ?? screens.Primary;
                return screen?.WorkingArea;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] work area lookup failed");
                return null;
            }
        }

        // ---------------------------------------------------------------- placement

        /// <summary>
        /// Put her back where she was: the persisted PHYSICAL rect on the persisted monitor, clamped
        /// into that monitor's work area. A monitor that is gone, or no saved rect at all, parks her
        /// bottom right on the main window's monitor.
        /// </summary>
        // ponytail: needs EmiState (Services/EmiDesk/EmiState.cs) for the saved rect and the
        // monitor name, which is not in Core. Until it is she is parked by default on every summon,
        // which is what a first run does anyway. The WIDTH half of the WPF body is ported: it lives
        // in the setting, not in EmiState, and re-reading it here is what puts a change made while
        // she was away - the shrink offer, the settings slider - on her the next time she comes out.
        public void RestorePlacement()
        {
            try
            {
                double wantW = CoreSettings.Current.EmiDeskWidth;
                if (Math.Abs(wantW - _bodyWidth) > 0.5) ApplyBodyWidth(wantW);
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
            // Position is physical pixels on both sides of this, so only the pad converts.
            Position = new PixelPoint((int)Math.Round(bodyLeftPx - OverlayPadX * s),
                                      (int)Math.Round(bodyTopPx - OverlayPad * s));
        }

        /// <summary>Bottom right of the monitor she is on, with a comfortable margin.</summary>
        // ponytail: WPF picked the monitor from App.MainWindowRef's HWND. No App shell on this head
        // yet, so it is her own screen, then the primary.
        public void ParkBottomRightOfMain()
        {
            try
            {
                var screens = Screens;
                if (screens is null || screens.ScreenCount == 0) return;
                var work = (screens.ScreenFromWindow(this) ?? screens.Primary)?.WorkingArea;
                if (work is null) return;
                var wa = work.Value;

                double s = DipScale;
                if (s <= 0) s = 1.0;
                double bw = _bodyWidth * s;
                double bh = _bodyWidth * BodyAspect * s;
                double margin = 24 * s;

                SetBodyPhysical(wa.Right - bw - margin, wa.Bottom - bh - margin);
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
                var body = BodyScreenRect;
                var work = WorkAreaAt(body);
                if (work is null) return;
                var wa = work.Value;

                double x = body.X, y = body.Y;
                if (body.Width < wa.Width) x = Math.Max(wa.X, Math.Min(wa.Right - body.Width, x));
                else x = wa.X;
                if (body.Height < wa.Height) y = Math.Max(wa.Y, Math.Min(wa.Bottom - body.Height, y));
                else y = wa.Y;

                SetBodyPhysical(x, y);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ClampIntoWorkArea failed");
            }
        }

        /// <summary>Persist where she is and how big, in physical pixels plus the monitor's name.</summary>
        // ponytail: needs EmiState (Services/EmiDesk/EmiState.cs), which is not in Core. The
        // geometry it would persist is BodyScreenRect plus Screens.ScreenFromPoint(...).DisplayName.
        public void SavePlacement()
        {
        }

        // ---------------------------------------------------------------- face + pose

        /// <summary>Paint one face frame. The chain player's draw hook; safe to call directly.</summary>
        // ponytail: needs EmiFace (Services/EmiDesk/EmiFace.cs), which renders the mood string as
        // pixel glyphs and honours `small` and `flat`. The placeholder draws the same string as mono
        // text in the same pink, so the glass is never blank and the cadence is still visible.
        public void DrawFace(string? text, bool small = false, bool flat = false)
        {
            try { _faceView.Text = string.IsNullOrEmpty(text) ? RestFace : text; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] DrawFace failed"); }
        }

        /// <summary>Swap the body pose PNG. A no-op when that pose is already up (this runs per sway step).</summary>
        // ponytail: needs EmiChains.FrameKey / EmiChains.BodyPath (Services/EmiDesk/EmiChains.cs) to
        // resolve a pose name to a file on disk, plus the BitmapImage cache that went with it.
        // Nothing here can put art on BodyImage, so BodyPlaceholder stays up; the pose name is still
        // tracked because the outfit overlay keys off it.
        public void SetPose(string? frame)
        {
            try
            {
                var key = string.IsNullOrWhiteSpace(frame) ? "idle" : frame!.Trim();
                if (key == _pose) return;
                _pose = key;
                // The stand-in stands down the moment real art lands, whichever road brings it.
                _bodyPlaceholder.IsVisible = _bodyImage.Source is null;
                PaintOutfitOver();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] SetPose failed for {Frame}", frame);
            }
        }

        /// <summary>
        /// Put a wardrobe sheet's OVERLAY on her, or take it off with null. THE SKIN LAW's one seam.
        ///
        /// <para>Deliberately not wired to anything: the desk has no outfit picker, no wardrobe and
        /// no outfit BODY sheets, and this method invents none of them. What it guarantees is that
        /// when a sheet does arrive, the part of it that crosses her glass lands in
        /// <c>OutfitOverImage</c> - which the markup authors ABOVE the face and the glass, so the
        /// garment is on top and stays there.</para>
        /// </summary>
        public void SetOutfit(string? outfit)
        {
            try
            {
                var want = string.IsNullOrWhiteSpace(outfit) ? null : outfit!.Trim();
                if (string.Equals(want, _outfit, StringComparison.Ordinal)) return;
                _outfit = want;
                PaintOutfitOver();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] SetOutfit failed for {Outfit}", outfit);
            }
        }

        /// <summary>
        /// Lay the overlay for the pose that is up, or take it off. The body's shadow: no timer, no
        /// pose logic and no geometry of its own - it is repainted from <see cref="SetPose"/>, which
        /// is the one place the body PNG changes.
        /// </summary>
        // ponytail: needs EmiChains.OverPath (Services/EmiDesk/EmiChains.cs) for the one-probe-per-
        // outfit file lookup and its cache. Without it there is no sheet to find, so the layer stays
        // down - which is exactly its resting state on the WPF head today, where nothing picks an
        // outfit either.
        private void PaintOutfitOver()
        {
            try
            {
                _outfitOverImage.IsVisible = _outfitOverImage.Source is not null;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] outfit overlay paint failed"); }
        }

        // ---------------------------------------------------------------- chains

        /// <summary>Play a canon chain by id. Unknown ids are ignored.</summary>
        // ponytail: needs EmiChains + EmiChains.Player (Services/EmiDesk/EmiChains.cs) - the chain
        // table, the frame clock and the Draw/Bubble/BodyFrame/Fx/Move hook set. Wired when the
        // chain player moves to Core.
        public void PlayChain(string chainId, Action? done = null, string? bodyFrameOverride = null)
        {
            done?.Invoke();
        }

        /// <summary>Say a line on the locked . / .. / ... cadence.</summary>
        // ponytail: needs EmiChains.MakeSay / SayHoldMs / FrameForFace, none of them in Core.
        public void Say(string? line, string reactionFace = "^_^", Action? done = null)
        {
            done?.Invoke();
        }

        /// <summary>Kill the running chain without firing its done hook.</summary>
        public void CancelChain()
        {
            try { OnBubbleTextCore(null); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] CancelChain failed"); }
        }

        // ponytail: the five canon body moves (bounce, thud, nod, droop, shiver) were WPF
        // DoubleAnimations on _crtScale / _moveShift. Reachable only through a chain, and nothing on
        // this head can start one, so the animation is left out rather than ported blind - the
        // upgrade is an Avalonia.Animation.Animation with KeyFrames on the same two transforms.
        private void RunBodyMove(string move)
        {
            bool handled = false;
            try { OnBodyMoveCore(move, ref handled); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] body-move seam threw"); }
        }

        // ---------------------------------------------------------------- idle beats

        /// <summary>True when something is on screen that an idle beat must not interrupt.</summary>
        private bool Busy()
        {
            if (_transiting || InputLocked) return true;
            bool glass = false;
            try { OnGlassLiveQuery(ref glass); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] glass-live seam threw"); }
            return glass;
        }

        /// <summary>Start (or restart) the idle blink cycle and the idle sway.</summary>
        // ponytail: needs EmiAlive.BlinkDelayMs and EmiChains.SwayCycle / SwayStepMs /
        // SwayCentreMinMs / SwayCentreMaxMs (Services/EmiDesk/), neither in Core. The two timers
        // themselves port straight across once those numbers do - the blink clock re-rolls its
        // jitter every tick so the cadence is 5200 +/- 600 ms and never a metronome.
        public void RestartIdleBeats()
        {
        }

        /// <summary>Stop the idle blink cycle and the sway.</summary>
        public void StopIdleBeats()
        {
        }

        // ---------------------------------------------------------------- pointer

        private void RaiseActivity()
        {
            try { PointerActivity?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] PointerActivity handler threw"); }
        }

        // ---- the chrome region ----------------------------------------------------

        /// <summary>
        /// WHEN HER CHROME IS LIT (owner, 2026-08-30: <i>"I gotta hover EMI and be fast enough to
        /// catch those buttons before they disappear"</i>).
        ///
        /// <para>The decision is a pure state machine in <see cref="EmiChromeHover"/> - which is
        /// already in Core, so this half ports whole: it only feeds it pointer events and paints the
        /// answer. Her body and all three chips are ONE region, and leaving it starts a grace timer
        /// instead of a fade.</para>
        /// </summary>
        private readonly EmiChromeHover _chrome = new();

        /// <summary>
        /// The one-shot that ends the grace. Never a poll: the region only changes on a pointer
        /// event, so a timer that ticks when nothing has happened is a timer with nothing to say.
        /// </summary>
        private DispatcherTimer? _chromeGrace;

        /// <summary>What <see cref="FadeChrome"/> last painted, so a move cannot restart a fade.</summary>
        private bool _chromeLit;

        /// <summary>The resize grip's resting opacity: faint, but never invisible (owner call).</summary>
        private const double GripRestOpacity = 0.35;

        /// <summary>The grip's minimum hit area in DIPs. The glyph drawn inside it stays 12.</summary>
        private const double GripHitSize = 22;

        /// <summary>
        /// The invisible forgiveness around each 18 DIP corner chip, in DIPs, per side. Applied
        /// ASYMMETRICALLY by <see cref="ApplyBodyWidth"/> so the hit area grows to
        /// <c>chip + 2 x ChipPad</c> without one pixel of it hanging over the edge of
        /// <c>BodyRoot</c> into the click-through air.
        /// </summary>
        private const double ChipPad = 8.0;

        /// <summary>
        /// What the gear's BOTTOM edge and the top edge of the ? take of the forgiving pad, in DIPs.
        /// Small, because those two sides face each other and the pad between them is the whole of
        /// what the eye reads as the distance between the two chips.
        /// </summary>
        private const double ChipStackPad = 3.0;

        /// <summary>Report one element's enters and leaves into the region.</summary>
        private void WireChromePart(Control el, EmiChromePart part)
        {
            el.PointerEntered += (_, _) => ChromeEnter(part);
            el.PointerExited += (_, _) =>
            {
                ChromeLeave(part);
                // The 1.2 s head-pat is armed off her BODY and nothing else, so it disarms with the
                // body and not with a chip: sliding from her forehead onto the x is not "the pointer
                // left her", but it is certainly not still a pat either.
                if (part == EmiChromePart.Body) DisarmPet();
            };
        }

        /// <summary>Hold the chrome open for as long as a chip is held down, however the press ends.</summary>
        private void WireChromePress(Button btn)
        {
            // TUNNEL, and that is load-bearing: Avalonia's Button marks PointerPressed handled in
            // its own bubble-phase handler, so a plain += here would never run. WPF needed no such
            // trick because it had a preview phase of its own, which is the same thing under
            // another name.
            btn.AddHandler(PointerPressedEvent, (_, _) => ChromeHold(EmiChromeHold.Press, true),
                           RoutingStrategies.Tunnel);
            btn.AddHandler(PointerReleasedEvent, (_, _) => ChromeHold(EmiChromeHold.Press, false),
                           RoutingStrategies.Tunnel);
            btn.PointerCaptureLost += (_, _) => ChromeHold(EmiChromeHold.Press, false);
        }

        private void ChromeEnter(EmiChromePart part)
        {
            try { if (_chrome.Enter(part, DateTime.UtcNow)) ApplyChrome(); else ArmChromeGrace(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chrome enter failed"); }
        }

        private void ChromeLeave(EmiChromePart part)
        {
            try { if (_chrome.Leave(part, DateTime.UtcNow)) ApplyChrome(); else ArmChromeGrace(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chrome leave failed"); }
        }

        /// <summary>Set or clear a sticky reason the chrome must stay lit.</summary>
        private void ChromeHold(EmiChromeHold reason, bool on)
        {
            try { if (_chrome.Hold(reason, on, DateTime.UtcNow)) ApplyChrome(); else ArmChromeGrace(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chrome hold failed"); }
        }

        /// <summary>Drop every latch. Used when she is hidden out from under the pointer.</summary>
        private void ResetChrome()
        {
            try
            {
                bool changed = _chrome.Reset();
                StopChromeGrace();
                if (changed || _chromeLit) ApplyChrome();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chrome reset failed"); }
        }

        /// <summary>
        /// Arm, re-arm or drop the one-shot that ends the grace. Re-armed on every event rather than
        /// left running, because a leave-return-leave cycle moves the deadline and the old timer
        /// would fire against a deadline that no longer exists.
        /// </summary>
        private void ArmChromeGrace()
        {
            try
            {
                if (!_chrome.GracePending) { StopChromeGrace(); return; }

                double ms = _chrome.GraceRemainingMs(DateTime.UtcNow);
                if (ms <= 0) { OnChromeGraceTick(null, EventArgs.Empty); return; }

                if (_chromeGrace == null)
                {
                    _chromeGrace = new DispatcherTimer(DispatcherPriority.Background);
                    _chromeGrace.Tick += OnChromeGraceTick;
                }
                _chromeGrace.Stop();
                _chromeGrace.Interval = TimeSpan.FromMilliseconds(ms);
                _chromeGrace.Start();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chrome grace arm failed"); }
        }

        private void StopChromeGrace()
        {
            try { _chromeGrace?.Stop(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chrome grace stop failed"); }
        }

        private void OnChromeGraceTick(object? sender, EventArgs e)
        {
            try
            {
                StopChromeGrace();
                if (_closingForGood) return;
                if (_chrome.Tick(DateTime.UtcNow)) ApplyChrome();
                else ArmChromeGrace();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chrome grace tick failed"); }
        }

        /// <summary>Paint whatever the region decided, and re-arm the grace if one is now running.</summary>
        private void ApplyChrome()
        {
            _chromeLit = _chrome.Lit;
            FadeChrome(_chromeLit ? 0.95 : 0.0);
            ArmChromeGrace();
        }

        /// <summary>
        /// WPF drove four separate 140 ms <c>DoubleAnimation</c>s here, one per element, because one
        /// animation object cannot drive two. Avalonia puts the 140 ms on each element's own
        /// <c>Transitions</c> in the markup, so this is four assignments.
        /// </summary>
        private void FadeChrome(double to)
        {
            try
            {
                bool lit = to > 0;

                _btnClose.Opacity = to;
                _btnGear.Opacity = to;
                _btnHelp.Opacity = to;

                // BOTH CHIPS ARE ONLY HIT-TESTABLE WHILE THEY ARE LIT, and that is not cosmetic.
                // Their hit areas are chip + 2 x ChipPad DIPs, which is a 34 DIP square in each top
                // corner of her at her default width; left permanently hit-testable, those squares
                // would eat the left click that wave 3 made the pat, in the two corners of her the
                // pointer arrives at most often.
                _btnClose.IsHitTestVisible = lit;
                _btnGear.IsHitTestVisible = lit;
                _btnHelp.IsHitTestVisible = lit;

                // The x is hover-only; the grip is not. It rests faint and goes solid under the
                // pointer, so a user who has never dragged her still sees where the corner is - and
                // it stays hit-testable at rest for the same reason.
                _resizeGrip.Opacity = lit ? 1.0 : GripRestOpacity;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] chrome fade failed");
            }
        }

        // ---- drag -----------------------------------------------------------------

        private void OnBodyPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_bodyRoot).Properties.IsLeftButtonPressed) return;
            if (InputLocked || _transiting) { e.Handled = true; return; }
            try
            {
                RaiseActivity();
                DisarmPet();
                // Moving her walks the pointer off her silhouette in the first few pixels at any
                // speed worth calling a drag. Hold the chrome open for the whole gesture, or the x
                // and the gear vanish the instant she starts to move.
                ChromeHold(EmiChromeHold.Drag, true);
                _dragging = true;
                _dragMoved = false;
                _dragStartScreen = this.PointToScreen(e.GetPosition(this));
                _dragStartPosition = Position;
                BeginWobble();
                e.Pointer.Capture(_bodyRoot);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] body pointer down failed");
                _dragging = false;
                ChromeHold(EmiChromeHold.Drag, false);
            }
        }

        private void OnBodyPointerMoved(object? sender, PointerEventArgs e)
        {
            try
            {
                if (!_dragging)
                {
                    UpdatePetHover(e.GetPosition(_bodyRoot));
                    return;
                }
                // PointToScreen and Position are BOTH physical pixels here, so the move itself needs
                // no conversion - only the "is this a drag yet?" test, which stays in DIPs so it
                // agrees with the ring's own threshold.
                var now = this.PointToScreen(e.GetPosition(this));
                int dx = now.X - _dragStartScreen.X;
                int dy = now.Y - _dragStartScreen.Y;
                double s = DipScale;
                if (s <= 0) s = 1.0;
                if (!_dragMoved && (Math.Abs(dx) + Math.Abs(dy)) / s > DragThresholdDip) _dragMoved = true;
                if (!_dragMoved) return;

                Position = new PixelPoint(_dragStartPosition.X + dx, _dragStartPosition.Y + dy);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] body pointer move failed");
            }
        }

        private void OnBodyPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                if (e.InitialPressMouseButton == MouseButton.Right) { OnBodyRightClick(e); return; }
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                if (!_dragging) return;

                _dragging = false;
                ChromeHold(EmiChromeHold.Drag, false);
                e.Pointer.Capture(null);
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
                var p = e.GetPosition(_bodyRoot);
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
                const KeyModifiers qa = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;
                if (e.KeyModifiers == qa) { ResetOnboarding(); return; }

                // LEFT CLICK IS THE PAT, everywhere on her. The ring is the RIGHT button now, or the
                // gear on hover.
                PetFromClick();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] body pointer up failed");
            }
        }

        /// <summary>
        /// RIGHT CLICK ON HER BODY OPENS THE RING (owner, 2026-08-29). It used to be the left click,
        /// which is now the pat.
        ///
        /// <para>No drag bookkeeping here on purpose: dragging is a left-button gesture, so a right
        /// click can never be the tail of a move and never needs the 6 DIP threshold. The squash
        /// still plays, so the button that does NOT pat her still visibly registers.</para>
        ///
        /// <para>WPF had a <c>MouseRightButtonUp</c> event of its own; Avalonia routes every button
        /// through <c>PointerReleased</c>, so this is called from there off
        /// <c>InitialPressMouseButton</c>.</para>
        /// </summary>
        private void OnBodyRightClick(PointerReleasedEventArgs e)
        {
            try
            {
                e.Handled = true;
                if (InputLocked || _transiting) return;

                // The two buttons agree about the entrance: if the left one may cut it, so may this
                // one, or the ring would open over a chain that is still animating her body.
                FinishSummon();
                CancelChain();

                RaiseActivity();
                PlayClickSquash();
                ToggleRingFromGesture();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] body right-click failed");
            }
        }

        private void EndDragHold()
        {
            if (!_dragging) return;
            _dragging = false;
            ChromeHold(EmiChromeHold.Drag, false);
            EndWobble();
        }

        // ---- the three chips ------------------------------------------------------

        /// <summary>
        /// THE GEAR: a plain left click opens her options panel beside her. A ring that happens to
        /// be up is folded FIRST - both surfaces are sibling windows anchored on the same body, and
        /// two of them open at once is a fan drawn under a panel with no way of telling which one a
        /// click belongs to.
        /// </summary>
        private void OnGearClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (InputLocked || _transiting) return;
                RaiseActivity();

                if (OptionsOpen) { CloseOptionsPanel(); return; }

                try { CloseRing(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] fold before options failed"); }

                OpenOptionsPanel();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] gear failed");
            }
        }

        /// <summary>
        /// HER BOOK. The ? under the gear opens the codex, and that is the whole of it.
        ///
        /// <para>It deliberately does NOT go through a moment. Everything else she opens, she is
        /// asked about first and may decline; this is the user reaching for the manual, and a manual
        /// that answers a direct request with a coin flip is not a manual.</para>
        /// </summary>
        private void OnHelpClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (InputLocked || _transiting) return;
                RaiseActivity();

                // Her two panels are anchored on the same body as the book's own window, and three
                // things fanned out around her is a mess nobody can aim at.
                try { CloseOptionsPanel(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options tidy before the book failed"); }
                try { CloseRing(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] fold before the book failed"); }

                OpenBook();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] the book chip failed");
            }
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                e.Handled = true;
                RaiseActivity();
                Dismiss();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] hover-x dismiss failed");
            }
        }

        // ---- her options panel ----------------------------------------------------

        private EmiOptionsWindow? _options;

        /// <summary>True while her options panel is beside her. Read by the glass before it flips.</summary>
        public bool OptionsOpen
        {
            get
            {
                try { return _options?.IsOpen == true; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Show her options beside her. Built lazily and KEPT, the way the ring window is: it
        /// carries a 25-tile pin picker, and rebuilding that on every gear click is a wall of
        /// decoded thumbnails per open.
        /// </summary>
        private void OpenOptionsPanel()
        {
            try
            {
                if (_options == null)
                {
                    _options = new EmiOptionsWindow(this);
                    // While the panel is up the chrome stays lit, so the gear that opened it is
                    // still there to close it again after the round trip across the desktop.
                    _options.PanelClosed += (_, _) => ChromeHold(EmiChromeHold.Menu, false);
                    _options.CardsRequested += (_, _) => ToggleRingFromGesture();
                }

                ChromeHold(EmiChromeHold.Menu, true);
                _options.OpenPanel();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] options panel failed to open");
                ChromeHold(EmiChromeHold.Menu, false);
            }
        }

        /// <summary>Fold her options. Idempotent, and safe where the panel was never built.</summary>
        public void CloseOptionsPanel()
        {
            try { _options?.ClosePanel(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options panel close failed"); }
        }

        /// <summary>Drop the options panel for good. It is a sibling window holding a subscription
        /// to this one's Moved/Resized, so it has to go before she does.</summary>
        private void KillOptionsPanel()
        {
            try { _options?.Kill(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] options kill failed"); }
            _options = null;
        }

        /// <summary>Let the panel keep the chrome lit while the pointer is inside it.</summary>
        internal void HoldChromeForPanel(bool on) => ChromeHold(EmiChromeHold.Menu, on);

        /// <summary>Clear the resize hold, whichever way the drag ended.</summary>
        private void EndResizeHold()
        {
            _resizing = false;
            ChromeHold(EmiChromeHold.Resize, false);
        }

        /// <summary>
        /// The one road to the ring, so the right click and the gear cannot drift apart. Goes
        /// through the same partial seam the left click used to, which keeps the ring's own file the
        /// only place that knows what a ring is.
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
        /// </summary>
        private void UpdatePetHover(Point p)
        {
            try
            {
                if (!IsOnHead(p)) { DisarmPet(); return; }
                if (_petArmed || _petTimer != null) return;

                _petTimer = new DispatcherTimer(DispatcherPriority.Background)
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

        /// <summary>
        /// Everything above the glass, plus a little of the bezel. Pure geometry off
        /// <see cref="HeadBottomFrac"/>, which is why it ports rather than stubs even though the
        /// chain it arms does not.
        /// </summary>
        private bool IsOnHead(Point p)
        {
            double bh = _bodyWidth * BodyAspect;
            return p.Y >= 0 && p.Y <= bh * HeadBottomFrac && p.X >= 0 && p.X <= _bodyWidth;
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
                if (_closingForGood) return;
                if (_transiting || InputLocked) return;
                // LAW 3: a line in flight is never cut for a head-pat. Ignore, do not queue.
                if (ChainLive) return;

                _petArmed = true;
                RaiseActivity();
                PlayPatSfx();   // the hover half of the one gesture - see PetFromClick

                // Inside the cooldown she only winks, so spam cannot loop the show (widget.js pet()).
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
                FireDeskEvent("petted");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] pet failed");
            }
        }

        // ---- resize ---------------------------------------------------------------

        private void OnGripPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_resizeGrip).Properties.IsLeftButtonPressed) return;
            if (InputLocked || _transiting) { e.Handled = true; return; }
            try
            {
                RaiseActivity();
                // THE ONE THAT MADE THE GRIP UNUSABLE. Growing her means dragging the corner DOWN
                // AND RIGHT, away from her, so the pointer is off BodyRoot within a few pixels and
                // the old leave-then-fade dropped the handle the user was holding back to
                // GripRestOpacity mid-gesture. The hold outlives the pointer.
                ChromeHold(EmiChromeHold.Resize, true);
                _resizing = true;
                _resizeStartScreen = this.PointToScreen(e.GetPosition(this));
                _resizeStartWidth = _bodyWidth;
                e.Pointer.Capture(_resizeGrip);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] grip pointer down failed");
                _resizing = false;
                ChromeHold(EmiChromeHold.Resize, false);
            }
        }

        private void OnGripPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_resizing) return;
            try
            {
                var now = this.PointToScreen(e.GetPosition(this));
                double s = DipScale;
                if (s <= 0) s = 1.0;
                ApplyBodyWidth(_resizeStartWidth + (now.X - _resizeStartScreen.X) / s);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] grip pointer move failed");
            }
        }

        private void OnGripPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_resizing) return;
            try
            {
                EndResizeHold();
                e.Pointer.Capture(null);
                e.Handled = true;
                ClampIntoWorkArea();
                SavePlacement();
                // Her width's one home is the setting, not EmiState. Half a pixel of slack, as WPF
                // had: a save rewrites the whole file and a grip drag ends on a fractional DIP.
                if (Math.Abs(CoreSettings.Current.EmiDeskWidth - _bodyWidth) > 0.5)
                {
                    CoreSettings.Current.EmiDeskWidth = _bodyWidth;
                    CoreSettings.Save();
                }
                // ponytail: needs App.EmiDesk.Fire("resized") - her own event bus, which moves with
                // the app shell.
                FireDeskEvent("resized");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] grip pointer up failed");
            }
        }

        // ---------------------------------------------------------------- sibling-partial stubs

        // Everything below lives in one of the seven OTHER partials of the WPF class, none of which
        // is part of this layer. Each is kept as a named no-op so the ported half of the widget
        // still reads as the same code and the later layers have the exact call sites to fill in.

        /// <summary>ponytail: EmiDeskWindow.Alive.cs - the 100 ms gaze/idle poll. Starts with her.</summary>
        private void StartAlive() { }

        /// <summary>ponytail: EmiDeskWindow.Alive.cs - stops the poll when she goes.</summary>
        private void StopAlive() { }

        /// <summary>ponytail: EmiDeskWindow.React.cs - the click squash-and-stretch on _squashScale.</summary>
        private void PlayClickSquash() { }

        /// <summary>ponytail: EmiDeskWindow.React.cs - the drag sway on _wobbleRotate.</summary>
        private void BeginWobble() { }

        /// <summary>ponytail: EmiDeskWindow.React.cs - the release pendulum on _wobbleRotate.</summary>
        private void EndWobble() { }

        /// <summary>ponytail: EmiDeskWindow.React.cs - the pat chain, its cooldown and its wink.</summary>
        private void PetFromClick() { }

        /// <summary>ponytail: EmiDeskWindow.React.cs - the pat counter behind her affection state.</summary>
        private void CountPat() { }

        /// <summary>ponytail: EmiDeskWindow.Fx.cs - ends the summon chain early so a click can land.</summary>
        private void FinishSummon() { }

        /// <summary>ponytail: EmiDeskWindow.Fx.cs - sweeps spent FX visuals off OverlayHost.</summary>
        private void SweepFx(bool all = false) { }

        /// <summary>ponytail: EmiDeskWindow.Ring.cs - folds the card fan.</summary>
        private void CloseRing() { }

        /// <summary>ponytail: EmiDeskWindow.Props.cs - lays the held plate out at the current width.</summary>
        private void LayoutProp() { }

        /// <summary>ponytail: EmiDeskWindow.Props.cs - takes the held plate off.</summary>
        private void HideProp() { }

        /// <summary>ponytail: EmiDeskWindow.React.cs - drops the reaction hooks.</summary>
        private void TearDownReactions() { }

        /// <summary>ponytail: EmiDeskWindow.Bubble.cs - drops the voice hooks.</summary>
        private void TearDownVox() { }

        /// <summary>ponytail: needs EmiSfx (Services/EmiDesk/EmiSfx.cs) - the pat sound.</summary>
        private void PlayPatSfx() { }

        /// <summary>ponytail: needs EmiBook.Open (Services/EmiDesk/EmiBook.cs) - her manual.</summary>
        private void OpenBook() { }

        /// <summary>ponytail: needs App.EmiDesk.Dismiss() - the hover x sends her away.</summary>
        private void Dismiss() { }

        /// <summary>ponytail: needs App.EmiDesk.ResetOnboarding() - the QA gesture replay.</summary>
        private void ResetOnboarding() { }

        /// <summary>ponytail: needs App.EmiDesk.Fire(...) - her own event bus.</summary>
        private void FireDeskEvent(string name) { }

        // ---------------------------------------------------------------- teardown

        /// <summary>Let her go for good: stop every timer and drop the window. Called at app shutdown.</summary>
        public void ShutDown()
        {
            try
            {
                _closingForGood = true;
                try { OnTearDownCore(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] teardown seam threw"); }
                KillOptionsPanel();
                StopIdleBeats();
                StopAlive();
                DisarmPet();
                StopChromeGrace();
                TearDownReactions();
                TearDownVox();
                CancelChain();
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
                KillOptionsPanel();
                StopIdleBeats();
                StopAlive();
                DisarmPet();
                StopChromeGrace();
                TearDownReactions();
                TearDownVox();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] close cleanup failed");
            }
        }
    }
}
