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
/// <para>Why a second window at all: <c>EmiDeskWindow</c> carries 120 DIPs of transparent pad, which
/// is nowhere near enough for a fan around a 420 DIP body. This window is sized to the WORK AREA of
/// the monitor she is standing on, so every card can be clamped onto that screen without resizing
/// anything, and it is placed with the same physical-pixels-over-own-DPI arithmetic the widget uses.
/// <c>BodyScreenRect</c> and <c>RingAnchorScreenPoint</c> are PHYSICAL PIXELS: converting them with
/// an assumed scale of 1.0 is the coordinate trap that ate the gaze work.</para>
///
/// <para>Closing on a click outside is done with a low-level mouse hook rather than a full-screen
/// catcher window. The hook costs nothing while the ring is shut (it is installed on open and
/// removed on close), it cannot steal focus, and, crucially, it NEVER swallows the click: the ring
/// folds and the click still lands on whatever the user was actually aiming at.</para>
/// </summary>
public partial class EmiRingWindow : Window
{
    // ---------------------------------------------------------------- constants

    /// <summary>Card size in DIPs, from the pitch demo.</summary>
    public const double CardW = 76.0;

    /// <inheritdoc cref="CardW"/>
    public const double CardH = 58.0;

    private const double PopStaggerMs = 40.0;
    private const double PopMs = 180.0;
    private const double FadeMs = 120.0;
    private const double HoverScale = 1.08;
    private const double EdgeMargin = 6.0;

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

    /// <summary>True while the fan is on screen.</summary>
    public bool IsOpen => _open;

    /// <summary>True when this opening ended in a card pick rather than a dismissal.</summary>
    public bool PickedThisOpening { get; private set; }

    /// <summary>A card was left-clicked. The ring has already folded; the caller opens the target.</summary>
    public event EventHandler<EmiRingSlot>? CardPicked;

    /// <summary>A card was right-clicked. The bool is the state it ended in (true = now pinned).</summary>
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
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

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
                return;
            }

            BuildCards();
            PlaceWindow();

            if (!IsVisible)
            {
                Show();
                // The window only has a DPI of its own once it has an HWND, so the first layout is
                // always done twice: once to get it on screen, once with its real scale.
                PlaceWindow();
            }

            Layout();
            PlayPop();
            InstallHooks();
            _open = true;

            Log.Information("[EmiDesk] ring open with {Count} cards", _slots.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring failed to open");
            try { CloseRing(); } catch { /* nothing else to try */ }
        }
    }

    /// <summary>Fold the ring. Idempotent, and safe to call from a hook continuation.</summary>
    public void CloseRing()
    {
        try
        {
            RemoveHooks();
            if (!_open && !IsVisible) return;
            _open = false;

            foreach (var c in _cards) StopCardAnimations(c);
            _cards.Clear();
            Field.Children.Clear();
            _hotPx = Array.Empty<Rect>();
            Hide();

            Log.Debug("[EmiDesk] ring closed (picked={Picked})", PickedThisOpening);
            try { RingClosed?.Invoke(this, PickedThisOpening); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] RingClosed handler threw"); }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring close failed");
        }
    }

    /// <summary>Re-run the layout in place (she moved or resized). No pop, no re-compose.</summary>
    public void Relayout()
    {
        if (!_open) return;
        try
        {
            PlaceWindow();
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

        var anchor = _owner.RingAnchorScreenPoint;      // PHYSICAL pixels
        _cx = (anchor.X - work.Left) / s;
        _cy = (anchor.Y - work.Top) / s;

        double w = Width, h = Height;
        double r = Math.Max(96.0, _owner.BodyWidth * 0.85 + 60.0);

        double start = -90, span = 360;
        bool nearR = _cx > w - r - 70, nearL = _cx < r + 70;
        bool nearT = _cy < r + 40, nearB = _cy > h - r - 40;

        if (nearR && !nearL) { start = 90; span = 180; }
        else if (nearL && !nearR) { start = -90; span = 180; }

        if (nearB && !nearT && span >= 360) { start = 180; span = 180; }
        else if (nearT && !nearB && span >= 360) { start = 0; span = 180; }

        int n = Math.Max(1, _cards.Count);
        var hot = new List<Rect>(n + 1);

        for (int i = 0; i < _cards.Count; i++)
        {
            double deg = span >= 360
                ? start + i * (360.0 / n)
                : start + (i + 0.5) * (span / n);
            double a = deg * Math.PI / 180.0;

            double x = _cx + Math.Cos(a) * r - CardW / 2.0;
            double y = _cy + Math.Sin(a) * r - CardH / 2.0;

            x = Math.Max(EdgeMargin, Math.Min(w - CardW - EdgeMargin, x));
            y = Math.Max(EdgeMargin, Math.Min(h - CardH - EdgeMargin, y));

            // Whole pixels: the pixel font goes to mush on a half-DIP offset.
            x = Math.Round(x);
            y = Math.Round(y);

            Canvas.SetLeft(_cards[i], x);
            Canvas.SetTop(_cards[i], y);

            hot.Add(new Rect(work.Left + x * s, work.Top + y * s, CardW * s, CardH * s));
        }

        try
        {
            var body = _owner.BodyScreenRect;
            hot.Add(body);   // a click on HER is her own toggle, not a dismissal
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] body rect probe failed"); }

        _hotPx = hot.ToArray();
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
        var pinkBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        pinkBrush.Freeze();

        var card = new Border
        {
            Width = CardW,
            Height = CardH,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0E, 0x0E, 0x1C)),
            BorderThickness = new Thickness(slot.Pinned ? 2 : 1),
            BorderBrush = slot.Pinned ? pinkBrush : new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x69, 0xB4)),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Tag = slot,
            ToolTip = TipFor(slot),
        };
        card.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1, 1), new TranslateTransform(0, 0) }
        };

        var grid = new Grid
        {
            // A Border never clips its child (the 2026-08-13 verdict), so the rounded corners are
            // done with an explicit clip geometry instead of hoping CornerRadius does it.
            Clip = new RectangleGeometry(new Rect(0, 0, CardW, CardH), 6, 6),
        };
        card.Child = grid;

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
            Padding = new Thickness(4, 3, 4, 3),
            IsHitTestVisible = false,
        };
        strip.Child = new TextBlock
        {
            Text = SafeLabel(slot.Target),
            FontFamily = new FontFamily("Press Start 2P, Consolas, Global Monospace"),
            FontSize = 7,
            LineHeight = 10,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 21,
            TextAlignment = TextAlignment.Center,
        };
        grid.Children.Add(strip);

        // ---- the badges -------------------------------------------------------
        if (slot.Locked)
        {
            grid.Children.Add(Badge("\U0001F512", HorizontalAlignment.Left, 0.65));
        }

        var pin = Badge("\U0001F4CC", HorizontalAlignment.Right, slot.Pinned ? 0.95 : 0.0);
        pin.Name = "PinGlyph";
        grid.Children.Add(pin);

        // ---- input ------------------------------------------------------------
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnCardPicked(slot); };
        card.MouseRightButtonUp += (_, e) => { e.Handled = true; OnCardPinToggled(slot); };
        card.MouseEnter += (_, _) => Hover(card, pin, slot, true);
        card.MouseLeave += (_, _) => Hover(card, pin, slot, false);

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
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Segoe UI"),
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
            },
        };
    }

    private void Hover(Border card, FrameworkElement pin, EmiRingSlot slot, bool on)
    {
        try
        {
            if (card.RenderTransform is not TransformGroup tg || tg.Children.Count < 1) return;
            if (tg.Children[0] is not ScaleTransform sc) return;

            double to = on ? HoverScale : 1.0;
            var dur = new Duration(TimeSpan.FromMilliseconds(110));
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, dur));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, dur));

            double pinTo = slot.Pinned ? 0.95 : (on ? 0.55 : 0.0);
            pin.BeginAnimation(OpacityProperty, new DoubleAnimation(pinTo, dur));
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

    private static string TipFor(EmiRingSlot slot)
    {
        try
        {
            string key = slot.Locked ? "emi_desk_ring_tip_locked"
                       : slot.Pinned ? "emi_desk_ring_tip_pinned"
                                     : "emi_desk_ring_tip_suggested";
            var s = Loc.Get(key);
            return string.IsNullOrWhiteSpace(s) ? string.Empty : s;
        }
        catch { return string.Empty; }
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
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
            var dur = new Duration(TimeSpan.FromMilliseconds(PopMs));

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
                sc.ScaleX = 0.4; sc.ScaleY = 0.4;
                card.Opacity = 0;

                tr.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(dx, 0, dur) { BeginTime = begin, EasingFunction = ease });
                tr.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(dy, 0, dur) { BeginTime = begin, EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.4, 1.0, dur) { BeginTime = begin, EasingFunction = ease });
                sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.4, 1.0, dur) { BeginTime = begin, EasingFunction = ease });
                card.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(FadeMs))) { BeginTime = begin });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring pop failed");
        }
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

    private void OnCardPinToggled(EmiRingSlot slot)
    {
        try
        {
            bool pinned = EmiSuggester.TogglePin(slot.Target.Id);
            Rebuild();
            PinToggled?.Invoke(this, (slot, pinned));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ring pin toggle failed for {Target}", slot.Target.Id);
        }
    }

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
