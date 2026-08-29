using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP: see the note in EmiDeskWindow.Fx.cs. Everything under Windows/ is FLAT
// ConditioningControlPanel; a ConditioningControlPanel.Windows namespace shadows the WinRT
// `Windows` root and breaks ScreenOcrService. Keep it flat.
namespace ConditioningControlPanel;

/// <summary>
/// THE VOICE: the speech bubble, the chip strip, and the whole life of one offer.
///
/// The talk rule is locked (EmiChains, EMI-DESIGN-LOCK): EMI never mouths words. The face stays
/// <c>0_0</c> while the bubble types <c>.</c> <c>..</c> <c>...</c>, then the line LANDS on the
/// reaction face and holds. Every word she says comes through here, whether it arrived as a chain
/// frame (<see cref="OnBubbleTextCore"/>) or as a drawn line (<see cref="SpeakLine"/>), so there is
/// exactly one bubble in the app and exactly one place its look is defined.
///
/// THE OFFER LAW (BRIEF 7): a question WAITS. There is no give-up timer, clicking elsewhere does
/// not cancel it, and a line that arrives while it is up parks instead of talking over it. It ends
/// on a chip, on Esc, on the hover x, or when a full-screen feature takes the screen. Anything but
/// a chip is an IGNORE: she pulls a <c>-_-</c>, thinks about it, and after three of those she stops
/// asking for the rest of the launch.
/// </summary>
public partial class EmiDeskWindow
{
    // ---------------------------------------------------------------- look

    // emi.css .bubble, with the desk palette instead of the campus cream: she is a CRT in a dark
    // room here, not a sticker on a beige page.
    private static readonly Brush BubbleFill = Frozen(Color.FromRgb(0x18, 0x18, 0x32));
    private static readonly Brush BubbleInk = Frozen(Color.FromRgb(0xFF, 0x69, 0xB4));
    private static readonly Brush ChipHoverFill = Frozen(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4));

    /// <summary>emi.css: the bubble's left edge sits at 58 % of her width, its bottom near her crown.</summary>
    private const double BubbleLeftFrac = 0.58;
    private const double BubbleBottomFrac = 0.96;

    /// <summary>emi.css font-size: 8. Held exactly at her default width and scaled with her from there.</summary>
    private const double BubbleFontAtDefaultWidth = 8.0;
    private const double BubbleFontRefWidth = 220.0;

    private const int AskDot1Ms = 420;        // the locked . / .. / ... cadence, same as MakeSay
    private const int AskDot2Ms = 420;
    private const int AskDot3Ms = 520;
    private const int IgnoreFaceMs = 1400;    // MOMENTS: askIgnored is a 1400 ms hold
    private const int IgnoreThinkMs = 700;

    /// <summary>
    /// Moments that take the whole screen. An offer cannot survive them: she asked, the answer
    /// stopped being possible, and leaving a dead question hanging over a video is the one way a
    /// waiting question turns into a nag.
    /// </summary>
    private static readonly HashSet<string> AskKillMoments = new(StringComparer.Ordinal)
    {
        "videoRunning", "lockdownArmed", "lockdownCountdown", "intakeOpened", "intakeRunning",
        "panicPressed"
    };

    // ---------------------------------------------------------------- state

    private Border? _bubble;
    private TextBlock? _bubbleText;
    private Polygon? _bubbleTail;
    private StackPanel? _chips;

    private AskDraw? _ask;
    private LineDraw? _parked;          // ONE slot. A second line while an ask waits is dropped.
    private DispatcherTimer? _askTimer;
    private DispatcherTimer? _holdTimer;
    private int _askStage;
    private bool _bubbleHooked;

    /// <summary>True while an offer is on screen and still waiting for an answer.</summary>
    public bool AskLive => _ask != null;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ---------------------------------------------------------------- the bubble itself

    /// <summary>
    /// The chain seam. Every frame of every chain that carries a bubble instruction lands here, so
    /// the say cadence, the ask cadence and anything a later chain adds all paint the same bubble.
    /// Null clears it.
    /// </summary>
    partial void OnBubbleTextCore(string? text)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) { HideBubble(); return; }
            ShowBubble(text!);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] bubble paint failed");
        }
    }

    private void EnsureBubble()
    {
        if (_bubble != null) return;

        _bubbleText = new TextBlock
        {
            Foreground = BubbleInk,
            FontFamily = EmiFace.PixelFont,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 1,                      // set for real in LayoutBubble
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            IsHitTestVisible = false
        };

        _bubble = new Border
        {
            Background = BubbleFill,
            BorderBrush = BubbleInk,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 6, 8, 6),
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
            Child = _bubbleText,
            Visibility = Visibility.Collapsed
        };

        // The tail: a 10 DIP wedge off the bubble's lower corner, pointing back at her crown. Drawn
        // as its own node rather than a rotated square so the fill and the 1 px edge stay crisp at
        // any DPI, and so flipping the bubble to her left is a point swap, not a transform.
        _bubbleTail = new Polygon
        {
            Fill = BubbleFill,
            Stroke = BubbleInk,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        _chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed
        };

        BubbleHost.Children.Add(_bubbleTail);
        BubbleHost.Children.Add(_bubble);
        BubbleHost.Children.Add(_chips);

        if (!_bubbleHooked)
        {
            _bubbleHooked = true;
            // Esc ends a waiting question, but only while she actually has the keyboard: this
            // window is ShowActivated=False and Focusable=False, so it only ever holds focus after
            // the user clicked a chip. No global hook, ever.
            PreviewKeyDown += OnBubbleKeyDown;
            Resized += (_, __) => LayoutBubble();
            try
            {
                var svc = App.EmiDesk;
                if (svc != null) svc.MomentFired += OnMomentForAsk;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ask moment hook failed"); }
        }
    }

    private void ShowBubble(string text)
    {
        EnsureBubble();
        if (_bubble == null || _bubbleText == null) return;

        _bubbleText.Text = text;
        _bubble.Visibility = Visibility.Visible;
        if (_bubbleTail != null) _bubbleTail.Visibility = Visibility.Visible;
        LayoutBubble();
    }

    private void HideBubble()
    {
        if (_bubble != null) _bubble.Visibility = Visibility.Collapsed;
        if (_bubbleTail != null) _bubbleTail.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Park the bubble over her crown, and flip it to her left when the screen runs out on the
    /// right (widget.js does the same with <c>.bubble-left</c>). Called on every text change and
    /// on every resize, because both change the box.
    /// </summary>
    private void LayoutBubble()
    {
        try
        {
            if (_bubble == null || _bubbleText == null || _bubble.Visibility != Visibility.Visible) return;

            double bw = BodyWidth;
            double bh = bw * BodyAspect;

            double fs = Math.Max(BubbleFontAtDefaultWidth,
                Math.Round(BubbleFontAtDefaultWidth * bw / BubbleFontRefWidth));
            _bubbleText.FontSize = fs;
            _bubbleText.LineHeight = Math.Round(fs * 1.4);
            _bubble.MaxWidth = Math.Max(150, Math.Min(280, bw * 0.95));

            _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _bubble.DesiredSize;

            // Body-local origin: BodyRoot is centred in a window OverlayPad DIPs bigger on each side.
            double bodyX = OverlayPad, bodyY = OverlayPad;

            double left = bodyX + bw * BubbleLeftFrac;
            double bottom = bodyY + bh - bh * BubbleBottomFrac;   // css bottom: 96%
            double top = bottom - size.Height;

            // Flip when her bubble would leave the monitor she is on. Measured in physical pixels
            // against that monitor's work area, because Left/Top here are DIPs and the screens are
            // not (THE COORDINATE TRAP).
            bool flip = false;
            try
            {
                double s = DipScale;
                var body = BodyScreenRect;
                var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(
                    (int)body.X, (int)body.Y, Math.Max(1, (int)body.Width), Math.Max(1, (int)body.Height)));
                double rightPx = (Left + left + size.Width) * s;
                flip = rightPx > screen.WorkingArea.Right;
            }
            catch { /* one monitor, or none enumerable: keep her on the right */ }

            if (flip) left = bodyX + bw * (1.0 - BubbleLeftFrac) - size.Width;

            // Never let her talk off the top of her own window either: the pad is all the room the
            // bubble has, and a clipped first line reads as a bug, not as a style.
            if (top < 2) top = 2;

            Canvas.SetLeft(_bubble, Math.Round(left));
            Canvas.SetTop(_bubble, Math.Round(top));

            LayoutTail(left, top + size.Height, size.Width, flip);
            LayoutChips(left, top + size.Height, flip);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] bubble layout failed");
        }
    }

    private void LayoutTail(double left, double bubbleBottom, double bubbleWidth, bool flip)
    {
        if (_bubbleTail == null) return;
        const double w = 10, h = 10;

        // Overlap the panel edge by a pixel so the shared border does not double-draw.
        double x = flip ? left + bubbleWidth - w : left;
        double y = bubbleBottom - 1;

        _bubbleTail.Points = flip
            ? new PointCollection { new Point(0, 0), new Point(w, 0), new Point(w, h) }
            : new PointCollection { new Point(w, 0), new Point(0, 0), new Point(0, h) };

        Canvas.SetLeft(_bubbleTail, Math.Round(x));
        Canvas.SetTop(_bubbleTail, Math.Round(y));
    }

    // ---------------------------------------------------------------- lines

    /// <summary>
    /// Say a drawn line. The engine already decided she may speak; this only decides HOW.
    ///
    /// A line that arrives while a question is waiting PARKS (one slot, newest wins) and is released
    /// when the question resolves. She does not talk over her own offer.
    /// </summary>
    public void SpeakLine(LineDraw? line)
    {
        if (line == null) return;
        try
        {
            if (AskLive)
            {
                _parked = line;
                Log.Debug("[EmiDesk] {Line} parked behind the open ask", line.Id);
                return;
            }

            CancelHold();
            CloseChannel(declined: true);

            // The line is on screen from the first dot, so it is spent from the first dot: the
            // engine must not be able to re-deal it because a later frame was cancelled.
            EmiLineEngine.Instance.Ack(line.Id);
            App.EmiDesk?.NoteEmiSpoke();

            if (string.IsNullOrWhiteSpace(line.Text))
            {
                // A wordless row: a chain, or just a face. Nothing to type.
                if (!string.IsNullOrEmpty(line.Chain)) PlayChain(line.Chain!);
                else DrawFace(line.Face);
                return;
            }

            Say(line.Text, string.IsNullOrEmpty(line.Face) ? "^_^" : line.Face);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] SpeakLine({Id}) failed", line.Id);
        }
    }

    /// <summary>
    /// A HOLD row: a face, held, with no bubble. This is how she reacts to the things she is not
    /// allowed to comment on (the avatar has the voice, panic was pressed, an offer was ignored):
    /// she is visibly present and visibly quiet.
    /// </summary>
    public void HoldFace(LineDraw? line)
    {
        if (line == null) return;
        try
        {
            CancelHold();
            CloseChannel(declined: true);
            CancelChain();
            StopIdleBeats();
            EmiLineEngine.Instance.Ack(line.Id);

            DrawFace(string.IsNullOrEmpty(line.Face) ? "-_-" : line.Face);

            int ms = line.HoldMs > 0 ? line.HoldMs : IgnoreFaceMs;
            _holdTimer = NewTimer(ms, () =>
            {
                CancelHold();
                DrawFace(EmiChains.RestFace);
                RestartIdleBeats();
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] HoldFace({Id}) failed", line.Id);
        }
    }

    private void CancelHold()
    {
        if (_holdTimer == null) return;
        try { _holdTimer.Stop(); } catch { /* already dead */ }
        _holdTimer = null;
    }

    // ---------------------------------------------------------------- the offer

    /// <summary>
    /// Put a question up and WAIT. No timer, no give-up: the two chips, Esc, the hover x and a
    /// full-screen feature are the only four ways out (BRIEF 7).
    /// </summary>
    public void ShowAsk(AskDraw? ask)
    {
        if (ask == null) return;
        try
        {
            EnsureBubble();
            CancelHold();
            CloseChannel(declined: true);
            CancelChain();
            StopIdleBeats();
            EndAskTimer();

            _ask = ask;
            EmiLineEngine.Instance.Ack(ask.Id);
            App.EmiDesk?.NoteEmiSpoke();

            // The same . / .. / ... cadence a said line gets, driven here instead of by a chain so
            // the last frame can wait forever instead of expiring.
            _askStage = 0;
            DrawFace(EmiChains.RestFace);
            ShowBubble(".");
            _askTimer = NewTimer(AskDot1Ms, AskCadenceStep);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ShowAsk({Id}) failed", ask.Id);
            _ask = null;
        }
    }

    private void AskCadenceStep()
    {
        var ask = _ask;
        if (ask == null) return;
        _askStage++;
        switch (_askStage)
        {
            case 1:
                ShowBubble("..");
                _askTimer = NewTimer(AskDot2Ms, AskCadenceStep);
                break;
            case 2:
                ShowBubble("...");
                _askTimer = NewTimer(AskDot3Ms, AskCadenceStep);
                break;
            default:
                DrawFace(string.IsNullOrEmpty(ask.Face) ? "^_^" : ask.Face);
                ShowBubble(ask.Question);
                BuildChips(ask);
                Log.Information("[EmiDesk] offer {Id} is up, waiting", ask.Id);
                break;
        }
    }

    private void BuildChips(AskDraw ask)
    {
        if (_chips == null) return;
        _chips.Children.Clear();

        IReadOnlyList<string> labels = ask.Chips != null && ask.Chips.Count >= 2
            ? ask.Chips
            : (IReadOnlyList<string>)new[] { "yes", "no" };

        for (int i = 0; i < 2; i++)
        {
            int idx = i;
            var b = MakeChip(labels[i]);
            b.Click += (_, e) => { e.Handled = true; AnswerAsk(idx); };
            _chips.Children.Add(b);
        }

        _chips.Visibility = Visibility.Visible;

        // The bubble layer is not hit-testable (a hit-testable overlay would eat her drag), so it
        // is opened for exactly as long as there are chips to click. An empty Canvas with no
        // Background does not hit-test itself, and the bubble and tail stay opted out, so the only
        // clickable pixels in the whole layer are the two buttons.
        BubbleHost.IsHitTestVisible = true;
        LayoutBubble();

        try { _chips.Children.OfType<Button>().FirstOrDefault()?.Focus(); }
        catch { /* focus is a nicety; Esc still works once a chip is clicked */ }
    }

    private Button MakeChip(string? label)
    {
        double fs = Math.Max(7, Math.Round(7.0 * BodyWidth / BubbleFontRefWidth));

        var text = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(label) ? "ok" : label!.Trim(),
            FontFamily = EmiFace.PixelFont,
            FontSize = fs,
            Foreground = BubbleInk,
            Margin = new Thickness(7, 4, 7, 4)
        };

        var shell = new Border
        {
            Background = BubbleFill,
            BorderBrush = BubbleInk,
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            Child = text
        };

        var b = new Button
        {
            Content = shell,
            Margin = new Thickness(0, 0, 5, 0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Template = BareButtonTemplate()
        };

        b.MouseEnter += (_, __) => shell.Background = ChipHoverFill;
        b.MouseLeave += (_, __) => shell.Background = BubbleFill;
        return b;
    }

    /// <summary>A button that draws nothing of its own: the chip's whole look is its content.</summary>
    private static ControlTemplate BareButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        t.VisualTree = presenter;
        t.Seal();
        return t;
    }

    private void LayoutChips(double bubbleLeft, double bubbleBottom, bool flip)
    {
        if (_chips == null || _chips.Visibility != Visibility.Visible) return;
        _chips.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = _chips.DesiredSize.Width;
        double x = flip ? bubbleLeft : bubbleLeft;
        Canvas.SetLeft(_chips, Math.Round(x));
        Canvas.SetTop(_chips, Math.Round(bubbleBottom + 12));
    }

    private void HideChips()
    {
        if (_chips != null)
        {
            _chips.Children.Clear();
            _chips.Visibility = Visibility.Collapsed;
        }
        // Shut the layer again the moment the last chip is gone: while it is open, this canvas sits
        // over her whole window, and a stray hit-testable node up here would swallow a drag.
        BubbleHost.IsHitTestVisible = false;
    }

    /// <summary>A chip was clicked. The only answer that counts as an answer.</summary>
    private void AnswerAsk(int index)
    {
        var ask = _ask;
        if (ask == null) return;
        try
        {
            bool yes = index == 0;
            var reply = yes ? ask.Yes : ask.No;
            string? effect = yes ? ask.Effect : ask.EffectNo;

            Log.Information("[EmiDesk] offer {Id} answered {Answer} -> {Effect}", ask.Id,
                yes ? "yes" : "no", string.IsNullOrEmpty(effect) ? "none" : effect);

            _ask = null;
            EndAskTimer();
            HideChips();
            HideBubble();

            EmiLineEngine.Instance.NoteAskAnswered();
            App.EmiDesk?.Fire("askAnswered", new { answer = yes ? "yes" : "no" });

            // The reply first, so she is already talking when the effect lands, then the effect.
            // fromAsk: the effect's own moment must not speak a SECOND line on top of the reply.
            if (reply != null) SpeakLine(reply);
            if (!string.IsNullOrEmpty(effect)) EmiOffers.Run(effect, fromAsk: true);

            ReleaseParked();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] answering offer {Id} failed", ask.Id);
            _ask = null;
            EndAskTimer();
            HideChips();
        }
    }

    /// <summary>
    /// The question went away without an answer. Not a punishment and not a sulk: a <c>-_-</c>, a
    /// beat of thinking, and then she drops it. Three of these and she stops asking this launch.
    /// </summary>
    public void CancelAsk(string why) => EndAskUnanswered(why, visual: true);

    /// <summary>
    /// The bookkeeping half, with the reaction optional. The reaction is skipped on a tear-down:
    /// she is already fading out and a face that lands after the power-off looks like a ghost.
    /// </summary>
    private void EndAskUnanswered(string why, bool visual)
    {
        var ask = _ask;
        if (ask == null) return;
        try
        {
            Log.Information("[EmiDesk] offer {Id} ignored ({Why})", ask.Id, why);
            _ask = null;
            EndAskTimer();
            HideChips();
            HideBubble();

            EmiLineEngine.Instance.NoteAskIgnored();
            App.EmiDesk?.Fire("askIgnored", new { });
            if (!visual) return;

            CancelChain();
            DrawFace("-_-");
            _askTimer = NewTimer(IgnoreFaceMs, () =>
            {
                ShowBubble("...");
                _askTimer = NewTimer(IgnoreThinkMs, () =>
                {
                    EndAskTimer();
                    HideBubble();
                    DrawFace(EmiChains.RestFace);
                    RestartIdleBeats();
                    ReleaseParked();
                });
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] cancelling offer {Id} failed", ask.Id);
            _ask = null;
            EndAskTimer();
            HideChips();
        }
    }

    /// <summary>Let the one line that arrived behind the question have its turn, now that it can.</summary>
    private void ReleaseParked()
    {
        var line = _parked;
        _parked = null;
        if (line == null) return;
        // A short beat so it does not step on the reply's own first frame.
        _holdTimer = NewTimer(900, () => { CancelHold(); SpeakLine(line); });
    }

    private void OnBubbleKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key != Key.Escape || !AskLive) return;
            e.Handled = true;
            CancelAsk("escape");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] Esc on the bubble failed");
        }
    }

    private void OnMomentForAsk(object? sender, EmiMoment m)
    {
        try
        {
            if (!AskLive || m == null) return;
            if (!AskKillMoments.Contains(m.Id)) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess()) { disp.BeginInvoke(new Action(() => CancelAsk(m.Id))); return; }
            CancelAsk(m.Id);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ask kill-moment handler failed");
        }
    }

    private void EndAskTimer()
    {
        if (_askTimer == null) return;
        try { _askTimer.Stop(); } catch { /* already dead */ }
        _askTimer = null;
    }

    /// <summary>
    /// A one-shot dispatcher timer whose tick is already wrapped. Every delayed step in this file
    /// goes through it, so there is one place the shutdown checks live.
    /// </summary>
    private DispatcherTimer NewTimer(int ms, Action step)
    {
        var t = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms))
        };
        void Tick(object? s, EventArgs e)
        {
            try
            {
                t.Stop();
                t.Tick -= Tick;
                if (Application.Current?.Dispatcher == null) return;
                if (Application.Current.Dispatcher.HasShutdownStarted) return;
                step();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] bubble timer step failed");
            }
        }
        t.Tick += Tick;
        t.Start();
        return t;
    }

    /// <summary>Take the bubble, the chips and any waiting question down. Called from the tear-down.</summary>
    private void TearDownBubble()
    {
        try
        {
            EndAskUnanswered("teardown", visual: false);
            _ask = null;
            _parked = null;
            EndAskTimer();
            CancelHold();
            HideChips();
            HideBubble();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] bubble tear-down failed");
        }
    }
}
