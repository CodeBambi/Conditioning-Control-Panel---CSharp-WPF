// PORTED from ConditioningControlPanel/Windows/Friends/FriendNotices.cs (7.1.5): the corner notifications.
// One small non-activating window in the bottom-right of the work area of the screen the anchor is on,
// holding up to three notices newest on top, "+N more" under them. The stack rules live in Core
// (FriendNoticeStack); this draws them, runs the lifetime bars, pauses on hover and closes itself when
// the last notice leaves. IN: a slide from the right + fade (a nudge for a repeat); OUT: a 140 ms fade.
// Avalonia rules: no Effect (the shadow is a BoxShadow), no Animation on a Transform (TransformTween),
// opacity through a 0..1 transition, the 50 ms tick lives only while a notice is up.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Friends;

/// <summary>How one corner notice reads and what its parts do. Replaced on a fold, so the words and
/// the action always belong to the newest arrival.</summary>
internal sealed class NoticeLook
{
    public string Line = "";
    /// <summary>A poke's word, drawn after the line in pink or mint. Null for the rest.</summary>
    public string? Word;
    public bool Pink;
    public string? AvatarUrl;
    public string? ActionLabel;
    public Action<object?>? Act;
    /// <summary>A click on the body: the panel and the drawer on that friend.</summary>
    public Action<object?>? Open;
    /// <summary>The notice left without its action (ran out, closed, clicked). Knocks fold into
    /// the Inbox here, so an invite still answers after its toast is gone.</summary>
    public Action<object?>? Left;
    /// <summary>The notice went unseen: it ran out, was pushed off a full stack, or a hold
    /// folded it. Not called for a close or a click. Pokes file an Inbox row here.</summary>
    public Action<object?>? Missed;
}

internal sealed class FriendNotices : Window
{
    private const double CardWidth = 300;
    private const int TickMs = 50;

    private static FriendNotices? _current;

    private readonly FriendNoticeStack _stack = new();
    private readonly Dictionary<FriendNotice, NoticeLook> _looks = new();
    private readonly Dictionary<FriendNotice, (Border Card, ScaleTransform Bar, TextBlock Ago)> _drawn = new();
    private readonly StackPanel _column = new() { Margin = new Thickness(0, LandingChrome.ShadowRoom, 0, 0) };
    private readonly DispatcherTimer _tick;
    private Window? _anchor;
    private FriendNotice? _fresh;
    private bool _freshFolded;
    private bool _closed;
    private DateTime _last = DateTime.UtcNow;
    private int _agoTick;

    private FriendNotices()
    {
        LandingChrome.Dress(this);
        Content = _column;
        PointerEntered += (_, _) => _stack.Paused = true;
        PointerExited += (_, _) => { _stack.Paused = false; _last = DateTime.UtcNow; };
        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _tick.Tick += (_, _) => OnTick();
        // Closed by anyone else (a shutdown sweep): never leave a dead window as the one Show reuses.
        Closed += (_, _) =>
        {
            _closed = true;
            _tick.Stop();
            if (ReferenceEquals(_current, this)) _current = null;
        };
    }

    /// <summary>Puts a notice up (or folds it into a live one). Never throws.</summary>
    public static void Show(Window? anchor, FriendNotice notice, NoticeLook look)
    {
        try
        {
            var w = _current;
            if (w == null || w._closed)
            {
                w = new FriendNotices();
                _current = w;
            }
            if (anchor != null) w._anchor = anchor;
            var (up, folded) = w._stack.Add(notice);
            foreach (var n in w._stack.TakeEvicted()) w.Leave(n, acted: false, missed: true);
            foreach (var n in new List<FriendNotice>(w._looks.Keys))
                if (!Contains(w._stack.All, n)) w._looks.Remove(n);
            w._looks[up] = look;
            w._fresh = up;
            w._freshFolded = folded;
            w.Rebuild();
            w.Place();   // before Show: the passive style goes on first, as the achievement toast does
            if (!w.IsVisible) { w.Show(); w._last = DateTime.UtcNow; w._tick.Start(); }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] notice: {E}", ex.Message); }
    }

    /// <summary>Everything down at once. The exit path passes no fold; a hold or a panic folds, so a
    /// knock that was up still waits in the Inbox.</summary>
    public static void CloseAll(bool fold = false)
    {
        var w = _current;
        _current = null;
        if (w == null) return;
        try
        {
            w._tick.Stop();
            if (fold) foreach (var n in new List<FriendNotice>(w._stack.All)) w.Leave(n, acted: false, missed: true);
            w._stack.Clear();
            w.Close();
        }
        catch { /* already gone */ }
    }

    public static bool AnyUp => _current?._stack.All.Count > 0;

    /// <summary>Test seam: the live window, or null.</summary>
    internal static FriendNotices? Current => _current;
    internal IReadOnlyList<FriendNotice> Up => _stack.All;
    /// <summary>Test seam: one tick of <paramref name="elapsedMs"/> without the wall clock.</summary>
    internal void Advance(double elapsedMs) => AfterTick(_stack.Tick(elapsedMs));
    /// <summary>Test seam: the notice's parts, by what a click on them does.</summary>
    internal void PressAction(FriendNotice n) { if (_looks.TryGetValue(n, out var l)) { Dismiss(n, acted: true); l.Act?.Invoke(n.Payload); } }
    internal void PressClose(FriendNotice n) => Dismiss(n, acted: false);

    private static bool Contains(IReadOnlyList<FriendNotice> list, FriendNotice n)
    {
        foreach (var x in list) if (ReferenceEquals(x, n)) return true;
        return false;
    }

    // ---------------------------------------------------------------- timers

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _last).TotalMilliseconds;
        _last = now;
        var gone = _stack.Tick(elapsed);
        foreach (var n in _stack.Visible)
            if (_drawn.TryGetValue(n, out var d)) d.Bar.ScaleX = n.Fraction;
        if (++_agoTick >= 1000 / TickMs)
        {
            _agoTick = 0;
            foreach (var n in _stack.Visible)
                if (_drawn.TryGetValue(n, out var d)) d.Ago.Text = AgoText(n);
        }
        AfterTick(gone);
    }

    private void AfterTick(List<FriendNotice> gone)
    {
        if (gone.Count == 0) return;
        foreach (var n in gone) Leave(n, acted: false, missed: true);
        AfterRemoval();
    }

    private void Leave(FriendNotice n, bool acted, bool missed = false)
    {
        if (!_looks.TryGetValue(n, out var look)) return;
        _looks.Remove(n);
        if (acted) return;
        try { look.Left?.Invoke(n.Payload); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] notice left: {E}", ex.Message); }
        if (!missed) return;
        try { look.Missed?.Invoke(n.Payload); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] notice missed: {E}", ex.Message); }
    }

    /// <summary>Takes one notice down after a press, with a quick fade where motion allows.</summary>
    private void Dismiss(FriendNotice n, bool acted)
    {
        if (!_stack.Remove(n)) return;
        Leave(n, acted);
        if (FriendsDrawer.MotionLevelNow() != MotionLevel.Off && _drawn.TryGetValue(n, out var d))
        {
            d.Card.IsHitTestVisible = false;
            d.Card.Opacity = 0;   // the card's 140 ms opacity transition is the OUT
            DispatcherTimer.RunOnce(AfterRemoval, TimeSpan.FromMilliseconds(140));
            return;
        }
        AfterRemoval();
    }

    private void AfterRemoval()
    {
        if (_closed) return;
        if (_stack.All.Count == 0)
        {
            if (ReferenceEquals(_current, this)) _current = null;
            try { _tick.Stop(); Close(); } catch { /* already gone */ }
            return;
        }
        _fresh = null;
        Rebuild();
        Place();
    }

    // ---------------------------------------------------------------- placement

    /// <summary>Bottom-right of the anchor's screen work area (FriendNoticeRules.Place), in device pixels.</summary>
    private void Place()
    {
        try
        {
            var want = LandingChrome.Want(_column);
            var at = LandingChrome.ScreenOf(this, _anchor);
            if (at is not { } s)
            {
                Width = want.Width;
                Height = want.Height;
                return;
            }
            var (left, top) = FriendNoticeRules.Place(s.Work.X / s.Scale, s.Work.Y / s.Scale, s.Work.Width / s.Scale, s.Work.Height / s.Scale,
                want.Width, want.Height);
            LandingChrome.PlacePassive(this, new PixelPoint((int)Math.Round(left * s.Scale), (int)Math.Round(top * s.Scale)), want, s.Scale);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] notice place: {E}", ex.Message); }
    }

    // ---------------------------------------------------------------- drawing

    private void Rebuild()
    {
        _column.Children.Clear();
        _drawn.Clear();
        foreach (var n in _stack.Visible)
        {
            if (!_looks.TryGetValue(n, out var look)) continue;
            var card = BuildCard(n, look, out var bar, out var ago);
            _drawn[n] = (card, bar, ago);
            _column.Children.Add(card);
            if (ReferenceEquals(n, _fresh)) Enter(card, _freshFolded);
        }
        if (_stack.Hidden > 0)
        {
            _column.Children.Add(new TextBlock
            {
                Text = string.Format(FriendsLanding.Str("friends_notice_more", "+{0} more"), _stack.Hidden),
                FontFamily = FriendsLook.Display,
                FontSize = 11.5,
                Foreground = FriendsLook.MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0),
                Tag = "friends-notice-more",
            });
        }
    }

    private static string AgoText(FriendNotice n)
    {
        var (key, arg) = FriendNoticeRules.Ago(n.At, DateTimeOffset.UtcNow);
        return key switch
        {
            "friends_notice_seconds" => string.Format(FriendsLanding.Str(key, "{0}s"), arg),
            "friends_notice_minutes" => string.Format(FriendsLanding.Str(key, "{0}m"), arg),
            "friends_notice_hours" => string.Format(FriendsLanding.Str(key, "{0}h"), arg),
            _ => FriendsLanding.Str(key, "now"),
        };
    }

    private Border BuildCard(FriendNotice n, NoticeLook look, out ScaleTransform bar, out TextBlock ago)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(LandingChrome.Avatar(n.Name, look.AvatarUrl, 34));

        var words = new StackPanel { Margin = new Thickness(10, 0, 4, 0) };
        var head = new TextBlock
        {
            Text = n.Name, FontFamily = FriendsLook.Display, FontWeight = FontWeight.SemiBold, FontSize = 13.5,
            Foreground = FriendsLook.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ago = new TextBlock
        {
            Text = AgoText(n), FontSize = 10.5, Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Foreground = FriendsLook.MutedBrush,
        };
        var headRow = new DockPanel();
        DockPanel.SetDock(ago, Dock.Right);
        headRow.Children.Add(ago);
        headRow.Children.Add(head);
        words.Children.Add(headRow);

        var line = new TextBlock
        {
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0),
            Foreground = FriendsLook.MutedBrush, Inlines = new InlineCollection(), Tag = "friends-notice-line",
        };
        line.Inlines!.Add(new Run(look.Line));
        if (!string.IsNullOrEmpty(look.Word))
            line.Inlines.Add(new Run(" " + look.Word) { FontWeight = FontWeight.SemiBold, Foreground = look.Pink ? FriendsLook.PinkBrush : FriendsLook.MintBrush });
        if (n.Count > 1)
            line.Inlines.Add(new Run(FriendNoticeRules.CountSuffix(n.Count)) { FontWeight = FontWeight.SemiBold, Foreground = FriendsLook.GoldBrush });
        words.Children.Add(line);

        if (look.ActionLabel != null && look.Act != null)
        {
            var act = FriendsLook.Pill(look.ActionLabel, FriendsLook.MintBrush, FriendsLook.MintInkBrush, "friends-notice-act", hoverBg: FriendsLook.MintBrush);
            act.HorizontalAlignment = HorizontalAlignment.Left;
            act.Margin = new Thickness(0, 7, 0, 0);
            act.Padding = new Thickness(12, 4, 12, 4);
            act.Focusable = false;
            act.Click += (_, e) =>
            {
                e.Handled = true;
                Dismiss(n, acted: true);
                try { look.Act(n.Payload); }
                catch (Exception ex) { Serilog.Log.Debug("[Friends] notice action: {E}", ex.Message); }
            };
            words.Children.Add(act);
        }
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        var close = FriendsLook.Pill(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI"), FontSize = 9,
        }, Brushes.Transparent, FriendsLook.MutedBrush, "friends-notice-close");
        close.Padding = new Thickness(5);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Focusable = false;
        ToolTip.SetTip(close, FriendsLanding.Str("friends_notice_close", "Dismiss"));
        ToolTip.SetPlacement(close, PlacementMode.Left);   // never under the pointer (the flicker trap)
        close.Click += (_, e) => { e.Handled = true; Dismiss(n, acted: false); };
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        bar = new ScaleTransform(n.Fraction, 1);
        var barLine = new Border
        {
            Height = 2, Margin = new Thickness(0, 9, 0, 0), CornerRadius = new CornerRadius(1),
            Background = LandingChrome.Brush(n.Kind == NoticeKind.Poke && look.Pink ? FriendsLook.Pink : FriendsLook.Lilac, 0xCC),
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            RenderTransform = bar,
        };

        var body = new StackPanel();
        body.Children.Add(grid);
        body.Children.Add(barLine);

        var card = new Border
        {
            Width = CardWidth,
            Background = LandingChrome.Brush(LandingChrome.Ground, 0xF2),
            BorderBrush = FriendsLook.Line2Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(11, 10, 9, 8),
            Margin = new Thickness(LandingChrome.ShadowRoom, 0, LandingChrome.ShadowRoom, 8 + LandingChrome.ShadowRoom / 2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = body,
            RenderTransform = new TranslateTransform(),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 5, Blur = 18, Color = Color.FromArgb(0x80, 0, 0, 0) }),
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) } },
            Tag = "friends-notice:" + n.Kind,
        };
        // Buttons mark their own press handled, so this only fires for a click on the body.
        card.PointerReleased += (_, e) =>
        {
            if (e.Handled || e.InitialPressMouseButton != MouseButton.Left) return;
            Dismiss(n, acted: false);
            try { look.Open?.Invoke(n.Payload); }
            catch (Exception ex) { Serilog.Log.Debug("[Friends] notice open: {E}", ex.Message); }
        };
        return card;
    }

    private static void Enter(Border card, bool folded)
    {
        var level = FriendsDrawer.MotionLevelNow();
        if (level == MotionLevel.Off) return;
        var full = level == MotionLevel.Full;
        var slide = (TranslateTransform)card.RenderTransform!;
        if (folded)
        {
            // A repeat: a small nudge, not a second arrival.
            if (full) Slide(slide, -6, 220);
            return;
        }
        if (full) Slide(slide, 28, 260);
        card.Opacity = 0;
        Dispatcher.UIThread.Post(() => card.Opacity = 1, DispatcherPriority.Render);   // 0 -> 1 through the transition
    }

    private static void Slide(TranslateTransform slide, double from, int ms)
    {
        slide.X = from;
        Helpers.TransformTween.Run(slide, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[]
        {
            (0, TranslateTransform.XProperty, from), (1, TranslateTransform.XProperty, 0),
        }, new CubicEaseOut());
    }
}
