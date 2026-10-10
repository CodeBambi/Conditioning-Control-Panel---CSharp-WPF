// PORTED from ConditioningControlPanel/Windows/Friends/KnockCard.cs (7.1.5): an invite at the door. The
// friend's avatar and name, one line ("invites you to the Back Room"), the destination's picture,
// Join / Not now, and a countdown to the invite's own expiry (five minutes, server time) with a ring that
// runs down with it. The card RINGS full size for LandingRules.KnockRingSeconds, then tucks to a small
// strip that carries the rest quietly; hovering opens it again. The small x puts it away without
// answering. Nothing happens until a button is pressed. Cards stack down the anchor's top-right corner.
// IN: a 6 px drop + fade; OUT: a 6 px lift + fade (180 ms). Unowned and never activated (LandingChrome).
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Friends;

/// <summary>How a knock card ended. <see cref="NotNow"/> answers the sender (declined);
/// <see cref="Later"/> only puts the card away (it waits in the Inbox while it is answerable).</summary>
internal enum KnockOutcome { Go, NotNow, Later, RanOut }

internal sealed class KnockCard : Window
{
    internal const double CardWidth = 232;
    internal const double TuckedWidth = 206;
    private const double RingSize = 22;
    private static readonly List<KnockCard> Open = new();

    private readonly InboxItem _item;
    private readonly DateTimeOffset _start;
    private readonly DateTimeOffset _end;
    private readonly Arc _arc = new();
    private readonly TextBlock _countdown = new();
    private readonly DispatcherTimer _tick;
    private readonly Action<InboxItem, KnockOutcome> _done;
    private readonly Action<InboxItem>? _shown;
    private readonly Window _anchor;
    private readonly Border _card;
    private readonly Control _line;
    private readonly Control _art;
    private DateTimeOffset _shownAt;
    private bool _folded;
    private bool _tucked;
    private bool _hovered;
    private int _lastSecond = -1;

    private KnockCard(Window anchor, InboxItem item, string line, string goLabel, string notNowLabel, string laterTip,
        Action<InboxItem, KnockOutcome> done, Action<InboxItem>? shown)
    {
        LandingChrome.Dress(this);
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
        _countdown.FontFamily = FriendsLook.Mono;
        _countdown.FontSize = 11;
        _countdown.Foreground = FriendsLook.MutedBrush;
        _countdown.VerticalAlignment = VerticalAlignment.Center;
        _countdown.Margin = new Thickness(0, 0, 6, 0);
        _countdown.Tag = "knock-countdown";
        clock.Children.Add(_countdown);
        clock.Children.Add(BuildRing());
        DockPanel.SetDock(clock, Dock.Right);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        top.Children.Add(LandingChrome.Avatar(item.FromName, item.FromAvatarUrl, 28));
        var words = new StackPanel { Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel();
        head.Children.Add(clock);
        head.Children.Add(new TextBlock
        {
            Text = item.FromName,
            FontFamily = FriendsLook.Display,
            FontSize = 13.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = FriendsLook.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = CardWidth - 84,
            VerticalAlignment = VerticalAlignment.Center,
        });
        words.Children.Add(head);
        var lineText = new TextBlock
        {
            Text = line,
            FontSize = 11,
            Foreground = FriendsLook.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = CardWidth - 84,
            Tag = "knock-line",
        };
        _line = lineText;
        words.Children.Add(lineText);
        Grid.SetColumn(words, 1);
        top.Children.Add(words);

        var later = FriendsLook.Pill(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI"), FontSize = 9,
        }, Brushes.Transparent, FriendsLook.MutedBrush, "knock-later");
        later.Padding = new Thickness(5);
        later.VerticalAlignment = VerticalAlignment.Top;
        later.Focusable = false;
        ToolTip.SetTip(later, laterTip);
        ToolTip.SetPlacement(later, PlacementMode.Left);   // never under the pointer
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
                ClipToBounds = true,
                Height = (CardWidth - 20) * 9 / 16,
                Child = new Image { Source = pic, Stretch = Stretch.UniformToFill },
                Tag = "knock-art",
            }
            : new Border { Height = 8 };
        stack.Children.Add(_art);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var go = FriendsLook.Pill(goLabel, FriendsLook.MintBrush, FriendsLook.MintInkBrush, "knock-go", FriendsLook.MintBrush, FriendsLook.MintBrush);
        go.Padding = new Thickness(10, 5, 10, 5);
        go.Focusable = false;
        go.Click += (_, _) => Fold(KnockOutcome.Go);
        var notNow = FriendsLook.Pill(notNowLabel, Brushes.Transparent, FriendsLook.MutedBrush, "knock-not-now", FriendsLook.Line2Brush);
        notNow.Padding = new Thickness(10, 5, 10, 5);
        notNow.Margin = new Thickness(6, 0, 0, 0);
        notNow.Focusable = false;
        notNow.Click += (_, _) => Fold(KnockOutcome.NotNow);
        buttons.Children.Add(go);
        buttons.Children.Add(notNow);
        stack.Children.Add(buttons);

        _card = new Border
        {
            Width = CardWidth,
            Background = LandingChrome.Brush(LandingChrome.Ground),
            BorderBrush = FriendsLook.Line2Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 10, 10, 9),
            Margin = new Thickness(LandingChrome.ShadowRoom, 6, LandingChrome.ShadowRoom, 12 + LandingChrome.ShadowRoom / 2),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 24, Color = Color.FromArgb(0x8C, 0, 0, 0) }),
            Child = stack,
            RenderTransform = new TranslateTransform(),
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) } },
            Tag = "knock-card",
        };
        Content = _card;

        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => Advance(ServerClock.UtcNow);
        // Escape puts it away without answering; only Not now tells the sender no.
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Fold(KnockOutcome.Later); };
        PointerEntered += (_, _) => { _hovered = true; ApplyTuck(); };
        PointerExited += (_, _) => { _hovered = false; ApplyTuck(); };
        UpdateCountdown(now);
    }

    internal InboxItem Item => _item;
    internal bool IsTucked => _tucked && !_hovered;
    internal string CountdownText => _countdown.Text ?? "";

    private Control BuildRing()
    {
        var grid = new Grid { Width = RingSize, Height = RingSize, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Ellipse
        {
            Width = RingSize, Height = RingSize,
            Stroke = LandingChrome.Brush(Colors.White, 0x14),
            StrokeThickness = 4,
        });
        // The lit part of the ring, clockwise from 12 o'clock.
        _arc.Width = RingSize;
        _arc.Height = RingSize;
        _arc.Stroke = FriendsLook.LilacBrush;
        _arc.StrokeThickness = 4;
        _arc.StrokeLineCap = PenLineCap.Round;
        _arc.StartAngle = -90;
        grid.Children.Add(_arc);
        DrawArc(LandingRules.RingFraction(_start, _end, ServerClock.UtcNow));
        return grid;
    }

    private void DrawArc(double fraction) => _arc.SweepAngle = Math.Clamp(fraction, 0, 1) * 360;

    private void UpdateCountdown(DateTimeOffset now)
    {
        var s = LandingRules.SecondsLeft(_end, now);
        if (s == _lastSecond) return;
        _lastSecond = s;
        _countdown.Text = LandingRules.Countdown(_end, now);
        // The last half minute reads warmer, so a glance says "now or never".
        _countdown.Foreground = s <= 30 ? FriendsLook.GoldBrush : FriendsLook.MutedBrush;
    }

    /// <summary>One step of the card's clock at server time <paramref name="now"/> (the tick; the suite steps it).</summary>
    internal void Advance(DateTimeOffset now)
    {
        if (_folded) return;
        var f = LandingRules.RingFraction(_start, _end, now);
        DrawArc(f);
        UpdateCountdown(now);
        if (!_tucked && LandingRules.KnockTucked(_shownAt, now)) { _tucked = true; ApplyTuck(); }
        if (f <= 0) Fold(KnockOutcome.RanOut);
    }

    /// <summary>Tucked and not hovered: the small strip. Otherwise the whole card.</summary>
    private void ApplyTuck()
    {
        if (_folded) return;
        bool small = _tucked && !_hovered;
        _art.IsVisible = !small;
        _line.IsVisible = !small;
        _card.Width = small ? TuckedWidth : CardWidth;
        _card.Opacity = small ? 0.9 : 1;
        Restack();
    }

    /// <summary>Ends the card with <paramref name="outcome"/> (a button, the clock, a hold). Once.</summary>
    internal void Fold(KnockOutcome outcome, bool instant = false)
    {
        if (_folded) return;
        _folded = true;
        _tick.Stop();
        Open.Remove(this);
        Restack();
        try { _done(_item, outcome); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] knock {Outcome} failed", outcome); }

        if (instant || FriendsDrawer.MotionLevelNow() == MotionLevel.Off) { SafeClose(); return; }
        _card.IsHitTestVisible = false;
        _card.Opacity = 0;   // the 180 ms opacity transition
        Lift(0, -6, 180);
        DispatcherTimer.RunOnce(SafeClose, TimeSpan.FromMilliseconds(180));
    }

    private void SafeClose()
    {
        try { Close(); } catch { /* already gone */ }
    }

    private void Enter()
    {
        _shownAt = ServerClock.UtcNow;
        var level = FriendsDrawer.MotionLevelNow();
        if (level != MotionLevel.Off)
        {
            Lift(-6, 0, level == MotionLevel.Full ? 250 : 125);
            _card.Opacity = 0;
            Dispatcher.UIThread.Post(() => { if (!_folded) _card.Opacity = IsTucked ? 0.9 : 1; }, DispatcherPriority.Render);
        }
        _tick.Start();
        // It is on screen now: that is what "seen" means on the wire.
        try { _shown?.Invoke(_item); }
        catch (Exception ex) { Serilog.Log.Debug("[Friends] knock seen: {E}", ex.Message); }
    }

    private void Lift(double from, double to, int ms)
    {
        if (_card.RenderTransform is not TranslateTransform t) return;
        t.Y = from;
        Helpers.TransformTween.Run(t, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[]
        {
            (0, TranslateTransform.YProperty, from), (1, TranslateTransform.YProperty, to),
        }, new CubicEaseOut());
    }

    /// <summary>Cards stack down the anchor's top-right corner, newest at the bottom.</summary>
    private static void Restack()
    {
        double y = 0;
        foreach (var card in Open)
        {
            try
            {
                var want = LandingChrome.Want(card._card);
                var k = card._anchor.DesktopScaling > 0 ? card._anchor.DesktopScaling : 1;
                var r = LandingChrome.AnchorRect(card._anchor);
                var at = new PixelPoint(
                    (int)Math.Round(r.Right - (want.Width + 14 - LandingChrome.ShadowRoom) * k),
                    (int)Math.Round(r.Y + (34 + y) * k));
                LandingChrome.PlacePassive(card, at, want, k);
                y += want.Height - LandingChrome.ShadowRoom / 2;
            }
            catch (Exception ex) { Serilog.Log.Debug("[Friends] knock restack: {E}", ex.Message); }
        }
    }

    /// <summary>Puts a card up for <paramref name="item"/> unless one is already up for it. <paramref name="shown"/>
    /// runs once the card is on screen (the seen receipt); <paramref name="done"/> runs once with how it ended.</summary>
    public static KnockCard? Show(Window? anchor, InboxItem item, string line, string goLabel, string notNowLabel, string laterTip,
        Action<InboxItem, KnockOutcome> done, Action<InboxItem>? shown = null)
    {
        if (anchor == null || item == null) return null;
        foreach (var c in Open) if (c._item.Id == item.Id) return null;
        try
        {
            var card = new KnockCard(anchor, item, line, goLabel, notNowLabel, laterTip, done, shown);
            // Closed from outside (a shutdown sweep): no outcome, just out of the stack with its clock stopped.
            card.Closed += (_, _) =>
            {
                card._folded = true;
                card._tick.Stop();
                if (Open.Remove(card)) Restack();
            };
            Open.Add(card);
            Restack();   // placed (and made passive) before Show
            card.Show();
            card.Enter();
            return card;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Friends] knock card failed"); return null; }
    }

    public static bool AnyUp => Open.Count > 0;

    /// <summary>Test seam: the cards up, oldest first.</summary>
    internal static IReadOnlyList<KnockCard> Up => Open;

    /// <summary>A hold started (lockdown, Strict Lock, a session) or panic was pressed: every card goes
    /// away unanswered, as <see cref="KnockOutcome.Later"/>, so it waits in the Inbox.</summary>
    public static void FoldAll(bool instant = false)
    {
        foreach (var c in Open.ToArray()) c.Fold(KnockOutcome.Later, instant);
    }

    /// <summary>Exit path: close every card without running its outcome.</summary>
    public static void CloseAll()
    {
        var all = new List<KnockCard>(Open);
        Open.Clear();
        foreach (var c in all)
        {
            c._folded = true;
            c._tick.Stop();
            c.SafeClose();
        }
    }
}
