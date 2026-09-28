using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.FriendsWindows;

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

/// <summary>
/// The corner notifications: one small non-activating window in the bottom-right of the work area
/// (above the taskbar) of the monitor the anchor is on, holding up to three notices newest on top,
/// "+N more" under them. The stack rules live in <see cref="FriendNoticeStack"/>; this draws them,
/// runs the lifetime bars, pauses on hover and closes itself when the last notice leaves.
/// </summary>
internal sealed class FriendNotices : Window
{
    private const double CardWidth = 300;
    private const int TickMs = 50;

    private static FriendNotices? _current;

    private readonly FriendNoticeStack _stack = new();
    private readonly Dictionary<FriendNotice, NoticeLook> _looks = new();
    private readonly Dictionary<FriendNotice, (Border Card, ScaleTransform Bar, TextBlock Ago)> _drawn = new();
    private readonly StackPanel _column = new();
    private readonly DispatcherTimer _tick;
    private Window? _anchor;
    private FriendNotice? _fresh;
    private bool _freshFolded;
    private DateTime _last = DateTime.UtcNow;
    private int _agoTick;

    private FriendNotices()
    {
        LandingChrome.Dress(this, null);
        Focusable = false;
        PassiveToastWindow.Apply(this);
        Content = _column;
        MouseEnter += (_, _) => _stack.Paused = true;
        MouseLeave += (_, _) => { _stack.Paused = false; _last = DateTime.UtcNow; };
        SizeChanged += (_, _) => Place();
        // A new monitor scale keeps the DIP size, so SizeChanged stays quiet: place again once WPF has moved us.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Place));
        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _tick.Tick += (_, _) => OnTick();
        // Closed by anyone else (a shutdown sweep): never leave a dead window as the one Show reuses.
        Closed += (_, _) =>
        {
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
            if (w == null)
            {
                w = new FriendNotices();
                _current = w;
                w.Left = -10000;
                w.Top = -10000;
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
            if (!w.IsVisible) { w.Show(); w._last = DateTime.UtcNow; w._tick.Start(); }
            w.Place();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] notice: {E}", ex.Message); }
    }

    /// <summary>Everything down at once. The exit path passes no fold; a hold starting folds, so a
    /// knock that was up still waits in the Inbox.</summary>
    public static void CloseAll(bool fold = false)
    {
        var w = _current;
        _current = null;
        if (w == null) return;
        try
        {
            w._tick.Stop();
            if (fold) foreach (var n in new System.Collections.Generic.List<FriendNotice>(w._stack.All)) w.Leave(n, acted: false, missed: true);
            w._stack.Clear();
            w.Close();
        }
        catch { /* already gone */ }
    }

    public static bool AnyUp => _current?._stack.All.Count > 0;

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
        catch (Exception ex) { App.Logger?.Debug("[Friends] notice left: {E}", ex.Message); }
        if (!missed) return;
        try { look.Missed?.Invoke(n.Payload); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] notice missed: {E}", ex.Message); }
    }

    /// <summary>Takes one notice down after a press, with a quick fade where motion allows.</summary>
    private void Dismiss(FriendNotice n, bool acted)
    {
        if (!_stack.Remove(n)) return;
        Leave(n, acted);
        if (MotionFx.Level != MotionLevel.Off && _drawn.TryGetValue(n, out var d))
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            fade.Completed += (_, _) => AfterRemoval();
            d.Card.BeginAnimation(OpacityProperty, fade);
            return;
        }
        AfterRemoval();
    }

    private void AfterRemoval()
    {
        if (_stack.All.Count == 0)
        {
            if (ReferenceEquals(_current, this)) _current = null;
            try { _tick.Stop(); Close(); } catch { /* already gone */ }
            return;
        }
        _fresh = null;
        Rebuild();
    }

    // ---------------------------------------------------------------- placement

    private void Place()
    {
        try
        {
            if (!IsVisible) return;
            var self = new WindowInteropHelper(this).Handle;
            if (self == IntPtr.Zero) return;
            // Device pixels end to end: the anchor's monitor may not share this window's scale, and
            // Left/Top would convert through the monitor the window is on now, not the one it goes to.
            var anchor = _anchor != null && PresentationSource.FromVisual(_anchor) != null
                ? new WindowInteropHelper(_anchor).Handle : IntPtr.Zero;
            var screen = anchor != IntPtr.Zero
                ? System.Windows.Forms.Screen.FromHandle(anchor)
                : System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            var s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (s <= 0) s = 1;
            var (left, top) = FriendNoticeRules.Place(wa.Left / s, wa.Top / s, wa.Width / s, wa.Height / s, ActualWidth, ActualHeight);
            SetWindowPos(self, IntPtr.Zero, (int)Math.Round(left * s), (int)Math.Round(top * s), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] notice place: {E}", ex.Message); }
    }

    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

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
                FontFamily = LandingChrome.Display,
                FontSize = 11.5,
                Foreground = LandingChrome.Brush(LandingChrome.Muted),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0),
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
            _ => FriendsLanding.Str(key, "now"),
        };
    }

    private Border BuildCard(FriendNotice n, NoticeLook look, out ScaleTransform bar, out TextBlock ago)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = LandingChrome.Avatar(n.Name, look.AvatarUrl, 34);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(avatar);

        var words = new StackPanel { Margin = new Thickness(10, 0, 4, 0) };
        var head = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        head.Inlines.Add(new Run(n.Name)
        {
            FontFamily = LandingChrome.Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            Foreground = LandingChrome.Brush(LandingChrome.Text),
        });
        ago = new TextBlock
        {
            Text = AgoText(n),
            FontSize = 10.5,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = LandingChrome.Brush(LandingChrome.Muted),
        };
        var headRow = new DockPanel();
        DockPanel.SetDock(ago, Dock.Right);
        headRow.Children.Add(ago);
        headRow.Children.Add(head);
        words.Children.Add(headRow);

        var line = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
            Foreground = LandingChrome.Brush(LandingChrome.Muted),
        };
        line.Inlines.Add(new Run(look.Line));
        if (!string.IsNullOrEmpty(look.Word))
            line.Inlines.Add(new Run(" " + look.Word)
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = LandingChrome.Brush(look.Pink ? LandingChrome.Pink : LandingChrome.Mint),
            });
        if (n.Count > 1)
            line.Inlines.Add(new Run(FriendNoticeRules.CountSuffix(n.Count))
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = LandingChrome.Brush(LandingChrome.Gold),
            });
        words.Children.Add(line);

        if (look.ActionLabel != null && look.Act != null)
        {
            var act = Pill(look.ActionLabel, primary: true);
            act.HorizontalAlignment = HorizontalAlignment.Left;
            act.Margin = new Thickness(0, 7, 0, 0);
            act.Click += (_, _) =>
            {
                Dismiss(n, acted: true);
                try { look.Act(n.Payload); }
                catch (Exception ex) { App.Logger?.Debug("[Friends] notice action: {E}", ex.Message); }
            };
            words.Children.Add(act);
        }
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        var close = Pill("", primary: false);
        close.FontFamily = new FontFamily("Segoe MDL2 Assets");
        close.FontSize = 9;
        close.Padding = new Thickness(5);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.ToolTip = FriendsLanding.Str("friends_notice_close", "Dismiss");
        close.Click += (_, _) => Dismiss(n, acted: false);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        bar = new ScaleTransform(n.Fraction, 1);
        var barLine = new Border
        {
            Height = 2,
            Margin = new Thickness(0, 9, 0, 0),
            CornerRadius = new CornerRadius(1),
            Background = LandingChrome.Brush(n.Kind == NoticeKind.Poke && look.Pink ? LandingChrome.Pink : LandingChrome.Lilac, 0xCC),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = bar,
        };

        var body = new StackPanel();
        body.Children.Add(grid);
        body.Children.Add(barLine);

        var card = new Border
        {
            Width = CardWidth,
            Background = LandingChrome.Brush(LandingChrome.Ground, 0xF2),
            BorderBrush = LandingChrome.Brush(LandingChrome.Line2),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(11, 10, 9, 8),
            Margin = new Thickness(16, 0, 0, 8),
            Cursor = Cursors.Hand,
            Child = body,
            RenderTransform = new TranslateTransform(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 18, ShadowDepth = 5, Opacity = 0.5, Direction = 270,
            },
        };
        // Buttons mark their own press handled, so this only fires for a click on the body.
        card.MouseLeftButtonUp += (_, _) =>
        {
            Dismiss(n, acted: false);
            try { look.Open?.Invoke(n.Payload); }
            catch (Exception ex) { App.Logger?.Debug("[Friends] notice open: {E}", ex.Message); }
        };
        return card;
    }

    private static void Enter(Border card, bool folded)
    {
        if (MotionFx.Level == MotionLevel.Off) return;
        var full = MotionFx.Level == MotionLevel.Full;
        var slide = (TranslateTransform)card.RenderTransform;
        if (folded)
        {
            // A repeat: a small nudge, not a second arrival.
            if (!full) return;
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } });
            return;
        }
        if (full)
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(full ? 200 : 140)));
    }

    private static Button Pill(string text, bool primary)
    {
        var face = new FrameworkElementFactory(typeof(Border));
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        face.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        face.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        face.AppendChild(cp);
        return new Button
        {
            Content = text,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = face },
            Background = primary ? LandingChrome.Brush(LandingChrome.Mint) : Brushes.Transparent,
            Foreground = primary ? LandingChrome.Brush(Color.FromRgb(0x10, 0x2A, 0x22)) : LandingChrome.Brush(LandingChrome.Muted),
            FontFamily = LandingChrome.Display,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            Cursor = Cursors.Hand,
            Focusable = false,
        };
    }
}
