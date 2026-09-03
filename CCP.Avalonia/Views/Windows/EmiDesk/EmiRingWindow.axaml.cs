using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// One card on the ring.
    ///
    /// <para>ponytail: stands in for <c>EmiRingSlot</c> + <c>EmiTarget</c>
    /// (ConditioningControlPanel/Services/EmiDesk/EmiSuggester.cs and EmiTargets.cs), which are
    /// still in the WPF head and drag <c>App</c>, the premium rail and every feature door in with
    /// them. This is the part of a slot the RING actually draws: a label key, a tile hue, and the
    /// two flags that change how the card looks. Delete it and take <c>EmiRingSlot</c> when the
    /// EmiDesk services move to Core.</para>
    /// </summary>
    /// <param name="Id">The target id, used as the label of last resort and in the logs.</param>
    /// <param name="LabelKey">Loc key for the card's visible name.</param>
    /// <param name="Hue">The flat tile colour behind the label when there is no art.</param>
    /// <param name="Locked">Paints the padlock and dims the face.</param>
    /// <param name="Pinned">Thicker, solid-pink frame.</param>
    public sealed record EmiRingCard(string Id, string LabelKey, Color Hue, bool Locked, bool Pinned);

    /// <summary>
    /// The ring: six feature cards fanned around EMI, in their own sibling window.
    ///
    /// <para>Why a second window at all: <c>EmiDeskWindow</c>'s transparent pad is nowhere near
    /// enough for a fan around a 420 DIP body. This window opens over the work area of the monitor
    /// she is standing on and is then sized down to the FAN'S OWN BOUNDING BOX by <see cref="Layout"/>.
    /// It is placed with the same physical-pixels-over-own-DPI arithmetic the widget uses:
    /// <c>BodyScreenRect</c> and <c>RingAnchorScreenPoint</c> are PHYSICAL PIXELS, and converting
    /// them with an assumed scale of 1.0 is the coordinate trap that ate the gaze work.</para>
    ///
    /// PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiRingWindow.xaml.cs. The card, the
    /// fan, the pop and the fold are all real; the deviations, every one of them forced:
    ///  - <b>The owner is gone.</b> The WPF ring read <c>_owner.BodyScreenRect</c> and
    ///    <c>_owner.RingAnchorScreenPoint</c> off <c>EmiDeskWindow</c>, which is not ported yet, so
    ///    the two rectangles come in through <see cref="SetWidgetGeometry"/> instead. That is the
    ///    whole of what the ring ever wanted from her.
    ///  - <b>Win32.</b> <c>SetWindowLong(GWL_EXSTYLE, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)</c> is
    ///    <c>ShowInTaskbar="False"</c> + <c>ShowActivated="False"</c> in the markup, so
    ///    <c>SourceInitialized</c> and its two P/Invokes go with it. <c>NativeStyle()</c> read
    ///    <c>WS_VISIBLE</c> straight off the HWND because WPF's own properties lied during the
    ///    invisible-first-open bug; there is no X11 twin, so the log line carries
    ///    <c>IsVisible</c> and the caveat that it is the property that once lied.
    ///  - <b>Click-through.</b> On Windows a layered window does not hit-test fully transparent
    ///    pixels, which is what let a click land on the desktop THROUGH a gap in the fan. X11 hit
    ///    tests the whole rectangle, and <c>X11Overlay.SetClickThrough</c> is all-or-nothing - the
    ///    whole window, which would kill the cards too. ponytail: needs X11Overlay to take a
    ///    REGION (XFixes can express it; the shim's own doc note says so), and that is its own
    ///    layer. Until then a click in a gap does not reach the desktop - but it is no longer
    ///    swallowed either, see <see cref="InstallHooks"/>.
    ///  - <b>The global hooks.</b> <c>GlobalMouseHook</c> / <c>GlobalKeyboardHook</c> are
    ///    <c>SetWindowsHookEx</c>, which has NO equivalent on this head, and with them goes
    ///    <c>_hotPx</c> - the frozen rect snapshot existed only to answer the hook thread. The half
    ///    of click-outside-to-close that lands INSIDE this window is recovered locally
    ///    (<see cref="InstallHooks"/>); the desktop-wide half and Escape-to-close are still gone.
    ///    Every other road into <see cref="CloseRing"/> (the pick, <see cref="Kill"/>, the caller's
    ///    own toggle) is intact.
    ///  - <b>The DPI.</b> <c>PresentationSource…TransformToDevice</c> and the
    ///    <c>System.Drawing.Graphics</c> fallback become <c>Screens.ScreenFromWindow(this).Scaling</c>;
    ///    <c>System.Windows.Forms.Screen.FromPoint</c> becomes <c>Screens.ScreenFromPoint</c>.
    ///  - <b>The animations.</b> <c>BeginAnimation</c> becomes <see cref="Tween"/>, a timer that
    ///    writes the value, cancelled through one <c>CancellationTokenSource</c> per opening instead
    ///    of a null <c>BeginAnimation</c> per property. It is NOT an Avalonia <c>Animation</c>, and
    ///    the first cut of this port was: <c>RunAsync</c> on a <c>ScaleTransform</c> throws, so the
    ///    pop, the fold and the hover grow were all silently inert. Read <see cref="Tween"/> before
    ///    reaching for <c>Animation</c> here again. WPF's <c>BackEase.Amplitude</c> has no Avalonia
    ///    equivalent - <c>BackEaseOut</c> is fixed at roughly amplitude 1.0, three times the settle
    ///    the owner tuned this to - so the pop's move uses <c>CubicEaseOut</c> and loses the
    ///    overshoot rather than exaggerating it.
    ///  - <b>PinToggled is dropped.</b> The WPF ring declares the event and never raises it,
    ///    because <c>EmiDeskWindow.Ring.cs</c> subscribes for the pin-nudge latch and the pin is
    ///    made from her options menu. Neither end of that exists on this head yet, so it would be
    ///    an unraised member with no subscriber; take it back with EmiDeskWindow.
    ///  - <b>The services.</b> <c>EmiSuggester.Compose</c>, <c>EmiRingLayout.Solve</c>,
    ///    <c>EmiSfx</c>, <c>EmiFace.PixelFont</c> and <c>ModResourceResolver</c> are all still in
    ///    the WPF head; each is a stub or a named placeholder below.
    /// </summary>
    public partial class EmiRingWindow : Window
    {
        // ---------------------------------------------------------------- constants

        /// <summary>
        /// Card size in DIPs. The pitch demo drew 76 x 58 on a browser stage a foot from your face;
        /// on a real desktop at a 220 DIP body that read as six postage stamps thrown across the
        /// screen, so the owner sized them up on the first live run (QA 2026-08-29).
        ///
        /// <para>Grown again on the third ("text on the EMI circle cards is too small"): the card is
        /// sized off the LABEL now rather than the other way round. See <see cref="CardLabelFont"/>
        /// for the arithmetic - 136 is the narrowest card that holds the longest word in the
        /// catalogue on one line at the new size, at every DPI the app ships on.</para>
        /// </summary>
        public const double CardW = 136.0;

        /// <inheritdoc cref="CardW"/>
        public const double CardH = 102.0;

        /// <summary>
        /// The card label's font, in DIPs. Press Start 2P is drawn on an 8-unit em and the next rung
        /// up its ladder is 16, which wants a 250 DIP card; 10 is the house's own written practice
        /// (a whole DIP size) and what <see cref="CardW"/>'s fit arithmetic is solved against - the
        /// longest word in the catalogue is "Subliminals" at 11 characters, which wants 110 DIPs of
        /// the 118 a pinned card offers.
        ///
        /// <para>WPF also set <c>TextOptions.TextFormattingMode = Display</c> on the strip so glyph
        /// advances round to whole DEVICE pixels. Avalonia has no text-formatting modes, so that
        /// goes; <see cref="Layout"/>'s whole-pixel card origins still hold the cells on the
        /// grid.</para>
        /// </summary>
        private const double CardLabelFont = 10.0;

        /// <summary>The label's line box. The em IS the cell, so this is pure leading (2 DIP either side).</summary>
        private const double CardLabelLine = 14.0;

        /// <summary>Horizontal padding inside the name strip. Part of the fit arithmetic above.</summary>
        private const double LabelPadX = 4.0;

        /// <summary>The lock badge's glyph. It grew with the card: a padlock nobody can see is not a gate.</summary>
        private const double BadgeFont = 12.0;

        /// <summary>Air between her silhouette and a card's inner edge (owner call).</summary>
        private const double BodyGap = 14.0;

        /// <summary>
        /// THE FAN-OUT, retuned on the owner's second live run ("the animations that spawns those is
        /// too fast and not smooth at all", 2026-08-29): nearly twice the travel of the first cut, a
        /// longer gap between neighbours so the fan reads as a deal rather than an explosion, and a
        /// fade that outlives the first third of the move. Total <c>5 x 62 + 340 = 650 ms</c> from
        /// the click to the last card at rest.
        /// </summary>
        private const double PopStaggerMs = 62.0;

        /// <inheritdoc cref="PopStaggerMs"/>
        private const double PopMs = 340.0;

        /// <inheritdoc cref="PopStaggerMs"/>
        private const double FadeMs = 210.0;

        /// <summary>
        /// Where a card starts. 0.4 was small enough that the GROWTH was the loudest thing in the
        /// animation; from 0.55 the card is already a card and only the move reads.
        /// </summary>
        private const double PopFromScale = 0.55;

        /// <summary>
        /// THE FOLD, the mirror of the fan (owner, third live run: "we might need the reverse
        /// animation when we close the circle"). The cards fly back into her, shrink to
        /// <see cref="PopFromScale"/> and fade, LAST-DEALT FIRST.
        ///
        /// <para>Deliberately about half the open: <c>5 x 26 + 190 = 320 ms</c> against the fan's
        /// 650. An entrance is a presentation and can afford to be watched; an exit is an
        /// acknowledgement, and one that takes as long as the entrance reads as the app being slow
        /// to let go. The ring is functionally SHUT the instant the fold starts - what is left is
        /// only the picture catching up. The easings are EaseIn rather than EaseOut: the cards
        /// should look pulled home rather than thrown.</para>
        /// </summary>
        private const double FoldStaggerMs = 26.0;

        /// <inheritdoc cref="FoldStaggerMs"/>
        private const double FoldMs = 190.0;

        /// <inheritdoc cref="FoldStaggerMs"/>
        private const double FoldFadeMs = 150.0;

        /// <summary>
        /// Slack on the fold's finish timer. A DispatcherTimer fires no EARLIER than its interval but
        /// can fire late, and hiding the window one frame before the last card has faded is the exact
        /// blink this whole wave is about; a couple of frames of an already-invisible card cost
        /// nothing.
        /// </summary>
        private const double FoldTailMs = 40.0;

        private const double HoverScale = 1.08;

        /// <summary>
        /// The card frame, thickened on the owner's second live run ("the border of those images in
        /// the circle of emi should be bolder, thicker"). A 1 DIP hairline read as a CSS outline on a
        /// dark desktop; 3 DIP of pink with a 1 DIP dark seam inside it reads as a PIXEL frame, which
        /// is the house look. Pinned goes to 4 and solid, so a pin is legible across the room.
        /// </summary>
        private const double CardBorder = 3.0;

        /// <inheritdoc cref="CardBorder"/>
        private const double CardBorderPinned = 4.0;

        /// <summary>The dark seam drawn INSIDE the pink frame. Without it the frame is a line, not a frame.</summary>
        private const double CardSeam = 1.0;

        private static readonly Color FramePink = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color FrameRest = Color.FromArgb(0x88, 0xFF, 0x69, 0xB4);

        /// <summary>
        /// ponytail: needs EmiFace.PixelFont (WPF head, Services/EmiDesk/EmiFace.cs), the resolver
        /// that loads the SHIPPED Press Start 2P rather than looking it up by name. Until it moves
        /// to Core this head does what EmiDock.axaml already does - names the family and accepts the
        /// fallback - which on Linux means the label draws in the fallback monospace face, not in
        /// the pixel font. Legibility is unaffected; the look is not the same.
        /// </summary>
        private static readonly FontFamily PixelFont = new FontFamily("Press Start 2P, Consolas, monospace");

        // ---------------------------------------------------------------- state

        private readonly Canvas _field;
        private readonly List<Border> _cards = new();
        private IReadOnlyList<EmiRingCard> _slots = Array.Empty<EmiRingCard>();

        /// <summary>Her silhouette and the point the fan orbits, both in PHYSICAL pixels, handed in
        /// by the widget. See <see cref="SetWidgetGeometry"/>.</summary>
        private PixelRect _bodyPx = new PixelRect(0, 0, 220, 420);

        private PixelPoint _anchorPx = new PixelPoint(110, 210);

        private double _cx, _cy;          // the fan centre, in this window's DIP canvas coords
        private bool _open;
        private bool _closingForGood;

        /// <summary>
        /// True between the first frame of the fold and the <see cref="HideNow"/> at the end of it.
        /// The ring is already SHUT here - this only says that the picture has not finished catching
        /// up, and it is what makes a second <see cref="CloseRing"/> during the fold a no-op instead
        /// of a second <see cref="RingClosed"/>.
        /// </summary>
        private bool _folding;

        /// <summary>
        /// Whether <see cref="RingClosed"/> has already been raised for the opening now ending. The
        /// event fires at the START of the fold (that is when the ring is actually shut), so a close
        /// that arrives again while the picture is still catching up - a Kill mid-fold, say - must
        /// hide the window without announcing a second dismissal into the ignore streak.
        /// </summary>
        private bool _closeAnnounced = true;

        /// <summary>The fold's finish. A timer rather than a completion callback: there are five
        /// animations per card and the last one to finish is not the last one to be started.</summary>
        private DispatcherTimer? _foldEnd;

        /// <summary>
        /// Every animation of the current opening. WPF cleared animations one property at a time with
        /// <c>BeginAnimation(prop, null)</c>; here one source cancels the whole pop or fold at once,
        /// and each <see cref="Tween"/> stops its own timer when it sees the token go.
        /// </summary>
        private CancellationTokenSource? _anim;

        /// <summary>The DPI scale <see cref="Layout"/> last solved against. See <see cref="OpenRing"/>.</summary>
        private double _laidOutAtScale;

        /// <summary>True while the fan is on screen.</summary>
        public bool IsOpen => _open;

        /// <summary>True when this opening ended in a card pick rather than a dismissal.</summary>
        public bool PickedThisOpening { get; private set; }

        /// <summary>A card was left-clicked. The ring has already folded; the caller opens the target.</summary>
        public event EventHandler<EmiRingCard>? CardPicked;

        /// <summary>The ring folded. The bool says whether a card was picked on the way out.</summary>
        public event EventHandler<bool>? RingClosed;

        // ---------------------------------------------------------------- ctor

        /// <summary>
        /// Builds the ring for one widget, at the geometry she is standing on. Created hidden;
        /// <see cref="OpenRing"/> shows it.
        /// </summary>
        /// <param name="bodyPx">Her silhouette, PHYSICAL pixels.</param>
        /// <param name="anchorPx">The point the fan orbits, PHYSICAL pixels.</param>
        public EmiRingWindow(PixelRect bodyPx, PixelPoint anchorPx)
        {
            AvaloniaXamlLoader.Load(this);
            _field = this.FindControl<Canvas>("Field")!;
            SetWidgetGeometry(bodyPx, anchorPx);
        }

        /// <summary>
        /// Render constructor: a full six-card fan of sample slots, laid out and settled at rest, so
        /// <c>--render-all</c> can discover the window and draw the ring rather than an empty canvas.
        /// Internal, so no production caller can ship the sample.
        /// </summary>
        internal EmiRingWindow() : this(new PixelRect(590, 290, 220, 420), new PixelPoint(700, 500))
        {
            // The six a brand new user sees, in catalogue order, with EmiTargets' own hues. No art:
            // LoadThumb is a stub on this head, which is the flat-hue-tile road the WPF ring takes
            // for a target with no PNG (the book) and the only one it can take here.
            _slots = new[]
            {
                new EmiRingCard("arcademy", "emi_desk_target_arcademy", Color.FromRgb(0xFF, 0x69, 0xB4), false, true),
                new EmiRingCard("loom",     "emi_desk_target_loom",     Color.FromRgb(0x6F, 0xD3, 0xFF), false, false),
                new EmiRingCard("fyp",      "emi_desk_target_fyp",      Color.FromRgb(0xB9, 0x80, 0xFF), true,  false),
                new EmiRingCard("sessions", "emi_desk_target_sessions", Color.FromRgb(0x8C, 0x9E, 0xFF), false, false),
                new EmiRingCard("flashes",  "emi_desk_target_flashes",  Color.FromRgb(0xFF, 0x8F, 0xA3), false, false),
                new EmiRingCard("codex",    "emi_desk_target_codex",    Color.FromRgb(0xE6, 0xD3, 0xA8), false, false),
            };

            BuildCards();
            Layout();
            SettleCards();
        }

        /// <summary>
        /// Where she is, in PHYSICAL pixels - the two values the WPF ring read straight off its
        /// owner. Call it again whenever the widget moves or resizes, then <see cref="Relayout"/>.
        /// </summary>
        public void SetWidgetGeometry(PixelRect bodyPx, PixelPoint anchorPx)
        {
            if (bodyPx.Width > 0 && bodyPx.Height > 0) _bodyPx = bodyPx;
            _anchorPx = anchorPx;
        }

        /// <summary>This window's own DPI scale. Never assume 1.0 on a multi-monitor desk.</summary>
        private double DipScale
        {
            get
            {
                try
                {
                    var s = Screens?.ScreenFromWindow(this)?.Scaling
                            ?? Screens?.Primary?.Scaling
                            ?? 1.0;
                    if (s > 0) return s;
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring scale probe failed"); }
                return 1.0;
            }
        }

        // ---------------------------------------------------------------- open / close

        /// <summary>Compose the ring, fan it out and start listening for the click that closes it.</summary>
        public void OpenRing()
        {
            if (_closingForGood) return;
            try
            {
                PickedThisOpening = false;
                _slots = Compose();
                if (_slots.Count == 0)
                {
                    Log.Information("[EmiDesk] ring has nothing to show, staying shut");
                    // A fold from the last opening could still be on screen behind this. Nothing is
                    // going to replace it, so put it away rather than leaving it hanging.
                    if (_folding || IsVisible) { CancelFold(); HideNow(); }
                    return;
                }

                // A fold from the PREVIOUS opening may still be in the air. Cancel it before anything
                // else: its finish timer would otherwise land in the middle of this open and hide a
                // ring that had just been dealt. (BuildCards below stops the old cards' animations
                // and clears the canvas, so there is nothing else of the fold left to unwind.)
                CancelFold();

                BuildCards();
                PlaceWindow();
                Layout();

                if (!IsVisible) Show();

                // A window that was hidden on one monitor and shown on another only learns its new
                // scale when it is shown. If that happened, the fan we just solved is in the wrong
                // space - and every card is still invisible at this point, so re-solving is free.
                if (Math.Abs(DipScale - _laidOutAtScale) > 0.001) Layout();

                PlayPop();
                InstallHooks();
                _open = true;

                // Armed last, and only on the road that really opened: an opening that never happened
                // (no slots, a throw before here) must not leave a dismissal owed to the ignore streak.
                _closeAnnounced = false;

                // ponytail: needs EmiSfx.RingOpen() (WPF head, Services/EmiDesk/EmiSfx.cs).
                Log.Information("[EmiDesk] ring open with {Count} cards, visible={Visible}", _slots.Count, IsVisible);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring failed to open");
                try { CloseRing(); } catch { /* nothing else to try */ }
            }
        }

        /// <summary>
        /// Fold the ring. Idempotent, and safe to call re-entrantly - it is called from Escape, from
        /// the drag watch, from the pick and from the tear-down seam, sometimes two of those inside
        /// one gesture.
        ///
        /// <para>The ring is SHUT when this returns, whether or not the fold is still playing:
        /// <see cref="IsOpen"/> is false and <see cref="RingClosed"/> has already fired. Everything
        /// after that point is a picture catching up, and a card in flight is not clickable.</para>
        /// </summary>
        public void CloseRing()
        {
            try
            {
                // Unconditionally first, exactly as in the original: the hooks must never outlive
                // the gesture that armed them. Both are stubs on this head - see InstallHooks.
                RemoveHooks();

                // A fold already owns this closing. Not a second RingClosed, not a second fold.
                if (_folding) return;
                if (!_open && !IsVisible) return;

                _open = false;

                // The bookkeeping IS the close, so it happens now rather than when the animation
                // ends: a handler that counts dismissals and a caller that reads IsOpen straight
                // after us can then never disagree about when the ring shut.
                if (!_closeAnnounced)
                {
                    _closeAnnounced = true;
                    Log.Information("[EmiDesk] ring closed (picked={Picked})", PickedThisOpening);
                    try { RingClosed?.Invoke(this, PickedThisOpening); }
                    catch (Exception ex) { Log.Debug(ex, "[EmiDesk] RingClosed handler threw"); }
                }

                // No fold when there is nothing to fold, when the window is already gone, or when the
                // app is on its way down: an animation racing a dispatcher shutdown is a hang, and a
                // fan that lingers after she has poofed reads as a crash (SEAMS: the ring folds first).
                if (_closingForGood || !IsVisible || _cards.Count == 0)
                {
                    HideNow();
                    return;
                }

                // ponytail: needs EmiSfx.RingClose() (WPF head, Services/EmiDesk/EmiSfx.cs).
                PlayFold();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring close failed");
                try { HideNow(); } catch { /* nothing else to try */ }
            }
        }

        /// <summary>
        /// The end of a close: drop the cards and put the window away. Called at the tail of the fold,
        /// and directly on every path that must not animate.
        /// </summary>
        private void HideNow()
        {
            _folding = false;
            StopFoldTimer();

            StopCardAnimations();
            _cards.Clear();
            _field.Children.Clear();
            Hide();

            // WPF also shrank the window to a pixel here, because a hidden LAYERED window keeps
            // whatever was last handed to UpdateLayeredWindow and Win32 puts that straight back on
            // screen the moment ShowWindow runs. There is no layered surface to go stale on this
            // head - the compositor drops a hidden window's buffer - and the shrink is dropped with
            // the trap it worked around. OpenRing sizes the window from PlaceWindow/Layout anyway.
        }

        /// <summary>
        /// Kill a fold in flight without hiding anything - a new opening is taking over the window.
        /// The cards themselves are dealt with by <see cref="BuildCards"/>, which stops their
        /// animations and clears the canvas; what must not survive is the finish timer.
        /// </summary>
        private void CancelFold()
        {
            if (!_folding && _foldEnd == null) return;
            _folding = false;
            StopFoldTimer();
        }

        private void StopFoldTimer()
        {
            try
            {
                if (_foldEnd == null) return;
                _foldEnd.Stop();
                _foldEnd = null;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring fold timer would not stop"); }
        }

        /// <summary>Re-run the layout in place (she moved or resized). No pop, no re-compose.</summary>
        public void Relayout()
        {
            if (!_open) return;
            try
            {
                // No PlaceWindow: Layout owns the window rect now (it sizes it to the fan), and doing
                // both means two resizes per follow-the-widget tick.
                Layout();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring relayout failed");
            }
        }

        /// <summary>Re-compose and repaint without folding (a pin changed under the pointer).</summary>
        public void Rebuild()
        {
            if (!_open) return;
            try
            {
                _slots = Compose();
                BuildCards();
                Layout();

                // SettleCards is new here. BuildCard pre-poses every card invisible for the pop, and
                // this path never pops, so the WPF original repainted the ring into nothing; the
                // cards were only ever seen again after the next OpenRing.
                SettleCards();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring rebuild failed");
            }
        }

        /// <summary>Let the ring go for good: app shutdown, or the widget closing.</summary>
        public void Kill()
        {
            try
            {
                _closingForGood = true;

                // A fold in flight would otherwise hold the close off (CloseRing yields to it) and
                // then fire its finish timer into a window that no longer exists.
                CancelFold();

                CloseRing();
                Close();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring kill failed");
            }
        }

        // ponytail: needs EmiSuggester.Compose() (WPF head, Services/EmiDesk/EmiSuggester.cs), which
        // scores every target by decayed usage and fills the six slots pins-first. It reaches
        // EmiState, EmiTargets, App and the premium rail, so it moves to Core as one piece. Until
        // then the ring composes nothing and OpenRing stays shut - the same road the real Compose
        // takes when every door is unavailable.
        private static IReadOnlyList<EmiRingCard> Compose() => Array.Empty<EmiRingCard>();

        // ---------------------------------------------------------------- placement

        /// <summary>The work area of the monitor she is standing on, in PHYSICAL pixels.</summary>
        private PixelRect WorkArea()
        {
            try
            {
                var centre = new PixelPoint(
                    _bodyPx.X + _bodyPx.Width / 2,
                    _bodyPx.Y + _bodyPx.Height / 2);

                var screen = Screens?.ScreenFromPoint(centre) ?? Screens?.Primary;
                if (screen != null) return screen.WorkingArea;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring work-area probe failed");
            }
            return new PixelRect(0, 0, 1920, 1080);
        }

        private void PlaceWindow()
        {
            var work = WorkArea();
            double s = DipScale;
            if (s <= 0) s = 1.0;

            // WPF's Left/Top are DIPs; Avalonia's Position is PHYSICAL pixels, so the division that
            // was needed there is exactly what must NOT happen here. Width/Height stay DIPs.
            Position = new PixelPoint(work.X, work.Y);
            Width = Math.Max(1, work.Width / s);
            Height = Math.Max(1, work.Height / s);
        }

        /// <summary>
        /// The fan: a full circle around her, and then the window shrunk to the fan's own bounding
        /// box - a full-work-area window repaints far more surface than it needs to on every
        /// animation frame, and that was most of what the owner felt as a stutter in the fan-out.
        /// </summary>
        private void Layout()
        {
            var work = WorkArea();
            double s = DipScale;
            if (s <= 0) s = 1.0;
            _laidOutAtScale = s;            // OpenRing re-solves if the window learns a different one

            double workW = work.Width / s;
            double workH = work.Height / s;

            double ax = (_anchorPx.X - work.X) / s;         // work-area DIPs
            double ay = (_anchorPx.Y - work.Y) / s;

            double bodyW = _bodyPx.Width / s;

            var plan = SolveFan(ax, ay, bodyW, _cards.Count);

            double minX = ax, maxX = ax, minY = ay, maxY = ay;
            foreach (var p in plan)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X + CardW);
                maxY = Math.Max(maxY, p.Y + CardH);
            }

            // Room for the hover's 8 % grow, and never wider than the work area itself.
            const double Bleed = 12.0;
            minX = Math.Max(0, minX - Bleed);
            minY = Math.Max(0, minY - Bleed);
            maxX = Math.Min(workW, maxX + Bleed);
            maxY = Math.Min(workH, maxY + Bleed);

            Position = new PixelPoint(work.X + (int)Math.Round(minX * s), work.Y + (int)Math.Round(minY * s));
            Width = Math.Max(1, maxX - minX);
            Height = Math.Max(1, maxY - minY);

            _cx = ax - minX;
            _cy = ay - minY;

            for (int i = 0; i < _cards.Count && i < plan.Count; i++)
            {
                // Whole pixels: the pixel font goes to mush on a half-DIP offset.
                Canvas.SetLeft(_cards[i], Math.Round(plan[i].X - minX));
                Canvas.SetTop(_cards[i], Math.Round(plan[i].Y - minY));
            }

            // The frozen hit-rect snapshot lived here. It answered the global mouse hook and nothing
            // else; the local click-away that replaces part of that hook reads the cards' own hit
            // testing instead, so there is nothing to snapshot - see InstallHooks.

            // Information, not Debug: the file sink's floor is Information, so a Debug line here is
            // invisible in the log the owner actually sends back. Everything needed to reproduce a
            // "the circle is offset" report by hand is on this one line.
            Log.Information("[EmiDesk] ring fan cards={N} | anchor px ({AX},{AY}) | body px {BX},{BY} {BW}x{BH} " +
                            "| work {WX},{WY} {WW}x{WH} | scale {S:F2} | window {W:F0}x{H:F0} at {L},{T}",
                            plan.Count, _anchorPx.X, _anchorPx.Y,
                            _bodyPx.X, _bodyPx.Y, _bodyPx.Width, _bodyPx.Height,
                            work.X, work.Y, work.Width, work.Height, s,
                            Width, Height, Position.X, Position.Y);
        }

        /// <summary>
        /// Where the cards go, in work-area DIPs (top-left of each card).
        ///
        /// <para>ponytail: needs EmiRingLayout.Solve (WPF head, Services/EmiDesk/EmiRingLayout.cs),
        /// which is pure geometry and unit-tested. This is its FULL-CIRCLE branch only, copied
        /// arithmetic for arithmetic - the same base radius, the same -90 degree start and the same
        /// even spacing. What is missing is the part that earns the solver its tests: the radius
        /// search, the feasible-arc half fan pushed away from a screen edge, and the column fallback
        /// for a corner park. On a desk where she stands near an edge, cards will sit off-screen
        /// until the solver moves to Core; the clamp WPF used before the solver is deliberately NOT
        /// reintroduced, because clamping is not a layout, it is a way of hiding that the layout did
        /// not fit.</para>
        /// </summary>
        private static IReadOnlyList<Point> SolveFan(double cx, double cy, double bodyW, int count)
        {
            if (count <= 0) return Array.Empty<Point>();

            double r = bodyW * 0.5 + CardW * 0.5 + BodyGap;
            var pts = new Point[count];
            for (int i = 0; i < count; i++)
            {
                double a = (-90.0 + i * (360.0 / count)) * Math.PI / 180.0;
                pts[i] = new Point(cx + Math.Cos(a) * r - CardW * 0.5,
                                   cy + Math.Sin(a) * r - CardH * 0.5);
            }
            return pts;
        }

        // ---------------------------------------------------------------- the cards

        private void BuildCards()
        {
            StopCardAnimations();
            _cards.Clear();
            _field.Children.Clear();

            foreach (var slot in _slots)
            {
                try
                {
                    var card = BuildCard(slot);
                    _cards.Add(card);
                    _field.Children.Add(card);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[EmiDesk] ring card build failed for {Target}", slot.Id);
                }
            }
        }

        private Border BuildCard(EmiRingCard slot)
        {
            var frameBrush = new SolidColorBrush(slot.Pinned ? FramePink : FrameRest);

            double thickness = slot.Pinned ? CardBorderPinned : CardBorder;

            var card = new Border
            {
                Width = CardW,
                Height = CardH,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0E, 0x0E, 0x1C)),
                BorderThickness = new Thickness(thickness),
                BorderBrush = frameBrush,
                Cursor = new Cursor(StandardCursorType.Hand),
                UseLayoutRounding = true,
                RenderTransformOrigin = RelativePoint.Center,
                Tag = slot,

                // WPF animated the frame brush's Colour, which needs an unfrozen SolidColorBrush and
                // a ColorAnimation. Avalonia has a first-class BrushTransition instead, so the hover
                // simply ASSIGNS a new brush and the transition does the 110 ms fade - and no pop or
                // fold animation touches BorderBrush, so the two can never fight over it.
                Transitions = new Transitions
                {
                    new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(110) },
                },
            };

            ToolTip.SetTip(card, TipFor(slot));

            // Pre-created, and pre-posed at the pop's starting values: BuildCards runs before the
            // window is even shown, and a card that is added at scale 1 / opacity 1 gets ONE frame at
            // its full size in the top-left corner before Layout() has placed it. That single frame is
            // the flash the owner saw as "not smooth".
            card.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(PopFromScale, PopFromScale),
                    new TranslateTransform(0, 0),
                },
            };
            card.Opacity = 0;

            // The dark seam INSIDE the pink, which is what turns a 3 DIP line into a frame. It is a
            // sibling border rather than a second BorderThickness because a Border has exactly one.
            var frame = new Grid();
            card.Child = frame;

            var seam = new Border
            {
                BorderThickness = new Thickness(CardSeam),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x08, 0x08, 0x12)),
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false,
            };

            double inner = thickness + CardSeam;
            var grid = new Grid
            {
                // A Border never clips its child (the 2026-08-13 verdict), so the rounded corners are
                // done with an explicit clip geometry instead of hoping CornerRadius does it.
                Margin = new Thickness(CardSeam),
                Clip = new RectangleGeometry(
                    new Rect(0, 0, Math.Max(1, CardW - 2 * inner), Math.Max(1, CardH - 2 * inner)), 4, 4),
            };
            frame.Children.Add(grid);
            frame.Children.Add(seam);

            // ---- the face of the card: dashboard art, or a flat hue tile ----------
            var art = LoadThumb(slot);
            if (art != null)
            {
                grid.Children.Add(new Image
                {
                    Source = art,
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false,
                    Opacity = slot.Locked ? 0.42 : 0.92,
                });
            }
            else
            {
                var tile = new SolidColorBrush(slot.Hue) { Opacity = slot.Locked ? 0.28 : 0.62 };
                grid.Children.Add(new Rectangle { Fill = tile, IsHitTestVisible = false });
            }

            // ---- the name strip ---------------------------------------------------
            var strip = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x0E, 0x0E, 0x1C)),
                Padding = new Thickness(LabelPadX, 3, LabelPadX, 3),
                IsHitTestVisible = false,
            };

            strip.Child = new TextBlock
            {
                Text = SafeLabel(slot),
                FontFamily = PixelFont,
                FontSize = CardLabelFont,
                LineHeight = CardLabelLine,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 2 * CardLabelLine + 2,     // two lines: "The Arcademy" breaks at its space
                TextAlignment = TextAlignment.Center,
            };
            grid.Children.Add(strip);

            // ---- the badges -------------------------------------------------------
            // The pin badge came off here on the owner's third live run ("the Pin button is not
            // usable right now, I propose we remove it from there"); pinning is in her options menu
            // now. What stays is how a pinned card LOOKS - the thicker solid-pink frame above.
            if (slot.Locked) grid.Children.Add(Badge("\U0001F512", HorizontalAlignment.Left, 0.65));

            // ---- input ------------------------------------------------------------
            // Left button only, on release, exactly as MouseLeftButtonUp. The right button is
            // deliberately unhandled: on Windows the global hook read a right-click outside the hot
            // rects as a dismissal, and a card without a gesture of its own should do nothing.
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                e.Handled = true;
                OnCardPicked(slot);
            };
            card.PointerEntered += (_, _) => Hover(card, slot, true);
            card.PointerExited += (_, _) => Hover(card, slot, false);

            return card;
        }

        private static Control Badge(string glyph, HorizontalAlignment side, double opacity)
        {
            return new Border
            {
                HorizontalAlignment = side,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3),
                Padding = new Thickness(2, 1, 2, 1),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x0E, 0x0E, 0x1C)),
                Opacity = opacity,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = glyph,
                    // NOT the pixel font: this is an emoji glyph and Press Start 2P has none. WPF had
                    // to name "Segoe UI Emoji" to land somewhere chosen; Avalonia renders colour
                    // emoji from the system fallback natively (CLAUDE.md, porting note 3), so the
                    // family name goes and the default face draws the padlock.
                    FontSize = BadgeFont,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
                },
            };
        }

        private void Hover(Border card, EmiRingCard slot, bool on)
        {
            try
            {
                if (card.RenderTransform is not TransformGroup tg || tg.Children.Count < 1) return;
                if (tg.Children[0] is not ScaleTransform sc) return;

                // No cancellation token, deliberately. One shared source would let a pointer swept
                // across two cards cancel the first card's shrink half way and leave it stuck at
                // 1.04; per-card sources for a 110 ms tween is bookkeeping nobody needs. Two
                // overlapping tweens on the same transform settle on the one that ends last, which
                // is always the newer.
                double to = on ? HoverScale : 1.0;
                Tween(v => sc.ScaleX = v, sc.ScaleX, to, 110, 0, new LinearEasing());
                Tween(v => sc.ScaleY = v, sc.ScaleY, to, 110, 0, new LinearEasing());

                // The frame lights to FULL pink under the pointer. A pinned card is already full, so
                // its own assignment is a no-op rather than a special case. The BrushTransition set
                // in BuildCard is what makes this a 110 ms fade rather than a jump.
                card.BorderBrush = new SolidColorBrush((on || slot.Pinned) ? FramePink : FrameRest);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring hover failed");
            }
        }

        private static string SafeLabel(EmiRingCard slot)
        {
            try
            {
                var s = Loc.Get(slot.LabelKey);
                return string.IsNullOrWhiteSpace(s) || s == slot.LabelKey ? slot.Id : s;
            }
            catch { return slot.Id; }
        }

        /// <summary>
        /// The card's hint. Returned as a built control rather than a bare string on purpose: a
        /// string gets the DEFAULT tooltip chrome, which sat on her pixel ring like a system error
        /// (QA 2026-08-29). Same palette and same face as the name strip, so it reads as part of the
        /// card.
        /// </summary>
        private static object? TipFor(EmiRingCard slot)
        {
            try
            {
                string key = slot.Locked ? "emi_desk_ring_tip_locked"
                           : slot.Pinned ? "emi_desk_ring_tip_pinned"
                                         : "emi_desk_ring_tip_suggested";
                var s = Loc.Get(key);
                if (string.IsNullOrWhiteSpace(s) || s == key) return null;

                return new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0E, 0x0E, 0x1C)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x69, 0xB4)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 5, 7, 5),
                    Child = new TextBlock
                    {
                        Text = s,
                        FontFamily = PixelFont,
                        FontSize = CardLabelFont,
                        LineHeight = 16,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 240,
                    },
                };
            }
            catch { return null; }
        }

        // ponytail: needs Services.ModResourceResolver.ResolveImageDecoded (WPF head), so a .ccpmod's
        // own card art wins exactly like the dashboard. Every card therefore draws the flat hue tile
        // - which is a road the WPF ring already takes for a target with no PNG.
        private static IImage? LoadThumb(EmiRingCard slot) => null;

        // ---------------------------------------------------------------- animation

        /// <summary>One property, from A to B. The Avalonia twin of WPF's
        /// <c>BeginAnimation(prop, new DoubleAnimation(from, to, dur) { BeginTime, EasingFunction })</c>.
        /// <c>FillMode.Forward</c> is what makes the end value stick the way WPF's does.</summary>
        /// <summary>The tween tick, in ms. One timer per animated property, as WPF had one clock.</summary>
        private const int TweenTickMs = 16;

        /// <summary>
        /// Drive one value from <paramref name="from"/> to <paramref name="to"/> and hand each
        /// sample to <paramref name="apply"/>.
        ///
        /// <para><b>This used to build an <c>Animation</c> and it never once ran.</b> Every card
        /// animation here targets a <c>ScaleTransform</c> or a <c>TranslateTransform</c>, and
        /// <c>Animation.RunAsync(someTransform)</c> resolves to <c>TransformAnimator</c>, which
        /// casts its target to <c>Visual</c> and throws <c>InvalidCastException</c> - straight into
        /// the <c>catch</c> around the loop, taking the card's opacity fade with it. So the pop, the
        /// fold and the hover grow were ALL inert, and every one of them logged at Debug and looked
        /// fine in a render. Measured 2026-09-04, alongside the same failure in
        /// <c>EmiDeskWindow</c>'s click squash and drag pendulum.</para>
        ///
        /// <para>Avalonia's supported road is a <c>TransformOperations</c> setter on the VISUAL, and
        /// a card cannot take it: its <c>TransformGroup</c> holds a scale and a translate that the
        /// hover and the pop drive independently. So the value is written by a timer, which is what
        /// the widget's own drag lean has always done.</para>
        ///
        /// <para><paramref name="from"/> is applied at once, before the delay, because that is what
        /// the callers were relying on <c>FillMode.Forward</c> plus a base value to do.</para>
        /// </summary>
        private void Tween(Action<double> apply, double from, double to,
                           double ms, double delayMs, Easing ease, CancellationToken token = default)
        {
            try
            {
                apply(from);
                if (ms <= 0) { apply(to); return; }

                var started = DateTime.UtcNow;
                DispatcherTimer? timer = null;
                timer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(TweenTickMs), DispatcherPriority.Render, (_, _) =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested || _closingForGood) { timer!.Stop(); return; }
                            double t = (DateTime.UtcNow - started).TotalMilliseconds - delayMs;
                            if (t < 0) return;                       // still in the stagger
                            double p = Math.Min(1, t / ms);
                            apply(from + (to - from) * ease.Ease(p));
                            if (p >= 1) timer!.Stop();
                        }
                        catch (Exception ex)
                        {
                            timer!.Stop();
                            Log.Debug(ex, "[EmiDesk] ring tween step failed");
                        }
                    });
                timer.Start();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring tween failed to start");
            }
        }

        private void PlayPop()
        {
            try
            {
                var token = RestartAnimations();

                // Two easings, not one. WPF gave the MOVE a BackEase at amplitude 0.30 - a settle you
                // feel rather than watch. Avalonia's BackEaseOut has no amplitude and overshoots
                // roughly three times as far, which is the "snap" the owner had this retuned away
                // from, so the move takes the SCALE's cubic instead and loses the settle. ponytail:
                // an amplitude-carrying BackEase is ~15 lines of Easing subclass; write it when a
                // second view wants one.
                var move = new CubicEaseOut();
                var grow = new CubicEaseOut();
                var fadeEase = new QuadraticEaseOut();

                for (int i = 0; i < _cards.Count; i++)
                {
                    var card = _cards[i];
                    if (card.RenderTransform is not TransformGroup tg || tg.Children.Count < 2) continue;
                    if (tg.Children[0] is not ScaleTransform sc) continue;
                    if (tg.Children[1] is not TranslateTransform tr) continue;

                    double dx = _cx - (Canvas.GetLeft(card) + CardW / 2.0);
                    double dy = _cy - (Canvas.GetTop(card) + CardH / 2.0);
                    double begin = i * PopStaggerMs;

                    // Each tween writes its From at once, before the stagger runs down, so a card
                    // waiting its turn sits balled up at her middle rather than flashing at the spot
                    // it is about to fly to.
                    Tween(v => tr.X = v, dx, 0, PopMs, begin, move, token);
                    Tween(v => tr.Y = v, dy, 0, PopMs, begin, move, token);
                    Tween(v => sc.ScaleX = v, PopFromScale, 1.0, PopMs, begin, grow, token);
                    Tween(v => sc.ScaleY = v, PopFromScale, 1.0, PopMs, begin, grow, token);
                    Tween(v => card.Opacity = v, 0, 1, FadeMs, begin, fadeEase, token);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring pop failed");
            }
        }

        /// <summary>
        /// The fold: the pop run backwards, LAST-DEALT FIRST, and at about half its length. Started
        /// only from <see cref="CloseRing"/>, which has already raised <see cref="RingClosed"/> - so
        /// nothing here is load-bearing and every early return can simply hide.
        /// </summary>
        private void PlayFold()
        {
            try
            {
                _folding = true;
                var token = RestartAnimations();

                var move = new CubicEaseIn();
                var shrink = new CubicEaseIn();
                var fadeEase = new QuadraticEaseIn();

                int n = _cards.Count;
                for (int i = 0; i < n; i++)
                {
                    var card = _cards[i];

                    // Belt as well as braces on "a card in flight is not clickable".
                    card.IsHitTestVisible = false;

                    if (card.RenderTransform is not TransformGroup tg || tg.Children.Count < 2) continue;
                    if (tg.Children[0] is not ScaleTransform sc) continue;
                    if (tg.Children[1] is not TranslateTransform tr) continue;

                    // Where home is, from wherever the card is sitting NOW. Computed off the canvas
                    // position rather than off the pop's own dx/dy so that a fold arriving mid-pop
                    // (open, then a pick half a second later) still flies to her and not past her.
                    double dx = _cx - (Canvas.GetLeft(card) + CardW / 2.0);
                    double dy = _cy - (Canvas.GetTop(card) + CardH / 2.0);

                    // Reverse stagger: the card dealt last is the first one taken back.
                    double begin = (n - 1 - i) * FoldStaggerMs;

                    // Every animation carries a From, read off where the card already is: cancelling
                    // the pop above leaves each property at its last animated value, and starting
                    // from that is what keeps a fold mid-pop continuous.
                    Tween(v => tr.X = v, tr.X, dx, FoldMs, begin, move, token);
                    Tween(v => tr.Y = v, tr.Y, dy, FoldMs, begin, move, token);
                    Tween(v => sc.ScaleX = v, sc.ScaleX, PopFromScale, FoldMs, begin, shrink, token);
                    Tween(v => sc.ScaleY = v, sc.ScaleY, PopFromScale, FoldMs, begin, shrink, token);
                    Tween(v => card.Opacity = v, card.Opacity, 0, FoldFadeMs, begin, fadeEase, token);
                }

                double total = (n <= 1 ? 0 : (n - 1) * FoldStaggerMs) + FoldMs + FoldTailMs;
                _foldEnd = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(total),
                };
                _foldEnd.Tick += OnFoldFinished;
                _foldEnd.Start();
            }
            catch (Exception ex)
            {
                // A fold that cannot be played is not a reason to leave the fan on the desktop.
                Log.Debug(ex, "[EmiDesk] ring fold failed, hiding straight away");
                try { HideNow(); } catch { /* nothing else to try */ }
            }
        }

        private void OnFoldFinished(object? sender, EventArgs e)
        {
            try { HideNow(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring fold finish failed"); }
        }

        /// <summary>Cancel whatever is in the air and hand out the token for what comes next.</summary>
        private CancellationToken RestartAnimations()
        {
            StopCardAnimations();
            _anim = new CancellationTokenSource();
            return _anim.Token;
        }

        /// <summary>
        /// WPF cleared five animations per card with <c>BeginAnimation(prop, null)</c>. Avalonia's
        /// are all on one token, so this is the whole set at once - and cancelling leaves each
        /// property at the value it had reached, which is exactly what the fold's From reads.
        /// </summary>
        private void StopCardAnimations()
        {
            try
            {
                _anim?.Cancel();
                _anim?.Dispose();
                _anim = null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring animation stop failed");
            }
        }

        /// <summary>
        /// Put every card at rest: full size, home, opaque. The pop's end state without the pop,
        /// which is what the render constructor needs (a headless frame is captured long before an
        /// animation clock has ticked) and what <see cref="Rebuild"/> wants when it repaints a ring
        /// that is already open.
        /// </summary>
        private void SettleCards()
        {
            foreach (var card in _cards)
            {
                card.Opacity = 1;
                if (card.RenderTransform is not TransformGroup tg || tg.Children.Count < 2) continue;
                if (tg.Children[0] is ScaleTransform sc) { sc.ScaleX = 1.0; sc.ScaleY = 1.0; }
                if (tg.Children[1] is TranslateTransform tr) { tr.X = 0; tr.Y = 0; }
            }
        }

        // ---------------------------------------------------------------- input

        private void OnCardPicked(EmiRingCard slot)
        {
            try
            {
                PickedThisOpening = true;
                CloseRing();
                CardPicked?.Invoke(this, slot);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring pick failed for {Target}", slot.Id);
            }
        }

        // The card's own pin gesture lived here. It is gone (owner, third live run) and pinning is
        // in her options menu, which is EmiDeskWindow's - see the PinToggled note in the class doc.

        // ---------------------------------------------------------------- the hooks

        /// <summary>
        /// CLICK AWAY TO FOLD, as far as this head can reach it.
        ///
        /// <para>WPF installed a low-level <c>SetWindowsHookEx</c> MOUSE hook: a click anywhere on
        /// the desktop outside the cards' hot rects folded the ring, and the hook never swallowed
        /// that click. X11 has no equivalent a view can reach - the shape of it is a passive XI2
        /// grab, which belongs next to <c>X11Overlay</c> in its own layer, not bolted onto a
        /// window - so the desktop-wide half of the gesture stays lost.</para>
        ///
        /// <para>WHAT IS RECOVERED HERE is the part that lands inside this window, and it is the
        /// part the port actually broke. On Windows a layered window does not hit-test fully
        /// transparent pixels, so a click in a GAP between two cards fell straight through to
        /// whatever was underneath and the hook read it as a dismissal. On X11 the server hit-tests
        /// the whole rectangle, so that same click hits this window and, with no handler, is
        /// silently eaten - the fan's own gaps swallowing the click that opened them. Folding on it
        /// is strictly better than eating it and is the same OUTCOME the hook produced; what is
        /// still missing is that the click also reached the desktop.</para>
        ///
        /// <para>On <c>PointerReleased</c>, not pressed: a card's own handler marks the release
        /// handled, and Avalonia does not raise a bubbled handler for an already-handled event, so
        /// a pick can never be read as a dismissal. Escape stays lost either way - this window is
        /// <c>Focusable="False"</c> and <c>ShowActivated="False"</c>, so it never has the keyboard
        /// and a local <c>KeyDown</c> would recover nothing.</para>
        /// </summary>
        private void InstallHooks()
        {
            PointerReleased -= OnClickAway;
            PointerReleased += OnClickAway;
        }

        /// <inheritdoc cref="InstallHooks"/>
        private void RemoveHooks() => PointerReleased -= OnClickAway;

        /// <inheritdoc cref="InstallHooks"/>
        private void OnClickAway(object? sender, PointerReleasedEventArgs e)
        {
            try
            {
                if (!_open || _folding) return;

                // Any button, exactly as the hook: it read a right-click outside the hot rects as a
                // dismissal too.
                Log.Information("[EmiDesk] ring dismissed by a click in the fan");
                CloseRing();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring click-away failed");
            }
        }
    }
}
