using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.FriendsWindows;

/// <summary>
/// A poke while a game has the screen: a compact pill in the game window's top-right corner,
/// "Sam: well played", for a couple of seconds. It is its own owned window, so it sits above the
/// game's WebView2 rather than fighting it for airspace. Never takes focus or the mouse.
/// </summary>
internal sealed class FriendToast : Window
{
    private const int HoldMs = 2600;
    private static FriendToast? _current;

    private FriendToast(string name, string? avatar, string word, bool pink)
    {
        LandingChrome.Dress(this, null);
        IsHitTestVisible = false;
        Focusable = false;
        LandingChrome.ClickThrough(this);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(LandingChrome.Avatar(name, avatar, 22));
        var line = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = LandingChrome.Display,
            FontSize = 13,
            Foreground = LandingChrome.Brush(LandingChrome.Text),
        };
        line.Inlines.Add(new System.Windows.Documents.Run(name + ": "));
        line.Inlines.Add(new System.Windows.Documents.Run(word)
        {
            Foreground = LandingChrome.Brush(pink ? LandingChrome.Pink : LandingChrome.Mint),
        });
        row.Children.Add(line);

        Content = new Border
        {
            Background = LandingChrome.Brush(Color.FromRgb(0x0F, 0x0A, 0x1D), 0xE6),
            BorderBrush = LandingChrome.Brush(Colors.White, 0x1A),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 7, 12, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Child = row,
            RenderTransform = new TranslateTransform(),
        };
    }

    public static void Show(Window game, string name, string? avatar, string word, bool pink)
    {
        if (game == null) return;
        try
        {
            try { _current?.Close(); } catch { /* already gone */ }
            var t = new FriendToast(name, avatar, word, pink);
            if (game.IsLoaded) t.Owner = game;
            _current = t;
            t.Loaded += (_, _) =>
            {
                var r = LandingChrome.OwnerRect(game);
                t.Left = r.Right - t.ActualWidth - 14;
                t.Top = r.Top + 34;
                t.Enter();
            };
            // Placed off screen until measured, so it never flashes at 0,0.
            t.Left = -10000;
            t.Top = -10000;
            t.Show();
        }
        catch (Exception ex) { App.Logger?.Debug("[Friends] toast: {E}", ex.Message); }
    }

    private void Enter()
    {
        var border = (Border)Content;
        var slide = (TranslateTransform)border.RenderTransform;
        if (MotionFx.Level != MotionLevel.Off)
        {
            var dur = TimeSpan.FromMilliseconds(MotionFx.Level == MotionLevel.Full ? 250 : 125);
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(8, 0, dur) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } });
            border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }

        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(HoldMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (MotionFx.Level == MotionLevel.Off) { Fold(); return; }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (_, _) => Fold();
            border.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    private void Fold()
    {
        try { Close(); } catch { /* already gone */ }
        if (ReferenceEquals(_current, this)) _current = null;
    }

    /// <summary>Exit path: close whatever is up.</summary>
    public static void CloseAll()
    {
        try { _current?.Close(); } catch { /* already gone */ }
        _current = null;
    }
}
