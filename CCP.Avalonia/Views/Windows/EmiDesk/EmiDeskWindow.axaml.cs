using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;
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
    /// and that file is ONE of eight partials of the WPF class. Six of the other seven
    /// (<c>.Alive</c>, <c>.Bubble</c>, <c>.Glass</c>, <c>.Props</c>, <c>.Ring</c>) are still ahead,
    /// so every member this file calls into them is a one-line <c>ponytail:</c> stub below rather
    /// than an invention. Two are exceptions and are REAL here. <c>.React</c>'s physical half - the
    /// click squash and the drag wobble - needs nothing that is not already here, and only its pat
    /// (a chain) is still a stub. <c>.Fx</c> - the summon and the dismiss - is whole: it plays two
    /// chains that no-op on this head and is otherwise self-contained, so she arrives and leaves
    /// with the smoke, the CRT stutter and the sparkle scatter. Nothing CALLS either of those two
    /// entry points yet; that is <c>EmiDeskService</c>'s job. The
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
        // Written by the summon / dismiss FX region below, which is the whole of what ever moved
        // it on the WPF head. Every gate that reads it was already ported, so the locks are live.
        private bool _transiting;
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
        // monitor name. It is NOT in Core and it is NOT pure, but it is two seam swaps away and
        // nothing else: App.UserDataPath -> CorePaths on line 231, and the 500 ms debounce's
        // Application.Current.Dispatcher / DispatcherTimer -> CoreDispatch. Every other line of its
        // 680 is Newtonsoft on a POCO. Until that lands she is parked by default on every summon,
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

        /// <summary>
        /// The window the app is showing right now, or null. WPF read the monitor off
        /// <c>App.MainWindowRef</c>'s HWND; the desktop lifetime's <see cref="Window"/> is the same
        /// thing on this head, and asking the lifetime rather than naming a shell type keeps this
        /// true whichever window the app has up (the shell, the first-run wizard, none at all).
        ///
        /// <para>Null on the headless <c>--render-view</c> path, which has no desktop lifetime -
        /// that is the branch the render exercises, and it falls through to her own screen.</para>
        /// </summary>
        private static Window? AppMainWindow
        {
            get
            {
                try
                {
                    return (Application.Current?.ApplicationLifetime
                        as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[EmiDesk] main-window lookup failed");
                    return null;
                }
            }
        }

        /// <summary>
        /// Bottom right of the monitor THE APP is on, with a comfortable margin - which is what the
        /// name says and what WPF did. She has no meaningful position of her own on a first summon,
        /// so parking her on her own screen put her on the primary monitor while the app sat on the
        /// second one, and the reader had to go find her.
        /// </summary>
        public void ParkBottomRightOfMain()
        {
            try
            {
                var screens = Screens;
                if (screens is null || screens.ScreenCount == 0) return;
                Screen? on = null;
                var main = AppMainWindow;
                // Its own try: a window that has not been shown yet has no platform impl to ask,
                // and "the app has not got a monitor yet" is her own screen's cue, not an error.
                if (main is not null)
                {
                    try { on = screens.ScreenFromWindow(main); }
                    catch (Exception ex) { Log.Debug(ex, "[EmiDesk] main-window screen probe failed"); }
                }

                var work = (on ?? screens.ScreenFromWindow(this) ?? screens.Primary)?.WorkingArea;
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
        // ponytail: needs EmiState (Services/EmiDesk/EmiState.cs) - see RestorePlacement above for
        // the exact two seam swaps that move it. The geometry it would persist is BodyScreenRect
        // plus Screens.ScreenFromPoint(...).DisplayName.
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
            // ChainLive is the WPF gate's `_player.IsLive` and is constant false on this head. It is
            // in the expression anyway: this is the one place that decides whether an idle beat may
            // interrupt her, and a gate that has to be REMEMBERED when the chain player lands is a
            // gate that will be forgotten.
            if (_transiting || ChainLive || InputLocked) return true;
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
        //
        // AUDITED 2026-09-04 rather than repeated, because "neither in Core" said nothing about how
        // far away either one is, and they are close. EmiAlive.cs is 428 lines whose ONLY using is
        // System, and its whole head coupling is two signatures - GazeTarget and WithinApproach
        // take System.Windows.Point / Rect - so it is one Core geometry type away, the same single
        // blocker EmiRingLayout has. EmiChains.cs is 654 lines whose only head type is the
        // DispatcherTimer inside Player (its file probing is AppContext.BaseDirectory, which Core
        // already uses in EmiProps), so it is ONE seam swap: a System.Threading.Timer ticking
        // through CoreDispatch.Post. Neither is a re-derivation candidate - inlining these numbers
        // here would be a second copy of a table EmiAlive's own header forbids retuning outside the
        // plan, and it would still drive a PlayChain that no-ops and a SetPose with no art to swap.
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

        // ---- her book -------------------------------------------------------------

        private EmiBookWindow? _book;

        /// <summary>
        /// HER MANUAL. Open the book beside her, at <paramref name="cardId"/> or at the first card.
        ///
        /// <para>The WPF path is <c>EmiBook.Open</c>, a static router that keeps the single instance
        /// and resolves the owner off <c>App.EmiDesk</c>. There is no app shell on this head, so the
        /// widget keeps the instance itself - the same shape it already uses for her options panel,
        /// and the widget IS the owner the router was going looking for.</para>
        ///
        /// <para>Single instance, the router's own rule: a second open on a live book NAVIGATES it
        /// rather than building a second panel.</para>
        ///
        /// <para>ponytail: three of the router's jobs are still blocked and none of them is view
        /// work. <c>EmiBook.Bookmark</c> / <c>NoteCard</c> (the card she last had open) and
        /// <c>EmiState.NoteCodexOpened()</c> need
        /// ConditioningControlPanel/Services/EmiDesk/EmiState.cs; <c>NoteSideChanged</c> needs the
        /// bubble dodge in EmiDeskWindow.Bubble.cs. So the book opens at the first card every time
        /// until EmiState lands.</para>
        /// </summary>
        private void OpenBook(string? cardId = null)
        {
            try
            {
                if (_book != null)
                {
                    _book.GoTo(cardId);
                    return;
                }

                var win = new EmiBookWindow(this);
                // The window can go away without anybody calling CloseBook - the fold finishing, or
                // the app shutting down - so the reference is dropped from the window's own Closed.
                // Without this the next ? click would GoTo a dead panel.
                win.Closed += OnBookClosed;
                _book = win;
                win.OpenBook(cardId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] the book failed to open");
                try { CloseBook(); } catch { /* nothing else to try */ }
            }
        }

        /// <summary>Fold the book. Safe when it is not up, and the road her close button takes.</summary>
        internal void CloseBook()
        {
            var win = _book;
            _book = null;
            if (win == null) return;
            try
            {
                win.Closed -= OnBookClosed;
                win.Kill();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book close failed"); }
        }

        private void OnBookClosed(object? sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, _book)) return;
            _book = null;
        }

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

        // ---------------------------------------------------------------- she feels the pointer

        // PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.React.cs, the physical
        // half of her reactions: the squash on a click and the drag wobble. It lands in THIS file
        // rather than a sibling partial because every call site is already here and both effects
        // own transform slots this file builds. The PAT half of React.cs stays a stub - a pat is a
        // chain, and no chain can run on this head yet (see PetFromClick below).
        //
        // Why raw animations and not chains, unchanged from the original: touch feedback must be
        // UNCONDITIONAL and instant, and a line engine that says "not now" would eat the one bit of
        // feedback a click has to give. _squashScale and _wobbleRotate are dedicated slots, so
        // neither can collide with _crtScale's power-on or with _moveShift's nod / droop / shiver.

        /// <summary>Squash down to this on Y, and the matching stretch on X. emi.css's own pop values.</summary>
        private const double SquashY = 0.92;

        /// <inheritdoc cref="SquashY"/>
        private const double SquashX = 1.06;

        /// <summary>How long the squash takes to reach its deepest point.</summary>
        private const double SquashDownMs = 90;

        /// <summary>...and how long the spring back takes. The two together are one animation.</summary>
        private const double SquashUpMs = 260;

        /// <summary>
        /// THE WHOLE SQUASH AS ONE CURVE, normalised 0 -> 1 -> 0.
        ///
        /// <para>WPF built this from two <c>EasingDoubleKeyFrame</c>s, a cubic in and an
        /// <c>ElasticEase</c> back out. Avalonia's <c>Animation.Easing</c> is animation-WIDE - a
        /// keyframe carries no easing function, only a <c>KeySpline</c>, and an elastic overshoot is
        /// not a cubic bezier - so the shape has to live in one <see cref="Easing"/> instead. The
        /// animation is then a plain rest -> peak tween and this decides all of the motion: it
        /// returns 1 at the deepest point and 0 at both ends, so one instance drives the X stretch
        /// and the Y squash with their own peaks.</para>
        ///
        /// <para>The dip BELOW zero after the peak is the overshoot past rest - the one small
        /// rebound that is the difference between "she reacted" and "the widget resized". WPF got it
        /// from <c>ElasticEase { Oscillations = 1, Springiness = 4 }</c>, and this is that easing's
        /// own arithmetic rather than a look-alike: exactly one negative excursion, reaching about
        /// 22 % of the squash depth, checked numerically before it was written down.</para>
        /// </summary>
        private sealed class SquashCurve : Easing
        {
            private const double Osc = 1.0;          // WPF ElasticEase.Oscillations
            private const double Spring = 4.0;       // WPF ElasticEase.Springiness

            private static readonly double Down = SquashDownMs / (SquashDownMs + SquashUpMs);

            public override double Ease(double progress)
            {
                if (progress <= 0 || progress >= 1) return 0;

                if (progress < Down)
                {
                    // CubicEaseOut into the squash: she takes the hit immediately.
                    double u = progress / Down;
                    double inv = 1 - u;
                    return 1 - inv * inv * inv;
                }

                // ElasticEaseOut back out of it, mirrored so 1 is the peak and 0 is rest.
                double v = 1 - (progress - Down) / (1 - Down);
                double expo = (Math.Exp(Spring * v) - 1) / (Math.Exp(Spring) - 1);
                return expo * Math.Sin((2 * Math.PI * Osc + Math.PI / 2) * v);
            }
        }

        private static readonly SquashCurve SquashEase = new();

        /// <summary>WPF's <c>SineEase{EaseInOut}</c>, per pendulum segment.</summary>
        private static double SineInOut(double u) => 0.5 - 0.5 * Math.Cos(Math.PI * u);

        /// <summary>The tween tick. The same 16 ms the drag sampler runs on.</summary>
        private const int TweenTickMs = 16;

        /// <summary>
        /// AVALONIA CANNOT ANIMATE A TRANSFORM OBJECT, AND IT DOES NOT SAY SO OUT LOUD.
        /// <c>new Animation { … Setter(ScaleTransform.ScaleXProperty, v) }.RunAsync(someTransform)</c>
        /// resolves to <c>TransformAnimator</c>, which casts its target to <c>Visual</c> and throws
        /// <c>InvalidCastException</c> - into whatever <c>catch</c> is nearest. Measured 2026-09-04:
        /// EVERY such call in this file threw, so the click squash and the drag-release pendulum
        /// had shipped wholly inert and silent about it. A PNG cannot show that; only running it
        /// with the catch opened up can.
        ///
        /// <para>Avalonia's supported road is a <c>TransformOperations</c> setter on the VISUAL, and
        /// this window cannot take it: <c>BodyRoot</c> carries four INDEPENDENT transform slots on
        /// purpose (see the ctor), and a single operations string would have each animation stamping
        /// on the other three. So the transforms are driven the way the drag sampler already drives
        /// the lean - a timer that writes the property.</para>
        ///
        /// <para><paramref name="apply"/> is handed the RAW 0..1 progress and eases it itself: two
        /// of the four callers here are discrete step tables, not curves. It is called once at 0
        /// before the clock starts, so there is never a frame at a stale value, and exactly once at
        /// 1 - which is what makes the end state a written value rather than an animation's fill.</para>
        /// </summary>
        private void Tween(double ms, CancellationToken token, Action<double> apply)
        {
            try
            {
                apply(0);
                if (ms <= 0) { apply(1); return; }

                var started = DateTime.UtcNow;
                DispatcherTimer? timer = null;
                timer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(TweenTickMs), DispatcherPriority.Render, (_, _) =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested || _closingForGood) { timer!.Stop(); return; }
                            double p = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / ms);
                            apply(p);
                            if (p >= 1) timer!.Stop();
                        }
                        catch (Exception ex)
                        {
                            timer!.Stop();
                            Log.Debug(ex, "[EmiDesk] tween step failed");
                        }
                    });
                timer.Start();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] tween failed to start");
            }
        }

        /// <summary>The squash's own animations. Cancelled and restarted rather than layered: a
        /// second click mid-bump replaces the first, exactly as WPF's BeginAnimation did.</summary>
        private CancellationTokenSource? _squashAnim;

        /// <summary>
        /// The click bump: she compresses about 8 % and springs back. Plays on EVERY click on her
        /// body, alongside whatever that click actually did (the ring toggle, the glass tap, the
        /// pat), and nothing else ever cancels it - it owns its own transform, so it cannot fight
        /// a chain.
        /// </summary>
        public void PlayClickSquash()
        {
            try
            {
                if (_closingForGood) return;

                _squashAnim?.Cancel();
                _squashAnim?.Dispose();
                var cts = new CancellationTokenSource();
                _squashAnim = cts;

                // REST IS WHERE THE CURVE ENDS, not a fill mode: SquashCurve returns 0 at both
                // ends, so the last tick writes 1 and a squash that finishes - or one the next
                // click cancels - lands at rest rather than freezing her compressed.
                double total = SquashDownMs + SquashUpMs;
                Tween(total, cts.Token, p =>
                {
                    double e = SquashEase.Ease(p);
                    _squashScale.ScaleX = 1 + (SquashX - 1) * e;
                    _squashScale.ScaleY = 1 + (SquashY - 1) * e;
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] click squash failed");
            }
        }

        // ---- the drag wobble ------------------------------------------------------

        /// <summary>Hardest she ever leans while being dragged, in degrees.</summary>
        private const double WobbleMaxDeg = 9.0;

        /// <summary>Degrees of lean per DIP/second of horizontal drag speed.</summary>
        private const double WobbleDegPerVel = 0.010;

        /// <summary>How much of the old velocity survives one tick. The low-pass; higher is smoother.</summary>
        private const double WobbleVelKeep = 0.78;

        /// <summary>How far the drawn angle closes on the target angle each tick. The second smoother.</summary>
        private const double WobbleFollow = 0.35;

        /// <summary>Above this drag speed (DIP/s) she pulls a face; above the second one, a worse one.</summary>
        private const double WobbleFaceVel = 420.0;

        /// <inheritdoc cref="WobbleFaceVel"/>
        private const double WobbleDizzyVel = 1150.0;

        /// <summary>The release pendulum's length. Two and a bit swings, each smaller than the last.</summary>
        private const int WobbleSettleMs = 720;

        /// <summary>
        /// The sampling tick. WPF hung this on <c>CompositionTarget.Rendering</c>, which fires every
        /// frame unconditionally; Avalonia's nearest twin,
        /// <c>TopLevel.RequestAnimationFrame</c>, is a ONE-SHOT that only lands when the compositor
        /// is producing a frame at all - so a pointer held still (target angle 0, nothing dirty)
        /// can end the chain of requests and leave her frozen at a lean. A timer cannot stall that
        /// way, and dt is measured rather than assumed below, so the physics is the same either way.
        /// </summary>
        private const int WobbleTickMs = 16;

        private DispatcherTimer? _wobbleTimer;
        private CancellationTokenSource? _wobbleSettle;
        private bool _wobbleLive;
        private double _wobbleLastX;
        private double _wobbleVx;
        private double _wobbleAngle;
        private DateTime _wobbleLastTick;
        private string? _wobbleFace;

        /// <summary>
        /// Start hanging. Called from the pointer-down, not from the first move, so the very first
        /// tick of a drag already has a velocity baseline to measure against.
        /// </summary>
        private void BeginWobble()
        {
            try
            {
                if (_wobbleLive || _closingForGood) return;
                _wobbleLive = true;

                // ORDER MATTERS, and it is the inverse of WPF's. A settle still running from the
                // last drop holds the angle at ANIMATION priority, which both masks a write and is
                // what the getter returns - so read the live angle FIRST, then cancel, then write it
                // back as the local value the ticks below will drive.
                _wobbleAngle = _wobbleRotate.Angle;
                CancelWobbleSettle();
                _wobbleRotate.Angle = _wobbleAngle;

                _wobbleLastX = Position.X / SafeScale();
                _wobbleVx = 0;
                _wobbleLastTick = DateTime.UtcNow;

                _wobbleTimer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(WobbleTickMs), DispatcherPriority.Render, OnWobbleTick);
                _wobbleTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] wobble start failed");
                _wobbleLive = false;
            }
        }

        /// <summary>
        /// One frame of hanging. Velocity is sampled off her actual window position on the tick
        /// rather than off the pointer-move, because a pointer that stops moving stops raising
        /// events: a move-driven wobble freezes mid-lean the instant you hold still, which is the
        /// one thing that would make her look broken instead of heavy.
        ///
        /// <para>WPF read <c>Left</c>, which is DIPs. <see cref="Window.Position"/> is PHYSICAL
        /// pixels, so the divide by the scale is what keeps <see cref="WobbleDegPerVel"/> meaning
        /// the same thing it was tuned to mean on a 200 % desk.</para>
        /// </summary>
        private void OnWobbleTick(object? sender, EventArgs e)
        {
            try
            {
                if (!_wobbleLive) return;

                var now = DateTime.UtcNow;
                double dt = (now - _wobbleLastTick).TotalSeconds;
                _wobbleLastTick = now;
                if (dt < 0.004) dt = 0.004;
                if (dt > 0.064) dt = 0.064;      // a stalled tick must not read as a huge velocity

                double x = Position.X / SafeScale();
                double raw = (x - _wobbleLastX) / dt;
                _wobbleLastX = x;

                _wobbleVx = _wobbleVx * WobbleVelKeep + raw * (1.0 - WobbleVelKeep);

                // She TRAILS the hand: drag her right and her feet swing left, which about a
                // head-high pivot is a positive (clockwise) angle in a y-down frame.
                double target = Math.Max(-WobbleMaxDeg, Math.Min(WobbleMaxDeg, _wobbleVx * WobbleDegPerVel));
                _wobbleAngle += (target - _wobbleAngle) * WobbleFollow;
                _wobbleRotate.Angle = _wobbleAngle;

                UpdateWobbleFace(Math.Abs(_wobbleVx));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] wobble frame failed");
            }
        }

        /// <summary>The face she pulls while she is being flung about. Never over a live chain.</summary>
        private void UpdateWobbleFace(double speed)
        {
            try
            {
                if (ChainLive) return;

                string? want = speed >= WobbleDizzyVel ? "@_@"
                             : speed >= WobbleFaceVel ? ">_<"
                             : null;

                if (want == _wobbleFace) return;
                _wobbleFace = want;
                DrawFace(want ?? RestFace);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] wobble face failed");
            }
        }

        /// <summary>
        /// Let go. She keeps the lean she had and swings it off in two and a bit diminishing arcs -
        /// the pendulum is what sells the mass, and stopping dead on the drop undoes the whole
        /// effect.
        /// </summary>
        private void EndWobble()
        {
            try
            {
                if (!_wobbleLive) return;
                _wobbleLive = false;
                StopWobbleTimer();

                if (_wobbleFace != null)
                {
                    _wobbleFace = null;
                    if (!ChainLive) DrawFace(RestFace);
                }

                double a = _wobbleAngle;
                _wobbleAngle = 0;
                CancelWobbleSettle();

                // Under half a degree there is nothing to swing off; snap and stop, so a click that
                // just cleared the drag threshold does not end in a visible wobble. Same road when
                // she is on her way out - a pendulum over a closing window is a callback into a
                // dead surface.
                if (Math.Abs(a) < 0.5 || _closingForGood)
                {
                    _wobbleRotate.Angle = 0;
                    return;
                }

                var cts = new CancellationTokenSource();
                _wobbleSettle = cts;

                // THE LEAN SHE HAD is where the pendulum starts, and it ends on a written 0 - the
                // tween's last tick, not an animation's fill. That is why every road out of the
                // settle - BeginWobble, EndWobble's snap, TearDownReactions - still cancels the
                // token and THEN writes the angle, in that order: cancelling alone leaves whatever
                // tick landed last standing.
                (double At, double Angle)[] swing =
                {
                    (0.00, a), (0.30, -a * 0.55), (0.58, a * 0.28), (0.82, -a * 0.12), (1.00, 0.0),
                };

                Tween(WobbleSettleMs, cts.Token, p =>
                {
                    int i = 0;
                    while (i < swing.Length - 2 && p >= swing[i + 1].At) i++;
                    double span = swing[i + 1].At - swing[i].At;
                    double u = span > 0 ? Math.Min(1, Math.Max(0, (p - swing[i].At) / span)) : 1;
                    _wobbleRotate.Angle = swing[i].Angle
                        + (swing[i + 1].Angle - swing[i].Angle) * SineInOut(u);
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] wobble settle failed");
                _wobbleLive = false;
            }
        }

        /// <summary><see cref="DipScale"/>, never zero. It already guards; this only spares the
        /// wobble's two hot lines the second check.</summary>
        private double SafeScale()
        {
            double s = DipScale;
            return s > 0 ? s : 1.0;
        }

        private void StopWobbleTimer()
        {
            try { _wobbleTimer?.Stop(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] wobble timer stop failed"); }
            _wobbleTimer = null;
        }

        private void CancelWobbleSettle()
        {
            try { _wobbleSettle?.Cancel(); _wobbleSettle?.Dispose(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] wobble settle cancel failed"); }
            _wobbleSettle = null;
        }

        /// <summary>
        /// Everything this region owns, off. Called from the tear-down and the close handler: a
        /// 16 ms timer that outlives the window is a callback into a dead surface for the rest of
        /// the process.
        /// </summary>
        private void TearDownReactions()
        {
            try
            {
                _wobbleLive = false;
                StopWobbleTimer();
                CancelWobbleSettle();

                try { _squashAnim?.Cancel(); _squashAnim?.Dispose(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] squash cancel failed"); }
                _squashAnim = null;

                _wobbleFace = null;
                _wobbleAngle = 0;
                _wobbleRotate.Angle = 0;
                _squashScale.ScaleX = 1;
                _squashScale.ScaleY = 1;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] reaction tear-down failed");
            }
        }

        // ---------------------------------------------------------------- summon / dismiss FX

        // PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Fx.cs, the seventh
        // partial of the WPF class and the only one of the six still missing whose whole dependency
        // set was already in this file. It needs no EmiDesk service: the two chains it plays
        // (`wake`, `wink`) go through PlayChain, which on this head runs its continuation straight
        // away, so she arrives and leaves with the smoke, the CRT and the sparkles but without the
        // little line either end. That degradation is visible and honest, not silent.
        //
        // NOTHING ON THIS HEAD CALLS RunSummon OR RunDismiss YET. Both are EmiDeskService's to call
        // (App.EmiDesk, which moves with the app shell), and the hover x's Dismiss() below is a stub
        // for the same reason. What this region buys today is that _transiting and InputLocked are
        // finally WRITTEN by something - every gate that reads them was already ported - and that
        // FinishSummon and SweepFx stop being no-ops that the pat path and ShutDown called into the
        // dark.

        private const int SmokeLeadMs = 380;      // smoke starts, she appears this long after
        private const int CrtOnMs = 220;          // the power-on stutter
        private const int CrtOffMs = 230;         // the power-off collapse
        private const int FxLifeMs = 900;         // a burst's container is swept this long after it starts

        /// <summary>Every burst's container, so one sweep can drop them all.</summary>
        private readonly List<Control> _fxLayers = new();

        /// <summary>One sweep timer for every burst: a per-chip completion handler would be 22
        /// closures fighting the same collection.</summary>
        private DispatcherTimer? _fxSweepTimer;

        /// <summary>The CRT's own token, so the power-off can cut a power-on that is still running
        /// and the dismiss can cut both.</summary>
        private CancellationTokenSource? _crtAnim;

        /// <summary>True between the wake chain starting and <see cref="FinishSummon"/>.</summary>
        private bool _summonChainLive;

        private Action? _summonDone;

        /// <summary>
        /// Bring her in: smoke bomb, CRT power-on, then the <c>wake</c> chain. Input is locked for
        /// the whole transition so a click cannot land mid-CRT and open a ring onto a 2 % tall EMI.
        /// </summary>
        public void RunSummon(Action? done = null)
        {
            try
            {
                if (_closingForGood) return;
                _transiting = true;
                InputLocked = true;

                CancelChain();
                StopIdleBeats();
                OnBubbleTextCore(null);
                TearDownReactions();

                // The window goes up first (the smoke has to be somewhere), but she does not.
                //
                // WPF used Visibility.Hidden here, which still takes part in layout; Avalonia has no
                // third state and IsVisible=false is Collapsed. It is harmless in this one spot and
                // only here: BodyRoot carries an explicit Width/Height from ApplyBodyWidth and is
                // Center-aligned inside a Grid whose own size is set on the window, so nothing
                // re-flows when it drops out and nothing moves when it comes back.
                _bodyRoot.IsVisible = false;
                SetCrt(0.02, 0.02);
                Show();
                Opacity = 1;

                Burst(spark: false);

                After(SmokeLeadMs, () =>
                {
                    SetPose("idle");
                    DrawFace("-_-");
                    _bodyRoot.IsVisible = true;
                    CrtOn();

                    After(CrtOnMs + 20, () =>
                    {
                        _transiting = false;
                        InputLocked = false;

                        // Her entrance is CUTTABLE from here on. A pat that lands during it ends it
                        // through FinishSummon so the idle beats still start and the caller's
                        // continuation still runs: the chain is interruptible, the bookkeeping is
                        // not.
                        _summonDone = done;
                        _summonChainLive = true;
                        PlayChain("wake", FinishSummon);
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] summon FX failed, showing her plain");
                try
                {
                    _transiting = false;
                    InputLocked = false;
                    _bodyRoot.IsVisible = true;
                    SetCrt(1, 1);
                    Show();
                    RestartIdleBeats();
                    done?.Invoke();
                }
                catch { /* nothing left to try */ }
            }
        }

        /// <summary>
        /// End the summon exactly once, however it ended: the wake chain ran out, or a pat cut it.
        /// Idempotent, because those two can arrive in either order and sometimes both.
        /// </summary>
        internal void FinishSummon()
        {
            if (!_summonChainLive) return;
            _summonChainLive = false;
            var done = _summonDone;
            _summonDone = null;
            try { RestartIdleBeats(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] idle beats failed to restart after the summon"); }
            try { done?.Invoke(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] summon continuation threw"); }
        }

        /// <summary>
        /// Send her away: a wink, the CRT collapse, a sparkle scatter, then hide. She always gets
        /// the wink first, so leaving never reads as a crash.
        /// </summary>
        public void RunDismiss(Action? done = null)
        {
            try
            {
                if (!IsVisible)
                {
                    done?.Invoke();
                    return;
                }

                _transiting = true;
                InputLocked = true;
                StopIdleBeats();
                DisarmPet();
                CancelChain();
                TearDownReactions();

                try { OnTearDownCore(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tear-down seam threw"); }

                PlayChain("wink", () =>
                {
                    if (_closingForGood) { FinishDismiss(done); return; }
                    CrtOff();
                    After(CrtOffMs, () =>
                    {
                        _bodyRoot.IsVisible = false;
                        Burst(spark: true);
                        After(FxLifeMs, () => FinishDismiss(done));
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] dismiss FX failed, hiding her plain");
                FinishDismiss(done);
            }
        }

        private void FinishDismiss(Action? done)
        {
            try
            {
                SweepFx(all: true);
                OnBubbleTextCore(null);
                Hide();
                _bodyRoot.IsVisible = true;
                SetCrt(1, 1);
                _transiting = false;
                InputLocked = false;
                SetPose("idle");
                DrawFace(RestFace);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] dismiss finish failed");
            }
            finally
            {
                try { done?.Invoke(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] dismiss callback threw"); }
            }
        }

        // ---- the CRT --------------------------------------------------------------

        /// <summary>
        /// The power-on: a dot, a horizontal line, then the picture. FOUR DISCRETE STEPS, no easing,
        /// because a smooth interpolation reads as a modern zoom and this is meant to be a CRT.
        /// WPF wrote them as <c>DiscreteDoubleKeyFrame</c>s; <see cref="Tween"/> is handed the raw
        /// progress, so a step table needs no interpolation to defeat.
        /// </summary>
        private void CrtOn()
            => BeginCrt(CrtOnMs, new[]
            {
                (0.00, 0.02, 0.02),   // a dot
                (0.30, 1.00, 0.03),   // a horizontal line
                (0.65, 1.00, 0.06),   // the line thickens
                (1.00, 1.00, 1.00),   // the picture
            });

        /// <summary>The power-off: the picture collapses to a line, then to a dot.</summary>
        private void CrtOff()
            => BeginCrt(CrtOffMs, new[]
            {
                (0.00, 1.00, 1.00),
                (0.45, 1.00, 0.06),
                (0.80, 0.35, 0.03),
                (1.00, 0.02, 0.02),
            });

        private void BeginCrt(int ms, (double At, double X, double Y)[] steps)
        {
            try
            {
                CancelCrt();
                var cts = new CancellationTokenSource();
                _crtAnim = cts;
                Tween(ms, cts.Token, p =>
                {
                    var step = steps[0];
                    foreach (var k in steps) if (p >= k.At) step = k;
                    _crtScale.ScaleX = step.X;
                    _crtScale.ScaleY = step.Y;
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] CRT animation failed");
            }
        }

        /// <summary>
        /// Put the CRT scale somewhere and mean it: cancel whatever is driving it, THEN write. The
        /// last step of a ladder holds after its timer stops - WPF's <c>FillBehavior.HoldEnd</c> -
        /// so a write without the cancel would be overwritten by the next tick.
        /// </summary>
        private void SetCrt(double x, double y)
        {
            CancelCrt();
            _crtScale.ScaleX = x;
            _crtScale.ScaleY = y;
        }

        private void CancelCrt()
        {
            try { _crtAnim?.Cancel(); _crtAnim?.Dispose(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] CRT cancel failed"); }
            _crtAnim = null;
        }

        // ---- the particles --------------------------------------------------------

        /// <summary>One chip in flight: where it is going, and how long it has to get there.</summary>
        private readonly record struct FxChip(TranslateTransform Shift, Rectangle Node, double Dx, double Dy, double LifeMs);

        private static readonly CubicEaseOut ChipEase = new();

        /// <summary>
        /// One pixel burst at her centre. Smoke on the way in (dark chips, a wider throw), sparks on
        /// the way out (pink chips, tighter and faster). Ported from
        /// <c>docs/emi-desk/reference/pitch-demo.js smoke()</c>: the count, the radius, the upward
        /// bias and the 380 + rand(320) ms lifetimes are its numbers.
        ///
        /// <para>WPF gave every chip three <c>DoubleAnimation</c>s of its own - 66 clocks per burst.
        /// Here the whole burst rides ONE tween and each chip reads its own progress off it, which
        /// is both cheaper and the only shape available: the throw is on a
        /// <see cref="TranslateTransform"/>, which Avalonia cannot animate (see
        /// <see cref="Tween"/>).</para>
        /// </summary>
        private void Burst(bool spark)
        {
            try
            {
                double cx = (double.IsNaN(Width) ? Bounds.Width : Width) / 2.0;
                double cy = (double.IsNaN(Height) ? Bounds.Height : Height) / 2.0;
                if (cx <= 0 || cy <= 0)
                {
                    cx = OverlayPadX + _bodyWidth / 2.0;
                    cy = OverlayPad + _bodyWidth * BodyAspect / 2.0;
                }

                int n = spark ? 14 : 22;

                var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
                _overlayCanvas.Children.Add(layer);
                _fxLayers.Add(layer);

                // Immutable on purpose - WPF froze the same three brushes. Nothing here ever
                // recolours a chip, so one brush per colour serves the whole burst.
                var pink = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
                var ink = new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x46));
                var cream = new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1));

                var rng = Random.Shared;
                var chips = new List<FxChip>(n);
                double longest = 1;

                for (int i = 0; i < n; i++)
                {
                    double ang = rng.NextDouble() * Math.PI * 2;
                    double r = (spark ? 30 : 22) + rng.NextDouble() * 40;
                    double dx = Math.Cos(ang) * r;
                    double dy = Math.Sin(ang) * r - (spark ? 10 : 20);
                    int life = 380 + rng.Next(320);
                    double size = spark ? 3 + rng.Next(2) : 4 + rng.Next(3);

                    var shift = new TranslateTransform();
                    var chip = new Rectangle
                    {
                        Width = size,
                        Height = size,
                        Fill = spark
                            ? (rng.NextDouble() < 0.35 ? cream : pink)
                            : (rng.NextDouble() < 0.30 ? pink : ink),
                        IsHitTestVisible = false,
                        UseLayoutRounding = true,
                        RenderTransform = shift,
                    };
                    Canvas.SetLeft(chip, cx - size / 2.0);
                    Canvas.SetTop(chip, cy - size / 2.0);
                    layer.Children.Add(chip);

                    chips.Add(new FxChip(shift, chip, dx, dy, life));
                    longest = Math.Max(longest, life);
                }

                Tween(longest, CancellationToken.None, p =>
                {
                    double elapsed = p * longest;
                    foreach (var c in chips)
                    {
                        double q = ChipEase.Ease(Math.Min(1, elapsed / c.LifeMs));
                        c.Shift.X = c.Dx * q;
                        c.Shift.Y = c.Dy * q;
                        c.Node.Opacity = 1 - q;
                    }
                });

                _fxSweepTimer ??= new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(FxLifeMs)
                };
                _fxSweepTimer.Tick -= OnFxSweepDue;
                _fxSweepTimer.Tick += OnFxSweepDue;
                _fxSweepTimer.Stop();
                _fxSweepTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] particle burst failed");
            }
        }

        private void OnFxSweepDue(object? sender, EventArgs e)
        {
            try
            {
                _fxSweepTimer?.Stop();
                SweepFx(all: true);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] FX sweep failed"); }
        }

        /// <summary>Drop spent FX layers. <paramref name="all"/> false is reserved for a partial
        /// sweep, exactly as in the WPF original - nothing passes it today.</summary>
        private void SweepFx(bool all = false)
        {
            try
            {
                if (_fxLayers.Count == 0) return;
                foreach (var layer in _fxLayers)
                {
                    try { _overlayCanvas.Children.Remove(layer); }
                    catch { /* already gone */ }
                }
                if (all) _fxLayers.Clear();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] SweepFx failed");
            }
        }

        /// <summary>
        /// A one-shot dispatcher delay that cannot outlive her. Every FX step goes through here so
        /// there is one place where the shutdown guard lives; WPF checked
        /// <c>Application.Current.Dispatcher.HasShutdownStarted</c>, and <c>_closingForGood</c> is
        /// this head's twin of it - ShutDown and the Closed handler both set it.
        /// </summary>
        private void After(int ms, Action act)
        {
            try
            {
                DispatcherTimer.RunOnce(() =>
                {
                    try
                    {
                        if (_closingForGood) return;
                        act();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "[EmiDesk] deferred FX step failed");
                    }
                }, TimeSpan.FromMilliseconds(Math.Max(1, ms)), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] After({Ms}) failed", ms);
            }
        }

        // ---------------------------------------------------------------- sibling-partial stubs

        // Everything below lives in one of the seven OTHER partials of the WPF class, none of which
        // is part of this layer. Each is kept as a named no-op so the ported half of the widget
        // still reads as the same code and the later layers have the exact call sites to fill in.
        // React.cs and Fx.cs are no longer among them: both are real, above.

        /// <summary>ponytail: EmiDeskWindow.Alive.cs - the 100 ms gaze/idle poll. Starts with her.</summary>
        private void StartAlive() { }

        /// <summary>ponytail: EmiDeskWindow.Alive.cs - stops the poll when she goes.</summary>
        private void StopAlive() { }

        /// <summary>
        /// ponytail: the pat is FIVE things, not one file, and none of them is on this head yet:
        /// EmiChains + EmiChains.Player for the <c>pet</c> chain and the poke flick,
        /// <c>EmiSfx.Pat()</c> for the sound, <c>EmiState.NotePet()</c> for the count behind her
        /// affection, <c>App.EmiDesk.Fire("petted")</c> for the moment, and EmiDeskWindow.Alive.cs's
        /// poke ladder for which face the flick wears. The cooldown arithmetic on its own would give
        /// a pat that changes nothing you can see or hear, so it stays here rather than half-landing:
        /// the click still squashes her, which is the one bit of feedback this head CAN give.
        /// </summary>
        private void PetFromClick() { }

        /// <summary>ponytail: needs EmiState.NotePet() - the pat counter behind her affection state.</summary>
        private void CountPat() { }

        /// <summary>ponytail: EmiDeskWindow.Ring.cs - folds the card fan.</summary>
        private void CloseRing() { }

        /// <summary>
        /// ponytail: EmiDeskWindow.Props.cs, and the note this used to carry was wrong about where
        /// the blocker is. <c>EmiProps</c> - the anchor, the three plates, their sizes, the hold and
        /// the rise - is ALREADY IN CORE (CCP.Core/Services/EmiDesk/EmiProps.cs) and is pure, so
        /// LayoutProp / ShowProp / HideProp / the rise port arithmetic for arithmetic. What stops
        /// them is the ART: <c>EmiProps.Path</c> probes
        /// <c>Resources/web/arcademy/art/emi/props/*.png</c> beside the exe, and CCP.Avalonia.csproj
        /// links no <c>Assets/web/</c> tree at all, so every lookup returns null and the whole beat
        /// is a silent no-op by the WPF original's own design. Take the csproj link and the port in
        /// ONE layer, or the port draws nothing and says nothing about why. The beat that starts it
        /// (RunPropBeat) additionally needs a chain, so it is two blockers, not one.
        /// </summary>
        private void LayoutProp() { }

        /// <inheritdoc cref="LayoutProp"/>
        private void HideProp() { }

        /// <summary>ponytail: EmiDeskWindow.Bubble.cs - drops the voice hooks.</summary>
        private void TearDownVox() { }

        /// <summary>ponytail: needs EmiSfx (Services/EmiDesk/EmiSfx.cs) - the pat sound.</summary>
        private void PlayPatSfx() { }

        /// <summary>
        /// ponytail: needs App.EmiDesk.Dismiss() - the hover x sends her away.
        ///
        /// <para>REFUSED as a view-local port, and the reason is written here so the next pass does
        /// not re-open it. <c>RunDismiss</c> IS on this head (the FX region above), so the outro
        /// itself could run from here - but that is the SMALLEST half of what the x does.
        /// <c>EmiDeskService.Dismiss</c> also bumps <c>_summonGen</c> so a summon parked in a
        /// nested pump cannot put her straight back, reconciles <c>IsOut</c> and raises
        /// <c>OutChanged</c>, cancels the summon moment and the empty-library beat, stops the
        /// nudges, and fires <c>dismissed</c> with the minutes she was out. A view-local dismissal
        /// would take her off the screen while every one of those still believed she was on it -
        /// a second, divergent copy of a service-owned behaviour, and the flag desync that
        /// service's own code logs a warning about. It lands with EmiDeskService and nowhere
        /// else.</para>
        /// </summary>
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
                // NOT in WPF's ShutDown, and deliberately: there the book is held by a static
                // router and the App shutdown closes every window. This head has no shell, so a
                // book left up would be a topmost window with two handlers pointing at a closed
                // widget and no road back to a close button.
                CloseBook();
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
                CloseBook();
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
