using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP (same note as DescentFuseWindow and EmiDeskWindow): everything under Windows\
// lives in the FLAT ConditioningControlPanel namespace. A ConditioningControlPanel.Windows.*
// namespace shadows the WinRT Windows root and breaks Services\ScreenOcrService.cs with a CS0234
// that names a file you never touched. Do not "tidy" it.
namespace ConditioningControlPanel;

/// <summary>
/// The ring: six feature cards fanned around EMI, in their own sibling window.
///
/// <para>Why a second window at all: <c>EmiDeskWindow</c>'s transparent pad is nowhere near enough
/// for a fan around a 420 DIP body. This window opens over the work area of the monitor she is
/// standing on and is then sized down to the FAN'S OWN BOUNDING BOX by <c>Layout()</c> - a
/// full-screen layered window repaints a whole per-pixel-alpha surface on every animation frame,
/// which is most of what the fan-out stutter was. It is placed with the same
/// physical-pixels-over-own-DPI arithmetic the widget uses. <c>BodyScreenRect</c> and
/// <c>RingAnchorScreenPoint</c> are PHYSICAL PIXELS: converting them with an assumed scale of 1.0
/// is the coordinate trap that ate the gaze work.</para>
///
/// <para>Where the cards actually go is <see cref="EmiRingLayout"/>, which is pure geometry and
/// unit-tested: the corner case (two screen edges at once) is the one that broke.</para>
///
/// <para>Closing on a click outside is done with a low-level mouse hook rather than a full-screen
/// catcher window. The hook costs nothing while the ring is shut (it is installed on open and
/// removed on close), it cannot steal focus, and, crucially, it NEVER swallows the click: the ring
/// folds and the click still lands on whatever the user was actually aiming at.</para>
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
    /// The card label's font, in DIPs.
    ///
    /// <para>TWO THINGS WERE WRONG HERE, and only one of them was the number. The family was built
    /// by NAME (<c>new FontFamily("Press Start 2P, Consolas, ...")</c>), and Press Start 2P is
    /// SHIPPED, not installed - a name lookup only ever sees installed faces, so every label on
    /// the ring was falling through to Consolas. Consolas advances 0.55 em per character against
    /// Press Start 2P's 1.0, so the strip was drawing at barely half the width the 8 DIP was
    /// chosen for, in the wrong typeface. It goes through <see cref="EmiFace.PixelFont"/> now,
    /// which is the resolver the bubble, the offer chips and the dock pill already use.</para>
    ///
    /// <para>Then the size. Press Start 2P is drawn on an 8-unit em, so 8 DIP is the only size at
    /// which one design pixel is exactly one DIP - and the next rung up that ladder is 16, which
    /// wants a 250 DIP card. So this follows the house's own written practice instead (EmiFace:
    /// "use it at whole pixel sizes", and the offer chips already ship at 7): a whole DIP size,
    /// with <c>TextFormattingMode.Display</c> on the strip so the glyph advances are rounded to
    /// whole DEVICE pixels instead of being sub-pixel positioned. That, and Layout()'s whole-pixel
    /// card origins, is what keeps the cells on the grid at 125 % as well as at 100 %.</para>
    ///
    /// <para>THE FIT, which is what sets <see cref="CardW"/>. Every glyph advances exactly 1 em,
    /// so an n-character label is n x CardLabelFont DIPs wide and the strip has
    /// <c>CardW - 2*border - 2*CardSeam - 2*LabelPadX</c> to put it in. The longest WORD in the
    /// catalogue is "Subliminals" (11) and the longest LABEL is "The Arcademy" (12, which breaks
    /// at its space onto two lines - that is fine, a mid-WORD break is not); all 9 language files
    /// carry the same English labels today. At 10 DIP, 11 characters want 110 DIPs and a PINNED
    /// card - the 4 DIP frame, the worst case - offers 118. Rounded up to whole device pixels the
    /// same word measures 143 px at 125 %, 165 at 150 %, 198 at 175 % and 200 at 200 %: 114, 110,
    /// 113 and 100 DIPs. All four clear 118, so the longest word never breaks mid-word on any
    /// scale the app ships on.</para>
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
    /// too fast and not smooth at all", 2026-08-29). The first cut threw the cards out in 180 ms on
    /// a 40 ms stagger with a 0.45 BackEase: the whole ring was on screen in a third of a second,
    /// every card SNAPPED past its mark and came back, and on a layered per-pixel-alpha window that
    /// reads as a stutter rather than as a bounce.
    ///
    /// <para>Now: nearly twice the travel, a longer gap between neighbours so the fan reads as a
    /// deal rather than an explosion, a fade that outlives the first third of the move, and an
    /// overshoot small enough to be a settle. Total <c>5 x 62 + 340 = 650 ms</c> from the click to
    /// the last card at rest.</para>
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
    /// BackEase amplitude. 0.45 overshot by about a tenth of the travel, which at 180 ms was a
    /// snap. 0.3 is a settle you feel rather than watch.
    /// </summary>
    private const double PopBackAmplitude = 0.30;

    /// <summary>
    /// THE FOLD, the mirror of the fan (owner, third live run: "we might need the reverse animation
    /// when we close the circle"). The cards fly back into her, shrink to <see cref="PopFromScale"/>
    /// and fade, LAST-DEALT FIRST, so the ring un-deals itself in the order it was dealt.
    ///
    /// <para>Deliberately about half the open: <c>5 x 26 + 190 = 320 ms</c> against the fan's 650.
    /// An entrance is a presentation and can afford to be watched; an exit is an acknowledgement,
    /// and one that takes as long as the entrance reads as the app being slow to let go. The ring
    /// is functionally SHUT the instant the fold starts (hooks off, hit rects dropped,
    /// <c>RingClosed</c> already fired) - what is left is only the picture catching up.</para>
    ///
    /// <para>No BackEase on the way out, and the easings are EaseIn rather than EaseOut: an
    /// overshoot at the end of a fold is a bounce off her chest, and the cards should look pulled
    /// home rather than thrown.</para>
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
    /// The card frame, thickened on the owner's second live run ("the border of those images in the
    /// circle of emi should be bolder, thicker"). A 1 DIP hairline read as a CSS outline on a dark
    /// desktop; 3 DIP of pink with a 1 DIP dark seam inside it reads as a PIXEL frame, which is the
    /// house look. Pinned goes to 4 and solid, so a pin is legible across the room.
    /// </summary>
    private const double CardBorder = 3.0;

    /// <inheritdoc cref="CardBorder"/>
    private const double CardBorderPinned = 4.0;

    /// <summary>The dark seam drawn INSIDE the pink frame. Without it the frame is a line, not a frame.</summary>
    private const double CardSeam = 1.0;

    private static readonly Color FramePink = Color.FromRgb(0xFF, 0x69, 0xB4);
    private static readonly Color FrameRest = Color.FromArgb(0x88, 0xFF, 0x69, 0xB4);

    // ---------------------------------------------------------------- state

    private readonly EmiDeskWindow _owner;
    private readonly List<Border> _cards = new();
    private IReadOnlyList<EmiRingSlot> _slots = Array.Empty<EmiRingSlot>();

    private Services.GlobalMouseHook? _mouse;
    private Services.GlobalKeyboardHook? _keys;

    /// <summary>
    /// The rectangles a global click is allowed to land in without folding the ring: the six cards
    /// plus EMI's own silhouette, in PHYSICAL pixels. Read on the hook thread, which is why it is a
    /// whole-array swap of a frozen snapshot and never a live WPF walk.
    /// </summary>
    private volatile Rect[] _hotPx = Array.Empty<Rect>();

    private double _cx, _cy;          // the fan centre, in this window's DIP canvas coords
    private bool _open;
    private bool _closingForGood;

    /// <summary>
    /// True between the first frame of the fold and the <see cref="Hide"/> at the end of it. The
    /// ring is already SHUT here - this only says that the picture has not finished catching up,
    /// and it is what makes a second <see cref="CloseRing"/> during the fold a no-op instead of a
    /// second <c>RingClosed</c>.
    /// </summary>
    private bool _folding;

    /// <summary>
    /// Whether <see cref="RingClosed"/> has already been raised for the opening now ending. The
    /// event fires at the START of the fold (that is when the ring is actually shut), so a close
    /// that arrives again while the picture is still catching up - a Kill mid-fold, say - must
    /// hide the window without announcing a second dismissal into the ignore streak.
    /// </summary>
    private bool _closeAnnounced = true;

    /// <summary>The fold's finish. A timer rather than a Storyboard.Completed: there are five
    /// animations per card and the last one to finish is not the last one to be started.</summary>
    private System.Windows.Threading.DispatcherTimer? _foldEnd;

    /// <summary>The DPI scale <see cref="Layout"/> last solved against. See <see cref="OpenRing"/>.</summary>
    private double _laidOutAtScale;

    /// <summary>True while the fan is on screen.</summary>
    public bool IsOpen => _open;

    /// <summary>True when this opening ended in a card pick rather than a dismissal.</summary>
    public bool PickedThisOpening { get; private set; }

    /// <summary>A card was left-clicked. The ring has already folded; the caller opens the target.</summary>
    public event EventHandler<EmiRingSlot>? CardPicked;

    /// <summary>
    /// A pin was made or removed. The bool is the state it ended in (true = now pinned).
    ///
    /// <para>THE RING NO LONGER RAISES THIS. The card's right-click pin came off on the owner's
    /// third live run ("the Pin button is not usable right now, I propose we remove it from
    /// there") and pinning lives in her options menu now. The event, its signature and its
    /// subscriber in <c>EmiDeskWindow.Ring.cs</c> are all kept on purpose: they carry the
    /// <c>pinAdded</c> moment and the pin-nudge latch, and that bookkeeping is the same wherever
    /// the pin was made. The options menu reaches it through
    /// <c>EmiDeskWindow.NotePinMadeElsewhere</c>.</para>
    /// </summary>
    public event EventHandler<(EmiRingSlot Slot, bool Pinned)>? PinToggled;

    /// <summary>The ring folded. The bool says whether a card was picked on the way out.</summary>
    public event EventHandler<bool>? RingClosed;

    // ---------------------------------------------------------------- ctor

    /// <summary>Builds the ring for one widget. Created hidden; <see cref="OpenRing"/> shows it.</summary>
    public EmiRingWindow(EmiDeskWindow owner)
    {
        InitializeComponent();
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring window ex-style failed");
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    /// <summary>
    /// The NATIVE visibility word for the "ring open" log line. WPF's own properties lied for the
    /// whole of the invisible-first-open bug (see the note at the top of the XAML); only the HWND
    /// told the truth, so this is what a repeat would be caught by.
    /// </summary>
    private string NativeStyle()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return "wsvisible=nohwnd";
            int st = GetWindowLong(h, GWL_STYLE);
            return "wsvisible=" + ((st & WS_VISIBLE) != 0);
        }
        catch { return "wsvisible=?"; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    /// <summary>This window's own DPI scale. Never assume 1.0 on a multi-monitor desk.</summary>
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

    // ---------------------------------------------------------------- open / close

    /// <summary>Compose the ring, fan it out and start listening for the click that closes it.</summary>
    public void OpenRing()
    {
        if (_closingForGood) return;
        try
        {
            PickedThisOpening = false;
            _slots = EmiSuggester.Compose();
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
            // ring that had just been dealt. (BuildCards below stops the old cards' animations and
            // clears the canvas, so there is nothing else of the fold left to unwind.)
            CancelFold();

            BuildCards();
            PlaceWindow();

            // THE ONE-FRAME FLASH OF THE FINISHED RING (owner, third live run: "I see a frame of
            // the full circle, then the fan animation").
            //
            // It is not a missing guard on the cards - BuildCard already pre-poses every one of
            // them at PopFromScale/Opacity 0 and PlayPop re-asserts the base values - because the
            // frame the owner sees is not drawn from this visual tree at all. It is the PREVIOUS
            // opening's, still sitting in the window's layered surface: CloseRing used to clear
            // the canvas and Hide() in the same synchronous block, so the render thread never got
            // to compose the empty ring, and a hidden window is not rendered. The last thing
            // UpdateLayeredWindow was ever handed was the finished, fully-opaque fan - and Win32
            // shows exactly that the instant ShowWindow runs, until WPF pushes a new surface. She
            // rarely moves between two opens, so the stale fan lands on top of where the new one
            // is about to be dealt, which is what makes it read as "the ring, then the animation".
            //
            // Two changes, and the fold is the more important of them. (1) CloseRing now animates
            // the cards out and only hides once they are invisible, so the surface left behind is
            // an empty ring; the no-animation paths shrink the window to a pixel first (HideNow).
            // (2) Here: the HWND is created WITHOUT being presented, so Layout() runs on the real
            // per-monitor scale BEFORE the first present instead of after it. That kills the
            // second half of it - the old order showed the window at the whole work area and then
            // resized a VISIBLE layered window twice, once per PlaceWindow/Layout pair.
            try { new WindowInteropHelper(this).EnsureHandle(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring handle could not be pre-created"); }

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

            EmiSfx.RingOpen();
            Log.Information("[EmiDesk] ring open with {Count} cards, {Native}", _slots.Count, NativeStyle());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring failed to open");
            try { CloseRing(); } catch { /* nothing else to try */ }
        }
    }

    /// <summary>
    /// Fold the ring. Idempotent, and safe to call from a hook continuation - it is called
    /// re-entrantly from the global mouse hook, from Escape, from the drag watch, from the pick
    /// and from the tear-down seam, sometimes two of those inside one gesture.
    ///
    /// <para>The ring is SHUT when this returns, whether or not the fold is still playing: the
    /// hooks are gone, <see cref="IsOpen"/> is false, the hit rects are dropped and
    /// <see cref="RingClosed"/> has already fired. Everything after that point is a picture
    /// catching up, and a card in flight is not clickable.</para>
    /// </summary>
    public void CloseRing()
    {
        try
        {
            // Unconditionally first, and in this order. The hooks are a GLOBAL low-level pair and
            // must never outlive the gesture that armed them; the hit rects are read on the hook
            // thread and are what would let a card that is already flying home be clicked.
            RemoveHooks();

            // A fold already owns this closing. Not a second RingClosed, not a second fold.
            if (_folding) return;
            if (!_open && !IsVisible) return;

            _open = false;
            _hotPx = Array.Empty<Rect>();

            // The bookkeeping IS the close, so it happens now rather than when the animation ends:
            // a handler that counts dismissals and a caller that reads IsOpen straight after us
            // can then never disagree about when the ring shut. (It also keeps the pick honest -
            // the target's Open() is invoked by OnCardPicked the moment we return, and never waits
            // for the fold.) Symmetrical with "ring open" at Information now: logging one half of
            // every toggle at Debug meant the file sink showed opens with no closes (primer 9).
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
            if (_closingForGood || AppIsShuttingDown || !IsVisible || _cards.Count == 0)
            {
                HideNow();
                return;
            }

            EmiSfx.RingClose();
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

        foreach (var c in _cards) StopCardAnimations(c);
        _cards.Clear();
        Field.Children.Clear();
        _hotPx = Array.Empty<Rect>();
        Hide();

        // A hidden layered window keeps whatever was last handed to UpdateLayeredWindow, and Win32
        // puts that straight back on screen the moment ShowWindow runs (see the note in OpenRing).
        // Shrinking to a pixel means the worst a stale surface can ever be is one transparent dot;
        // OpenRing sizes the window from PlaceWindow/Layout before it is shown again, so nothing
        // downstream reads this rect.
        try { Width = 1; Height = 1; }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring could not be parked small"); }
    }

    /// <summary>
    /// Kill a fold in flight without hiding anything - a new opening is taking over the window.
    /// The cards themselves are dealt with by <c>BuildCards</c>, which stops their animations and
    /// clears the canvas; what must not survive is the finish timer.
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

    /// <summary>True once the app has started going down. An animation past this point is a hang.</summary>
    private static bool AppIsShuttingDown
    {
        get
        {
            try
            {
                var d = Application.Current?.Dispatcher;
                return d == null || d.HasShutdownStarted;
            }
            catch { return true; }
        }
    }

    /// <summary>Re-run the layout in place (she moved or resized). No pop, no re-compose.</summary>
    public void Relayout()
    {
        if (!_open) return;
        try
        {
            // No PlaceWindow: Layout owns the window rect now (it sizes it to the fan), and doing
            // both means two resizes of a layered window per follow-the-widget tick.
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
            _slots = EmiSuggester.Compose();
            BuildCards();
            Layout();
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

    // ---------------------------------------------------------------- placement

    private System.Drawing.Rectangle WorkArea()
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens == null || screens.Length == 0)
                return new System.Drawing.Rectangle(0, 0, 1920, 1080);

            var body = _owner.BodyScreenRect;
            var centre = new System.Drawing.Point(
                (int)Math.Round(body.X + body.Width / 2),
                (int)Math.Round(body.Y + body.Height / 2));
            return System.Windows.Forms.Screen.FromPoint(centre).WorkingArea;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring work-area probe failed");
            return new System.Drawing.Rectangle(0, 0, 1920, 1080);
        }
    }

    private void PlaceWindow()
    {
        var work = WorkArea();
        double s = DipScale;
        if (s <= 0) s = 1.0;

        Left = work.Left / s;
        Top = work.Top / s;
        Width = Math.Max(1, work.Width / s);
        Height = Math.Max(1, work.Height / s);
    }

    /// <summary>
    /// The fan, ported from <c>layoutRing</c> in the pitch demo: a full circle when there is room,
    /// and a half fan pushed AWAY from whichever screen edge she is parked against, so the ring
    /// never spills off the desktop. Every card is then clamped into the work area regardless, which
    /// is what keeps a corner park (two edges at once) honest.
    /// </summary>
    private void Layout()
    {
        var work = WorkArea();
        double s = DipScale;
        if (s <= 0) s = 1.0;
        _laidOutAtScale = s;            // OpenRing re-solves if the window learns a different one

        double workW = work.Width / s;
        double workH = work.Height / s;

        var anchor = _owner.RingAnchorScreenPoint;      // PHYSICAL pixels
        double ax = (anchor.X - work.Left) / s;         // work-area DIPs
        double ay = (anchor.Y - work.Top) / s;

        var bodyPx = _owner.BodyScreenRect;
        double bodyW = bodyPx.Width / s;
        double bodyH = bodyPx.Height / s;

        // THE SOLVER OWNS THE GEOMETRY (EmiRingLayout). What used to be here fanned on a fixed
        // radius and then clamped each card into the work area, which is why a bottom-right park
        // stacked three cards on top of each other under the taskbar: clamping is not a layout, it
        // is a way of hiding that the layout did not fit.
        var plan = EmiRingLayout.Solve(ax, ay, bodyW, bodyH, workW, workH,
                                       _cards.Count, CardW, CardH, BodyGap);

        // The window is sized to the FAN, not to the desktop. A full-work-area layered window
        // repaints a whole 1920 x 1080 per-pixel-alpha surface on every animation frame, and that
        // is most of what the owner felt as a stutter in the fan-out.
        double minX = ax, maxX = ax, minY = ay, maxY = ay;
        foreach (var p in plan.Cards)
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

        Left = work.Left / s + minX;
        Top = work.Top / s + minY;
        Width = Math.Max(1, maxX - minX);
        Height = Math.Max(1, maxY - minY);

        _cx = ax - minX;
        _cy = ay - minY;

        var hot = new List<Rect>(plan.Cards.Count + 1);

        for (int i = 0; i < _cards.Count && i < plan.Cards.Count; i++)
        {
            // Whole pixels: the pixel font goes to mush on a half-DIP offset.
            double x = Math.Round(plan.Cards[i].X - minX);
            double y = Math.Round(plan.Cards[i].Y - minY);

            Canvas.SetLeft(_cards[i], x);
            Canvas.SetTop(_cards[i], y);

            hot.Add(new Rect(work.Left + (minX + x) * s, work.Top + (minY + y) * s,
                             CardW * s, CardH * s));
        }

        try
        {
            hot.Add(bodyPx);   // a click on HER is her own toggle, not a dismissal
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] body rect probe failed"); }

        _hotPx = hot.ToArray();

        // Information, not Debug: the file sink's floor is Information, so a Debug line here is
        // invisible in the log the owner actually sends back. Everything needed to reproduce a
        // "the circle is offset" report by hand is on this one line - where the code believes she
        // is, what it orbited, and what the solver did with it.
        Log.Information("[EmiDesk] ring fan {Shape} r={R:F0} span={Span:F0} deg | anchor px ({AX:F0},{AY:F0}) " +
                        "| body px {BX:F0},{BY:F0} {BW:F0}x{BH:F0} | work {WX},{WY} {WW}x{WH} | scale {S:F2} " +
                        "| window {W:F0}x{H:F0} at {L:F0},{T:F0}",
                        plan.Shape, plan.Radius, plan.SpanDeg,
                        anchor.X, anchor.Y,
                        bodyPx.X, bodyPx.Y, bodyPx.Width, bodyPx.Height,
                        work.Left, work.Top, work.Width, work.Height, s,
                        Width, Height, Left, Top);
    }


    // ---------------------------------------------------------------- the cards

    private void BuildCards()
    {
        foreach (var c in _cards) StopCardAnimations(c);
        _cards.Clear();
        Field.Children.Clear();

        foreach (var slot in _slots)
        {
            try
            {
                var card = BuildCard(slot);
                _cards.Add(card);
                Field.Children.Add(card);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring card build failed for {Target}", slot.Target.Id);
            }
        }
    }

    private Border BuildCard(EmiRingSlot slot)
    {
        // NOT frozen: the hover animates this brush's Colour from the rest pink to the full one, so
        // the frame lights up with the card instead of only growing. A frozen brush cannot animate.
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
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Tag = slot,
            ToolTip = TipFor(slot),
        };

        // Pre-created, and pre-posed at the pop's starting values: BuildCards runs before the
        // window is even shown, and a card that is added at scale 1 / opacity 1 gets ONE frame at
        // its full size in the top-left corner before Layout() has placed it. That single frame is
        // the flash the owner saw as "not smooth".
        var sc0 = new ScaleTransform(PopFromScale, PopFromScale);
        var tr0 = new TranslateTransform(0, 0);
        card.RenderTransform = new TransformGroup { Children = { sc0, tr0 } };
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
        var art = LoadThumb(slot.Target);
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
            var tile = new SolidColorBrush(slot.Target.Hue) { Opacity = slot.Locked ? 0.28 : 0.62 };
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

        var label = new TextBlock
        {
            Text = SafeLabel(slot.Target),
            // EmiFace.PixelFont, not a family built from a NAME: Press Start 2P is shipped under
            // Resources/emi/fonts and never installed, so a name lookup silently landed on
            // Consolas. See CardLabelFont.
            FontFamily = EmiFace.PixelFont,
            FontSize = CardLabelFont,
            LineHeight = CardLabelLine,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 2 * CardLabelLine + 2,     // two lines: "The Arcademy" breaks at its space
            TextAlignment = TextAlignment.Center,
        };

        // GDI-compatible metrics: every glyph advance is rounded to a whole DEVICE pixel instead
        // of being sub-pixel positioned. It is the same law Layout() keeps for the card origins -
        // a pixel font goes to mush on a half-pixel offset - one level down, at the glyph.
        TextOptions.SetTextFormattingMode(label, TextFormattingMode.Display);
        strip.Child = label;
        grid.Children.Add(strip);

        // ---- the badges -------------------------------------------------------
        // The pin badge came off here on the owner's third live run ("the Pin button is not usable
        // right now, I propose we remove it from there"); pinning is in her options menu now. What
        // stays is how a pinned card LOOKS - the thicker solid-pink frame above - so a pin made in
        // the menu is still legible on the fan.
        if (slot.Locked)
        {
            grid.Children.Add(Badge("\U0001F512", HorizontalAlignment.Left, 0.65));
        }

        // ---- input ------------------------------------------------------------
        // Left click only. The right button is deliberately unhandled now: it bubbles to nothing
        // here, and the global hook reads a right-click outside the hot rects as a dismissal,
        // which is the behaviour a card without a gesture of its own should have.
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnCardPicked(slot); };
        card.MouseEnter += (_, _) => Hover(card, slot, true);
        card.MouseLeave += (_, _) => Hover(card, slot, false);

        return card;
    }

    private static FrameworkElement Badge(string glyph, HorizontalAlignment side, double opacity)
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
                // NOT the pixel font: this is an emoji glyph and Press Start 2P has none, so it
                // would fall through to a system face anyway. Named explicitly so it falls
                // somewhere chosen. 8 DIP was a padlock you had to look for; it grew with the card.
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI"),
                FontSize = BadgeFont,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
            },
        };
    }

    private void Hover(Border card, EmiRingSlot slot, bool on)
    {
        try
        {
            if (card.RenderTransform is not TransformGroup tg || tg.Children.Count < 1) return;
            if (tg.Children[0] is not ScaleTransform sc) return;

            double to = on ? HoverScale : 1.0;
            var dur = new Duration(TimeSpan.FromMilliseconds(110));
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, dur));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, dur));

            // The frame lights to FULL pink under the pointer. A pinned card is already full, so
            // its own animation is a no-op rather than a special case.
            if (card.BorderBrush is SolidColorBrush frame && !frame.IsFrozen)
            {
                var toColour = (on || slot.Pinned) ? FramePink : FrameRest;
                frame.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(toColour, dur));
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring hover failed");
        }
    }

    private static string SafeLabel(EmiTarget t)
    {
        try
        {
            var s = t.Label;
            return string.IsNullOrWhiteSpace(s) || s == t.LabelKey ? t.Id : s;
        }
        catch { return t.Id; }
    }

    /// <summary>
    /// The card's hint. Returned as a built <see cref="ToolTip"/> rather than a bare string on
    /// purpose: a string gets the NATIVE Windows tooltip - beige, Segoe UI, square - which sat on
    /// her pixel ring like a system error (QA 2026-08-29). Same palette and same face as the name
    /// strip, so it reads as part of the card.
    /// </summary>
    private static object? TipFor(EmiRingSlot slot)
    {
        try
        {
            string key = slot.Locked ? "emi_desk_ring_tip_locked"
                       : slot.Pinned ? "emi_desk_ring_tip_pinned"
                                     : "emi_desk_ring_tip_suggested";
            var s = Loc.Get(key);
            if (string.IsNullOrWhiteSpace(s) || s == key) return null;

            // Same typeface and the same step up the cards got, through the shipped-font resolver
            // rather than a family name (see CardLabelFont). 24 characters to a line at 10 DIP.
            var tip = new TextBlock
            {
                Text = s,
                FontFamily = EmiFace.PixelFont,
                FontSize = CardLabelFont,
                LineHeight = 16,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 240,
            };
            TextOptions.SetTextFormattingMode(tip, TextFormattingMode.Display);

            return new ToolTip
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HasDropShadow = false,
                Content = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0E, 0x0E, 0x1C)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x69, 0xB4)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7, 5, 7, 5),
                    Child = tip,
                },
            };
        }
        catch { return null; }
    }

    private static ImageSource? LoadThumb(EmiTarget t)
    {
        if (string.IsNullOrWhiteSpace(t.ThumbPath)) return null;
        try
        {
            // Through the mod resolver so a .ccpmod's own card art wins, exactly like the dashboard.
            return Services.ModResourceResolver.ResolveImageDecoded(t.ThumbPath, 192);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring art missing for {Target} ({Path})", t.Id, t.ThumbPath);
            return null;
        }
    }

    // ---------------------------------------------------------------- animation

    private void PlayPop()
    {
        try
        {
            // Two easings, not one. The MOVE keeps the (much gentler) overshoot, because that is
            // where the life is; the SCALE is a plain cubic, because a card that overshoots its
            // size as well as its position reads as two separate wobbles fighting.
            var move = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = PopBackAmplitude };
            var grow = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fadeEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var dur = new Duration(TimeSpan.FromMilliseconds(PopMs));
            var fade = new Duration(TimeSpan.FromMilliseconds(FadeMs));

            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (card.RenderTransform is not TransformGroup tg || tg.Children.Count < 2) continue;
                if (tg.Children[0] is not ScaleTransform sc) continue;
                if (tg.Children[1] is not TranslateTransform tr) continue;

                double dx = _cx - (Canvas.GetLeft(card) + CardW / 2.0);
                double dy = _cy - (Canvas.GetTop(card) + CardH / 2.0);
                var begin = TimeSpan.FromMilliseconds(i * PopStaggerMs);

                // Base values first: during a delayed animation's BeginTime the property still shows
                // its base value, so a card that starts at its final spot would flash there.
                tr.X = dx; tr.Y = dy;
                sc.ScaleX = PopFromScale; sc.ScaleY = PopFromScale;
                card.Opacity = 0;

                tr.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(dx, 0, dur) { BeginTime = begin, EasingFunction = move });
                tr.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(dy, 0, dur) { BeginTime = begin, EasingFunction = move });
                sc.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(PopFromScale, 1.0, dur) { BeginTime = begin, EasingFunction = grow });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(PopFromScale, 1.0, dur) { BeginTime = begin, EasingFunction = grow });
                card.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, fade) { BeginTime = begin, EasingFunction = fadeEase });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring pop failed");
        }
    }

    /// <summary>
    /// The fold: the pop run backwards, LAST-DEALT FIRST, and at about half its length. Started
    /// only from <see cref="CloseRing"/>, which has already taken the hooks down, dropped the hit
    /// rects and raised <see cref="RingClosed"/> - so nothing here is load-bearing and every early
    /// return can simply hide.
    /// </summary>
    private void PlayFold()
    {
        try
        {
            _folding = true;

            var move = new CubicEase { EasingMode = EasingMode.EaseIn };
            var shrink = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fadeEase = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            var dur = new Duration(TimeSpan.FromMilliseconds(FoldMs));
            var fade = new Duration(TimeSpan.FromMilliseconds(FoldFadeMs));

            int n = _cards.Count;
            for (int i = 0; i < n; i++)
            {
                var card = _cards[i];

                // Belt as well as braces on "a card in flight is not clickable": _hotPx is already
                // empty, which is what the global hook reads, and this is what WPF's own hit test
                // reads. The two roads into OnCardPicked are now both shut.
                card.IsHitTestVisible = false;

                if (card.RenderTransform is not TransformGroup tg || tg.Children.Count < 2) continue;
                if (tg.Children[0] is not ScaleTransform sc) continue;
                if (tg.Children[1] is not TranslateTransform tr) continue;

                // Where home is, from wherever the card is sitting NOW. Computed off the canvas
                // position rather than off the pop's own dx/dy so that a fold arriving mid-pop
                // (open, then Escape half a second later) still flies to her and not past her.
                double dx = _cx - (Canvas.GetLeft(card) + CardW / 2.0);
                double dy = _cy - (Canvas.GetTop(card) + CardH / 2.0);

                // Reverse stagger: the card dealt last is the first one taken back.
                var begin = TimeSpan.FromMilliseconds((n - 1 - i) * FoldStaggerMs);

                // The base values matter here for the same reason they do in PlayPop - during a
                // delayed animation's BeginTime the property shows its base - except that here the
                // base is where the card already IS, so it is read rather than written. A card
                // still travelling on the pop's clock has an animated value; BeginAnimation with a
                // From makes that irrelevant, which is why every animation below carries one.
                tr.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(tr.X, dx, dur) { BeginTime = begin, EasingFunction = move });
                tr.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(tr.Y, dy, dur) { BeginTime = begin, EasingFunction = move });
                sc.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(sc.ScaleX, PopFromScale, dur) { BeginTime = begin, EasingFunction = shrink });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(sc.ScaleY, PopFromScale, dur) { BeginTime = begin, EasingFunction = shrink });
                card.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(card.Opacity, 0, fade) { BeginTime = begin, EasingFunction = fadeEase });
            }

            double total = (n <= 1 ? 0 : (n - 1) * FoldStaggerMs) + FoldMs + FoldTailMs;
            _foldEnd = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal)
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

    private static void StopCardAnimations(Border card)
    {
        try
        {
            card.BeginAnimation(OpacityProperty, null);
            if (card.RenderTransform is TransformGroup tg)
            {
                if (tg.Children.Count > 0 && tg.Children[0] is ScaleTransform sc)
                {
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                }
                if (tg.Children.Count > 1 && tg.Children[1] is TranslateTransform tr)
                {
                    tr.BeginAnimation(TranslateTransform.XProperty, null);
                    tr.BeginAnimation(TranslateTransform.YProperty, null);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring animation stop failed");
        }
    }

    // ---------------------------------------------------------------- input

    private void OnCardPicked(EmiRingSlot slot)
    {
        try
        {
            PickedThisOpening = true;
            CloseRing();
            CardPicked?.Invoke(this, slot);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring pick failed for {Target}", slot.Target.Id);
        }
    }

    // The card's own pin gesture lived here. It is gone (owner, third live run) and pinning is in
    // her options menu; EmiSuggester.TogglePin, EmiState.Pins and the PinToggled event above are
    // all untouched, because the menu writes through exactly those. The ONE pin store rule is
    // unchanged - this window simply stopped being one of its front ends.

    // ---------------------------------------------------------------- the hooks

    private void InstallHooks()
    {
        try
        {
            if (_mouse == null)
            {
                _mouse = new Services.GlobalMouseHook { LeftDown = OnGlobalDown, RightDown = OnGlobalDown };
                _mouse.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring mouse hook failed to install, click-away will not close it");
        }

        try
        {
            if (_keys == null)
            {
                _keys = new Services.GlobalKeyboardHook();
                _keys.KeyPressed += OnGlobalKey;
                _keys.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring key hook failed to install, Escape will not close it");
        }
    }

    private void RemoveHooks()
    {
        try
        {
            if (_mouse != null)
            {
                _mouse.LeftDown = null;
                _mouse.RightDown = null;
                _mouse.Stop();
                _mouse.Dispose();
                _mouse = null;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring mouse hook removal failed"); }

        try
        {
            if (_keys != null)
            {
                _keys.KeyPressed -= OnGlobalKey;
                _keys.Stop();
                _keys.Dispose();
                _keys = null;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring key hook removal failed"); }
    }

    /// <summary>
    /// Runs on the HOOK thread. It must be cheap, must touch nothing but the frozen rect snapshot,
    /// and must always return false: swallowing the click would make closing the ring cost the user
    /// the thing they were clicking on.
    /// </summary>
    private bool OnGlobalDown(Point ptPx)
    {
        try
        {
            var hot = _hotPx;
            for (int i = 0; i < hot.Length; i++)
            {
                if (hot[i].Contains(ptPx)) return false;
            }
            Post(CloseRing);
        }
        catch { /* a hook callback never throws */ }
        return false;
    }

    private void OnGlobalKey(Key k)
    {
        try
        {
            if (k != Key.Escape) return;
            Post(CloseRing);
        }
        catch { /* a hook callback never throws */ }
    }

    private static void Post(Action a)
    {
        try
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            d.BeginInvoke(new Action(() =>
            {
                try { a(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring posted action threw"); }
            }));
        }
        catch { /* shutting down */ }
    }
}
