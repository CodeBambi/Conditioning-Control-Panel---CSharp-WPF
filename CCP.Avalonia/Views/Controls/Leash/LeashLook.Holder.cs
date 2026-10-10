// WPF 7.1.5 LeashLook pieces only the holder card uses: the 7-day strip cell and the stacked
// (icon over word) chunky button, with the card's shade / idle / button plates. Flat: no glow
// Effects (Avalonia effect/cache rule).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal static partial class LeashLook
{
    internal static readonly IBrush Shade = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
        Idle = new SolidColorBrush(Color.FromRgb(0x4A, 0x3D, 0x61)),
        ButtonBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33));

    /// <summary>One cell of the 7-day strip with its weekday letter.</summary>
    internal static Control WeekCell(WeekMark mark, string letter)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Tag = "leash-week:" + mark };
        var dot = new Ellipse { Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Center };
        switch (mark)
        {
            case WeekMark.Did: dot.Fill = FriendsDrawer.Mint; break;
            case WeekMark.Punished: dot.Fill = FriendsDrawer.Red; break;
            case WeekMark.Today:
                dot.Fill = Brushes.Transparent;
                dot.Stroke = FriendsDrawer.Gold;
                dot.StrokeThickness = 2;
                dot.StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 1.6, 1.2 };
                break;
            default: dot.Fill = Idle; break;
        }
        sp.Children.Add(dot);
        sp.Children.Add(new TextBlock
        {
            Text = letter,
            FontFamily = FriendsDrawer.Mono,
            FontSize = 9.5,
            FontWeight = FontWeight.Bold,
            Foreground = FriendsDrawer.Dim,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });
        return sp;
    }

    /// <summary>WPF LeashLook.Chunky(text, tone, icon, stacked: true, size: 12): the icon over the word.</summary>
    internal static Button Stacked(string text, Tone tone, string icon, string tag)
    {
        // WPF LeashLook.Colors: a top-to-bottom gradient body, ink text, a faint white edge. The WPF
        // glow Effect is left off (Avalonia effect/cache rule).
        var ink = InkBrush;
        var (top, bottom, fg) = tone switch
        {
            Tone.Mint => (Color.FromRgb(0xA8, 0xFF, 0xE6), Color.FromRgb(0x5F, 0xFF, 0xD0), ink),
            Tone.Red => (Color.FromRgb(0xFF, 0x8A, 0x9E), Color.FromRgb(0xFF, 0x5F, 0x7A), (IBrush)Brushes.White),
            Tone.Ghost => (Color.FromRgb(0x2E, 0x20, 0x46), Color.FromRgb(0x26, 0x1A, 0x3C), FriendsDrawer.Muted),
            _ => (Color.FromRgb(0xFF, 0xE3, 0xA0), Color.FromRgb(0xFF, 0xCF, 0x6B), ink),
        };
        var bg = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
        };
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var ic = Icon(icon, fg, 20);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        ic.Margin = new Thickness(0, 0, 0, 3);
        sp.Children.Add(ic);
        var t = new TextBlock { Text = text, FontFamily = FriendsDrawer.Display, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        sp.Children.Add(t);
        var b = FriendsDrawer.Pill(sp, bg, fg, tag, new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));
        b.CornerRadius = new CornerRadius(11);
        b.Padding = new Thickness(8, 8, 8, 7);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.Cursor = FriendsDrawer.Hand();
        return b;
    }
}
