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

/// <summary>How a knock card ended.</summary>
internal enum KnockOutcome { Go, Later, RanOut }

/// <summary>
/// An invite or a watch at the door: the friend's avatar and name, one line ("invites you to the
/// Back Room", "sends you a watch"), the destination's picture, a Join / Watch button, Later, and
/// a ring that runs out over the seconds left. When the ring runs out the card folds. Nothing
/// happens until a button is pressed. Cards stack down the owner's top-right corner.
/// </summary>
internal sealed class KnockCard : Window
{
    private const double CardWidth = 232;
    private const double RingSize = 22;
    private static readonly List<KnockCard> Open = new();

    private readonly InboxItem _item;
    private readonly DateTimeOffset _start;
    private readonly DateTimeOffset _end;
    private readonly Path _arc = new();
    private readonly DispatcherTimer _tick;
    private readonly Action<InboxItem, KnockOutcome> _done;
    private readonly Window _anchor;
    private bool _folded;

    private KnockCard(Window anchor, InboxItem item, string line, string goLabel, string laterLabel,
        Action<InboxItem, KnockOutcome> done)
    {
        LandingChrome.Dress(this, null);
        _anchor = anchor;
        _item = item;
        _done = done;
        _start = DateTimeOffset.UtcNow;
        _end = LandingRules.KnockEnds(item, _start);

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(LandingChrome.Avatar(item.FromName, item.FromAvatarUrl, 28));
        var words = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = item.FromName,
            FontFamily = LandingChrome.Display,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = LandingChrome.Brush(LandingChrome.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = CardWidth - 60,
        });
        words.Children.Add(new TextBlock
        {
            Text = line,
            FontSize = 11,
            Foreground = LandingChrome.Brush(LandingChrome.Muted),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = CardWidth - 60,
        });
        top.Children.Add(words);

        var stack = new StackPanel();
        stack.Children.Add(top);

        var art = LandingChrome.Picture(LandingRules.KnockArt(item));
        if (art != null)
        {
            stack.Children.Add(new Border
            {
                Margin = new Thickness(0, 8, 0, 8),
                CornerRadius = new CornerRadius(8),
                Height = (CardWidth - 20) * 9 / 16,
                Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
            });
        }
        else
        {
            stack.Children.Add(new Border { Height = 8 });
        }

        var buttons = new DockPanel { LastChildFill = false };
        var go = MakeButton(goLabel, primary: true);
        go.Click += (_, _) => Fold(KnockOutcome.Go);
        var later = MakeButton(laterLabel, primary: false);
        later.Margin = new Thickness(6, 0, 0, 0);
        later.Click += (_, _) => Fold(KnockOutcome.Later);
        DockPanel.SetDock(go, Dock.Left);
        DockPanel.SetDock(later, Dock.Left);
        buttons.Children.Add(go);
        buttons.Children.Add(later);
        var ring = BuildRing();
        DockPanel.SetDock(ring, Dock.Right);
        buttons.Children.Add(ring);
        stack.Children.Add(buttons);

        Content = new Border
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

        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(60) };
        _tick.Tick += (_, _) => Advance();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Fold(KnockOutcome.Later); };
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
        DrawArc(1);
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

    private void Advance()
    {
        var f = LandingRules.RingFraction(_start, _end, DateTimeOffset.UtcNow);
        DrawArc(f);
        if (f <= 0) Fold(KnockOutcome.RanOut);
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

        var border = (Border)Content;
        if (MotionFx.Level == MotionLevel.Off) { SafeClose(); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => SafeClose();
        border.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)border.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(180)));
    }

    private void SafeClose()
    {
        try { Close(); } catch { /* already gone */ }
    }

    private void Enter()
    {
        var border = (Border)Content;
        if (MotionFx.Level != MotionLevel.Off)
        {
            var dur = TimeSpan.FromMilliseconds(MotionFx.Level == MotionLevel.Full ? 250 : 125);
            ((TranslateTransform)border.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-6, 0, dur) { EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut } });
            border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }
        _tick.Start();
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

    public static void Show(Window anchor, InboxItem item, string line, string goLabel, string laterLabel,
        Action<InboxItem, KnockOutcome> done)
    {
        if (anchor == null || item == null) return;
        foreach (var c in Open) if (c._item.Id == item.Id) return;
        try
        {
            var card = new KnockCard(anchor, item, line, goLabel, laterLabel, done);
            if (anchor.IsLoaded) card.Owner = anchor;
            card.Left = -10000;
            card.Top = -10000;
            card.Loaded += (_, _) =>
            {
                Open.Add(card);
                Restack();
                card.Enter();
            };
            card.Show();
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "[Friends] knock card failed"); }
    }

    /// <summary>Exit path: close every card without running its outcome.</summary>
    public static void CloseAll()
    {
        foreach (var c in Open.ToArray())
        {
            c._folded = true;
            c._tick.Stop();
            c.SafeClose();
        }
        Open.Clear();
    }
}
