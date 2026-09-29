using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.FriendsWindows;

/// <summary>How a knock card ended. <see cref="NotNow"/> answers the sender (declined);
/// <see cref="Later"/> only puts the card away (it waits in the Inbox while it is answerable).</summary>
internal enum KnockOutcome { Go, NotNow, Later, RanOut }

/// <summary>
/// An invite at the door: the friend's avatar and name, one line ("invites you to the Back Room"),
/// the destination's picture, Join / Not now, and a countdown to the invite's own expiry with a ring
/// that runs down with it. An invite lives five minutes: the card RINGS full size for
/// <see cref="LandingRules.KnockRingSeconds"/>, then tucks to a small strip (name, countdown, the two
/// buttons) that carries the rest quietly; hovering it opens it up again. The small x puts it away
/// without answering. Nothing happens until a button is pressed. Cards stack down the owner's
/// top-right corner.
/// </summary>
internal sealed class KnockCard : Window
{
    private const double CardWidth = 232;
    private const double TuckedWidth = 206;
    private const double RingSize = 22;
    private static readonly List<KnockCard> Open = new();
    // Shown but not loaded yet: a second open of the same Inbox row in that gap must not stack a twin.
    private static readonly List<KnockCard> Coming = new();

    private readonly InboxItem _item;
    private readonly DateTimeOffset _start;
    private readonly DateTimeOffset _end;
    private readonly Path _arc = new();
    private readonly TextBlock _countdown = new();
    private readonly DispatcherTimer _tick;
    private readonly Action<InboxItem, KnockOutcome> _done;
    private readonly Action<InboxItem>? _shown;
    private readonly Window _anchor;
    private readonly Border _card;
    private readonly FrameworkElement _line;
    private readonly FrameworkElement _art;
    private DateTimeOffset _shownAt;
    private bool _folded;
    private bool _tucked;
    private bool _hovered;
    private int _lastSecond = -1;

    private KnockCard(Window anchor, InboxItem item, string line, string goLabel, string notNowLabel, string laterTip,
        Action<InboxItem, KnockOutcome> done, Action<InboxItem>? shown)
    {
        LandingChrome.Dress(this, null);
        _anchor = anchor;
        _item = item;
        _done = done;
        _shown = shown;
        var now = ServerClock.UtcNow;
        _start = LandingRules.KnockStarts(item, now);
        _end = LandingRules.KnockEnds(item, now);
        _shownAt = now;

        // The clock (countdown and ring) sits beside the name, so no button label, in any language,
        // can push it off the card. The line under the name keeps the whole width.
        var clock = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        _countdown.FontFamily = new FontFamily("Consolas, Courier New");
        _countdown.FontSize = 11;
        _countdown.Foreground = LandingChrome.Brush(LandingChrome.Muted);
        _countdown.VerticalAlignment = VerticalAlignment.Center;
        _countdown.Margin = new Thickness(0, 0, 6, 0);
        clock.Children.Add(_countdown);
        clock.Children.Add(BuildRing());
        DockPanel.SetDock(clock, Dock.Right);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(LandingChrome.Avatar(item.FromName, item.FromAvatarUrl, 28));
        var words = new StackPanel { Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel();
        head.Children.Add(clock);
        head.Children.Add(new TextBlock
        {
            Text = item.FromName,
            FontFamily = LandingChrome.Display,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = LandingChrome.Brush(LandingChrome.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = CardWidth - 84,
            VerticalAlignment = VerticalAlignment.Center,
        });
        words.Children.Add(head);
        var lineText = new TextBlock
        {
            Text = line,
            FontSize = 11,
            Foreground = LandingChrome.Brush(LandingChrome.Muted),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = CardWidth - 84,
        };
        _line = lineText;
        words.Children.Add(lineText);
        Grid.SetColumn(words, 1);
        top.Children.Add(words);

        var later = MakeButton("", primary: false);
        later.FontFamily = new FontFamily("Segoe MDL2 Assets");
        later.FontSize = 9;
        later.Padding = new Thickness(5);
        later.BorderThickness = new Thickness(0);
        later.VerticalAlignment = VerticalAlignment.Top;
        later.ToolTip = laterTip;
        later.Click += (_, _) => Fold(KnockOutcome.Later);
        Grid.SetColumn(later, 2);
        top.Children.Add(later);

        var stack = new StackPanel();
        stack.Children.Add(top);

        var pic = LandingChrome.Picture(LandingRules.KnockArt(item));
        _art = pic != null
            ? new Border
            {
                Margin = new Thickness(0, 8, 0, 8),
                CornerRadius = new CornerRadius(8),
                Height = (CardWidth - 20) * 9 / 16,
                Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill },
            }
            : new Border { Height = 8 };
        stack.Children.Add(_art);

        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 0) };
        var go = MakeButton(goLabel, primary: true);
        go.Click += (_, _) => Fold(KnockOutcome.Go);
        var notNow = MakeButton(notNowLabel, primary: false);
        notNow.Margin = new Thickness(6, 0, 0, 0);
        notNow.Click += (_, _) => Fold(KnockOutcome.NotNow);
        DockPanel.SetDock(go, Dock.Left);
        DockPanel.SetDock(notNow, Dock.Left);
        buttons.Children.Add(go);
        buttons.Children.Add(notNow);
        stack.Children.Add(buttons);

        _card = new Border
        {
            Width = CardWidth,
            Background = LandingChrome.Brush(LandingChrome.Ground),
            BorderBrush = LandingChrome.Brush(LandingChrome.Line2),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 10, 10, 9),
            Margin = new Thickness(0, 0, 0, 12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 24, ShadowDepth = 8, Opacity = 0.55, Direction = 270,
            },
            Child = stack,
            RenderTransform = new TranslateTransform(),
        };
        Content = _card;

        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => Advance();
        // Escape puts it away without answering; only Not now tells the sender no.
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Fold(KnockOutcome.Later); };
        MouseEnter += (_, _) => { _hovered = true; ApplyTuck(); };
        MouseLeave += (_, _) => { _hovered = false; ApplyTuck(); };
        SizeChanged += (_, _) => Restack();
        UpdateCountdown(ServerClock.UtcNow);
    }

    private static Button MakeButton(string label, bool primary)
    {
        var b = new Button
        {
            Content = label,
            FontFamily = LandingChrome.Display,
            FontSize = 12.5,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = Cursors.Hand,
            Foreground = LandingChrome.Brush(primary ? Color.FromRgb(0x06, 0x2A, 0x1F) : LandingChrome.Muted),
            Background = primary ? LandingChrome.Brush(LandingChrome.Mint) : Brushes.Transparent,
            BorderBrush = LandingChrome.Brush(primary ? LandingChrome.Mint : LandingChrome.Line2),
            BorderThickness = new Thickness(1),
            FocusVisualStyle = null,
        };
        // A flat, rounded face: the default chrome would draw the system button over the card.
        var template = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        template.VisualTree = bd;
        b.Template = template;
        b.MouseEnter += (_, _) => MotionFx.HoverLift(b, true);
        b.MouseLeave += (_, _) => MotionFx.HoverLift(b, false);
        return b;
    }

    private FrameworkElement BuildRing()
    {
        var grid = new Grid { Width = RingSize, Height = RingSize, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Ellipse
        {
            Width = RingSize, Height = RingSize,
            Stroke = LandingChrome.Brush(Colors.White, 0x14),
            StrokeThickness = 4,
        });
        _arc.Stroke = LandingChrome.Brush(LandingChrome.Lilac);
        _arc.StrokeThickness = 4;
        _arc.StrokeStartLineCap = PenLineCap.Round;
        _arc.StrokeEndLineCap = PenLineCap.Round;
        grid.Children.Add(_arc);
        DrawArc(LandingRules.RingFraction(_start, _end, ServerClock.UtcNow));
        return grid;
    }

    /// <summary>The lit part of the ring, clockwise from 12 o'clock. The maths is LandingRules.RingPoint.</summary>
    private void DrawArc(double fraction)
    {
        const double r = (RingSize - 4) / 2;
        const double c = RingSize / 2;
        if (fraction <= 0) { _arc.Data = null; return; }
        if (fraction >= 0.999)
        {
            _arc.Data = new EllipseGeometry(new Point(c, c), r, r);
            return;
        }
        var (x, y) = LandingRules.RingPoint(c, c, r, fraction);
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        fig.Segments.Add(new ArcSegment(new Point(x, y), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        _arc.Data = g;
    }

    private void UpdateCountdown(DateTimeOffset now)
    {
        var s = LandingRules.SecondsLeft(_end, now);
        if (s == _lastSecond) return;
        _lastSecond = s;
        _countdown.Text = LandingRules.Countdown(_end, now);
        // The last half minute reads warmer, so a glance says "now or never".
        _countdown.Foreground = LandingChrome.Brush(s <= 30 ? LandingChrome.Gold : LandingChrome.Muted);
    }

    private void Advance()
    {
        var now = ServerClock.UtcNow;
        var f = LandingRules.RingFraction(_start, _end, now);
        DrawArc(f);
        UpdateCountdown(now);
        if (!_tucked && LandingRules.KnockTucked(_shownAt, now)) { _tucked = true; ApplyTuck(); }
        if (f <= 0) Fold(KnockOutcome.RanOut);
    }

    /// <summary>Tucked and not hovered: the small strip. Otherwise the whole card.</summary>
    private void ApplyTuck()
    {
        bool small = _tucked && !_hovered;
        var vis = small ? Visibility.Collapsed : Visibility.Visible;
        _art.Visibility = vis;
        _line.Visibility = vis;
        _card.Width = small ? TuckedWidth : CardWidth;
        _card.Opacity = small ? 0.9 : 1;
    }

    private void Fold(KnockOutcome outcome)
    {
        if (_folded) return;
        _folded = true;
        _tick.Stop();
        Open.Remove(this);
        Restack();
        try { _done(_item, outcome); }
        catch (Exception ex) { App.Logger?.Warning(ex, "[Friends] knock {Outcome} failed", outcome); }

        if (MotionFx.Level == MotionLevel.Off) { SafeClose(); return; }
        var fade = new DoubleAnimation(_card.Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => SafeClose();
        _card.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)_card.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(180)));
    }

    private void SafeClose()
    {
        try { Close(); } catch { /* already gone */ }
    }

    private void Enter()
    {
        _shownAt = ServerClock.UtcNow;
        if (MotionFx.Level != MotionLevel.Off)
        {
            var dur = TimeSpan.FromMilliseconds(MotionFx.Level == MotionLevel.Full ? 250 : 125);
            ((TranslateTransform)_card.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-6, 0, dur) { EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut } });
            _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
        }
        _tick.Start();
        // It is on screen now: that is what "seen" means on the wire.
        try { _shown?.Invoke(_item); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] knock seen: {E}", ex.Message); }
    }

    /// <summary>Cards stack down the anchor's top-right corner, newest at the bottom.</summary>
    private static void Restack()
    {
        double y = 0;
        foreach (var card in Open)
        {
            try
            {
                var r = LandingChrome.OwnerRect(card._anchor);
                card.Left = r.Right - card.ActualWidth - 14;
                card.Top = r.Top + 34 + y;
                y += card.ActualHeight;
            }
            catch (Exception ex) { App.Logger?.Debug("[Friends] knock restack: {E}", ex.Message); }
        }
    }

    /// <summary>Puts a card up for <paramref name="item"/> unless one is already up for it. <paramref name="shown"/>
    /// runs once the card is on screen (the seen receipt); <paramref name="done"/> runs once with how it ended.</summary>
    public static void Show(Window anchor, InboxItem item, string line, string goLabel, string notNowLabel, string laterTip,
        Action<InboxItem, KnockOutcome> done, Action<InboxItem>? shown = null)
    {
        if (anchor == null || item == null) return;
        foreach (var c in Open) if (c._item.Id == item.Id) return;
        foreach (var c in Coming) if (c._item.Id == item.Id) return;
        try
        {
            var card = new KnockCard(anchor, item, line, goLabel, notNowLabel, laterTip, done, shown);
            if (anchor.IsLoaded) card.Owner = anchor;
            card.Left = -10000;
            card.Top = -10000;
            card.Loaded += (_, _) =>
            {
                if (!Coming.Remove(card)) return;   // closed on the way (CloseAll)
                Open.Add(card);
                Restack();
                card.Enter();
            };
            // Closed from outside (its owner went): no outcome, just out of the stack with its clock stopped.
            card.Closed += (_, _) =>
            {
                Coming.Remove(card);
                card._folded = true;
                card._tick.Stop();
                if (Open.Remove(card)) Restack();
            };
            Coming.Add(card);
            card.Show();
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "[Friends] knock card failed"); }
    }

    public static bool AnyUp => Open.Count > 0;

    /// <summary>A hold started (lockdown, Strict Lock, a program session): every card goes away
    /// unanswered, as <see cref="KnockOutcome.Later"/>, so it waits in the Inbox.</summary>
    public static void FoldAll()
    {
        foreach (var c in Open.ToArray()) c.Fold(KnockOutcome.Later);
    }

    /// <summary>Exit path: close every card without running its outcome.</summary>
    public static void CloseAll()
    {
        var all = new List<KnockCard>(Open);
        all.AddRange(Coming);
        Open.Clear();
        Coming.Clear();
        foreach (var c in all)
        {
            c._folded = true;
            c._tick.Stop();
            c.SafeClose();
        }
    }
}
