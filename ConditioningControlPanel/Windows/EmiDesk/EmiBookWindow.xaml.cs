using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP (same note as EmiDeskWindow, EmiRingWindow and EmiOptionsWindow): everything under
// Windows\ lives in the FLAT ConditioningControlPanel namespace. A ConditioningControlPanel.Windows.*
// namespace shadows the WinRT Windows root and breaks Services\ScreenOcrService.cs with a CS0234 that
// names a file you never touched. Do not "tidy" it.
namespace ConditioningControlPanel;

/// <summary>
/// HER BOOK: the flipbook that unfurls beside her.
///
/// <para><b>What it is.</b> One card per feature. The top of the card is a little 8-bit loop that
/// SHOWS the feature happening, and under it sit a title, one pink line, up to four nudges, the
/// catch on its own strip, and
/// at most one button. Roughly forty words. The owner's brief was "imagine you are explaining this
/// to people with ADHD", and the loop is the load-bearing half: somebody who reads nothing at all
/// should still come away knowing what the feature does.</para>
///
/// <para><b>The window recipe is not optional</b> and is copied from
/// <see cref="EmiOptionsWindow"/>: WindowStyle None, AllowsTransparency, ShowActivated false,
/// ShowInTaskbar false, Topmost, and WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE stamped in
/// <see cref="OnSourceInitialized"/>. She is a desktop ornament, not an application window.</para>
///
/// <para><b>WS_EX_NOACTIVATE means this window can never hold the keyboard.</b> Everything in here
/// is mouse-driven: no text, no tab stops, and no keyboard shortcut for the pager, because the key
/// events would never arrive.</para>
///
/// <para><b>Placement is PHYSICAL pixels over this window's own DPI</b>, both sides of the sum.
/// <c>BodyScreenRect</c> is physical; every WPF <c>Left</c>/<c>Top</c>/<c>Width</c> here is DIPs.
/// Mixing the two is the documented trap that ate the gaze work, and at 150 % it puts the book a
/// third of the way across the desk.</para>
///
/// <para><b>One clock, one buffer.</b> The window owns a single 30 fps timer and a single
/// <see cref="EmiPixelCanvas"/>; the current card's <see cref="EmiDemoPainter"/> repaints the whole
/// 96 x 72 buffer each tick. Only the visible card ever paints, and under reduced motion the clock
/// never starts at all - the painter is asked for one still frame and that is what sits there.</para>
///
/// <para><b>It does not close on a click outside</b>, unlike her options panel. The options panel is
/// a menu and a menu that outlives the click is a bug; the book is something you read WHILE you use
/// the app, and one that folded the moment you touched the tab it just told you to open would be
/// useless. It closes on its own x, or when she leaves.</para>
/// </summary>
public partial class EmiBookWindow : Window
{
    /// <summary>Air between her silhouette and the book's near edge, in DIPs.</summary>
    private const double BodyGap = 12.0;

    /// <summary>The book at its full drawn height, in DIPs. Matches the XAML.</summary>
    private const double FullHeight = 728.0;

    /// <summary>The floor the book will not shrink under, even on a very short desk.</summary>
    private const double MinPanelHeight = 430.0;

    /// <summary>Panel height at or above which the demo stage gets its 3x, 288 x 216 pixels.</summary>
    private const double BigStageFloor = 640.0;

    /// <summary>The demo buffer, in cells. 288 = 3 x 96 exactly, which is why the stage is 288 wide.</summary>
    private const int BufW = 96;

    /// <inheritdoc cref="BufW"/>
    private const int BufH = 72;

    private readonly EmiDeskWindow _owner;
    private readonly EmiPixelCanvas _canvas = new(BufW, BufH);
    private readonly List<Border> _dots = new();

    private DispatcherTimer? _clock;
    private readonly Stopwatch _since = new();
    private EmiDemoPainter? _painter;

    private int _index;
    private int _tab;
    private bool _closingForGood;
    private bool _folding;

    /// <summary>True while the panel sits to her LEFT, which flips the unfurl's origin.</summary>
    private bool _onHerLeft;

    /// <summary>
    /// Which side of her this panel took: <c>-1</c> her LEFT, <c>+1</c> her RIGHT. Read through
    /// <see cref="EmiBook.SideOfHer"/>, never off this window: the book announces where it is and
    /// does not know or care who is listening.
    /// </summary>
    internal int SideOfHer => _onHerLeft ? -1 : 1;

    // ---------------------------------------------------------------- ctor

    /// <summary>Builds the book for one widget. Created hidden; <see cref="OpenBook"/> shows it.</summary>
    public EmiBookWindow(EmiDeskWindow owner)
    {
        InitializeComponent();
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        SourceInitialized += OnSourceInitialized;

        ApplyFonts();
        WireControls();

        // The chrome on her body stays lit while the pointer is in here, so the ? chip that opened
        // this is still on screen after the round trip.
        MouseEnter += (_, _) => Hold(true);
        MouseLeave += (_, _) => Hold(false);

        _owner.Moved += OnOwnerMoved;
        _owner.Resized += OnOwnerResized;
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
            Log.Warning(ex, "[EmiDesk] book window ex-style failed");
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

    // ---------------------------------------------------------------- wiring

    /// <summary>
    /// FONTS ARE ASSIGNED HERE, never named in XAML. Press Start 2P ships as Content under
    /// <c>Resources\emi\fonts</c> and is loaded through a FontFamily base URI; naming it in markup
    /// resolves against installed system fonts only, finds nothing, and silently falls back to the
    /// default UI face - which is the exact trap EmiRingWindow documents.
    ///
    /// <para>Press Start 2P has one weight and an 8 x 8 cell, so every size here is a whole number
    /// of pixels. 16 for the title, 8 for the chrome. A fractional size hints the cell and the whole
    /// panel stops looking drawn.</para>
    /// </summary>
    private void ApplyFonts()
    {
        try
        {
            var pixel = EmiFace.PixelFont;
            var face = EmiFace.FaceFont;

            foreach (var b in new[] { Tab0, Tab1, Tab2 })
            {
                b.FontFamily = pixel;
                b.FontSize = 8;
            }

            BtnClose.FontFamily = pixel;
            BtnClose.FontSize = 8;
            BtnPrev.FontFamily = pixel;
            BtnPrev.FontSize = 10;
            BtnNext.FontFamily = pixel;
            BtnNext.FontSize = 10;
            BtnGo.FontFamily = pixel;
            BtnGo.FontSize = 8;

            StageLabel.FontFamily = pixel;
            StageLabel.FontSize = 6;

            CardTitle.FontFamily = pixel;
            CardTitle.FontSize = 16;

            CardGist.FontFamily = face;
            CardGist.FontSize = 13;

            CardCatch.FontFamily = face;
            CardCatch.FontSize = 11.5;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] book fonts failed");
        }
    }

    private void WireControls()
    {
        BtnClose.Click += (_, _) => EmiBook.Close();
        BtnPrev.Click += (_, _) => Step(-1);
        BtnNext.Click += (_, _) => Step(+1);
        BtnGo.Click += (_, _) => Go();

        Tab0.Click += (_, _) => PickTab(0);
        Tab1.Click += (_, _) => PickTab(1);
        Tab2.Click += (_, _) => PickTab(2);

        BtnClose.ToolTip = EmiBookCards.L("emi_book_close", "close the book");
        StageLabel.Text = EmiBookCards.L("emi_book_stage", "DEMO");
    }

    private void Hold(bool on)
    {
        try { _owner.HoldChromeForPanel(on); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book chrome hold failed"); }
    }

    // ---------------------------------------------------------------- open / close

    /// <summary>Unfurl the book beside her, at <paramref name="cardId"/> or at the first card.</summary>
    public void OpenBook(string? cardId)
    {
        if (_closingForGood) return;
        try
        {
            SelectCard(EmiBookCards.IndexOf(cardId) is var i and >= 0 ? i : 0, speak: false);

            PlaceWindow();
            if (!IsVisible)
            {
                Show();
                // The window has no DPI of its own until it has an HWND, so the first placement is
                // always done twice: once to get it on screen, once with its real scale.
                PlaceWindow();
            }

            // WS_VISIBLE is read off the HWND rather than off Window.IsVisible on purpose. Those
            // two disagreed for the whole of the invisible-book bug and only the native word was
            // telling the truth, so this line is what a repeat would be caught by. See the note on
            // Visibility at the top of the XAML.
            Log.Information("[EmiDesk] book shown at {X},{Y} {W}x{H} {N}",
                Left, Top, ActualWidth, ActualHeight, NativeStyle());

            Unfurl();
            StartClock();
            SpeakMargin();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] book failed to open");
            try { EmiBook.Close(); } catch { /* nothing else to try */ }
        }
    }

    /// <summary>Navigate a book that is already up. Used when a second Open arrives.</summary>
    public void GoTo(string? cardId)
    {
        try
        {
            int i = EmiBookCards.IndexOf(cardId);
            if (i < 0) return;
            SelectCard(i, speak: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book navigate failed");
        }
    }

    /// <summary>Fold the book and let it go: her dismissal, or app shutdown.</summary>
    public void Kill()
    {
        try
        {
            _closingForGood = true;
            StopClock();
            Hold(false);
            _owner.Moved -= OnOwnerMoved;
            _owner.Resized -= OnOwnerResized;

            if (!IsVisible)
            {
                Close();
                return;
            }
            Fold();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book kill failed");
            try { Close(); } catch { /* it is going away either way */ }
        }
    }

    private const int GWL_STYLE = -16;
    private const int WS_VISIBLE = 0x10000000;

    /// <summary>
    /// The window's NATIVE style word, for the one log line that reports the panel is up. WPF's own
    /// IsVisible and Visibility are not enough on their own: through the whole invisible-book bug
    /// both of them read Visible on a window the window manager had already hidden, and only this
    /// told the truth. See the note on Visibility at the top of the XAML.
    /// </summary>
    private string NativeStyle()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return "hwnd=none";
            int st = GetWindowLong(h, GWL_STYLE);
            return $"hwnd={h.ToInt64()} style=0x{st:X8} wsvisible={(st & WS_VISIBLE) != 0}";
        }
        catch { return "hwnd=?"; }
    }

    // ---------------------------------------------------------------- the unfurl

    /// <summary>
    /// Two moves with two origins, which is why there are two transforms.
    ///
    /// <para>A flat line shoots out FROM HER EDGE (ScaleX about the near edge), then it opens like a
    /// CRT coming on (ScaleY about the middle) with a single bright frame at the seam. One transform
    /// cannot do both: an origin that suits the shoot puts the CRT open's hinge on the panel's edge
    /// and the whole thing slides instead of opening.</para>
    ///
    /// <para>Under reduced motion the book is simply there. The panel is not a decoration that can
    /// be slowed down; it is the content, so the fallback is no animation rather than a longer one.</para>
    /// </summary>

    private void Unfurl()
    {
        try
        {
            SweepHost.RenderTransformOrigin = new Point(_onHerLeft ? 1 : 0, 0.5);

            if (!MotionFx.AllowTransitions)
            {
                SweepScale.ScaleX = 1;
                OpenScale.ScaleY = 1;
                Flash.Opacity = 0;
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            SweepScale.ScaleX = 0;
            OpenScale.ScaleY = 0.04;
            Flash.Opacity = 0;

            var sweep = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease };
            SweepScale.BeginAnimation(ScaleTransform.ScaleXProperty, sweep);

            var open = new DoubleAnimation(0.04, 1, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(160),
                EasingFunction = ease,
            };
            OpenScale.BeginAnimation(ScaleTransform.ScaleYProperty, open);

            var flash = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(160) };
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.72, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));
            Flash.BeginAnimation(OpacityProperty, flash);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book unfurl failed");
            try { SweepScale.ScaleX = 1; OpenScale.ScaleY = 1; } catch { /* nothing left to try */ }
        }
    }

    /// <summary>The unfurl backwards, and then the window is gone.</summary>
    private void Fold()
    {
        if (_folding) return;
        _folding = true;
        try
        {
            if (!MotionFx.AllowTransitions)
            {
                Close();
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            var open = new DoubleAnimation(1, 0.04, TimeSpan.FromMilliseconds(130)) { EasingFunction = ease };
            OpenScale.BeginAnimation(ScaleTransform.ScaleYProperty, open);

            var sweep = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
            {
                BeginTime = TimeSpan.FromMilliseconds(120),
                EasingFunction = ease,
            };
            // Closed fires on the LAST animation, not on a timer, so a slow frame cannot leave the
            // window on screen at zero width with nothing coming to close it.
            sweep.Completed += (_, _) => { try { Close(); } catch { /* already gone */ } };
            SweepScale.BeginAnimation(ScaleTransform.ScaleXProperty, sweep);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book fold failed");
            try { Close(); } catch { /* already gone */ }
        }
    }

    // ---------------------------------------------------------------- the clock

    /// <summary>
    /// One 30 fps timer for the whole book. It drives whichever painter the current card owns, and
    /// there is never more than one: a card that is not on screen is not costing anything.
    /// </summary>
    private void StartClock()
    {
        try
        {
            // Reduced motion: one still frame and no clock at all. The painter nominates the frame,
            // because the most legible moment of a loop is a property of that loop (the panic key
            // freezes on the EMPTY screen, not on the noise).
            if (!MotionFx.AllowAmbientLoops)
            {
                PaintStill();
                return;
            }

            if (_clock == null)
            {
                _clock = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(33),
                };
                _clock.Tick += OnTick;
            }
            _since.Restart();
            _clock.Start();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] book clock failed to start");
        }
    }

    private void StopClock()
    {
        try
        {
            _clock?.Stop();
            _since.Stop();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book clock stop failed"); }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var p = _painter;
        if (p == null) return;
        try
        {
            double t = _since.Elapsed.TotalMilliseconds % Math.Max(1, p.LoopMs);
            p.Draw(_canvas, t);
            _canvas.Commit();
        }
        catch (Exception ex)
        {
            // A painter that throws takes itself off the clock rather than throwing thirty times a
            // second into the log. The card keeps its words; only the loop goes dark.
            Log.Warning(ex, "[EmiDesk] book demo {Demo} threw, dropped", p.Id);
            _painter = null;
        }
    }

    private void PaintStill()
    {
        var p = _painter;
        if (p == null) return;
        try
        {
            p.Draw(_canvas, p.StillMs);
            _canvas.Commit();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book still frame failed"); }
    }

    /// <summary>
    /// Freeze the stage on one exact frame. FOR THE OFFSCREEN SHOT RIG ONLY
    /// (<c>Services/Dev/BookShooter.cs</c>): a review of a 30 fps loop needs a determinate frame,
    /// and a capture taken while the clock runs lands wherever the scheduler happened to be.
    /// Stops the clock, because a shot rig that leaves it running races its own next capture.
    /// </summary>
    internal void ShootFrame(string? cardId, double tMs)
    {
        try
        {
            StopClock();
            int i = EmiBookCards.IndexOf(cardId);
            if (i >= 0) SelectCard(i, speak: false);

            var p = _painter;
            if (p == null) return;
            p.Draw(_canvas, Math.Max(0, Math.Min(p.LoopMs - 1, tMs)));
            _canvas.Commit();
            if (Stage.Source == null) Stage.Source = _canvas.Source;
        }
        catch (Exception ex) { Log.Warning(ex, "[EmiDesk] book shot frame failed"); }
    }

    // ---------------------------------------------------------------- the cards

    /// <summary>Move to a card by index, redraw everything, and remember where we are.</summary>
    private void SelectCard(int index, bool speak)
    {
        var cards = EmiBookCards.All;
        if (cards.Count == 0) return;

        _index = Math.Max(0, Math.Min(cards.Count - 1, index));
        var card = cards[_index];
        _tab = card.Tab;

        _painter = EmiBookDemos.For(card.Id);
        // The clock is not restarted between cards: a shared elapsed time means a card you flip back
        // to is already mid-loop, which reads as "it was running all along" rather than as a rewind.
        if (_painter != null && Stage.Source == null) Stage.Source = _canvas.Source;
        if (_painter == null) Stage.Source = null;
        if (!_since.IsRunning) PaintStill();

        RenderCard(card);
        RenderTabs();
        RenderRail();
        RenderDots();

        EmiBook.NoteCard(card.Id);
        if (speak) SpeakMargin();
    }

    private void Step(int delta)
    {
        var cards = EmiBookCards.All;
        if (cards.Count == 0) return;
        int next = _index + delta;
        if (next < 0 || next >= cards.Count) return;
        SelectCard(next, speak: true);
    }

    private void PickTab(int tab)
    {
        int first = EmiBookCards.FirstOnTab(tab);
        if (first < 0) return;
        if (first == _index && _tab == tab) return;
        SelectCard(first, speak: true);
    }

    /// <summary>The bullets' body text.</summary>
    private static readonly Brush NudgePlain = Frozen(0xC9, 0xC3, 0xE2);

    /// <summary>
    /// The key words, in every row that has any.
    ///
    /// <para>Pink, and NOT a third hue. The panel already spends pink on the gist and gold on the
    /// catch, so an emphasis colour of its own would have made three accents fighting on a 292
    /// pixel column. Reusing the pink says the same thing the gist says - this is the part that
    /// matters - and ties the bullets to the line above them.</para>
    ///
    /// <para>The colour is also the load-bearing half of the emphasis. The runs are set bold as
    /// well, but the face is Noto Sans Mono shipped as a single-weight variable font, and WPF has
    /// no variable axis support: bold there is a synthesised embolden, which is real but slight.
    /// Anything that depended on weight alone would be close to invisible.</para>
    /// </summary>
    private static readonly Brush NudgeHot = Frozen(0xFF, 0x69, 0xB4);

    /// <summary>The gist line, and its key words a shade up from it.</summary>
    private static readonly Brush GistPlain = Frozen(0xFF, 0x69, 0xB4);

    /// <inheritdoc cref="GistPlain"/>
    private static readonly Brush GistHot = Frozen(0xFF, 0xD2, 0xEE);

    /// <summary>The catch strip, and its key words a shade up from it.</summary>
    private static readonly Brush CatchPlain = Frozen(0xE8, 0xC4, 0x6A);

    /// <inheritdoc cref="CatchPlain"/>
    private static readonly Brush CatchHot = Frozen(0xF9, 0xE2, 0xAC);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>How many bullets a card may show, whatever the deck hands over. See the owner's
    /// ceiling in <see cref="EmiBookCards"/>.</summary>
    private const int MaxNudges = 4;

    private void RenderCard(EmiBookCard card)
    {
        try
        {
            CardTitle.Text = card.Title;
            Emphasize(CardGist, card.Gist, GistPlain, GistHot);
            Emphasize(CardCatch, card.Catch, CatchPlain, CatchHot);

            Nudges.Children.Clear();
            int n = 0;
            foreach (var nudge in card.Nudges)
            {
                if (n++ >= MaxNudges) break;
                Nudges.Children.Add(NudgeRow(nudge));
            }

            RenderButton(card);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] book card render failed for {Card}", card.Id);
        }
    }

    /// <summary>
    /// Lay a line of card copy into a TextBlock as runs, with the <c>*asterisk*</c> key words drawn
    /// loud. Falls all the way back to the plain string if anything at all goes wrong: a card that
    /// lost its emphasis is a card, a card that lost its text is a blank panel.
    /// </summary>
    private static void Emphasize(TextBlock target, string? line, Brush plain, Brush hot)
    {
        try
        {
            target.Inlines.Clear();
            foreach (var run in EmiBookText.Parse(line))
            {
                target.Inlines.Add(new Run(run.Text)
                {
                    Foreground = run.Hot ? hot : plain,
                    FontWeight = run.Hot ? FontWeights.Bold : FontWeights.Normal,
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book emphasis failed, falling back to plain text");
            try { target.Inlines.Clear(); target.Text = EmiBookText.Strip(line); } catch { }
        }
    }

    /// <summary>
    /// One nudge: a hard little square and a wrapped line, with its key words bold and pink. The
    /// catch is NOT one of these any more - it has its own strip at the foot of the card, because
    /// it was spending one of the four bullets to say a thing that is not an action.
    /// </summary>
    private DockPanel NudgeRow(string text)
    {
        // Nine, not the six the two-nudge card used. Four bullets that each wrap to two lines is
        // eight lines of body text in a 278 pixel column, and at six the rows ran together into one
        // block you had to re-find your place in. The space came out of the slack above the catch
        // strip, so the card is no longer any longer for it.
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9), LastChildFill = true };

        var bullet = new Border
        {
            Width = 6,
            Height = 6,
            Margin = new Thickness(0, 5, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = NudgeHot,
        };
        DockPanel.SetDock(bullet, Dock.Left);
        row.Children.Add(bullet);

        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16,
            FontSize = 12.5,
            Foreground = NudgePlain,
        };
        try { tb.FontFamily = EmiFace.FaceFont; } catch { /* the default face still reads */ }
        Emphasize(tb, text, NudgePlain, NudgeHot);
        row.Children.Add(tb);

        return row;
    }

    /// <summary>
    /// The one button. A card with no detour has no button at all rather than a dead one, and a
    /// card whose detour cannot run right now keeps the button but ghosts it, so the shape of the
    /// card does not change under the reader between one launch and the next.
    /// </summary>
    private void RenderButton(EmiBookCard card)
    {
        if (card.Target == null && card.Tour == null)
        {
            BtnGo.Visibility = Visibility.Collapsed;
            return;
        }

        BtnGo.Visibility = Visibility.Visible;

        if (card.Target != null)
        {
            var t = EmiTargets.Find(card.Target);
            BtnGo.Content = EmiBookCards.L("emi_book_go", "TAKE ME THERE");
            BtnGo.IsEnabled = t != null && t.Available;
            return;
        }

        BtnGo.Content = EmiBookCards.L("emi_book_walk", "WALK ME THROUGH IT");
        BtnGo.IsEnabled = TourReady();
    }

    /// <summary>
    /// The same four conditions <c>EmiOffers.TourFeasible</c> checks, minus the "already done"
    /// latch. A tour you have already taken is one you are allowed to take again from the book:
    /// the latch exists to stop her ASKING twice, not to stop you asking.
    /// </summary>
    private static bool TourReady()
    {
        try
        {
            if (Application.Current?.MainWindow is not MainWindow) return false;
            if (SessionEngine.Active?.IsRunning == true) return false;
            if (App.Tutorial?.IsActive == true) return false;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book tour probe failed");
            return false;
        }
    }

    private void Go()
    {
        var cards = EmiBookCards.All;
        if (_index < 0 || _index >= cards.Count) return;
        var card = cards[_index];

        try
        {
            if (card.Target != null)
            {
                // Straight through EmiTargets.Open, which is the ring's own Pick: the usage counter
                // and the moments belong to it, and a door opened from the book is a door used.
                var t = EmiTargets.Find(card.Target);
                if (t == null) return;
                t.Open();
                App.EmiDesk?.Fire("effectFired", new { channel = "bookGo", target = card.Target });
                return;
            }

            if (card.Tour != null)
            {
                if (Application.Current?.MainWindow is not MainWindow main) return;
                if (!Enum.TryParse<TutorialType>(card.Tour, out var type))
                {
                    // A NAME, never an ordinal: the ledger persists names and an ordinal would move
                    // the day somebody inserted a value into the middle of the enum.
                    Log.Warning("[EmiDesk] book card {Card} names an unknown tour {Tour}", card.Id, card.Tour);
                    return;
                }

                // The tutorial overlay owns the screen from here, and a book sitting on top of its
                // coachmarks is exactly the thing the coachmarks are pointing at.
                EmiBook.Close();
                main.StartTutorial(type);
                App.EmiDesk?.Fire("effectFired", new { channel = "bookTour", tour = card.Tour });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] book detour failed for {Card}", card.Id);
        }
    }

    private void RenderTabs()
    {
        var tabs = new[] { Tab0, Tab1, Tab2 };
        for (int i = 0; i < tabs.Length; i++)
        {
            try
            {
                tabs[i].Content = EmiBookCards.TabName(i);
                // A tab with nothing behind it is drawn and dead, not hidden: the shape of the book
                // is honest from day one, the same way the codex greyed volumes IV to VI.
                tabs[i].IsEnabled = EmiBookCards.TabHasCards(i);
                tabs[i].Tag = i == _tab ? "on" : null;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book tab {Tab} render failed", i); }
        }
    }

    /// <summary>
    /// THE SIDE RAIL: one icon per card on this tab, and clicking one goes straight there.
    ///
    /// <para>Rebuilt on every card change rather than built once and re-tagged. It is at most eight
    /// buttons carrying cached image sources, the glyphs never animate, and a rail rebuilt from the
    /// deck each time cannot drift out of step with the deck.</para>
    ///
    /// <para>The dimming is applied to the Image, NOT to the chip: a glyph is transparent where it
    /// is not drawn, so fading the whole chip would fade the lit pink ground with it and the
    /// selected card would stop reading as selected.</para>
    /// </summary>
    private void RenderRail()
    {
        try
        {
            Rail.Children.Clear();

            var cards = EmiBookCards.All;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Tab != _tab) continue;

                int target = i;
                var card = cards[i];
                bool here = i == _index;

                var chip = new Button
                {
                    Style = (Style)FindResource("RailChip"),
                    Tag = here ? "on" : null,
                    ToolTip = card.Title,
                };

                var src = EmiBookGlyphs.For(card.Id);
                if (src != null)
                {
                    var img = new Image { Source = src, Width = 32, Height = 32, Opacity = here ? 1.0 : 0.5 };
                    // A 16 cell buffer blown up 2x. Anything but nearest neighbour turns it to soup,
                    // exactly as on the demo stage.
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                    chip.Content = img;
                }
                else
                {
                    // A card with no glyph still gets a chip. Skipping it would silently drop a page
                    // out of the only navigation that names its destinations.
                    chip.Content = new Border { Width = 10, Height = 10, Background = here ? NudgeHot : NudgePlain };
                }

                chip.Click += (_, _) => SelectCard(target, speak: true);
                Rail.Children.Add(chip);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book rail render failed");
        }
    }

    /// <summary>
    /// One dot per card ON THIS TAB, not per card in the book. The dots are a "where am I in this
    /// chapter" reading, and fourteen of them would be a progress bar for a book nobody asked to
    /// finish.
    /// </summary>
    private void RenderDots()
    {
        try
        {
            Dots.Children.Clear();
            _dots.Clear();

            var cards = EmiBookCards.All;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Tab != _tab) continue;

                int target = i;
                bool here = i == _index;
                var dot = new Border
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(4, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(here
                        ? Color.FromRgb(0xFF, 0x69, 0xB4)
                        : Color.FromRgb(0x46, 0x43, 0x6D)),
                };
                dot.MouseLeftButtonUp += (_, _) => SelectCard(target, speak: true);
                Dots.Children.Add(dot);
                _dots.Add(dot);
            }

            BtnPrev.IsEnabled = _index > 0;
            BtnNext.IsEnabled = _index < cards.Count - 1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book dots render failed");
        }
    }

    /// <summary>
    /// Her line in the margin. It is a comment ON the card, not a reading of it, which is the whole
    /// reason the book has a mascot leaning on it: the panel explains, she reacts.
    ///
    /// <para>It goes through <c>SpeakLine</c> like everything else she says, so it parks behind an
    /// open question instead of talking over her own offer. Priority 2, because a margin quip is not
    /// ceremony and has no business bypassing the global floor.</para>
    /// </summary>
    private void SpeakMargin()
    {
        try
        {
            var cards = EmiBookCards.All;
            if (_index < 0 || _index >= cards.Count) return;
            var card = cards[_index];
            if (string.IsNullOrWhiteSpace(card.MarginEn)) return;

            _owner.SpeakLine(new LineDraw(
                "book.margin." + card.Id, "book", card.MarginEn, card.MarginFace,
                null, 2, false, 0));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book margin line failed");
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
            Log.Debug(ex, "[EmiDesk] book work-area probe failed");
            return new System.Drawing.Rectangle(0, 0, 1920, 1080);
        }
    }

    /// <summary>
    /// Put the book beside her, on the side with room, vertically centred on her and clamped into
    /// the work area of the monitor she is standing on.
    ///
    /// <para>PHYSICAL PIXELS over THIS window's own DPI, both sides of the sum, exactly as
    /// <c>EmiOptionsWindow.PlaceWindow</c> and <c>EmiRingWindow.PlaceWindow</c> do it.</para>
    ///
    /// <para>The book prefers her RIGHT, unlike the options panel: the panel is opened by a gear on
    /// her left and wants the short pointer trip back, while the book is read rather than operated
    /// and the near side of a widget that usually lives at the right of the desk is the left one.
    /// It flips when that side has no room.</para>
    ///
    /// <para>On a short desk the stage drops from 3x to 2x rather than the book scrolling. Nine
    /// screen pixels per cell down to four keeps the integer multiple, and a demo with a half-pixel
    /// seam in it is worse than a smaller demo.</para>
    /// </summary>
    private void PlaceWindow()
    {
        try
        {
            var work = WorkArea();
            double s = DipScale;
            if (s <= 0) s = 1.0;

            double workL = work.Left / s;
            double workT = work.Top / s;
            double workW = work.Width / s;
            double workH = work.Height / s;

            double h = Math.Min(FullHeight, Math.Max(MinPanelHeight, workH - 24));
            if (Math.Abs(Height - h) > 0.5) Height = h;
            // The stage drops to 2x on a short desk, and the test is ROOM rather than "did we get
            // the full height": a 1366 x 768 laptop lands at about 704, which is a hair under
            // FullHeight and has ample room for the big stage. Measuring against FullHeight sent
            // those machines to the small stage for a 8 pixel shortfall.
            ApplyStageScale(h >= BigStageFloor ? 3 : 2);

            UpdateLayout();

            var bodyPx = _owner.BodyScreenRect;
            double bodyL = bodyPx.Left / s;
            double bodyT = bodyPx.Top / s;
            double bodyR = bodyPx.Right / s;
            double bodyH = bodyPx.Height / s;

            double w = ActualWidth > 1 ? ActualWidth : Width;

            bool wasOnHerLeft = _onHerLeft;
            double left = bodyR + BodyGap;
            _onHerLeft = false;
            if (left + w > workL + workW)
            {
                double alt = bodyL - BodyGap - w;
                if (alt >= workL) { left = alt; _onHerLeft = true; }
            }

            // Centred on her, not aligned to her head: the book is nearly three of her tall and a
            // top-aligned one hangs off the bottom of every desk she stands near the middle of.
            double top = bodyT + bodyH / 2 - h / 2;

            Left = Math.Max(workL, Math.Min(workL + workW - w, left));
            Top = Math.Max(workT, Math.Min(workT + workH - h, top));

            SweepHost.RenderTransformOrigin = new Point(_onHerLeft ? 1 : 0, 0.5);

            // Dragging her across the middle of the desk flips the panel under the pointer, and her
            // bubble dodges to whichever side the panel is NOT on. Say so, last, once the side is
            // settled - the announcement is a broadcast and the book does not know who reads it.
            if (wasOnHerLeft != _onHerLeft) EmiBook.NoteSideChanged();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] book placement failed");
        }
    }

    /// <summary>Integer stage scale only. 3 gives 288 x 216, 2 gives 192 x 144.</summary>
    private void ApplyStageScale(int mult)
    {
        try
        {
            int m = Math.Max(2, Math.Min(3, mult));
            double w = BufW * m;
            double hh = BufH * m;
            if (Math.Abs(Stage.Width - w) < 0.5) return;
            Stage.Width = w;
            Stage.Height = hh;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book stage scale failed"); }
    }

    private void OnOwnerMoved(object? sender, EventArgs e)
    {
        if (_closingForGood || !IsVisible) return;
        try { PlaceWindow(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book follow-move failed"); }
    }

    private void OnOwnerResized(object? sender, double width)
    {
        if (_closingForGood || !IsVisible) return;
        try { PlaceWindow(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book follow-resize failed"); }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            StopClock();
            _owner.Moved -= OnOwnerMoved;
            _owner.Resized -= OnOwnerResized;
            Hold(false);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book teardown failed"); }
        base.OnClosed(e);
    }
}
